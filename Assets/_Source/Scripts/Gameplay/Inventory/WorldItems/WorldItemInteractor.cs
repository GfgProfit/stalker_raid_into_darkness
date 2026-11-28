using RaidIntoDarkness.Inventory.Domain;
using System;
using RaidIntoDarkness.Inventory.Application;
using RaidIntoDarkness.FirstPerson;
using RaidIntoDarkness.Player;
using RaidIntoDarkness.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RaidIntoDarkness.Inventory.Infrastructure
{
    [DisallowMultipleComponent]
    public sealed class WorldItemInteractor : MonoBehaviour, IWorldItemInteraction
    {
        [SerializeField] private InputActionReference _pickUp;
        [SerializeField] [Min(0.1f)] private float _pickUpDistance = 3f;
        [SerializeField] [Min(0f)] private float _aimRadius = 0.15f;
        [SerializeField] [Min(0.1f)] private float _dropDistance = 1f;
        [SerializeField] private LayerMask _mask = ~0;

        private readonly WorldItemTargetSearch _targetSearch = new();
        private readonly InventoryObservers<Action<(ItemInstance Item, InventoryResult Result)>> _pickupObservers = new();
        private PlayerInventoryRuntime _inventory;
        private WorldItem _hintTarget;
        private WorldItem _pickupTarget;
        private bool _pickupPending;

        public event Action<string> HintChanged;
        public event Action<bool> DropAvailabilityChanged;

        public event Action<(ItemInstance Item, InventoryResult Result)> PickupCompleted
        {
            add => _pickupObservers.Add(value);
            remove => _pickupObservers.Remove(value);
        }

        public bool CanDrop => isActiveAndEnabled && _inventory != null;
        public string ActivePickupBinding => _hintTarget != null ? _pickUp != null && _pickUp.action != null ? _pickUp.action.GetBindingDisplayString() : string.Empty : null;
        public bool IsPickingUp => _pickupPending;
        public WorldItem PickupTarget => _pickupPending ? _pickupTarget : null;

        private StalkerPlayerController Player => _inventory != null ? _inventory.Player : null;

        public void Configure(InputActionReference pickUp)
        {
            _pickUp = pickUp;
        }

        internal void BindInventory(PlayerInventoryRuntime inventory)
        {
            _inventory = inventory;
            DropAvailabilityChanged?.Invoke(CanDrop);
        }

        internal void UnbindInventory(PlayerInventoryRuntime inventory)
        {
            if (ReferenceEquals(_inventory, inventory) == false)
            {
                return;
            }

            CancelPickup();
            _inventory = null;
            SetHintTarget(null);
            DropAvailabilityChanged?.Invoke(false);
        }

        public WorldItem FindTarget() => _targetSearch.Find(Player, _pickUpDistance, _aimRadius, _mask);

        public bool BeginPickUp(WorldItem worldItem)
        {
            if (worldItem == null || worldItem.isActiveAndEnabled == false || _inventory == null || _pickupPending || IsReloading())
            {
                return false;
            }

            FirstPersonPickupAnimator pickup = GetPickupAnimator();
            if (pickup == null)
            {
                return TryPickUp(worldItem);
            }

            FirstPersonRig rig = Player.Rig;
            if (pickup.IsPlaying || (rig != null && rig.HandsBusy && !pickup.CanOverlay))
            {
                return false;
            }

            _pickupTarget = worldItem;
            _pickupPending = true;
            if (pickup.TryPlay(OnPickupTake, OnPickupFinished))
            {
                return true;
            }

            _pickupTarget = null;
            _pickupPending = false;
            return TryPickUp(worldItem);
        }

        public bool TryPickUp(WorldItem worldItem)
        {
            PlayerInventoryRuntime inventory = _inventory;
            if (worldItem == null || worldItem.Definition == null || inventory == null || IsReloading() || worldItem.TryClaimPickup() == false)
            {
                return false;
            }

            bool removedFromWorld = false;
            try
            {
                InventoryModel model = inventory.Model;
                ItemInstance item = worldItem.Instance ?? model.CreateItem(worldItem.Definition, worldItem.Amount);
                InventoryResult result = model.TryAddAuto(item, inventory.PrimaryContainerId);

                if (result.Success == false)
                {
                    PublishPickup(item, result);
                    return false;
                }

                removedFromWorld = true;
                if (worldItem != null)
                {
                    GameObject pickedObject = worldItem.gameObject;
                    pickedObject.SetActive(false);
                    Destroy(pickedObject);
                }

                PublishPickup(item, result);
                return true;
            }
            finally
            {
                if (removedFromWorld == false && worldItem != null)
                {
                    worldItem.ReleasePickupClaim();
                }
            }
        }

        public WorldItem Spawn(ItemDefinition definition, int amount)
        {
            StalkerPlayerController player = Player;
            Transform view = WorldItemDrop.GetView(player);
            WorldItem prefab = WorldItemDrop.GetWorldPrefab(definition);

            if (view == null || prefab == null)
            {
                return null;
            }

            Vector3 forward = WorldItemDrop.GetHorizontalForward(view, player.transform);
            CharacterController character = player.GetComponent<CharacterController>();
            WorldItem worldItem = Instantiate(prefab, view.position, Quaternion.LookRotation(forward, Vector3.up));
            worldItem.Configure(definition, amount);
            Physics.SyncTransforms();
            Bounds bounds = new Bounds(worldItem.transform.position, Vector3.zero);

            foreach (Collider collider in worldItem.GetComponentsInChildren<Collider>())
            {
                if (collider.enabled && !collider.isTrigger)
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            float playerRadius = character != null ? Mathf.Max(character.bounds.extents.x, character.bounds.extents.z) : 0.5f;
            float itemRadius = new Vector2(bounds.extents.x, bounds.extents.z).magnitude;
            float distance = Mathf.Max(_dropDistance, playerRadius + itemRadius + 0.1f);
            Vector3 position = view.position + forward * distance;
            position.y += Mathf.Max(0f, view.forward.y) * _dropDistance;
            worldItem.transform.position = position;
            Physics.SyncTransforms();
            return worldItem;
        }

        private void OnEnable()
        {
            if (_pickUp != null && _pickUp.action != null && _pickUp.action.enabled == false)
            {
                _pickUp.action.Enable();
            }

            DropAvailabilityChanged?.Invoke(CanDrop);
        }

        private void OnDisable()
        {
            SetHintTarget(null);
            CancelPickup();
            DropAvailabilityChanged?.Invoke(false);
        }

        private void Update()
        {
            if (_inventory == null)
            {
                return;
            }

            if (Player != null && Player.GameplayInputBlocked || IsReloading())
            {
                SetHintTarget(null);
                return;
            }

            WorldItem target = FindAvailableTarget();

            if (target != null && _pickUp != null && _pickUp.action != null && _pickUp.action.WasPressedThisFrame())
            {
                BeginPickUp(target);
                target = FindAvailableTarget();
            }

            SetHintTarget(target);
        }

        private void SetHintTarget(WorldItem target)
        {
            if (ReferenceEquals(_hintTarget, target))
            {
                return;
            }

            _hintTarget = target;
            HintChanged?.Invoke(ActivePickupBinding);
        }

        private WorldItem FindAvailableTarget()
        {
            WorldItem target = FindTarget();
            return _pickupPending && ReferenceEquals(target, _pickupTarget) ? null : target;
        }

        private void OnPickupTake()
        {
            if (_pickupTarget != null)
            {
                TryPickUp(_pickupTarget);
            }
        }

        private void OnPickupFinished(bool taken)
        {
            _pickupTarget = null;
            _pickupPending = false;
        }

        private void CancelPickup()
        {
            if (_pickupPending == false)
            {
                return;
            }

            FirstPersonPickupAnimator pickup = GetPickupAnimator();
            if (pickup != null)
            {
                pickup.Cancel();
            }

            _pickupTarget = null;
            _pickupPending = false;
        }

        private void PublishPickup(ItemInstance item, InventoryResult result)
        {
            _pickupObservers.Publish((Item: item, Result: result), static (observer, value) => observer(value), static value => $"{nameof(PickupCompleted)} for item '{value.Item.InstanceId}'");
        }

        private bool IsReloading()
        {
            StalkerPlayerController player = Player;
            return player != null && player.Rig != null && player.Rig.HandsOwner is FirstPersonWeaponController weapon && weapon.State == FirstPersonWeaponState.Reloading;
        }

        private FirstPersonPickupAnimator GetPickupAnimator()
        {
            StalkerPlayerController player = Player;
            return player != null ? player.Pickup : null;
        }
    }
}

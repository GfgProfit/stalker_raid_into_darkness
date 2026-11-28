using Inventory;
using Inventory.UI;
using RaidIntoDarkness.FirstPerson;
using RaidIntoDarkness.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RaidIntoDarkness.Integrations.Inventory
{
    [DisallowMultipleComponent]
    public sealed class WorldItemInteractor : MonoBehaviour
    {
        private const float DROP_HEIGHT_BELOW_VIEW = 0.3f;
        private const float DROP_FORWARD_SPEED = 2.1f;
        private const float DROP_DOWNWARD_SPEED = 0.65f;
        private const float DROP_PITCH_SPEED = 4f;
        private const float DROP_ROLL_SPEED = 2f;
        private const float DROP_MAX_DEPENETRATION_SPEED = 2f;
        private const float DROP_OBSTRUCTION_IGNORE_SECONDS = 0.65f;

        [SerializeField] private InventoryWindowController _controller;
        [SerializeField] private InputActionReference _pickUp;
        [SerializeField] [Min(0.1f)] private float _pickUpDistance = 3f;
        [SerializeField] [Min(0f)] private float _aimRadius = 0.15f;
        [SerializeField] [Min(0.1f)] private float _dropDistance = 1f;
        [SerializeField] private LayerMask _mask = ~0;

        private readonly RaycastHit[] _hits = new RaycastHit[32];
        private readonly WorldItemDropPlacement _dropPlacement = new();
        private InventoryWindow _dropWindow;
        private WorldItem _hintTarget;
        private WorldItem _pickupTarget;
        private bool _pickupPending;

        public bool IsPickingUp => _pickupPending;
        public WorldItem PickupTarget => _pickupPending ? _pickupTarget : null;

        public void Configure(InventoryWindowController controller, InputActionReference pickUp)
        {
            _controller = controller;
            _pickUp = pickUp;
        }

        public WorldItem FindTarget()
        {
            Transform view = GetView();

            if (view == null)
            {
                return null;
            }

            Transform player = _controller.Player.transform.root;
            float blocker = _pickUpDistance;
            int count = Physics.RaycastNonAlloc(view.position, view.forward, _hits, _pickUpDistance, _mask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider collider = _hits[i].collider;

                if (collider.transform.IsChildOf(player) == false && collider.GetComponentInParent<WorldItem>() == null && _hits[i].distance < blocker)
                {
                    blocker = _hits[i].distance;
                }
            }

            count = Physics.SphereCastNonAlloc(view.position, _aimRadius, view.forward, _hits, _pickUpDistance, _mask, QueryTriggerInteraction.Ignore);
            WorldItem target = null;
            float nearest = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                WorldItem worldItem = _hits[i].collider.GetComponentInParent<WorldItem>();

                if (worldItem != null && _hits[i].distance <= blocker + _aimRadius && _hits[i].distance < nearest)
                {
                    target = worldItem;
                    nearest = _hits[i].distance;
                }
            }

            return target;
        }

        public bool BeginPickUp(WorldItem worldItem)
        {
            if (worldItem == null || _pickupPending || IsReloading())
            {
                return false;
            }

            FirstPersonPickupAnimator pickup = GetPickupAnimator();
            if (pickup == null)
            {
                return TryPickUp(worldItem);
            }

            if (pickup.IsPlaying || (_controller.Player.Rig != null && _controller.Player.Rig.HandsBusy && !pickup.CanOverlay))
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
            if (worldItem == null || worldItem.Definition == null || _controller == null || _controller.Inventory == null || _controller.Window == null || IsReloading())
            {
                return false;
            }

            InventoryWindow window = _controller.Window;
            InventoryModel model = _controller.Inventory.Model;
            ItemInstance item = worldItem.Instance ?? model.CreateItem(worldItem.Definition, worldItem.Amount);
            int before = item.Amount;
            InventoryResult result = model.TryAddAuto(item, window.PrimaryContainerId);

            if (result.Success)
            {
                window.ShowStatus(window.FormatLabel("status_picked", "Picked up {0}", worldItem.Definition.DisplayName));
                PlayTakeSound(item);
                worldItem.gameObject.SetActive(false);
                Destroy(worldItem.gameObject);
                return true;
            }

            if (item.Amount < before)
            {
                worldItem.Configure(item);
                PlayTakeSound(item);
                window.ShowStatus(window.FormatLabel("status_picked", "Picked up {0}", worldItem.Definition.DisplayName));
                return true;
            }

            window.ShowStatus(window.Label("failure_" + result.Failure, result.Failure.ToString()));
            return false;
        }

        public WorldItem Spawn(ItemDefinition definition, int amount)
        {
            Transform view = GetView();
            WorldItem prefab = GetWorldPrefab(definition);

            if (view == null || prefab == null)
            {
                return null;
            }

            Vector3 forward = GetHorizontalForward(view);
            CharacterController character = _controller.Player.GetComponent<CharacterController>();
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

            if (_controller != null && _controller.Window != null)
            {
                _dropWindow = _controller.Window;
                _dropWindow.DropToWorld = TryDropToWorld;
            }
        }

        private void OnDisable()
        {
            SetHintTarget(null);

            if (_pickupPending)
            {
                FirstPersonPickupAnimator pickup = GetPickupAnimator();
                if (pickup != null)
                {
                    pickup.Cancel();
                }

                _pickupTarget = null;
                _pickupPending = false;
            }

            if (_dropWindow != null)
            {
                if (_dropWindow.DropToWorld == TryDropToWorld)
                {
                    _dropWindow.DropToWorld = null;
                }

                _dropWindow = null;
            }
        }

        private void Update()
        {
            if (_controller == null || _controller.Window == null)
            {
                return;
            }

            if (_controller.IsOpen || _controller.GameplayBlocked || IsReloading())
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
            InventoryWindow window = _controller != null ? _controller.Window : null;

            if (window == null)
            {
                return;
            }

            if (target == null)
            {
                window.ClearHint();
                return;
            }

            string key = _pickUp != null && _pickUp.action != null ? _pickUp.action.GetBindingDisplayString() : string.Empty;
            window.ShowHint(window.FormatLabel("hint_pick_up", "[{0}] Pick up", key));
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

        private void PlayTakeSound(ItemInstance item)
        {
            if (_controller.ItemSounds != null)
            {
                _controller.ItemSounds.PlayTake(item);
            }
        }

        private bool IsReloading() => _controller != null && _controller.Player != null && _controller.Player.Rig != null && _controller.Player.Rig.HandsOwner is FirstPersonWeaponController weapon && weapon.State == FirstPersonWeaponState.Reloading;

        private FirstPersonPickupAnimator GetPickupAnimator() => _controller != null && _controller.Player != null ? _controller.Player.Pickup : null;

        private InventoryResult TryDropToWorld(ItemInstance item)
        {
            InventoryModel model = _dropWindow != null ? _dropWindow.Model : null;

            if (model == null || model.ContainsItem(item) == false)
            {
                return InventoryResult.Fail(InventoryFailure.ItemNotFound);
            }

            Transform view = GetView();
            WorldItem prefab = GetWorldPrefab(item.Definition);

            if (view == null || prefab == null)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            Transform player = _controller.Player.transform;
            Vector3 forward = GetHorizontalForward(view);
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            CharacterController character = _controller.Player.GetComponent<CharacterController>();
            Vector3 origin = character != null ? character.bounds.center : view.position;
            origin.y = view.position.y - DROP_HEIGHT_BELOW_VIEW;
            WorldItem worldItem = Instantiate(prefab, origin, Quaternion.LookRotation(right, Vector3.up));
            worldItem.Configure(item);
            Collider[] colliders = worldItem.GetComponentsInChildren<Collider>();

            if (_dropPlacement.TryFindPosition(worldItem.transform, colliders, player.root, origin, forward, out Vector3 position, out Vector3 direction) == false)
            {
                worldItem.gameObject.SetActive(false);
                Destroy(worldItem.gameObject);
                return InventoryResult.Fail(InventoryFailure.NoSpace);
            }

            worldItem.transform.position = position;
            Physics.SyncTransforms();
            InventoryResult result = model.TryRemove(item.InstanceId);

            if (result.Success == false)
            {
                worldItem.gameObject.SetActive(false);
                Destroy(worldItem.gameObject);
                return result;
            }

            Rigidbody body = worldItem.GetComponent<Rigidbody>();
            if (worldItem.TryGetComponent(out WorldItemImpactSound impactSound))
            {
                impactSound.ExpectInventoryDropImpact();
            }

            body.maxDepenetrationVelocity = DROP_MAX_DEPENETRATION_SPEED;
            body.linearVelocity = direction * DROP_FORWARD_SPEED - Vector3.up * DROP_DOWNWARD_SPEED;
            body.angularVelocity = Vector3.Cross(Vector3.up, direction) * DROP_PITCH_SPEED + direction * DROP_ROLL_SPEED;
            FirstPersonRig rig = _controller.Player.Rig;
            if (rig != null)
            {
                rig.IgnoreWeaponObstruction(colliders, DROP_OBSTRUCTION_IGNORE_SECONDS);
            }

            return result;
        }

        private Vector3 GetHorizontalForward(Transform view)
        {
            Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);

            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Vector3.ProjectOnPlane(_controller.Player.transform.forward, Vector3.up);
            }

            return forward.normalized;
        }

        private static WorldItem GetWorldPrefab(ItemDefinition definition) => definition != null && definition.TryGetComponent(out WorldItemComponent component) ? component.Prefab : null;

        private Transform GetView()
        {
            if (_controller == null || _controller.Player == null || _controller.Player.CameraController == null || _controller.Player.CameraController.Camera == null)
            {
                return null;
            }

            return _controller.Player.CameraController.Camera.transform;
        }
    }
}

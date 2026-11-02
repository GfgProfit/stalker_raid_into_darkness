using Inventory;
using Inventory.UI;
using RaidIntoDarkness.FirstPerson;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RaidIntoDarkness.Integrations.Inventory
{
    [DisallowMultipleComponent]
    public sealed class WorldItemInteractor : MonoBehaviour
    {
        [SerializeField] private InventoryWindowController _controller;
        [SerializeField] private InputActionReference _pickUp;
        [SerializeField] [Min(0.1f)] private float _pickUpDistance = 3f;
        [SerializeField] [Min(0f)] private float _aimRadius = 0.15f;
        [SerializeField] [Min(0.1f)] private float _dropDistance = 1f;
        [SerializeField] private LayerMask _mask = ~0;

        private readonly RaycastHit[] _hits = new RaycastHit[32];
        private InventoryWindow _subscribedWindow;
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
            if (worldItem == null || _pickupPending)
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
            if (worldItem == null || worldItem.Definition == null || _controller == null || _controller.Inventory == null || _controller.Window == null)
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

            if (definition == null || view == null || definition.TryGetComponent(out WorldItemComponent component) == false || component.Prefab == null)
            {
                return null;
            }

            Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);

            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Vector3.ProjectOnPlane(_controller.Player.transform.forward, Vector3.up);
            }

            forward.Normalize();
            WorldItem worldItem = Instantiate(component.Prefab, view.position, Quaternion.LookRotation(forward, Vector3.up));
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

            CharacterController character = _controller.Player.GetComponent<CharacterController>();
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
                _subscribedWindow = _controller.Window;
                _subscribedWindow.ItemDropped += OnItemDropped;
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

            if (_subscribedWindow != null)
            {
                _subscribedWindow.ItemDropped -= OnItemDropped;
                _subscribedWindow = null;
            }
        }

        private void Update()
        {
            if (_controller == null || _controller.Window == null)
            {
                return;
            }

            if (_controller.IsOpen || _controller.GameplayBlocked)
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

        private FirstPersonPickupAnimator GetPickupAnimator() => _controller != null && _controller.Player != null ? _controller.Player.Pickup : null;

        private void OnItemDropped(ItemInstance item)
        {
            if (item != null)
            {
                WorldItem worldItem = Spawn(item.Definition, item.Amount);

                if (worldItem != null)
                {
                    worldItem.Configure(item);
                }
            }
        }

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

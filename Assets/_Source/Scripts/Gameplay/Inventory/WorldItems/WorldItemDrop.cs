using RaidIntoDarkness.Inventory.Domain;
using RaidIntoDarkness.FirstPerson;
using RaidIntoDarkness.Player;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Infrastructure
{
    internal sealed class WorldItemDrop : IOutfitDropSource
    {
        private const float DROP_HEIGHT_BELOW_VIEW = 0.3f;
        private const float DROP_FORWARD_SPEED = 2.1f;
        private const float DROP_DOWNWARD_SPEED = 0.65f;
        private const float DROP_PITCH_SPEED = 4f;
        private const float DROP_ROLL_SPEED = 2f;
        private const float DROP_MAX_DEPENETRATION_SPEED = 2f;
        private const float DROP_OBSTRUCTION_IGNORE_SECONDS = 0.65f;

        private readonly WorldItemDropPlacement _dropPlacement = new();
        private readonly Behaviour _host;
        private readonly StalkerPlayerController _owner;

        public WorldItemDrop(Behaviour host, StalkerPlayerController owner)
        {
            _host = host;
            _owner = owner;
        }

        public bool Available => _host != null && _host.isActiveAndEnabled;

        public InventoryResult TryDrop(InventoryModel model, ItemInstance item)
        {
            if (model.ContainsItem(item) == false)
            {
                return InventoryResult.Fail(InventoryFailure.ItemNotFound);
            }

            Transform view = GetView(_owner);
            WorldItem prefab = GetWorldPrefab(item.Definition);

            if (view == null || prefab == null)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            Transform player = _owner.transform;
            GetDropPose(view, out Vector3 origin, out Vector3 forward, out Quaternion rotation);
            WorldItem worldItem = Object.Instantiate(prefab, origin, rotation);
            worldItem.Configure(item);
            Collider[] colliders = worldItem.GetComponentsInChildren<Collider>();

            if (_dropPlacement.TryFindPosition(worldItem.transform, colliders, player.root, origin, forward, out Vector3 position, out Vector3 direction) == false)
            {
                Cancel(worldItem);
                return InventoryResult.Fail(InventoryFailure.NoSpace);
            }

            worldItem.transform.position = position;
            Physics.SyncTransforms();
            InventoryResult result = model.TryRemove(item.InstanceId);

            if (result.Success == false)
            {
                Cancel(worldItem);
                return result;
            }

            ApplyMotion(_owner, worldItem, colliders, direction);
            return result;
        }

        public bool CanPrepare(ItemInstance item)
        {
            WorldItem prefab = GetWorldPrefab(item.Definition);
            return Available && GetView(_owner) != null && prefab != null && prefab.enabled && prefab.gameObject.activeSelf && prefab.TryGetComponent<Rigidbody>(out _);
        }

        public InventoryResult TryPrepare(ItemInstance item, out IPreparedOutfitDrop drop)
        {
            drop = null;
            Transform view = GetView(_owner);
            WorldItem prefab = GetWorldPrefab(item.Definition);
            if (Available == false || view == null || prefab == null || prefab.enabled == false || prefab.gameObject.activeSelf == false || prefab.TryGetComponent<Rigidbody>(out _) == false)
            {
                return InventoryResult.Fail(InventoryFailure.NoSpace);
            }

            Transform player = _owner.transform;
            GetDropPose(view, out Vector3 origin, out Vector3 forward, out Quaternion rotation);
            var staging = new GameObject(nameof(PreparedWorldItemDrop));
            staging.SetActive(false);
            WorldItem worldItem = null;

            try
            {
                worldItem = Object.Instantiate(prefab, origin, rotation, staging.transform);
                worldItem.enabled = false;
                worldItem.transform.SetParent(null, true);
                worldItem.ConfigureDropAppearance(item);
                Collider[] colliders = worldItem.GetComponentsInChildren<Collider>();
                if (_dropPlacement.TryFindPosition(worldItem.transform, colliders, player.root, origin, forward, out Vector3 position, out Vector3 direction) == false)
                {
                    return InventoryResult.Fail(InventoryFailure.NoSpace);
                }

                worldItem.transform.position = position;
                Physics.SyncTransforms();
                drop = new PreparedWorldItemDrop(_owner, worldItem, colliders, direction);
                worldItem = null;
                return InventoryResult.Ok(item, item.Amount);
            }
            finally
            {
                if (worldItem != null)
                {
                    Cancel(worldItem);
                }

                Object.Destroy(staging);
            }
        }

        internal static void Cancel(WorldItem worldItem)
        {
            worldItem.gameObject.SetActive(false);
            Object.Destroy(worldItem.gameObject);
        }

        private void GetDropPose(Transform view, out Vector3 origin, out Vector3 forward, out Quaternion rotation)
        {
            forward = GetHorizontalForward(view, _owner.transform);
            rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, forward), Vector3.up);
            CharacterController character = _owner.GetComponent<CharacterController>();
            origin = character != null ? character.bounds.center : view.position;
            origin.y = view.position.y - DROP_HEIGHT_BELOW_VIEW;
        }

        internal static void ApplyMotion(StalkerPlayerController owner, WorldItem worldItem, Collider[] colliders, Vector3 direction)
        {
            Rigidbody body = worldItem.GetComponent<Rigidbody>();
            if (worldItem.TryGetComponent(out WorldItemImpactSound impactSound))
            {
                impactSound.ExpectInventoryDropImpact();
            }

            body.maxDepenetrationVelocity = DROP_MAX_DEPENETRATION_SPEED;
            body.linearVelocity = direction * DROP_FORWARD_SPEED - Vector3.up * DROP_DOWNWARD_SPEED;
            body.angularVelocity = Vector3.Cross(Vector3.up, direction) * DROP_PITCH_SPEED + direction * DROP_ROLL_SPEED;
            FirstPersonRig rig = owner.Rig;
            if (rig != null)
            {
                rig.IgnoreWeaponObstruction(colliders, DROP_OBSTRUCTION_IGNORE_SECONDS);
            }
        }

        internal static Vector3 GetHorizontalForward(Transform view, Transform player)
        {
            Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);

            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Vector3.ProjectOnPlane(player.forward, Vector3.up);
            }

            return forward.normalized;
        }

        internal static WorldItem GetWorldPrefab(ItemDefinition definition) => definition != null && definition.TryGetComponent(out WorldItemComponent component) ? component.Prefab : null;

        internal static Transform GetView(StalkerPlayerController player)
        {
            if (player == null || player.CameraController == null || player.CameraController.Camera == null)
            {
                return null;
            }

            return player.CameraController.Camera.transform;
        }
    }
}

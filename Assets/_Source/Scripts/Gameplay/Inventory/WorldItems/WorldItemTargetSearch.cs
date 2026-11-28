using RaidIntoDarkness.Player;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Infrastructure
{
    internal sealed class WorldItemTargetSearch
    {
        private readonly RaycastHit[] _hits = new RaycastHit[32];

        public WorldItem Find(StalkerPlayerController owner, float distance, float aimRadius, LayerMask mask)
        {
            Transform view = WorldItemDrop.GetView(owner);

            if (view == null)
            {
                return null;
            }

            Transform player = owner.transform.root;
            float blocker = distance;
            int count = Physics.RaycastNonAlloc(view.position, view.forward, _hits, distance, mask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider collider = _hits[i].collider;

                if (collider.transform.IsChildOf(player) == false && collider.GetComponentInParent<WorldItem>() == null && _hits[i].distance < blocker)
                {
                    blocker = _hits[i].distance;
                }
            }

            count = Physics.SphereCastNonAlloc(view.position, aimRadius, view.forward, _hits, distance, mask, QueryTriggerInteraction.Ignore);
            WorldItem target = null;
            float nearest = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                WorldItem worldItem = _hits[i].collider.GetComponentInParent<WorldItem>();

                if (worldItem != null && worldItem.isActiveAndEnabled && _hits[i].distance <= blocker + aimRadius && _hits[i].distance < nearest)
                {
                    target = worldItem;
                    nearest = _hits[i].distance;
                }
            }

            return target;
        }
    }
}

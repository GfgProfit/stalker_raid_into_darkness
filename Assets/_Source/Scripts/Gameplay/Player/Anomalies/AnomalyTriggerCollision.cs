using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Player
{
    internal static class AnomalyTriggerCollision
    {
        private const string TRIGGER_LAYER = "Anomaly Trigger";

        private static readonly int TRIGGER_LAYER_INDEX = LayerMask.NameToLayer(TRIGGER_LAYER);
        private static readonly int LEVEL_LAYER_INDEX = LayerMask.NameToLayer(XRayLevelMetadata.COLLISION_LAYER);

        internal static void Configure(Collider trigger)
        {
            if (trigger == null || !trigger.isTrigger)
            {
                return;
            }

            int excluded = trigger.excludeLayers.value;
            if (LEVEL_LAYER_INDEX >= 0)
            {
                excluded |= 1 << LEVEL_LAYER_INDEX;
            }
            if (TRIGGER_LAYER_INDEX >= 0)
            {
                excluded |= 1 << TRIGGER_LAYER_INDEX;
                if (trigger.gameObject.layer == 0 && ContainsOnlyTriggerColliders(trigger))
                {
                    trigger.gameObject.layer = TRIGGER_LAYER_INDEX;
                }
            }
            if (trigger.excludeLayers.value != excluded)
            {
                trigger.excludeLayers = excluded;
            }
        }

        private static bool ContainsOnlyTriggerColliders(Collider trigger)
        {
            if (trigger.TryGetComponent<Renderer>(out _) || trigger.TryGetComponent<Camera>(out _) || trigger.TryGetComponent<Volume>(out _))
            {
                return false;
            }
            foreach (Collider collider in trigger.GetComponents<Collider>())
            {
                if (!collider.isTrigger)
                {
                    return false;
                }
            }
            return true;
        }
    }
}

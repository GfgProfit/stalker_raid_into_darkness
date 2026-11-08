using UnityEngine;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    public sealed class AnomalyProbe : MonoBehaviour
    {
        public static bool CanActivate(float elapsed, bool ignoreSmallObjects, float ignoreAfter)
        {
            return ignoreSmallObjects == false && (ignoreAfter < 0f || elapsed <= ignoreAfter);
        }
    }
}

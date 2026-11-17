using System;
using UnityEngine;

namespace XRay.Importer.Levels
{
    public static class XRayVideoOverrides
    {
        private static Func<float?> _punctualShadowDefaults;

        public static float? PunctualShadowDistance { get; private set; }
        public static bool? LensFlare { get; private set; }
        public static int? FogQuality { get; private set; }
        public static float LodBias { get; private set; } = 1.0f;

        public static float? DefaultPunctualShadowDistance
        {
            get
            {
                float? distance = _punctualShadowDefaults?.Invoke();
                return distance.HasValue && float.IsFinite(distance.Value) && distance.Value > 0.0f ? distance : null;
            }
        }

        internal static event Action PunctualShadowDistanceChanged;

        public static Func<float?> SetPunctualShadowDefaultsProvider(Func<float?> provider)
        {
            Func<float?> previous = _punctualShadowDefaults;
            _punctualShadowDefaults = provider;
            return previous;
        }

        public static void Apply(float? punctualShadowDistance, bool? lensFlare, int? fogQuality, float lodBias)
        {
            LensFlare = lensFlare;
            FogQuality = fogQuality;
            LodBias = lodBias;
            if (PunctualShadowDistance != punctualShadowDistance)
            {
                PunctualShadowDistance = punctualShadowDistance;
                PunctualShadowDistanceChanged?.Invoke();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _punctualShadowDefaults = null;
            PunctualShadowDistance = null;
            LensFlare = null;
            FogQuality = null;
            LodBias = 1.0f;
            PunctualShadowDistanceChanged = null;
        }
    }
}

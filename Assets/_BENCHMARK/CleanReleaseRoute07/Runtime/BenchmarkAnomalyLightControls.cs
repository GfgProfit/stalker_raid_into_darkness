using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    public static class BenchmarkAnomalyLightControls
    {
        private static readonly FieldInfo JITTER = typeof(AnomalyVisual).GetField("_idleLightRangeJitter", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo LIGHT = typeof(AnomalyVisual).GetField("_idleIllumination", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Dictionary<AnomalyVisual, float> RANGES = new Dictionary<AnomalyVisual, float>();
        private static readonly Dictionary<Light, LightShadows> SHADOWS = new Dictionary<Light, LightShadows>();

        public static void ConfigureDiagnosticIdleRange(this AnomalyVisual visual, bool fixedRange)
        {
            if (!RANGES.TryGetValue(visual, out float jitter))
            {
                jitter = (float)JITTER.GetValue(visual);
                RANGES.Add(visual, jitter);
            }
            JITTER.SetValue(visual, fixedRange ? 0f : jitter);
        }

        public static void ConfigureDiagnosticIdleShadows(this AnomalyVisual visual, bool disabled)
        {
            var light = (Light)LIGHT.GetValue(visual);
            if (light == null)
            {
                return;
            }
            if (!SHADOWS.TryGetValue(light, out var shadows))
            {
                shadows = light.shadows;
                SHADOWS.Add(light, shadows);
            }
            light.shadows = disabled ? LightShadows.None : shadows;
        }
    }
}

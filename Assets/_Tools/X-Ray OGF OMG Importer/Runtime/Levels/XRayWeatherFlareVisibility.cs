using System;
using UnityEngine;

namespace XRay.Importer.Levels
{
    [Serializable]
    public struct XRayWeatherFlareProfileOverride
    {
        [SerializeField] private XRayWeatherProfile _profile;
        [SerializeField, Range(0.0f, 1.0f)] private float _dayVisibility;
        [SerializeField, Range(0.0f, 1.0f)] private float _moonVisibility;

        public XRayWeatherProfile Profile => _profile;
        public Vector2 Visibility => new Vector2(_dayVisibility, _moonVisibility);
    }

    public static class XRayWeatherFlareVisibility
    {
        public static Vector2 Resolve(XRayWeatherProfile profile, XRayWeatherSnapshot snapshot, Vector2? profileOverride = null)
        {
            Vector2 direct = profileOverride ?? DefaultVisibility(profile == null ? null : profile.CycleName);
            return new Vector2(Mathf.Clamp01(direct.x), Mathf.Clamp01(direct.y)) * FogTransmission(snapshot);
        }

        public static float AtTime(Vector2 visibility, double gameSeconds)
        {
            float seconds = (float)XRayWeatherProfile.NormalizeTime(gameSeconds);
            float sunrise = Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(14400.0f, 18000.0f, seconds));
            float sunset = Mathf.SmoothStep(1.0f, 0.0f, Mathf.InverseLerp(72000.0f, 75600.0f, seconds));
            return Mathf.Lerp(visibility.y, visibility.x, sunrise * sunset);
        }

        internal static float FogTransmission(XRayWeatherSnapshot snapshot)
        {
            float distance = Mathf.InverseLerp(30.0f, 220.0f, snapshot.FogDistance);
            return Mathf.Lerp(1.0f, Mathf.SmoothStep(0.35f, 1.0f, distance), Mathf.Clamp01(snapshot.FogDensity));
        }

        internal static float UntintedBrightness(float lux, bool moon)
        {
            return Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(lux / (moon ? 32.0f : 1000.0f)));
        }

        private static Vector2 DefaultVisibility(string cycle)
        {
            return cycle switch
            {
                "w_partly1" or "w_partly2" or "w_partly3" => new Vector2(0.82f, 0.80f),
                "w_cloudy1" or "w_cloudy2" => new Vector2(0.16f, 0.28f),
                "w_cloudy2_dark" or "w_cloudy3_dark" => new Vector2(0.0f, 0.08f),
                "w_foggy1" or "w_foggy3" => new Vector2(0.38f, 0.48f),
                "w_foggy2" => new Vector2(0.25f, 0.34f),
                "w_cloudy3" => new Vector2(0.22f, 0.27f),
                "w_rain1" or "w_rain2" => new Vector2(0.12f, 0.23f),
                "w_rain3" or "w_rain4" => new Vector2(0.06f, 0.15f),
                "w_storm1" or "w_storm2" or "w_storm3" => new Vector2(0.0f, 0.04f),
                _ => Vector2.one
            };
        }
    }
}

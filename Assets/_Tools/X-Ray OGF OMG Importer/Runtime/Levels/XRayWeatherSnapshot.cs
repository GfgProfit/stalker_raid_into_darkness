using System;
using UnityEngine;

namespace XRay.Importer.Levels
{
    [Serializable]
    public struct XRayWeatherSnapshot
    {
        [field: SerializeField] public double GameSeconds { get; private set; }
        [field: SerializeField] public string Key0 { get; private set; }
        [field: SerializeField] public string Key1 { get; private set; }
        [field: SerializeField] public float ResourceBlend { get; private set; }
        [field: SerializeField] public Color SunColor { get; private set; }
        [field: SerializeField] public Vector3 SunDirection { get; private set; }
        [field: SerializeField] public Color AmbientColor { get; private set; }
        [field: SerializeField] public Color HemisphereColor { get; private set; }
        [field: SerializeField] public float WindDirectionRadians { get; private set; }
        [field: SerializeField] public float WindVelocity { get; private set; }
        [field: SerializeField] public float TreeAmplitudeIntensity { get; private set; }
        [field: SerializeField] public float RainDensity { get; private set; }
        [field: SerializeField] public Color RainColor { get; private set; }
        [field: SerializeField] public Color FogColor { get; private set; }
        [field: SerializeField] public float FogDensity { get; private set; }
        [field: SerializeField] public float FogDistance { get; private set; }
        [field: SerializeField] public float FarPlane { get; private set; }
        [field: SerializeField] public Color SkyColor { get; private set; }
        [field: SerializeField] public Color CloudsColor { get; private set; }
        [field: SerializeField] public float SkyRotationRadians { get; private set; }
        [field: SerializeField] public XRayWeatherResources SkyResources { get; private set; }
        [field: SerializeField] public XRayWeatherResources CloudsResources { get; private set; }
        [field: SerializeField] public XRayWeatherResources EnvironmentResources { get; private set; }
        [field: SerializeField] public string SunDefinition { get; private set; }
        [field: SerializeField] public string AmbientDefinition { get; private set; }
        [field: SerializeField] public string ThunderboltCollection { get; private set; }
        [field: SerializeField] public float ThunderboltPeriod { get; private set; }
        [field: SerializeField] public float ThunderboltDuration { get; private set; }
        [field: SerializeField] public float SunShaftsIntensity { get; private set; }
        [field: SerializeField] public float WaterIntensity { get; private set; }

        public XRayWeatherSnapshot(string key, double gameSeconds, Color sunColor, Color ambientColor, Color hemisphereColor, float windDirectionRadians, float windVelocity, float treeAmplitudeIntensity, float rainDensity, Color rainColor, Color fogColor, float fogDensity, float fogDistance, float farPlane, Color skyColor, Color cloudsColor, float skyRotationRadians, XRayWeatherResources skyResources, XRayWeatherResources cloudsResources, string sunDefinition = "", string ambientDefinition = "", string thunderboltCollection = "", float thunderboltPeriod = 0.0f, float thunderboltDuration = 0.0f, float sunShaftsIntensity = 0.0f, float waterIntensity = 0.0f, XRayWeatherResources environmentResources = default)
        {
            GameSeconds = gameSeconds;
            Key0 = Key1 = key;
            ResourceBlend = 0.0f;
            SunColor = sunColor;
            SunDirection = Vector3.down;
            AmbientColor = ambientColor;
            HemisphereColor = hemisphereColor;
            WindDirectionRadians = windDirectionRadians;
            WindVelocity = windVelocity;
            TreeAmplitudeIntensity = treeAmplitudeIntensity;
            RainDensity = Mathf.Clamp01(rainDensity);
            RainColor = rainColor;
            FogColor = fogColor;
            FogDensity = fogDensity;
            FogDistance = fogDistance;
            FarPlane = farPlane;
            SkyColor = skyColor;
            CloudsColor = cloudsColor;
            SkyRotationRadians = skyRotationRadians;
            SkyResources = skyResources;
            CloudsResources = cloudsResources;
            EnvironmentResources = environmentResources;
            SunDefinition = sunDefinition;
            AmbientDefinition = ambientDefinition;
            ThunderboltCollection = thunderboltCollection;
            ThunderboltPeriod = thunderboltPeriod;
            ThunderboltDuration = thunderboltDuration;
            SunShaftsIntensity = sunShaftsIntensity;
            WaterIntensity = waterIntensity;
        }

        public float FogNear => (1.0f - FogDensity) * 0.85f * FogDistance;
        public float FogFar => 0.99f * FogDistance;
        public string SkyTextureName0 => SkyResources.Name(0);
        public string SkyTextureName1 => SkyResources.Name(1);
        public Texture SkyTexture0 => SkyResources.Texture(0);
        public Texture SkyTexture1 => SkyResources.Texture(1);

        public XRayWeatherSnapshot WithSampling(double seconds, Vector3 direction, float visibilityScale = 1.0f)
        {
            var result = this;
            result.GameSeconds = seconds;
            result.SunDirection = direction;
            result.FarPlane *= visibilityScale;
            result.FogDistance = Mathf.Clamp(result.FogDistance, 1.0f, result.FarPlane - 10.0f);
            return result;
        }

        public XRayWeatherSnapshot WithResources(XRayWeatherResources sky, XRayWeatherResources clouds, XRayWeatherResources environment)
        {
            var result = this;
            result.SkyResources = sky;
            result.CloudsResources = clouds;
            result.EnvironmentResources = environment;
            return result;
        }

        public static XRayWeatherSnapshot Blend(XRayWeatherSnapshot a, XRayWeatherSnapshot b, float weight)
        {
            float f = Mathf.Clamp01(weight);
            float fi = 1.0f - f;
            XRayWeatherSnapshot result = f < 0.5f ? a : b;
            result.GameSeconds = a.GameSeconds * fi + b.GameSeconds * f;
            result.Key0 = a.Key0;
            result.Key1 = b.Key1;
            result.ResourceBlend = f;
            result.SunColor = Color.LerpUnclamped(a.SunColor, b.SunColor, f);
            result.SunDirection = Vector3.LerpUnclamped(a.SunDirection, b.SunDirection, f).normalized;
            result.AmbientColor = Color.LerpUnclamped(a.AmbientColor, b.AmbientColor, f);
            result.HemisphereColor = Color.LerpUnclamped(a.HemisphereColor, b.HemisphereColor, f);
            result.WindDirectionRadians = fi * a.WindDirectionRadians + f * b.WindDirectionRadians;
            result.WindVelocity = fi * a.WindVelocity + f * b.WindVelocity;
            result.TreeAmplitudeIntensity = fi * a.TreeAmplitudeIntensity + f * b.TreeAmplitudeIntensity;
            result.RainDensity = fi * a.RainDensity + f * b.RainDensity;
            result.RainColor = Color.LerpUnclamped(a.RainColor, b.RainColor, f);
            result.FogColor = Color.LerpUnclamped(a.FogColor, b.FogColor, f);
            result.FogDensity = fi * a.FogDensity + f * b.FogDensity;
            result.FogDistance = fi * a.FogDistance + f * b.FogDistance;
            result.FarPlane = fi * a.FarPlane + f * b.FarPlane;
            result.SkyColor = Color.LerpUnclamped(a.SkyColor, b.SkyColor, f);
            result.CloudsColor = Color.LerpUnclamped(a.CloudsColor, b.CloudsColor, f);
            result.SkyRotationRadians = fi * a.SkyRotationRadians + f * b.SkyRotationRadians;
            result.SkyResources = XRayWeatherResources.Blend(a.SkyResources, b.SkyResources, f);
            result.CloudsResources = XRayWeatherResources.Blend(a.CloudsResources, b.CloudsResources, f);
            result.EnvironmentResources = XRayWeatherResources.Blend(a.EnvironmentResources, b.EnvironmentResources, f);
            result.ThunderboltPeriod = fi * a.ThunderboltPeriod + f * b.ThunderboltPeriod;
            result.ThunderboltDuration = fi * a.ThunderboltDuration + f * b.ThunderboltDuration;
            result.SunShaftsIntensity = fi * a.SunShaftsIntensity + f * b.SunShaftsIntensity;
            result.WaterIntensity = fi * a.WaterIntensity + f * b.WaterIntensity;
            return result;
        }
    }
}

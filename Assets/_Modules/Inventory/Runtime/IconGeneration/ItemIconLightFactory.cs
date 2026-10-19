using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Inventory.IconGeneration
{
    internal static class ItemIconLightFactory
    {
        private const int MAX_LIGHT_COUNT = 4;
        private const float MIN_DIRECTION_MAGNITUDE = 0.0001f;

        public static Light[] CreateReusable(ItemIconGeneratorSettings settings)
        {
            var lights = new Light[MAX_LIGHT_COUNT];

            for (int i = 0; i < lights.Length; i++)
            {
                lights[i] = CreateDirectionalLight(settings, $"Runtime Item Icon Light {i + 1}");
                lights[i].gameObject.SetActive(false);
            }

            return lights;
        }

        public static void Configure(IReadOnlyList<Light> lights, ItemDefinition definition, Transform cameraTransform, ItemIconGeneratorSettings settings, IconRenderProfile renderProfile)
        {
            Disable(lights);

            if (lights == null || lights.Count == 0)
            {
                return;
            }

            int lightIndex = 0;
            float baseIntensity = renderProfile.LightIntensity;

            ConfigureLight(lights[lightIndex++], $"Runtime Icon Key Light - {definition.name}", baseIntensity, cameraTransform.rotation);

            if (settings.UseFrontFillLight && lightIndex < lights.Count)
            {
                ConfigureLight(lights[lightIndex++], $"Runtime Icon Front Fill Light - {definition.name}", baseIntensity * settings.FrontFillLightIntensityMultiplier, GetDirectionalLightRotation(cameraTransform.TransformDirection(settings.FrontFillLightCameraDirection), cameraTransform.up));
            }

            if (settings.UseSideFillLight && lightIndex < lights.Count)
            {
                ConfigureLight(lights[lightIndex++], $"Runtime Icon Side Fill Light - {definition.name}", baseIntensity * settings.SideFillLightIntensityMultiplier, GetDirectionalLightRotation(cameraTransform.TransformDirection(settings.SideFillLightCameraDirection), cameraTransform.up));
            }

            if (settings.UseRimLight && lightIndex < lights.Count)
            {
                ConfigureLight(lights[lightIndex], $"Runtime Icon Rim Light - {definition.name}", baseIntensity * settings.RimLightIntensityMultiplier, GetDirectionalLightRotation(cameraTransform.TransformDirection(settings.RimLightCameraDirection), cameraTransform.up));
            }
        }

        public static void Disable(IReadOnlyList<Light> lights)
        {
            if (lights == null)
            {
                return;
            }

            for (int i = 0; i < lights.Count; i++)
            {
                if (lights[i] != null)
                {
                    lights[i].gameObject.SetActive(false);
                }
            }
        }

        private static Light CreateDirectionalLight(ItemIconGeneratorSettings settings, string lightName)
        {
            var lightObject = new GameObject(lightName, typeof(Light))
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            var renderLight = lightObject.GetComponent<Light>();
            HDAdditionalLightData hdLightData = lightObject.AddComponent<HDAdditionalLightData>();
            renderLight.type = LightType.Directional;
            renderLight.cullingMask = settings.RenderLayerMask;
            renderLight.renderingLayerMask = (int)settings.RenderingLayerMask;
            renderLight.shadows = settings.GeneratedLightShadows;

            var iconLightLayer = (UnityEngine.Rendering.HighDefinition.RenderingLayerMask)settings.RenderingLayerMask;
            hdLightData.SetLightLayer(iconLightLayer, iconLightLayer);
            return renderLight;
        }

        private static void ConfigureLight(Light renderLight, string lightName, float intensity, Quaternion rotation)
        {
            if (renderLight == null)
            {
                return;
            }

            renderLight.name = lightName;
            renderLight.intensity = Mathf.Max(0f, intensity);
            renderLight.transform.rotation = rotation;
            renderLight.gameObject.SetActive(true);
        }

        private static Quaternion GetDirectionalLightRotation(Vector3 direction, Vector3 up)
        {
            if (direction.sqrMagnitude <= MIN_DIRECTION_MAGNITUDE)
            {
                return Quaternion.identity;
            }

            Vector3 normalizedUp = up.sqrMagnitude > MIN_DIRECTION_MAGNITUDE ? up.normalized : Vector3.up;
            return Quaternion.LookRotation(direction.normalized, normalizedUp);
        }
    }

    internal static class ItemIconVolumeFactory
    {
        private const float VOLUME_PRIORITY = 10000f;
        private const float VOLUME_RADIUS = 128f;

        public static GameObject Create(string itemName, ItemIconGeneratorSettings settings, out VolumeProfile volumeProfile)
        {
            var volumeObject = new GameObject($"Runtime Icon Volume - {itemName}")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = settings.RenderLayer
            };
            volumeObject.transform.position = settings.RenderOrigin;

            volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            volumeProfile.hideFlags = HideFlags.HideAndDontSave;
            volumeProfile.name = $"{itemName} Runtime Icon Volume Profile";
            ConfigureProfile(volumeProfile, settings);

            Volume volume = volumeObject.AddComponent<Volume>();
            SphereCollider volumeCollider = volumeObject.AddComponent<SphereCollider>();
            volumeCollider.isTrigger = true;
            volumeCollider.radius = VOLUME_RADIUS;

            volume.isGlobal = false;
            volume.blendDistance = 0f;
            volume.priority = VOLUME_PRIORITY;
            volume.weight = 1f;
            volume.sharedProfile = volumeProfile;

            return volumeObject;
        }

        private static void ConfigureProfile(VolumeProfile volumeProfile, ItemIconGeneratorSettings settings)
        {
            VisualEnvironment visualEnvironment = volumeProfile.Add<VisualEnvironment>(true);
            visualEnvironment.cloudType.Override(0);
            visualEnvironment.skyAmbientMode.Override(settings.SkyAmbientMode);
            visualEnvironment.renderingSpace.Override(settings.SkyRenderingSpace);

            if (settings.EnableSkyReflection || settings.EnableAtmosphericScattering)
            {
                visualEnvironment.skyType.Override(SkySettings.GetUniqueID<PhysicallyBasedSky>());

                PhysicallyBasedSky physicallyBasedSky = volumeProfile.Add<PhysicallyBasedSky>(true);
                physicallyBasedSky.active = true;
                physicallyBasedSky.type.Override(settings.PhysicallyBasedSkyModel);
                physicallyBasedSky.groundTint.Override(settings.PhysicallyBasedSkyGroundTint);
            }
            else
            {
                visualEnvironment.skyType.Override(0);
            }

            Fog fog = volumeProfile.Add<Fog>(true);
            fog.enabled.Override(settings.FogEnabled);
            fog.colorMode.Override(settings.FogColorMode);
            fog.maxFogDistance.Override(settings.MaxFogDistance);
            fog.meanFreePath.Override(settings.MeanFreePath);
            fog.enableVolumetricFog.Override(settings.EnableVolumetricFog);
            fog.anisotropy.Override(settings.FogAnisotropy);

            Exposure exposure = volumeProfile.Add<Exposure>(true);
            exposure.mode.Override(settings.ExposureMode);
            exposure.adaptationMode.Override(AdaptationMode.Fixed);
            exposure.fixedExposure.Override(settings.FixedExposure);
            exposure.compensation.Override(settings.ExposureCompensation);
            exposure.limitMin.Override(settings.ExposureLimitMin);
            exposure.limitMax.Override(settings.ExposureLimitMax);
            exposure.histogramPercentages.Override(settings.HistogramPercentages);

            ColorAdjustments colorAdjustments = volumeProfile.Add<ColorAdjustments>(true);
            colorAdjustments.postExposure.Override(settings.PostExposure);
            colorAdjustments.contrast.Override(settings.Contrast);
            colorAdjustments.colorFilter.Override(settings.ColorFilter);
            colorAdjustments.hueShift.Override(settings.HueShift);
            colorAdjustments.saturation.Override(settings.Saturation);

            Tonemapping tonemapping = volumeProfile.Add<Tonemapping>(true);
            tonemapping.mode.Override(settings.TonemappingMode);
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RaidIntoDarkness.Effects;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Rvm.CleanReleaseRoute07
{
    public static class AnomalyLight10Inventory
    {
        public static string Capture(IEnumerable<AnomalyVisual> visuals, Camera camera, bool rendered)
        {
            var records = new List<Record>();
            var asset = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
            foreach (var visual in visuals)
            {
                var light = Field(visual, "_idleIllumination") as Light;
                if (light == null)
                {
                    continue;
                }
                var hd = light.GetComponent<HDAdditionalLightData>();
                var animation = Field(visual, "_idleLightAnimation") as AnomalyLightAnimation;
                var properties = new List<Value>();
                if (hd != null)
                {
                    if (asset != null)
                    {
                        var shadows = asset.currentPlatformRenderPipelineSettings.hdShadowInitParams;
                        properties.Add(new Value { name = "resolvedPunctualShadowResolution", value = hd.shadowResolution.Value(shadows.shadowResolutionPunctual).ToString() });
                        properties.Add(new Value { name = "maxPunctualShadowMapResolution", value = shadows.maxPunctualShadowMapResolution.ToString() });
                        properties.Add(new Value { name = "punctualShadowAtlas", value = JsonUtility.ToJson(shadows.punctualLightShadowAtlas) });
                    }
                    foreach (string name in new[] { "volumetricDimmer", "volumetricFadeDistance", "fadeDistance", "affectDiffuse", "affectSpecular", "lightlayersMask", "linkShadowLayers", "shadowDimmer", "volumetricShadowDimmer", "shadowFadeDistance", "shadowUpdateMode", "affectsVolumetric" })
                    {
                        var property = typeof(HDAdditionalLightData).GetProperty(name);
                        if (property == null)
                        {
                            throw new InvalidOperationException("Missing installed HDRP API: " + name);
                        }
                        properties.Add(new Value { name = name, value = property.GetValue(hd)?.ToString() });
                    }
                }
                records.Add(new Record { owner = Hierarchy(visual.transform), path = Hierarchy(light.transform), type = light.type.ToString(), enabled = light.enabled, active = light.gameObject.activeInHierarchy, range = light.range, nominalRange = (float)Field(visual, "_idleLightRange"), jitter = (float)Field(visual, "_idleLightRangeJitter"), animationValid = animation != null && animation.IsValid, animation = animation != null ? JsonUtility.ToJson(animation) : null, intensity = light.intensity, lightUnit = light.lightUnit.ToString(), color = light.color, shadows = light.shadows.ToString(), shadowNearPlane = light.shadowNearPlane, shadowStrength = light.shadowStrength, cullingMask = light.cullingMask, renderingLayerMask = light.renderingLayerMask, hdProperties = properties.ToArray(), hdSerialized = hd != null ? JsonUtility.ToJson(hd) : null, shadowResolutionLevel = hd != null ? hd.shadowResolution.level : -1, shadowResolutionOverride = hd != null && hd.shadowResolution.useOverride, shadowResolutionOverrideValue = hd != null ? hd.shadowResolution.@override : 0 });
            }
            var frames = new List<Value>();
            if (rendered && camera != null)
            {
                var settings = HDCamera.GetOrCreate(camera).frameSettings;
                foreach (string name in new[] { "ShadowMaps", "ContactShadows", "Volumetrics", "ReprojectionForVolumetrics", "LightLayers" })
                {
                    var flag = (FrameSettingsField)Enum.Parse(typeof(FrameSettingsField), name);
                    frames.Add(new Value { name = name, value = settings.IsEnabled(flag).ToString() });
                }
                var fog = HDCamera.GetOrCreate(camera).volumeStack.GetComponent<Fog>();
                frames.Add(new Value { name = "Fog.enabled", value = fog.enabled.value.ToString() });
                frames.Add(new Value { name = "Fog.enableVolumetricFog", value = fog.enableVolumetricFog.value.ToString() });
            }
            return JsonUtility.ToJson(new Report { mode = AnomalyVisual08Target.Mode, time = Time.time, renderedCameraSettings = rendered, cameraPosition = camera != null ? camera.transform.position : Vector3.zero, cameraCullingMask = camera != null ? camera.cullingMask : 0, cameraSerialized = camera != null ? JsonUtility.ToJson(camera.GetComponent<HDAdditionalCameraData>()) : null, pipeline = asset != null ? JsonUtility.ToJson(asset.currentPlatformRenderPipelineSettings) : null, frameSettings = frames.ToArray(), lights = records.ToArray() }, true);
        }

        public static List<AnomalyVisual> Targets()
        {
            var output = new List<AnomalyVisual>();
            foreach (var target in AnomalyVisual08Target.Targets)
            {
                var visual = target != null ? target.GetComponent<AnomalyVisual>() : null;
                if (visual != null)
                {
                    output.Add(visual);
                }
            }
            return output;
        }

        public static object Field(object value, string name)
        {
            return typeof(AnomalyVisual).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
        }

        private static string Hierarchy(Transform value)
        {
            string path = value.name;
            while (value.parent != null)
            {
                value = value.parent;
                path = value.name + "/" + path;
            }
            return path;
        }

        [Serializable]
        private sealed class Report
        {
            public string mode;
            public float time;
            public bool renderedCameraSettings;
            public Vector3 cameraPosition;
            public int cameraCullingMask;
            public string cameraSerialized;
            public string pipeline;
            public Value[] frameSettings;
            public Record[] lights;
        }

        [Serializable]
        private sealed class Value
        {
            public string name;
            public string value;
        }

        [Serializable]
        private sealed class Record
        {
            public string owner;
            public string path;
            public string type;
            public bool enabled;
            public bool active;
            public float range;
            public float nominalRange;
            public float jitter;
            public bool animationValid;
            public string animation;
            public float intensity;
            public string lightUnit;
            public Color color;
            public string shadows;
            public float shadowNearPlane;
            public float shadowStrength;
            public int cullingMask;
            public int renderingLayerMask;
            public Value[] hdProperties;
            public string hdSerialized;
            public int shadowResolutionLevel;
            public bool shadowResolutionOverride;
            public int shadowResolutionOverrideValue;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using RaidIntoDarkness.Effects;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Rvm.CleanReleaseRoute07
{
    public static class AnomalyShadow11State
    {
        private const BindingFlags FLAGS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static string Capture(Camera camera)
        {
            object pipeline = RenderPipelineManager.currentPipeline;
            object processed = Get(pipeline, "m_ProcessedLightsBuilder");
            object gpu = Get(pipeline, "m_GpuLightsBuilder");
            Type databaseType = typeof(HDRenderPipeline).Assembly.GetType("UnityEngine.Rendering.HighDefinition.HDLightRenderDatabase", true);
            object database = databaseType.GetProperty("instance", FLAGS).GetValue(null);
            var targets = new Dictionary<int, AnomalyVisual>();
            var allLights = new List<LightState>();
            var idleLights = new HashSet<Light>();
            foreach (var visual in AnomalyLight10Inventory.Targets())
            {
                var light = (Light)AnomalyLight10Inventory.Field(visual, "_idleIllumination");
                var hd = light.GetComponent<HDAdditionalLightData>();
                object entity = Get(hd, "lightEntity");
                int dataIndex = (int)databaseType.GetMethod("GetEntityDataIndex", FLAGS).Invoke(database, new[] { entity });
                targets.Add(dataIndex, visual);
                idleLights.Add(light);
            }
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                allLights.Add(new LightState { path = Hierarchy(light.transform), targetIdle = idleLights.Contains(light), enabled = light.enabled, shadows = light.shadows.ToString(), intensity = light.intensity, cullingMask = light.cullingMask, renderingLayerMask = light.renderingLayerMask });
            }
            var records = new List<RenderedLight>();
            int count = (int)Get(processed, "sortedLightCounts");
            int directionals = (int)Get(processed, "sortedDirectionalLightCounts");
            object keys = Get(processed, "sortKeys");
            object entities = Get(processed, "processedEntities");
            object gpuLights = Get(gpu, "lights");
            for (int index = 0; index < count; index++)
            {
                uint key = (uint)Item(keys, index);
                int visibleIndex = (int)(key & 0xffff);
                object entity = Item(entities, visibleIndex);
                int dataIndex = (int)Get(entity, "dataIndex");
                if (!targets.TryGetValue(dataIndex, out var visual))
                {
                    continue;
                }
                bool offscreen = (key & (1u << 30)) != 0;
                object lightData = !offscreen && index >= directionals ? Item(gpuLights, index - directionals) : null;
                records.Add(new RenderedLight { owner = Hierarchy(visual.transform), visibleIndex = visibleIndex, dataIndex = dataIndex, offscreen = offscreen, shadowMapFlags = Convert.ToInt32(Get(entity, "shadowMapFlags")), gpuRecordAvailable = lightData != null, shadowIndex = lightData != null ? (int)Get(lightData, "shadowIndex") : -2, contactShadowMask = lightData != null ? (int)Get(lightData, "contactShadowMask") : 0, lightColor = lightData != null ? (Vector3)Get(lightData, "color") : Vector3.zero });
            }
            var contact = HDCamera.GetOrCreate(camera).volumeStack.GetComponent<ContactShadows>();
            return JsonUtility.ToJson(new Report { frame = Time.frameCount, camera = camera.name, contactPassEnabled = (bool)Get(pipeline, "m_EnableContactShadow"), contactMaskCount = (int)Get(gpu, "contactShadowIndex"), contactVolumeEnabled = contact.enable.value, contactLength = contact.length.value, directionalOnly = contact.directionalOnly.value, sortedLightCount = count, targetedRenderedRecords = records.ToArray(), allLights = allLights.ToArray() }, true);
        }

        private static object Get(object value, string name)
        {
            var field = value.GetType().GetField(name, FLAGS);
            if (field != null)
            {
                return field.GetValue(value);
            }
            var property = value.GetType().GetProperty(name, FLAGS);
            if (property == null)
            {
                throw new InvalidOperationException("Installed HDRP state member missing: " + name);
            }
            return property.GetValue(value);
        }

        private static object Item(object array, int index)
        {
            return array.GetType().GetProperty("Item", FLAGS).GetValue(array, new object[] { index });
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
            public int frame;
            public string camera;
            public bool contactPassEnabled;
            public int contactMaskCount;
            public bool contactVolumeEnabled;
            public float contactLength;
            public bool directionalOnly;
            public int sortedLightCount;
            public RenderedLight[] targetedRenderedRecords;
            public LightState[] allLights;
        }

        [Serializable]
        private sealed class RenderedLight
        {
            public string owner;
            public int visibleIndex;
            public int dataIndex;
            public bool offscreen;
            public int shadowMapFlags;
            public bool gpuRecordAvailable;
            public int shadowIndex;
            public int contactShadowMask;
            public Vector3 lightColor;
        }

        [Serializable]
        private sealed class LightState
        {
            public string path;
            public bool targetIdle;
            public bool enabled;
            public string shadows;
            public float intensity;
            public int cullingMask;
            public int renderingLayerMask;
        }
    }
}

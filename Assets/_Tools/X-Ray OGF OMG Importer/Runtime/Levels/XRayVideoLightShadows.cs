using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Levels
{
    public static class XRayVideoLightShadows
    {
        private const int PRUNE_INTERVAL = 64;

        private static readonly Dictionary<Light, LightSettings> _lights = new();
        private static readonly List<Light> _staleLights = new();
        private static int? _quality;
        private static bool? _contactShadows;
        private static int _addedSincePrune;

        private static bool IsActive => _quality.HasValue || _contactShadows.HasValue;

        public static void Apply(int? quality, bool? contactShadows)
        {
            if (_quality == quality && _contactShadows == contactShadows) return;
            bool wasActive = IsActive;
            _quality = quality;
            _contactShadows = contactShadows;
            if (wasActive != IsActive)
            {
                if (IsActive) SceneManager.sceneUnloaded += OnSceneUnloaded;
                else SceneManager.sceneUnloaded -= OnSceneUnloaded;
            }

            PruneLights();
            foreach (LightSettings settings in _lights.Values)
            {
                settings.Apply(quality, contactShadows);
            }
            if (!IsActive)
            {
                _lights.Clear();
                return;
            }

            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (!_lights.ContainsKey(light)) Register(light);
            }
        }

        internal static void Register(Light light)
        {
            if (!IsActive || light == null || !light.gameObject.scene.IsValid()) return;
            if (!_lights.TryGetValue(light, out LightSettings settings) || !settings.IsAlive)
            {
                if (!light.TryGetComponent(out HDAdditionalLightData data)) return;
                settings = new LightSettings(light, data);
                _lights[light] = settings;
                if (++_addedSincePrune >= PRUNE_INTERVAL) PruneLights();
            }
            settings.Apply(_quality, _contactShadows);
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            PruneLights();
        }

        private static void PruneLights()
        {
            foreach (KeyValuePair<Light, LightSettings> entry in _lights)
            {
                if (entry.Key == null || !entry.Value.IsAlive) _staleLights.Add(entry.Key);
            }
            foreach (Light light in _staleLights)
            {
                _lights.Remove(light);
            }
            _staleLights.Clear();
            _addedSincePrune = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Apply(null, null);
        }

        private sealed class LightSettings
        {
            private readonly Light _light;
            private readonly HDAdditionalLightData _data;
            private (bool UseOverride, int Level)? _originalResolution;
            private (bool UseOverride, bool Value)? _originalContactShadows;
            private bool _enabledShadowMap;

            public bool IsAlive => _data != null;

            public LightSettings(Light light, HDAdditionalLightData data)
            {
                _light = light;
                _data = data;
            }

            public void Apply(int? quality, bool? contactShadows)
            {
                if (quality.HasValue)
                {
                    EnableLocalShadowMap();
                    _originalResolution ??= (_data.shadowResolution.useOverride, _data.shadowResolution.level);
                    _data.SetShadowResolutionLevel(quality.Value);
                    _data.SetShadowResolutionOverride(false);
                }
                else if (_originalResolution.HasValue)
                {
                    if (_enabledShadowMap && _light.shadows == LightShadows.Soft) _data.EnableShadows(false);
                    _enabledShadowMap = false;
                    _data.SetShadowResolutionLevel(_originalResolution.Value.Level);
                    _data.SetShadowResolutionOverride(_originalResolution.Value.UseOverride);
                    _originalResolution = null;
                }

                if (contactShadows.HasValue)
                {
                    _originalContactShadows ??= (_data.useContactShadow.useOverride, _data.useContactShadow.@override);
                    _data.useContactShadow.@override = contactShadows.Value;
                    _data.useContactShadow.useOverride = true;
                }
                else if (_originalContactShadows.HasValue)
                {
                    _data.useContactShadow.@override = _originalContactShadows.Value.Value;
                    _data.useContactShadow.useOverride = _originalContactShadows.Value.UseOverride;
                    _originalContactShadows = null;
                }
            }

            private void EnableLocalShadowMap()
            {
                if (_light.shadows != LightShadows.None || _light.bakingOutput.lightmapBakeType == LightmapBakeType.Baked) return;
                LightType type = _light.type;
                if (type != LightType.Point && !type.IsSpot() && type != LightType.Rectangle) return;
                _data.EnableShadows(true);
                _enabledShadowMap = true;
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Levels
{
    public static class XRayVideoLightShadows
    {
        private const int PRUNE_INTERVAL = 64;
        private const float SELECTED_LIGHT_SCORE_SCALE = 0.8f;

        private static readonly Dictionary<Light, LightSettings> _lights = new();
        private static readonly List<Light> _staleLights = new();
        private static readonly List<BudgetView> _budgetViews = new();
        private static readonly List<LightSettings> _budgetCasters = new();
        private static readonly List<LightSettings> _suppressedLights = new();
        private static readonly List<(LightSettings Light, float Score)> _rankedLights = new();
        private static readonly HashSet<LightSettings> _budgetSelection = new();
        private static int? _quality;
        private static bool? _contactShadows;
        private static int? _shadowBudget;
        private static int _addedSincePrune;
        private static int _contextDepth;
        private static int _contextFrame;
        private static bool _budgetInUse;

        private static bool IsActive => _quality.HasValue || _contactShadows.HasValue || _shadowBudget.HasValue;

        public static void Apply(int? quality, bool? contactShadows, int? punctualShadowBudget = null)
        {
            int? budget = punctualShadowBudget >= 0 ? punctualShadowBudget : null;
            if (_quality == quality && _contactShadows == contactShadows && _shadowBudget == budget) return;
            bool wasActive = IsActive;
            bool wasLimited = _shadowBudget.HasValue;
            _quality = quality;
            _contactShadows = contactShadows;
            _shadowBudget = budget;
            RestoreBudgetLights();
            _budgetSelection.Clear();
            if (wasActive != IsActive)
            {
                if (IsActive) SceneManager.sceneUnloaded += OnSceneUnloaded;
                else SceneManager.sceneUnloaded -= OnSceneUnloaded;
            }
            if (wasLimited != budget.HasValue)
            {
                if (budget.HasValue)
                {
                    RenderPipelineManager.beginContextRendering += BeginBudgetContext;
                    RenderPipelineManager.endContextRendering += EndBudgetContext;
                }
                else
                {
                    RenderPipelineManager.beginContextRendering -= BeginBudgetContext;
                    RenderPipelineManager.endContextRendering -= EndBudgetContext;
                    _budgetViews.Clear();
                    _contextDepth = 0;
                }
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
                _budgetSelection.Remove(settings);
                settings = new LightSettings(light, data);
                _lights[light] = settings;
                if (++_addedSincePrune >= PRUNE_INTERVAL) PruneLights();
            }
            settings.Apply(_quality, _contactShadows);
            if (_budgetInUse && !_budgetSelection.Contains(settings) && settings.CanBudgetShadow)
            {
                if (_budgetSelection.Count < _shadowBudget.GetValueOrDefault()) _budgetSelection.Add(settings);
                else Suppress(settings);
            }
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            PruneLights();
        }

        private static void BeginBudgetContext(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (_contextFrame != Time.frameCount)
            {
                RestoreBudgetLights();
                _contextDepth = 0;
                _contextFrame = Time.frameCount;
            }
            if (_contextDepth++ != 0) return;
            int budget = _shadowBudget.GetValueOrDefault();

            int viewCount = 0;
            foreach (Camera camera in cameras)
            {
                if (camera == null || camera.cameraType != CameraType.Game || camera.targetTexture != null) continue;
                if (_budgetViews.Count == viewCount) _budgetViews.Add(new BudgetView());
                _budgetViews[viewCount++].Prepare(camera, budget > 0);
            }
            if (viewCount == 0) return;

            foreach (LightSettings settings in _lights.Values)
            {
                if (!settings.CanBudgetShadow) continue;
                _budgetCasters.Add(settings);
                if (budget == 0) continue;
                float score = settings.ShadowScore(_budgetViews, viewCount);
                if (_budgetSelection.Contains(settings)) score *= SELECTED_LIGHT_SCORE_SCALE;
                Rank(settings, score, budget);
            }

            _budgetSelection.Clear();
            foreach (var candidate in _rankedLights)
            {
                _budgetSelection.Add(candidate.Light);
            }
            _rankedLights.Clear();
            foreach (LightSettings settings in _budgetCasters)
            {
                if (!_budgetSelection.Contains(settings)) Suppress(settings);
            }
            _budgetCasters.Clear();
            _budgetInUse = true;
        }

        private static void EndBudgetContext(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (_contextDepth > 0 && --_contextDepth == 0) RestoreBudgetLights();
        }

        private static void Rank(LightSettings settings, float score, int budget)
        {
            if (!float.IsFinite(score) || _rankedLights.Count == budget && score >= _rankedLights[budget - 1].Score) return;
            int index = _rankedLights.Count;
            while (index > 0 && score < _rankedLights[index - 1].Score) index--;
            if (_rankedLights.Count == budget) _rankedLights.RemoveAt(budget - 1);
            _rankedLights.Insert(index, (settings, score));
        }

        private static void Suppress(LightSettings settings)
        {
            settings.SuppressShadow();
            _suppressedLights.Add(settings);
        }

        private static void RestoreBudgetLights()
        {
            foreach (LightSettings settings in _suppressedLights)
            {
                settings.RestoreShadow();
            }
            _suppressedLights.Clear();
            _budgetInUse = false;
        }

        private static void PruneLights()
        {
            foreach (KeyValuePair<Light, LightSettings> entry in _lights)
            {
                if (entry.Key == null || !entry.Value.IsAlive) _staleLights.Add(entry.Key);
            }
            foreach (Light light in _staleLights)
            {
                _budgetSelection.Remove(_lights[light]);
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

        private sealed class BudgetView
        {
            private readonly Plane[] _planes = new Plane[6];

            private Vector3 _position;
            private int _cullingMask;

            public void Prepare(Camera camera, bool calculateFrustum)
            {
                _position = camera.transform.position;
                _cullingMask = camera.cullingMask;
                if (calculateFrustum) GeometryUtility.CalculateFrustumPlanes(camera, _planes);
            }

            public float Score(Bounds bounds, float fadeSquared, int lightMask)
            {
                if ((_cullingMask & lightMask) == 0) return float.PositiveInfinity;
                float distanceSquared = (bounds.center - _position).sqrMagnitude;
                if (distanceSquared >= fadeSquared) return float.PositiveInfinity;
                return GeometryUtility.TestPlanesAABB(_planes, bounds) ? distanceSquared : distanceSquared * 4f + 1f;
            }
        }

        private sealed class LightSettings
        {
            private readonly Light _light;
            private readonly HDAdditionalLightData _data;
            private (bool UseOverride, int Level)? _originalResolution;
            private (bool UseOverride, bool Value)? _originalContactShadows;
            private bool _enabledShadowMap;
            private float _originalShadowDimmer;

            public bool IsAlive => _data != null;
            public bool CanBudgetShadow => IsAlive && _light != null && _light.isActiveAndEnabled && _light.shadows != LightShadows.None && _light.bakingOutput.lightmapBakeType != LightmapBakeType.Baked && (_light.type == LightType.Point || _light.type.IsSpot()) && _data.shadowDimmer > 0f;

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

            public float ShadowScore(List<BudgetView> views, int viewCount)
            {
                if (_light.intensity <= 0f || _data.lightDimmer <= 0f && _data.volumetricDimmer <= 0f) return float.PositiveInfinity;
                float distance = Mathf.Min(_data.shadowFadeDistance, _data.fadeDistance);
                float fadeSquared = distance * distance;
                var bounds = new Bounds(_light.transform.position, Vector3.one * (2f * _light.range));
                int lightMask = _light.cullingMask;
                float score = float.PositiveInfinity;
                for (int i = 0; i < viewCount; i++) score = Mathf.Min(score, views[i].Score(bounds, fadeSquared, lightMask));
                return score;
            }

            public void SuppressShadow()
            {
                _originalShadowDimmer = _data.shadowDimmer;
                _data.shadowDimmer = 0f;
            }

            public void RestoreShadow()
            {
                if (IsAlive && _data.shadowDimmer == 0f) _data.shadowDimmer = _originalShadowDimmer;
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

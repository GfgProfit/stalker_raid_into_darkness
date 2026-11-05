using System;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Levels
{
    public sealed class XRayPrecipitationSystem : IDisposable
    {
        private static readonly ProfilerMarker _wetnessTickMarker = new ProfilerMarker("XRay.Precipitation.WetnessTick");

        private readonly GameObject _owner;

        private PrecipitationConfig _settings;
        private CustomPassVolume _volume;
        private XRayPrecipitationPass _pass;
        private XRayWetnessField _wetness;
        private XRayWetMaterialBindings _wetMaterials;
        private int _revision = -1;
        private int _materialGeometryCapture = -1;
        private float _materialRefreshElapsed;

        public bool Enabled { get; private set; }
        public float RainDensity { get; private set; }
        public float RealTime { get; private set; }
        public string Diagnostic { get; private set; }
        public XRayPrecipitationGeometry Geometry { get; }
        public XRayPrecipitationPass Pass => _pass;
        public XRayWetnessField Wetness => _wetness;
        public XRayWetMaterialBindings WetMaterials => _wetMaterials;
        public long EstimatedBytes => Geometry.EstimatedBytes + (_pass != null ? _pass.EstimatedBytes : 0L) + (_wetness != null ? _wetness.EstimatedBytes : 0L) + (_wetMaterials != null ? _wetMaterials.EstimatedBytes : 0L);

        public XRayPrecipitationSystem(GameObject owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            _owner = owner;
            Geometry = new XRayPrecipitationGeometry(owner.scene);
        }

        public void Publish(PrecipitationConfig config, XRayWeatherSnapshot weather, float realDelta, float radianceScale, bool enabled, bool rainVisible)
        {
            if (!float.IsFinite(realDelta) || realDelta < 0.0f) throw new ArgumentOutOfRangeException(nameof(realDelta));
            if (config == null)
            {
                Diagnostic = "Connect the shared PrecipitationConfig through GameConfig before enabling precipitation.";
                Suspend();
                return;
            }
            if (!enabled)
            {
                Suspend();
                return;
            }
            bool changed = _settings != config || _revision != config.Revision;
            if (changed && !config.TryValidate(out string diagnostic, true))
            {
                Diagnostic = diagnostic;
                Suspend();
                return;
            }
            _settings = config;
            _revision = config.Revision;
            if (!SystemInfo.supportsComputeShaders)
            {
                Diagnostic = "Precipitation requires compute shader support.";
                Suspend();
                return;
            }
            Diagnostic = null;
            Enabled = true;
            RainDensity = Mathf.Clamp01(weather.RainDensity * config.IntensityMultiplier);
            RealTime += realDelta;
            Geometry.Refresh(config, realDelta, changed);
            PublishWetness(config, realDelta, changed);
            EnsurePass();
            float noise = Mathf.PerlinNoise(RealTime * 0.3f, 0.5f) * 2.0f - 1.0f;
            Vector3 direction = XRayPrecipitationMath.FallDirection(weather.WindDirectionRadians, weather.WindVelocity, noise, config.WindResponse);
            float speed = XRayPrecipitationMath.SpeedFactor(weather.WindVelocity, config.DropSpeed);
            Color tint = weather.RainColor.linear;
            Color ambient = weather.AmbientColor.linear + weather.HemisphereColor.linear * 0.205f;
            Color color = tint * ambient * Mathf.Max(0.0f, radianceScale);
            color.a = 1.0f;
            _pass.Configure(_owner, config, Geometry, color, direction, speed, weather.WindVelocity, RainDensity, RealTime, realDelta, rainVisible);
        }

        public void ResetRain()
        {
            if (_pass != null) _pass.ResetRain();
        }

        public void Suspend()
        {
            Enabled = false;
            RainDensity = 0.0f;
            if (_wetness != null) _wetness.Unpublish();
            if (_wetMaterials != null) _wetMaterials.Dispose();
            _wetMaterials = null;
            _materialGeometryCapture = -1;
            if (_pass != null) _pass.Release();
            if (_volume != null) _volume.enabled = false;
        }

        public void Dispose()
        {
            Suspend();
            if (_volume != null) CoreUtils.Destroy(_volume.gameObject);
            _volume = null;
            _pass = null;
            if (_wetness != null) _wetness.Dispose();
            _wetness = null;
            Geometry.Dispose();
        }

        private void PublishWetness(PrecipitationConfig config, float realDelta, bool changed)
        {
            if (!config.WetnessEnabled && !config.PuddlesEnabled)
            {
                if (_wetness != null) _wetness.Unpublish();
                if (_wetMaterials != null) _wetMaterials.Dispose();
                _wetMaterials = null;
                _materialGeometryCapture = -1;
                return;
            }
            if (_wetness == null) _wetness = new XRayWetnessField(_owner);
            using (_wetnessTickMarker.Auto())
            {
                _wetness.Tick(config, Geometry.Heights, Geometry.WorldMapping, RainDensity, realDelta);
            }
            _materialRefreshElapsed += realDelta;
            if (_wetMaterials == null || changed || _materialGeometryCapture != Geometry.CaptureCount || _materialRefreshElapsed >= config.GeometryRefreshSeconds)
            {
                if (_wetMaterials == null) _wetMaterials = new XRayWetMaterialBindings();
                _wetMaterials.Bind(_owner.scene, config);
                if (changed) _wetMaterials.Refresh(config);
                _materialGeometryCapture = Geometry.CaptureCount;
                _materialRefreshElapsed = 0.0f;
            }
            _wetness.Publish(config, RealTime, RainDensity);
        }

        private void EnsurePass()
        {
            if (_volume == null)
            {
                var root = new GameObject("X-Ray Precipitation");
                root.hideFlags = HideFlags.HideAndDontSave;
                SceneManager.MoveGameObjectToScene(root, _owner.scene);
                root.transform.SetParent(_owner.transform, false);
                root.layer = XRayWeatherFogRenderer.GetVolumeLayer(_owner);
                _volume = root.AddComponent<CustomPassVolume>();
                _volume.isGlobal = true;
                _volume.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
                _pass = new XRayPrecipitationPass();
                _volume.customPasses.Add(_pass);
            }
            _volume.enabled = true;
        }
    }
}

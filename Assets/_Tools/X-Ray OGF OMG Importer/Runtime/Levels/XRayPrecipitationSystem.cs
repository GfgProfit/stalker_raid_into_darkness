using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Levels
{
    public sealed class XRayPrecipitationSystem : IDisposable
    {
        private readonly GameObject _owner;

        private PrecipitationConfig _settings;
        private CustomPassVolume _volume;
        private XRayPrecipitationPass _pass;
        private int _revision = -1;

        public bool Enabled { get; private set; }
        public float RainDensity { get; private set; }
        public float RealTime { get; private set; }
        public string Diagnostic { get; private set; }
        public XRayPrecipitationGeometry Geometry { get; }
        public XRayPrecipitationPass Pass => _pass;
        public long EstimatedBytes => Geometry.EstimatedBytes + (_pass != null ? _pass.EstimatedBytes : 0L);

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
            if (_pass != null) _pass.Release();
            if (_volume != null) _volume.enabled = false;
        }

        public void Dispose()
        {
            Suspend();
            if (_volume != null) CoreUtils.Destroy(_volume.gameObject);
            _volume = null;
            _pass = null;
            Geometry.Dispose();
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

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace XRay.Importer.Levels
{
    public sealed class XRayWeatherCelestialRenderer
    {
        private readonly GameObject _owner;
        private readonly XRayWeatherCelestialSettings _settings;
        private readonly XRayWeatherFlareElement[] _daylightGhosts;

        private XRayWeatherFlareDefinition _targetDefinition;
        private XRayWeatherFlareDefinition _styledSource;
        private XRayWeatherFlareDefinition _styledDefinition;
        private string _targetName;
        private XRayWeatherFlareFade _fade;
        private CustomPassVolume _volume;
        private XRayWeatherCelestialPass _pass;

        public CustomPassVolume Volume => _volume;
        public float ShaftsIntensity { get; private set; }
        public XRayWeatherFlareDefinition FlareDefinition => _fade.Definition;
        public float FlareFade => _fade.Weight;

        public XRayWeatherCelestialRenderer(GameObject owner, XRayWeatherCelestialSettings settings)
        {
            _owner = owner;
            _settings = settings;
            XRayWeatherFlareDefinition template = settings.FindDefinition("clear_morning");
            if (template == null) return;
            var ghosts = new List<XRayWeatherFlareElement>(template.Elements.Count);
            foreach (XRayWeatherFlareElement element in template.Elements)
            {
                if (element.Position != 1.0f) ghosts.Add(element);
            }
            _daylightGhosts = ghosts.ToArray();
        }

        public void Publish(XRayWeatherSnapshot snapshot, bool shafts, bool flare, float gameDelta, bool snap, bool expressiveDaylight = false)
        {
            ShaftsIntensity = shafts ? ResolveShaftsIntensity(snapshot.SunShaftsIntensity, _settings.ShaftsMin, _settings.ShaftsValue) : 0.0f;
            if (!string.Equals(_targetName, snapshot.SunDefinition, StringComparison.Ordinal))
            {
                _targetName = snapshot.SunDefinition;
                _targetDefinition = _settings.FindDefinition(_targetName);
            }
            _fade.Step(flare ? _targetDefinition : null, gameDelta, snap || !flare);
            bool visible = snapshot.SunColor.r > 0.0f || snapshot.SunColor.g > 0.0f || snapshot.SunColor.b > 0.0f;
            if (!visible || _fade.Definition == null)
            {
                if (_pass != null) _pass.ClearFrame();
                return;
            }
            if (_volume == null) CreateVolume();
            bool expressive = expressiveDaylight && HasDaylightSource(_targetDefinition);
            XRayWeatherFlareDefinition definition = expressive ? StyleDefinition(_fade.Definition) : _fade.Definition;
            _pass.SetFrame(_owner, -snapshot.SunDirection, 0.0f, flare, snapshot.SunColor, definition, _fade.Weight, expressive);
        }

        private XRayWeatherFlareDefinition StyleDefinition(XRayWeatherFlareDefinition definition)
        {
            if (!HasDaylightSource(definition) || _daylightGhosts == null || _daylightGhosts.Length == 0) return definition;
            if (_styledSource == definition) return _styledDefinition;
            _styledSource = definition;
            _styledDefinition = new XRayWeatherFlareDefinition(definition.Name, definition.Source, definition.Gradient, _daylightGhosts, definition.RiseTime, definition.DownTime);
            return _styledDefinition;
        }

        private static bool HasDaylightSource(XRayWeatherFlareDefinition definition)
        {
            return definition != null && definition.Source.Texture != null && !definition.Name.StartsWith("moon", StringComparison.OrdinalIgnoreCase);
        }

        public void Release()
        {
            if (_pass != null) _pass.ClearFrame();
            if (_volume != null)
            {
                _volume.enabled = false;
                CoreUtils.Destroy(_volume.gameObject);
            }
            _pass = null;
            _volume = null;
            _fade = default;
            ShaftsIntensity = 0.0f;
        }

        public static float ResolveShaftsIntensity(float raw, float minimum, float value)
        {
            if (!float.IsFinite(raw) || !float.IsFinite(minimum) || !float.IsFinite(value)) return 0.0f;
            return Mathf.Clamp01((float)(((double)raw * (1.0 - minimum) + minimum) * value));
        }

        private void CreateVolume()
        {
            var child = new GameObject("Source Weather Celestial (Runtime)");
            child.hideFlags = HideFlags.HideAndDontSave;
            child.layer = XRayWeatherFogRenderer.GetVolumeLayer(_owner);
            child.transform.SetParent(_owner.transform, false);
            _volume = child.AddComponent<CustomPassVolume>();
            _volume.isGlobal = true;
            _volume.priority = 1.0f;
            _volume.injectionPoint = CustomPassInjectionPoint.AfterPostProcess;
            _pass = new XRayWeatherCelestialPass();
            _pass.Configure(_settings.Shader);
            _volume.customPasses.Add(_pass);
        }
    }

    public struct XRayWeatherFlareFade
    {
        private FadePhase _phase;

        public XRayWeatherFlareDefinition Definition { get; private set; }
        public float Weight { get; private set; }

        public void Step(XRayWeatherFlareDefinition target, float gameDelta, bool snap)
        {
            if (snap)
            {
                Definition = target;
                Weight = target == null ? 0.0f : 1.0f;
                _phase = FadePhase.Idle;
                return;
            }
            if (!float.IsFinite(gameDelta) || gameDelta < 0.0f) return;
            switch (_phase)
            {
                case FadePhase.None:
                    Definition = target;
                    _phase = FadePhase.Show;
                    break;
                case FadePhase.Idle:
                    if (Definition != target) _phase = FadePhase.Hide;
                    break;
                case FadePhase.Show:
                    Weight = Definition == null ? 1.0f : Mathf.MoveTowards(Weight, 1.0f, Fraction(gameDelta, Definition.RiseTime));
                    if (Weight >= 1.0f) _phase = FadePhase.Idle;
                    break;
                case FadePhase.Hide:
                    Weight = Definition == null ? 0.0f : Mathf.MoveTowards(Weight, 0.0f, Fraction(gameDelta, Definition.DownTime));
                    if (Weight > 0.0f) break;
                    Definition = target;
                    Weight = target == null ? 0.0f : Mathf.Clamp01(Fraction(gameDelta, target.RiseTime));
                    _phase = FadePhase.Show;
                    break;
            }
        }

        private static float Fraction(float elapsed, float duration)
        {
            return duration > 0.0f ? elapsed / duration : 1.0f;
        }

        private enum FadePhase
        {
            None,
            Idle,
            Show,
            Hide
        }
    }
}

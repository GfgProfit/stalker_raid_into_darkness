using System;
using System.Collections.Generic;
using UnityEngine;

namespace XRay.Importer.Levels
{
    public sealed class XRayWeatherFxCatalog : ScriptableObject
    {
        private const double DAY_START = 5.0 * 3600.0;
        private const double NIGHT_START = 21.0 * 3600.0;

        [SerializeField] private XRayWeatherCatalog _ordinaryCatalog;
        [SerializeField] private XRayWeatherFxProfile[] _effects = Array.Empty<XRayWeatherFxProfile>();

        public XRayWeatherCatalog OrdinaryCatalog => _ordinaryCatalog;
        public IReadOnlyList<XRayWeatherFxProfile> Effects => _effects;

        public void Initialize(XRayWeatherCatalog ordinary, XRayWeatherFxProfile[] effects)
        {
            if (ordinary == null || effects == null || effects.Length == 0) throw new ArgumentException("A weather effect catalog requires an ordinary catalog and imported effects.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (XRayWeatherFxProfile effect in effects)
            {
                if (effect == null || !effect.Validate(out _) || !ordinary.IsCompatible(effect.SourceProfile) || !names.Add(effect.EffectName)) throw new ArgumentException("Weather effects must be unique and share the ordinary catalog's source, settings and sun table.", nameof(effects));
            }
            _ordinaryCatalog = ordinary;
            _effects = (XRayWeatherFxProfile[])effects.Clone();
        }

        public bool Contains(XRayWeatherFxProfile effect)
        {
            return effect != null && Array.IndexOf(_effects, effect) >= 0;
        }

        public bool CanAttachTo(XRayWeatherProfile profile)
        {
            return _ordinaryCatalog != null && _ordinaryCatalog.CanAttachTo(profile);
        }

        public XRayWeatherFxProfile FindVariant(XRayWeatherFxKind kind, double worldSeconds)
        {
            if (double.IsNaN(worldSeconds) || double.IsInfinity(worldSeconds)) return null;
            double seconds = XRayWeatherProfile.NormalizeTime(worldSeconds);
            string suffix = seconds >= DAY_START && seconds < NIGHT_START ? "_day" : "_night";
            foreach (XRayWeatherFxProfile effect in _effects)
            {
                if (effect != null && effect.Kind == kind && (kind == XRayWeatherFxKind.Scenario || effect.EffectName.EndsWith(suffix, StringComparison.Ordinal))) return effect;
            }
            return null;
        }
    }
}

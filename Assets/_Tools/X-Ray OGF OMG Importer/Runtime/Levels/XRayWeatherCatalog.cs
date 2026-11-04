using System;
using System.Collections.Generic;
using UnityEngine;

namespace XRay.Importer.Levels
{
    public sealed class XRayWeatherCatalog : ScriptableObject
    {
        [SerializeField] private XRayWeatherProfile[] _profiles = Array.Empty<XRayWeatherProfile>();

        public IReadOnlyList<XRayWeatherProfile> Profiles => _profiles;

        public bool Contains(XRayWeatherProfile profile)
        {
            return profile != null && Array.IndexOf(_profiles, profile) >= 0;
        }

        public bool IsCompatible(XRayWeatherProfile profile)
        {
            if (profile == null || !profile.Validate(out _) || _profiles.Length == 0) return false;
            XRayWeatherProfile first = _profiles[0];
            return first != null && first.SourceIdentity == profile.SourceIdentity && first.HasSameSunPositions(profile) && first.HasSameSourceSettings(profile);
        }

        public bool CanAttachTo(XRayWeatherProfile profile)
        {
            if (IsCompatible(profile)) return true;
            if (profile == null || !profile.Validate(out _) || !(profile.GetKeyframe(0).SkyResources.Texture(0) is Cubemap)) return false;
            foreach (XRayWeatherProfile current in _profiles)
            {
                if (current == null || current.CycleName != profile.CycleName || current.KeyframeCount != profile.KeyframeCount || !current.HasSameSunPositions(profile) || !current.HasSameSourceSettings(profile)) continue;
                for (int key = 0; key < profile.KeyframeCount; key++)
                {
                    if (!current.GetKeyframe(key).Equals(profile.GetKeyframe(key))) return false;
                }
                return true;
            }
            return false;
        }

        public void Initialize(XRayWeatherProfile[] profiles)
        {
            if (profiles == null || profiles.Length == 0 || profiles[0] == null) throw new ArgumentException("A weather catalog requires imported profiles.", nameof(profiles));
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            XRayWeatherProfile first = profiles[0];
            foreach (XRayWeatherProfile profile in profiles)
            {
                if (profile == null || !profile.Validate(out _) || !names.Add(profile.CycleName) || profile.SourceIdentity != first.SourceIdentity || !first.HasSameSunPositions(profile) || !first.HasSameSourceSettings(profile)) throw new ArgumentException("Weather catalog profiles must be unique and share the same source, settings and sun position table.", nameof(profiles));
            }
            _profiles = (XRayWeatherProfile[])profiles.Clone();
        }
    }
}

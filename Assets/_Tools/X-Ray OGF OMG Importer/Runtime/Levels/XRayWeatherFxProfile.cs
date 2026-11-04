using System;
using UnityEngine;

namespace XRay.Importer.Levels
{
    public enum XRayWeatherFxKind
    {
        Blowout,
        SuperBlowout,
        PsiStorm,
        Scenario
    }

    public sealed class XRayWeatherFxProfile : ScriptableObject
    {
        private const int CURRENT_SCHEMA = 1;

        [SerializeField] private int _schemaVersion;
        [SerializeField] private string _effectName;
        [SerializeField] private XRayWeatherFxKind _kind;
        [SerializeField] private XRayWeatherProfile _sourceProfile;
        [SerializeField, TextArea] private string _provenance;
        [SerializeField] private XRayWeatherSnapshot[] _keyframes = Array.Empty<XRayWeatherSnapshot>();

        public string EffectName => _effectName;
        public XRayWeatherFxKind Kind => _kind;
        public XRayWeatherProfile SourceProfile => _sourceProfile;
        public string SourceIdentity => _sourceProfile == null ? null : _sourceProfile.SourceIdentity;
        public string Provenance => _provenance;
        public int KeyframeCount => _keyframes?.Length ?? 0;
        public double AuthoredDurationSeconds => KeyframeCount == 0 ? 0.0 : _keyframes[KeyframeCount - 1].GameSeconds;

        public void Initialize(string effectName, XRayWeatherFxKind kind, XRayWeatherProfile sourceProfile, string provenance, XRayWeatherSnapshot[] keyframes)
        {
            _schemaVersion = CURRENT_SCHEMA;
            _effectName = effectName;
            _kind = kind;
            _sourceProfile = sourceProfile;
            _provenance = provenance;
            _keyframes = keyframes == null ? Array.Empty<XRayWeatherSnapshot>() : (XRayWeatherSnapshot[])keyframes.Clone();
            if (!Validate(out string diagnostic)) throw new ArgumentException(diagnostic);
        }

        public XRayWeatherSnapshot GetKeyframe(int index)
        {
            return _keyframes[index];
        }

        public bool Validate(out string diagnostic)
        {
            diagnostic = null;
            if (_schemaVersion != CURRENT_SCHEMA || string.IsNullOrWhiteSpace(_effectName) || !Enum.IsDefined(typeof(XRayWeatherFxKind), _kind)) diagnostic = "Unknown weather effect schema, name or kind.";
            else if (_sourceProfile == null || !_sourceProfile.Validate(out _)) diagnostic = "A weather effect requires a compatible ordinary weather source profile.";
            else if (KeyframeCount < 2 || _keyframes[0].GameSeconds != 0.0) diagnostic = "A weather effect requires an authored zero slot and at least one positive relative key.";
            if (diagnostic != null) return false;
            for (int i = 0; i < _keyframes.Length; i++)
            {
                XRayWeatherSnapshot frame = _keyframes[i];
                if (XRayWeatherProfile.IsValidDescriptor(frame, _sourceProfile.VisibilityScale) && (i == 0 || frame.GameSeconds > _keyframes[i - 1].GameSeconds)) continue;
                diagnostic = "Invalid or unsorted weather effect descriptor at index " + i + ".";
                return false;
            }
            return true;
        }
    }
}

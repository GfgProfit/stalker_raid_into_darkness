using System;
using UnityEngine;

namespace XRay.Importer.Levels
{
    [CreateAssetMenu(menuName = "X-Ray/Weather Profile")]
    public sealed class XRayWeatherProfile : ScriptableObject
    {
        public const int CURRENT_SCHEMA = 1;
        public const double DAY_SECONDS = 86400.0;

        [SerializeField] private int _schemaVersion;
        [SerializeField] private string _sourceTarget;
        [SerializeField] private string _cycleName;
        [SerializeField] private string _sourceIdentity;
        [SerializeField, TextArea] private string _provenance;
        [SerializeField] private XRayWeatherSnapshot[] _keyframes = Array.Empty<XRayWeatherSnapshot>();
        [SerializeField] private Vector2[] _hourlySunPositions = Array.Empty<Vector2>();
        [SerializeField] private bool _hasSsfxWindTrees;
        [SerializeField] private Vector4 _ssfxWindTrees;
        [SerializeField] private bool _hasLightSetup;
        [SerializeField] private Vector4 _lightSetup;
        [SerializeField] private bool _hasFloraFixes;
        [SerializeField] private Vector4 _floraFixes;
        [SerializeField] private bool _hasSunLightScale;
        [SerializeField] private Vector3 _sunLightScale = Vector3.one;
        [SerializeField] private bool _hasEnvironmentScale;
        [SerializeField] private Vector2 _environmentScale;
        [SerializeField] private float _visibilityScale = 1.0f;

        public int SchemaVersion => _schemaVersion;
        public string SourceTarget => _sourceTarget;
        public string CycleName => _cycleName;
        public string SourceIdentity => _sourceIdentity;
        public string Provenance => _provenance;
        public bool IsCompatible => _schemaVersion == CURRENT_SCHEMA && _sourceTarget == "Rvm";
        public int KeyframeCount => _keyframes?.Length ?? 0;
        public bool HasSsfxWindTrees => _hasSsfxWindTrees;
        public Vector4 SsfxWindTrees => _ssfxWindTrees;
        public bool HasLightSetup => _hasLightSetup;
        public Vector4 LightSetup => _lightSetup;
        public bool HasFloraFixes => _hasFloraFixes;
        public Vector4 FloraFixes => _floraFixes;
        public bool HasSunLightScale => _hasSunLightScale;
        public Vector3 SunLightScale => _sunLightScale;
        public bool HasEnvironmentScale => _hasEnvironmentScale;
        public Vector2 EnvironmentScale => _environmentScale;
        public float VisibilityScale => _visibilityScale;

        public void Initialize(string cycleName, string sourceIdentity, string provenance, XRayWeatherSnapshot[] keyframes, Vector2[] hourlySunPositions, Vector4? ssfxWindTrees = null, Vector4? lightSetup = null, Vector4? floraFixes = null, Vector3? sunLightScale = null, float visibilityScale = 1.0f, Vector2? environmentScale = null)
        {
            _schemaVersion = CURRENT_SCHEMA;
            _sourceTarget = "Rvm";
            _cycleName = cycleName;
            _sourceIdentity = sourceIdentity;
            _provenance = provenance;
            _keyframes = keyframes == null ? Array.Empty<XRayWeatherSnapshot>() : (XRayWeatherSnapshot[])keyframes.Clone();
            _hourlySunPositions = hourlySunPositions == null ? Array.Empty<Vector2>() : (Vector2[])hourlySunPositions.Clone();
            _hasSsfxWindTrees = ssfxWindTrees.HasValue;
            _ssfxWindTrees = ssfxWindTrees.GetValueOrDefault();
            _hasLightSetup = lightSetup.HasValue;
            _lightSetup = lightSetup.GetValueOrDefault();
            _hasFloraFixes = floraFixes.HasValue;
            _floraFixes = floraFixes.GetValueOrDefault();
            _hasSunLightScale = sunLightScale.HasValue;
            _sunLightScale = sunLightScale ?? Vector3.one;
            _hasEnvironmentScale = environmentScale.HasValue;
            _environmentScale = environmentScale.GetValueOrDefault();
            _visibilityScale = visibilityScale;
            if (!Validate(out string diagnostic)) throw new ArgumentException(diagnostic);
        }

        public XRayWeatherSnapshot GetKeyframe(int index)
        {
            return _keyframes[index];
        }

        public bool Validate(out string diagnostic)
        {
            diagnostic = null;
            if (!IsCompatible) diagnostic = "Unknown weather source target or incompatible schema.";
            else if (string.IsNullOrWhiteSpace(_sourceIdentity) || string.IsNullOrWhiteSpace(_cycleName)) diagnostic = "Missing weather source identity or cycle.";
            else if (_keyframes == null || _keyframes.Length < 2) diagnostic = "A weather cycle requires at least two descriptors.";
            else if (_hourlySunPositions == null || _hourlySunPositions.Length != 24) diagnostic = "A weather profile requires 24 hourly sun positions.";
            else if (_hasSsfxWindTrees && !Finite(_ssfxWindTrees) || _hasLightSetup && !Finite(_lightSetup) || _hasFloraFixes && !Finite(_floraFixes)) diagnostic = "Invalid saved weather setup vector.";
            else if (_hasSunLightScale && (!Finite(_sunLightScale.x) || !Finite(_sunLightScale.y) || !Finite(_sunLightScale.z) || _sunLightScale.x < 0.0f || _sunLightScale.y < 0.0f || _sunLightScale.z < 0.0f)) diagnostic = "Invalid source sunlight scale.";
            else if (_hasEnvironmentScale && (!Finite(_environmentScale.x) || !Finite(_environmentScale.y) || _environmentScale.x < 0.0f || _environmentScale.y < 0.0f)) diagnostic = "Invalid source environment scale.";
            else if (!Finite(_visibilityScale) || _visibilityScale <= 0.0f) diagnostic = "Invalid source visibility scale.";
            if (diagnostic != null) return false;
            for (int i = 0; i < _keyframes.Length; i++)
            {
                XRayWeatherSnapshot frame = _keyframes[i];
                if (double.IsNaN(frame.GameSeconds) || double.IsInfinity(frame.GameSeconds) || frame.GameSeconds < 0.0 || frame.GameSeconds >= DAY_SECONDS || i > 0 && frame.GameSeconds <= _keyframes[i - 1].GameSeconds || !Finite(frame.WindDirectionRadians) || !Finite(frame.WindVelocity) || frame.WindVelocity < 0.0f || !Finite(frame.TreeAmplitudeIntensity) || frame.TreeAmplitudeIntensity < 0.0f || !Finite(frame.RainDensity) || frame.RainDensity < 0.0f || frame.RainDensity > 1.0f || !Finite(frame.FogDensity) || !Finite(frame.FogDistance) || !Finite(frame.FarPlane) || frame.FarPlane <= 11.0f || !Finite(frame.FarPlane * _visibilityScale) || frame.FarPlane * _visibilityScale <= 11.0f || !Finite(frame.SkyRotationRadians) || !Finite(frame.SunColor) || !Finite(frame.AmbientColor) || !Finite(frame.HemisphereColor) || !Finite(frame.RainColor) || !Finite(frame.FogColor) || !Finite(frame.SkyColor) || !Finite(frame.CloudsColor) || !Finite(frame.ThunderboltPeriod) || !Finite(frame.ThunderboltDuration) || !Finite(frame.SunShaftsIntensity) || !Finite(frame.WaterIntensity))
                {
                    diagnostic = "Invalid or unsorted weather descriptor at index " + i + ".";
                    return false;
                }
            }
            foreach (Vector2 position in _hourlySunPositions)
            {
                if (!Finite(position.x) || !Finite(position.y))
                {
                    diagnostic = "Invalid sun position.";
                    return false;
                }
            }
            return true;
        }

        public bool TryEvaluate(double gameSeconds, out XRayWeatherSnapshot snapshot, out string diagnostic)
        {
            snapshot = default;
            if (!Validate(out diagnostic)) return false;
            if (double.IsNaN(gameSeconds) || double.IsInfinity(gameSeconds))
            {
                diagnostic = "Non-finite game time.";
                return false;
            }
            double seconds = NormalizeTime(gameSeconds);
            int next = 0;
            while (next < _keyframes.Length && _keyframes[next].GameSeconds < seconds) next++;
            if (next == _keyframes.Length) next = 0;
            int previous = next == 0 ? _keyframes.Length - 1 : next - 1;
            XRayWeatherSnapshot a = _keyframes[previous];
            XRayWeatherSnapshot b = _keyframes[next];
            double length = TimeDiff(a.GameSeconds, b.GameSeconds);
            float weight = length <= 0.00001 ? 0.0f : Mathf.Clamp01((float)(TimeDiff(a.GameSeconds, seconds) / length));
            snapshot = XRayWeatherSnapshot.Blend(a, b, weight).WithSampling(seconds, SunDirection(seconds), _visibilityScale);
            return true;
        }

        internal bool HasSameSourceSettings(XRayWeatherProfile other)
        {
            return _hasSsfxWindTrees == other._hasSsfxWindTrees && _ssfxWindTrees.Equals(other._ssfxWindTrees) && _hasLightSetup == other._hasLightSetup && _lightSetup.Equals(other._lightSetup) && _hasFloraFixes == other._hasFloraFixes && _floraFixes.Equals(other._floraFixes) && _hasSunLightScale == other._hasSunLightScale && _sunLightScale.Equals(other._sunLightScale) && _hasEnvironmentScale == other._hasEnvironmentScale && _environmentScale.Equals(other._environmentScale) && _visibilityScale.Equals(other._visibilityScale);
        }

        internal bool HasSameSunPositions(XRayWeatherProfile other)
        {
            if (other == null || _hourlySunPositions == null || other._hourlySunPositions == null || _hourlySunPositions.Length != 24 || other._hourlySunPositions.Length != 24) return false;
            for (int i = 0; i < _hourlySunPositions.Length; i++)
            {
                if (_hourlySunPositions[i].x != other._hourlySunPositions[i].x || _hourlySunPositions[i].y != other._hourlySunPositions[i].y) return false;
            }
            return true;
        }

        public Vector3 SunDirection(double gameSeconds)
        {
            double hours = NormalizeTime(gameSeconds) / 3600.0;
            int hour = (int)Math.Floor(hours);
            Vector2 hp = Vector2.LerpUnclamped(_hourlySunPositions[hour], _hourlySunPositions[(hour + 1) % 24], (float)(hours - hour)) * Mathf.Deg2Rad;
            float cp = Mathf.Cos(hp.y);
            return new Vector3(-cp * Mathf.Sin(hp.x), Mathf.Sin(hp.y), cp * Mathf.Cos(hp.x));
        }

        public static double NormalizeTime(double seconds)
        {
            return (seconds % DAY_SECONDS + DAY_SECONDS) % DAY_SECONDS;
        }

        private static double TimeDiff(double a, double b)
        {
            return a > b ? DAY_SECONDS - a + b : b - a;
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool Finite(Color color)
        {
            return Finite(color.r) && Finite(color.g) && Finite(color.b) && Finite(color.a) && color.r >= 0.0f && color.g >= 0.0f && color.b >= 0.0f;
        }

        private static bool Finite(Vector4 vector)
        {
            return Finite(vector.x) && Finite(vector.y) && Finite(vector.z) && Finite(vector.w) && vector.x >= 0.0f && vector.y >= 0.0f && vector.z >= 0.0f && vector.w >= 0.0f;
        }
    }
}

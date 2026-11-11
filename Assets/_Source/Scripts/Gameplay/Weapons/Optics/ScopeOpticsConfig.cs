using UnityEngine;

namespace RaidIntoDarkness.Weapons.Optics
{
    public enum ScopeProjectionMode
    {
        GeometricLens,
        CalibratedFieldOfView
    }

    public enum ScopeReticlePlane
    {
        FirstFocalPlane,
        SecondFocalPlane
    }

    [CreateAssetMenu(menuName = "Raid Into Darkness/Weapons/Scope Optics", fileName = "ScopeOptics")]
    public sealed class ScopeOpticsConfig : ScriptableObject
    {
        [Header("Projection")]
        [SerializeField, Min(0.1f)] private float _minMagnification = 1f;
        [SerializeField, Min(0.1f)] private float _maxMagnification = 6f;
        [SerializeField, Min(0.1f)] private float _calibrationMagnification = 6f;
        [SerializeField] private ScopeProjectionMode _projectionMode;
        [SerializeField, Range(0.01f, 170f)] private float _calibrationVerticalFov = 5f;
        [SerializeField, Range(0.25f, 4f)] private float _trueOnePowerCalibration = 1f;
        [SerializeField] private AnimationCurve _fieldOfViewMultiplier = AnimationCurve.Linear(1f, 1f, 6f, 1f);
        [Header("Reticle")]
        [SerializeField] private ScopeReticlePlane _reticlePlane;
        [Header("Sight Corrections")]
        [SerializeField, Range(0f, 100f)] private float _maximumCorrectionMilliradians = 30f;

        public float MinMagnification => ScopeOpticsMath.FiniteClamp(_minMagnification, 0.1f, 100f, 1f);
        public float MaxMagnification => ScopeOpticsMath.FiniteClamp(_maxMagnification, MinMagnification, 100f, MinMagnification);
        public float CalibrationMagnification => ScopeOpticsMath.ClampMagnification(_calibrationMagnification, MinMagnification, MaxMagnification);
        public ScopeProjectionMode ProjectionMode => _projectionMode;
        public float CalibrationVerticalFov => ScopeOpticsMath.FiniteClamp(_calibrationVerticalFov, 0.01f, 170f, 5f);
        public float TrueOnePowerCalibration => ScopeOpticsMath.FiniteClamp(_trueOnePowerCalibration, 0.25f, 4f, 1f);
        public ScopeReticlePlane ReticlePlane => _reticlePlane;
        public float MaximumCorrectionMilliradians => ScopeOpticsMath.FiniteClamp(_maximumCorrectionMilliradians, 0f, 100f, 30f);

        public float EvaluateFieldOfViewMultiplier(float magnification)
        {
            return EvaluatePositiveCurve(_fieldOfViewMultiplier, magnification, 1f, 0.01f, 100f);
        }

        public bool TryValidate(out string diagnostic)
        {
            diagnostic = null;
            if (!float.IsFinite(_minMagnification) || _minMagnification < 0.1f || _minMagnification > 100f) diagnostic = "Scope MinMagnification must be finite and between 0.1 and 100.";
            else if (!float.IsFinite(_maxMagnification) || _maxMagnification < _minMagnification || _maxMagnification > 100f) diagnostic = "Scope MaxMagnification must be finite, no smaller than MinMagnification, and at most 100.";
            else if (!float.IsFinite(_calibrationMagnification) || _calibrationMagnification < _minMagnification || _calibrationMagnification > _maxMagnification) diagnostic = "Scope CalibrationMagnification must lie within its magnification range.";
            else if (_projectionMode != ScopeProjectionMode.GeometricLens && _projectionMode != ScopeProjectionMode.CalibratedFieldOfView) diagnostic = "Scope ProjectionMode is invalid.";
            else if (_reticlePlane != ScopeReticlePlane.FirstFocalPlane && _reticlePlane != ScopeReticlePlane.SecondFocalPlane) diagnostic = "Scope ReticlePlane is invalid.";
            else if (!float.IsFinite(_calibrationVerticalFov) || _calibrationVerticalFov < 0.01f || _calibrationVerticalFov > 170f) diagnostic = "Scope CalibrationVerticalFov must be between 0.01 and 170 degrees.";
            return diagnostic == null;
        }

        private float EvaluatePositiveCurve(AnimationCurve curve, float magnification, float fallback, float minimum, float maximum)
        {
            float clampedMagnification = ScopeOpticsMath.ClampMagnification(magnification, MinMagnification, MaxMagnification);
            float value = curve != null && curve.length > 0 ? curve.Evaluate(clampedMagnification) : fallback;
            return ScopeOpticsMath.FiniteClamp(value, minimum, maximum, fallback);
        }
    }
}

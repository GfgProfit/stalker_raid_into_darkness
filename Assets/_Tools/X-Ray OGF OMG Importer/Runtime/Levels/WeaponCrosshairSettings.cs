using System;
using UnityEngine;

namespace XRay.Importer.Levels
{
    [Serializable]
    public sealed class WeaponCrosshairSettings
    {
        [SerializeField] private bool _enabled = true;
        [SerializeField] private Material _material;
        [SerializeField] private Color _color = new Color(1f, 1f, 1f, 0.85f);
        [SerializeField, Min(1f)] private float _maximumDistanceMeters = 2000f;
        [SerializeField, Min(0.1f)] private float _lineLengthPixels = 6f;
        [SerializeField, Min(0.1f)] private float _lineWidthPixels = 1.5f;
        [SerializeField, Min(0f)] private float _minimumGapPixels = 3f;
        [SerializeField, Min(1f)] private float _maximumGapPixels = 60f;
        [SerializeField, Min(0.01f)] private float _fadeDurationSeconds = 0.12f;
        [SerializeField, Min(0.1f)] private float _spreadResponse = 16f;
        [SerializeField, Range(0f, 0.2f)] private float _surfaceOffsetMeters = 0.015f;
        [SerializeField, Range(0f, 1f)] private float _barrelOffsetMeters = 0.05f;

        public bool Enabled => _enabled;
        public Material Material => _material;
        public Color Color => _color;
        public float MaximumDistanceMeters => Safe(_maximumDistanceMeters, 1f, 10000f, 2000f);
        public float LineLengthPixels => Safe(_lineLengthPixels, 0.1f, 100f, 6f);
        public float LineWidthPixels => Safe(_lineWidthPixels, 0.1f, 20f, 1.5f);
        public float MinimumGapPixels => Safe(_minimumGapPixels, 0f, 100f, 3f);
        public float MaximumGapPixels => Safe(_maximumGapPixels, MinimumGapPixels, 500f, 60f);
        public float FadeDurationSeconds => Safe(_fadeDurationSeconds, 0.01f, 5f, 0.12f);
        public float SpreadResponse => Safe(_spreadResponse, 0.1f, 100f, 16f);
        public float BarrelOffsetMeters => Safe(_barrelOffsetMeters, 0f, 1f, 0.05f);
        public float SurfaceOffsetMeters => Safe(_surfaceOffsetMeters, 0f, 0.2f, 0.015f);

        private static float Safe(float value, float minimum, float maximum, float fallback)
        {
            return Mathf.Clamp(float.IsFinite(value) ? value : fallback, minimum, maximum);
        }
    }
}

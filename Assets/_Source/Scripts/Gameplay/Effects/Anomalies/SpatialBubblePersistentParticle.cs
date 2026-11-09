using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class SpatialBubblePersistentParticle : MonoBehaviour
    {
        private const float SOURCE_STEP = 0.033f;
        private const int MAX_STEPS = 3;

        [SerializeField] private ParticleSystem _system;
        [SerializeField] private int _maximum = 1;
        [SerializeField] private float _sourceRate = 25f;
        [SerializeField] private Vector3 _sourceSize = Vector3.one;
        [SerializeField] private Color _sourceColor = Color.white;
        [SerializeField] private Color _targetColor = Color.white;
        [SerializeField] private float _colorScale;
        [SerializeField] private float _colorFrom = 0.05f;
        [SerializeField] private float _colorTo = 0.95f;
        [SerializeField] private float _sourceRotation;
        [SerializeField] private float _targetRotation;
        [SerializeField] private float _rotationScale;
        [SerializeField] private int _frameCount = 1;
        [SerializeField] private float _framesPerSecond;

        private ParticleSystem.Particle[] _particles;
        private Color[] _colors;
        private float[] _rotations;
        private float[] _ages;
        private float[] _frames;
        private List<Vector4> _customData;
        private int _count;
        private float _accumulated;

        private void Awake()
        {
            if (_system == null)
            {
                _system = GetComponent<ParticleSystem>();
            }
            EnsureStorage();
        }

        private void OnEnable()
        {
            ResetState();
        }

        private void Update()
        {
            _accumulated += Time.deltaTime;
            int steps = Mathf.Min(MAX_STEPS, Mathf.FloorToInt(_accumulated / SOURCE_STEP));
            if (steps == 0)
            {
                return;
            }
            _accumulated %= SOURCE_STEP;
            for (int index = 0; index < steps; index++)
            {
                AdvanceStep(Random.value);
            }
            Present();
        }

        private void OnDisable()
        {
            ResetState();
        }

        private void EnsureStorage()
        {
            int maximum = Mathf.Max(1, _maximum);
            if (_particles != null && _particles.Length == maximum)
            {
                return;
            }
            _particles = new ParticleSystem.Particle[maximum];
            _colors = new Color[maximum];
            _rotations = new float[maximum];
            _ages = new float[maximum];
            _frames = new float[maximum];
            _customData = new List<Vector4>(maximum);
        }

        private void ResetState()
        {
            EnsureStorage();
            _count = 0;
            _accumulated = 0f;
            _customData.Clear();
            if (_system != null)
            {
                _system.Clear(false);
            }
        }

        private void AdvanceStep(float emissionRoll)
        {
            EnsureStorage();
            float exact = Mathf.Max(0f, _sourceRate) * SOURCE_STEP;
            int emitted = Mathf.FloorToInt(exact);
            if (emissionRoll < exact - emitted)
            {
                emitted++;
            }
            int targetCount = Mathf.Min(_particles.Length, _count + emitted);
            while (_count < targetCount)
            {
                int index = _count++;
                _colors[index] = Quantize(_sourceColor);
                _rotations[index] = _sourceRotation;
                _ages[index] = 0f;
                _frames[index] = 0f;
                _particles[index] = new ParticleSystem.Particle
                {
                    position = _system != null ? _system.transform.position : Vector3.zero,
                    velocity = Vector3.zero,
                    startLifetime = float.PositiveInfinity,
                    remainingLifetime = float.PositiveInfinity,
                    startSize3D = _sourceSize,
                    randomSeed = (uint)(index + 1)
                };
            }
            for (int index = 0; index < _count; index++)
            {
                if (_ages[index] >= _colorFrom && _ages[index] <= _colorTo)
                {
                    _colors[index] = Quantize(Color.LerpUnclamped(_colors[index], _targetColor, _colorScale * SOURCE_STEP));
                }
                float rotation = _rotations[index];
                _rotations[index] += (Mathf.Abs(_targetRotation) - Mathf.Abs(rotation)) * (rotation >= 0f ? 1f : -1f) * _rotationScale * SOURCE_STEP;
                _ages[index] += SOURCE_STEP;
                _frames[index] = SpatialBubbleParticleFrames.AdvanceFrame(_frames[index], _framesPerSecond, _frameCount, false);
                _particles[index].startColor = _colors[index];
                _particles[index].rotation = _rotations[index] * Mathf.Rad2Deg;
            }
        }

        private void Present()
        {
            if (_system == null)
            {
                return;
            }
            _customData.Clear();
            for (int index = 0; index < _count; index++)
            {
                _customData.Add(new Vector4(Mathf.Floor(_frames[index]), 0f, 0f, 0f));
            }
            _system.SetParticles(_particles, _count);
            _system.SetCustomParticleData(_customData, ParticleSystemCustomData.Custom1);
        }

        private static Color Quantize(Color value)
        {
            return new Color32((byte)Mathf.Clamp(Mathf.FloorToInt(value.r * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.FloorToInt(value.g * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.FloorToInt(value.b * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.FloorToInt(value.a * 255f), 0, 255));
        }
    }
}

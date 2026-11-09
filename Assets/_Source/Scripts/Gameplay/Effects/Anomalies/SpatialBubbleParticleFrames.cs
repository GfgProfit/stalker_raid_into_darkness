using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class SpatialBubbleParticleFrames : MonoBehaviour
    {
        private const float SOURCE_STEP = 0.033f;
        private const float FRAME_PRECISION = 255f;

        [SerializeField] private ParticleSystem _system;
        [SerializeField] private int _frameCount = 1;
        [SerializeField] private float _framesPerSecond;
        [SerializeField] private bool _animated;
        [SerializeField] private bool _randomStart;
        [SerializeField] private bool _randomPlayback;

        private ParticleSystem.Particle[] _particles;
        private List<Vector4> _frames;

        private void Awake()
        {
            if (_system == null)
            {
                _system = GetComponent<ParticleSystem>();
            }
            EnsureStorage();
        }

        private void LateUpdate()
        {
            if (_system == null)
            {
                return;
            }
            EnsureStorage();
            int count = _system.GetParticles(_particles);
            _frames.Clear();
            for (int index = 0; index < count; index++)
            {
                ParticleSystem.Particle particle = _particles[index];
                float age = particle.startLifetime - particle.remainingLifetime;
                float frame = EvaluateFrame(age, particle.randomSeed, _frameCount, _framesPerSecond, _animated, _randomStart, _randomPlayback);
                _frames.Add(new Vector4(Mathf.Floor(frame), 0f, 0f, 0f));
            }
            _system.SetCustomParticleData(_frames, ParticleSystemCustomData.Custom1);
        }

        public static float EvaluateFrame(float age, uint randomSeed, int frameCount, float framesPerSecond, bool animated, bool randomStart, bool randomPlayback)
        {
            int count = Mathf.Max(1, frameCount);
            uint random = unchecked(randomSeed * 747796405u + 2891336453u);
            random ^= random >> 16;
            float frame = randomStart ? random % (uint)count : 0f;
            if (animated == false)
            {
                return frame;
            }
            bool backward = randomPlayback && (random & 0x80000000u) != 0u;
            int steps = Mathf.Max(1, Mathf.FloorToInt(Mathf.Max(0f, age) / SOURCE_STEP) + 1);
            for (int index = 0; index < steps; index++)
            {
                frame = AdvanceFrame(frame, framesPerSecond, count, backward);
            }
            return frame;
        }

        public static float AdvanceFrame(float frame, float framesPerSecond, int frameCount, bool backward)
        {
            float value = frame + (backward ? -1f : 1f) * framesPerSecond * SOURCE_STEP;
            int count = Mathf.Max(1, frameCount);
            if (value > count)
            {
                value -= count;
            }
            if (value < 0f)
            {
                value += count;
            }
            return (ushort)Mathf.FloorToInt(value * FRAME_PRECISION) / FRAME_PRECISION;
        }

        private void EnsureStorage()
        {
            int maximum = _system != null ? Mathf.Max(1, _system.main.maxParticles) : 1;
            if (_particles != null && _particles.Length == maximum)
            {
                return;
            }
            _particles = new ParticleSystem.Particle[maximum];
            _frames = new List<Vector4>(maximum);
        }
    }
}

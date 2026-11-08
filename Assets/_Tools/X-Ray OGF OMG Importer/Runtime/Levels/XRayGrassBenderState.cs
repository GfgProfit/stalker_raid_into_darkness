using System;
using UnityEngine;

namespace XRay.Importer.Levels
{
    public sealed class XRayGrassBenderState
    {
        public const int SLOT_COUNT = 8;
        public const string COUNT_PROPERTY = "_MonolithGrassBenderCount";
        public const string CURRENT_PROPERTY = "_MonolithGrassBenders";
        public const string PREVIOUS_PROPERTY = "_MonolithGrassBendersPrevious";

        private static readonly int COUNT_ID = Shader.PropertyToID(COUNT_PROPERTY);
        private static readonly int CURRENT_ID = Shader.PropertyToID(CURRENT_PROPERTY);
        private static readonly int PREVIOUS_ID = Shader.PropertyToID(PREVIOUS_PROPERTY);

        private readonly Explosion[] _explosions = new Explosion[SLOT_COUNT];
        private readonly Vector4[] _current = new Vector4[SLOT_COUNT * 2];
        private readonly Vector4[] _previous = new Vector4[SLOT_COUNT * 2];
        private int _currentCount;
        private int _previousCount;

        public bool HasData => _currentCount > 0 || _previousCount > 0;
        public int ActiveCount
        {
            get
            {
                int count = 0;
                foreach (Explosion explosion in _explosions)
                {
                    if (explosion.IsActive)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public bool TryAddExplosion(UnityEngine.Object owner, Vector3 position, float fade, float speed, float intensity, float radius)
        {
            if (owner == null || fade <= 0f || speed <= 0f || intensity <= 0f || radius <= 0f)
            {
                return false;
            }
            for (int index = 0; index < SLOT_COUNT; index++)
            {
                if (_explosions[index].IsActive == false)
                {
                    _explosions[index] = new Explosion(owner, position, fade, speed, intensity, radius);
                    return true;
                }
            }
            return false;
        }

        public void RemoveOwner(UnityEngine.Object owner)
        {
            for (int index = 0; index < SLOT_COUNT; index++)
            {
                if (ReferenceEquals(_explosions[index].Owner, owner))
                {
                    _explosions[index] = default;
                }
            }
        }

        public void Advance(float deltaTime)
        {
            Array.Copy(_current, _previous, _current.Length);
            _previousCount = _currentCount;
            _currentCount = 0;
            for (int index = 0; index < SLOT_COUNT; index++)
            {
                _explosions[index].Advance(Mathf.Max(0f, deltaTime));
                Explosion explosion = _explosions[index];
                bool active = explosion.IsActive;
                _current[index] = active ? new Vector4(explosion.Position.x, explosion.Position.y, explosion.Position.z, explosion.CurrentRadius) : Vector4.zero;
                _current[index + SLOT_COUNT] = active ? new Vector4(0f, -99f, 0f, explosion.Strength) : Vector4.zero;
                if (active)
                {
                    _currentCount++;
                }
            }
        }

        public Vector4 Position(int slot, bool previous = false)
        {
            return previous ? _previous[slot] : _current[slot];
        }

        public float Strength(int slot, bool previous = false)
        {
            return previous ? _previous[slot + SLOT_COUNT].w : _current[slot + SLOT_COUNT].w;
        }

        public Vector3 MaximumDisplacement(float height)
        {
            float strength = 0f;
            for (int index = 0; index < SLOT_COUNT; index++)
            {
                strength += Mathf.Max(Strength(index), Strength(index, true));
            }
            float scale = Mathf.Max(0f, height) * strength;
            return new Vector3(2f * scale, 0.6f * scale, 2f * scale);
        }

        public Bounds ExpandBounds(Bounds bounds, float height)
        {
            if (HasData == false || height <= 0f)
            {
                return bounds;
            }
            for (int index = 0; index < SLOT_COUNT; index++)
            {
                float strength = Mathf.Max(IntersectingStrength(bounds, Position(index), Strength(index)), IntersectingStrength(bounds, Position(index, true), Strength(index, true)));
                float scale = height * strength;
                bounds.Expand(new Vector3(4f * scale, 1.2f * scale, 4f * scale));
            }
            return bounds;
        }

        public void Clear()
        {
            Array.Clear(_explosions, 0, _explosions.Length);
            Array.Clear(_current, 0, _current.Length);
            Array.Clear(_previous, 0, _previous.Length);
            _currentCount = 0;
            _previousCount = 0;
        }

        public void Apply()
        {
            Shader.SetGlobalInteger(COUNT_ID, HasData ? SLOT_COUNT : 0);
            Shader.SetGlobalVectorArray(CURRENT_ID, _current);
            Shader.SetGlobalVectorArray(PREVIOUS_ID, _previous);
        }

        private static float IntersectingStrength(Bounds bounds, Vector4 position, float strength)
        {
            if (position.w <= 0f || strength <= 0f)
            {
                return 0f;
            }
            Bounds area = new Bounds(new Vector3(position.x, position.y, position.z), new Vector3((position.w + 0.001f) * 2f, position.w * 2f, (position.w + 0.001f) * 2f));
            return bounds.Intersects(area) ? strength : 0f;
        }

        private struct Explosion
        {
            private float _fade;
            private float _speed;
            private float _radius;
            private float _time;
            private float _target;

            public UnityEngine.Object Owner { get; private set; }
            public Vector3 Position { get; }
            public float CurrentRadius { get; private set; }
            public float Strength { get; private set; }
            public bool IsActive => Owner != null;

            public Explosion(UnityEngine.Object owner, Vector3 position, float fade, float speed, float intensity, float radius)
            {
                Owner = owner;
                Position = position;
                _fade = fade;
                _speed = speed;
                _radius = radius;
                _time = 0f;
                _target = intensity;
                CurrentRadius = 0.01f;
                Strength = intensity;
            }

            public void Advance(float deltaTime)
            {
                if (IsActive == false)
                {
                    return;
                }
                _time += deltaTime * _speed;
                CurrentRadius = _radius * Mathf.Min(1f, _time);
                _target = Mathf.Min(1f, _target);
                float difference = Mathf.Max(0.1f, Mathf.Abs(Strength - _target));
                if (_target <= Strength)
                {
                    Strength -= deltaTime * _fade * difference;
                }
                else
                {
                    Strength += deltaTime * _speed * difference;
                    if (Strength >= _target)
                    {
                        _target = 0f;
                    }
                }
                if (Strength < 0f)
                {
                    Owner = null;
                }
            }
        }
    }
}

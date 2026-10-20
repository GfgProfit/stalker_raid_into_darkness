using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    public struct XRayParticle
    {
        public Vector3 Position;
        public Vector3 PreviousPosition;
        public Vector3 Velocity;
        public Vector3 Size;
        public Vector3 Rotation;
        public Vector4 Color;
        public float Age;
        public float Frame;
        public bool AnimateBackward;
    }

    public sealed class XRayParticleSimulation
    {
        public const float STEP = 0.033f;
        public const int MAX_STEPS = 3;

        private readonly XRayParticleEffectAsset _asset;
        private readonly IReadOnlyList<XRayParticleAction> _actions;
        private readonly bool[] _silent;
        private readonly XRayParticleDomain[] _position;
        private readonly XRayParticleDomain[] _velocity;
        private readonly Vector3[] _targetVelocity;
        private readonly Vector3[] _parentVelocity;
        private readonly XRayParticle[] _particles;
        private float _elapsedLimit;
        private float _accumulated;

        public XRayParticleSimulation(XRayParticleEffectAsset asset)
        {
            _asset = asset;
            _actions = asset.Actions;
            int count = _actions.Count;
            _silent = new bool[count];
            _position = new XRayParticleDomain[count];
            _velocity = new XRayParticleDomain[count];
            _targetVelocity = new Vector3[count];
            _parentVelocity = new Vector3[count];
            _particles = new XRayParticle[Mathf.Max(1, asset.MaxParticles)];

            for (int i = 0; i < count; i++)
            {
                _position[i] = _actions[i].Position;
                _velocity[i] = _actions[i].Velocity;
                _targetVelocity[i] = _actions[i].Vector;
                _silent[i] = (_actions[i].Flags & XRayParticleAction.SOURCE_SILENT) != 0;
            }

            _elapsedLimit = asset.TimeLimit;
        }

        public XRayParticleEffectAsset Asset => _asset;
        public XRayParticle[] Particles => _particles;
        public int Count { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool IsDeferredStop { get; private set; }

        public void Play()
        {
            IsDeferredStop = false;
            IsPlaying = true;
            _elapsedLimit = _asset.TimeLimit;
            _accumulated = 0f;

            for (int i = 0; i < _actions.Count; i++)
            {
                if (_actions[i].Type == XRayParticleActionType.Source)
                {
                    _silent[i] = false;
                }
            }
        }

        public void Stop(bool deferred)
        {
            for (int i = 0; i < _actions.Count; i++)
            {
                if (_actions[i].Type == XRayParticleActionType.Source)
                {
                    _silent[i] = true;
                }
            }

            if (deferred)
            {
                IsDeferredStop = true;
                return;
            }

            Count = 0;
            IsPlaying = false;
        }

        public void UpdateParent(Matrix4x4 full, Vector3 velocity)
        {
            Matrix4x4 translation = Matrix4x4.Translate(full.GetColumn(3));

            for (int i = 0; i < _actions.Count; i++)
            {
                XRayParticleAction action = _actions[i];
                Matrix4x4 matrix = action.AllowRotate ? full : translation;

                if (action.Type == XRayParticleActionType.Source)
                {
                    _position[i] = action.Position.Transform(matrix);
                    _velocity[i] = action.Velocity.TransformDirection(matrix);
                    _parentVelocity[i] = velocity * action.ParentMotion;
                }
                else if (action.Type == XRayParticleActionType.TargetVelocity)
                {
                    _targetVelocity[i] = matrix.MultiplyVector(action.Vector);
                }
            }
        }

        public void Tick(float deltaTime)
        {
            if (IsPlaying == false)
            {
                return;
            }

            _accumulated += deltaTime;
            int steps = 0;

            if (_accumulated >= STEP)
            {
                steps = Mathf.Min(MAX_STEPS, Mathf.FloorToInt(_accumulated / STEP));
                _accumulated %= STEP;
            }

            for (; steps > 0; steps--)
            {
                if (_asset.HasTimeLimit && IsDeferredStop == false)
                {
                    _elapsedLimit -= STEP;

                    if (_elapsedLimit < 0f)
                    {
                        _elapsedLimit = _asset.TimeLimit;
                        Stop(true);
                        break;
                    }
                }

                Step(STEP);

                if (_asset.IsAnimated)
                {
                    Animate(STEP);
                }

                if (IsDeferredStop && Count == 0)
                {
                    IsPlaying = false;
                    IsDeferredStop = false;
                    break;
                }
            }
        }

        private void Step(float dt)
        {
            float killOldTime = 1f;

            for (int i = 0; i < _actions.Count; i++)
            {
                XRayParticleAction action = _actions[i];

                switch (action.Type)
                {
                    case XRayParticleActionType.Source:
                        Emit(i, action, dt);
                        break;
                    case XRayParticleActionType.KillOld:
                        killOldTime = action.Age;
                        KillOld(action.Age, action.KillLessThan);
                        break;
                    case XRayParticleActionType.Move:
                        Move(dt);
                        break;
                    case XRayParticleActionType.TargetColor:
                        TargetColor(action, dt, killOldTime);
                        break;
                    case XRayParticleActionType.TargetSize:
                        TargetSize(action, dt);
                        break;
                    case XRayParticleActionType.TargetRotate:
                        TargetRotate(action, dt);
                        break;
                    case XRayParticleActionType.TargetVelocity:
                        TargetVelocity(_targetVelocity[i], action.Factor * dt);
                        break;
                    case XRayParticleActionType.Gravity:
                        Gravity(action.Vector * dt);
                        break;
                    case XRayParticleActionType.RandomAccel:
                        RandomAccel(action.Position, dt);
                        break;
                    case XRayParticleActionType.Damping:
                        Damping(action, dt);
                        break;
                }
            }
        }

        private void Emit(int index, XRayParticleAction action, float dt)
        {
            if (_silent[index])
            {
                return;
            }

            float exact = action.Rate * dt;
            int rate = Mathf.FloorToInt(exact);

            if (Random.value < exact - rate)
            {
                rate++;
            }

            rate = Mathf.Min(rate, _particles.Length - Count);
            bool singleSize = (action.Flags & XRayParticleAction.SOURCE_SINGLE_SIZE) != 0;

            for (int i = 0; i < rate; i++)
            {
                Vector3 position = _position[index].Generate();
                Vector3 size = action.Size.Generate();

                if (singleSize)
                {
                    size = new Vector3(size.x, size.x, size.x);
                }

                Vector3 color = action.Color.Generate();
                var particle = new XRayParticle
                {
                    Position = position,
                    PreviousPosition = position,
                    Velocity = _velocity[index].Generate() + _parentVelocity[index],
                    Size = size,
                    Rotation = action.Rotation.Generate(),
                    Color = Clamp(new Vector4(color.x, color.y, color.z, action.Alpha)),
                    Age = action.Age + XRayParticleDomain.Gaussian(action.AgeSigma)
                };

                OnBirth(ref particle);
                _particles[Count++] = particle;
            }
        }

        private void OnBirth(ref XRayParticle particle)
        {
            if (_asset.IsFramed == false)
            {
                return;
            }

            if (_asset.HasFlags(XRayParticleEffectAsset.RANDOM_FRAME))
            {
                particle.Frame = Random.Range(0, _asset.FrameCount);
            }

            if (_asset.HasFlags(XRayParticleEffectAsset.RANDOM_PLAYBACK))
            {
                particle.AnimateBackward = Random.value < 0.5f;
            }
        }

        private void KillOld(float ageLimit, bool killLessThan)
        {
            for (int i = Count - 1; i >= 0; i--)
            {
                if (((_particles[i].Age < ageLimit) ^ killLessThan) == false)
                {
                    _particles[i] = _particles[--Count];
                }
            }
        }

        private void Move(float dt)
        {
            for (int i = 0; i < Count; i++)
            {
                _particles[i].Age += dt;
                _particles[i].PreviousPosition = _particles[i].Position;
                _particles[i].Position += _particles[i].Velocity * dt;
            }
        }

        private void TargetColor(XRayParticleAction action, float dt, float killOldTime)
        {
            float factor = action.Factor * dt;
            var target = new Vector4(action.Vector.x, action.Vector.y, action.Vector.z, action.Alpha);

            for (int i = 0; i < Count; i++)
            {
                float age = _particles[i].Age;

                if (age < action.TimeFrom * killOldTime || age > action.TimeTo * killOldTime)
                {
                    continue;
                }

                _particles[i].Color = Clamp(_particles[i].Color + (target - _particles[i].Color) * factor);
            }
        }

        private void TargetSize(XRayParticleAction action, float dt)
        {
            Vector3 factor = action.Scale * dt;

            for (int i = 0; i < Count; i++)
            {
                Vector3 difference = action.Vector - _particles[i].Size;
                _particles[i].Size += Vector3.Scale(difference, factor);
            }
        }

        private void TargetRotate(XRayParticleAction action, float dt)
        {
            float factor = action.Factor * dt;
            float target = Mathf.Abs(action.Vector.x);

            for (int i = 0; i < Count; i++)
            {
                float current = _particles[i].Rotation.x;
                float sign = current >= 0f ? factor : -factor;
                _particles[i].Rotation.x = current + (target - Mathf.Abs(current)) * sign;
            }
        }

        private void TargetVelocity(Vector3 target, float factor)
        {
            for (int i = 0; i < Count; i++)
            {
                _particles[i].Velocity += (target - _particles[i].Velocity) * factor;
            }
        }

        private void Gravity(Vector3 delta)
        {
            for (int i = 0; i < Count; i++)
            {
                _particles[i].Velocity += delta;
            }
        }

        private void RandomAccel(XRayParticleDomain domain, float dt)
        {
            for (int i = 0; i < Count; i++)
            {
                _particles[i].Velocity += domain.Generate() * dt;
            }
        }

        private void Damping(XRayParticleAction action, float dt)
        {
            Vector3 scale = Vector3.one - (Vector3.one - action.Vector) * dt;

            for (int i = 0; i < Count; i++)
            {
                _particles[i].Velocity = Vector3.Scale(_particles[i].Velocity, scale);
            }
        }

        private void Animate(float dt)
        {
            float speed = _asset.FrameSpeed * dt;
            int count = _asset.FrameCount;

            for (int i = 0; i < Count; i++)
            {
                float frame = _particles[i].Frame + (_particles[i].AnimateBackward ? -speed : speed);

                if (frame > count)
                {
                    frame -= count;
                }

                if (frame < 0f)
                {
                    frame += count;
                }

                _particles[i].Frame = frame;
            }
        }

        private static Vector4 Clamp(Vector4 color)
        {
            return new Vector4(Mathf.Clamp01(color.x), Mathf.Clamp01(color.y), Mathf.Clamp01(color.z), Mathf.Clamp01(color.w));
        }
    }
}

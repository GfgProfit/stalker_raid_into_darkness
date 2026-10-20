using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class XRayParticleEffectPlayer : MonoBehaviour
    {
        private const float RENDER_LIFETIME = 1000f;
        private const float MIN_SPEED = 0.0000001f;
        private const float PARALLEL_LIMIT = 0.99f;

        private static readonly List<ParticleSystemVertexStream> STREAMS = new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV, ParticleSystemVertexStream.Custom1X };

        [SerializeField] private XRayParticleEffectAsset _asset;

        private XRayParticleSimulation _simulation;
        private ParticleSystem _system;
        private ParticleSystemRenderer _renderer;
        private ParticleSystem.Particle[] _buffer;
        private readonly List<Vector4> _custom = new List<Vector4>();
        private Vector3 _lastPosition;
        private bool _follow = true;
        private bool _hasParent;

        public XRayParticleEffectAsset Asset => _asset;
        public bool IsPlaying => _simulation != null && _simulation.IsPlaying;
        public int ParticleCount => _simulation != null ? _simulation.Count : 0;

        public void SetAsset(XRayParticleEffectAsset asset)
        {
            _asset = asset;
            _simulation = null;
            _system = null;
            ConfigureSystem();
        }

        public void Play(bool follow)
        {
            if (EnsureSimulation() == false)
            {
                return;
            }

            _follow = follow;
            _hasParent = false;
            UpdateParent(0f);
            _simulation.Play();

            if (_system.isPlaying == false)
            {
                _system.Play();
            }
        }

        public void Stop(bool deferred)
        {
            if (_simulation == null)
            {
                return;
            }

            _simulation.Stop(deferred);

            if (deferred == false)
            {
                Render();
            }
        }

        public void Tick(float deltaTime)
        {
            if (_simulation == null || _simulation.IsPlaying == false)
            {
                return;
            }

            if (_follow)
            {
                UpdateParent(deltaTime);
            }

            _simulation.Tick(deltaTime);
            Render();
        }

        private void Awake()
        {
            ConfigureSystem();
        }

        private bool EnsureSimulation()
        {
            if (_asset == null)
            {
                return false;
            }

            ConfigureSystem();

            if (_simulation == null || _simulation.Asset != _asset)
            {
                _simulation = new XRayParticleSimulation(_asset);
                _buffer = new ParticleSystem.Particle[Mathf.Max(1, _asset.MaxParticles)];
            }

            return true;
        }

        private void ConfigureSystem()
        {
            if (_system != null)
            {
                return;
            }

            _system = GetComponent<ParticleSystem>();
            _renderer = GetComponent<ParticleSystemRenderer>();
            ParticleSystem.MainModule main = _system.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.simulationSpeed = 0f;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.startSize3D = true;
            main.maxParticles = _asset != null ? Mathf.Max(1, _asset.MaxParticles) : 1;
            ParticleSystem.EmissionModule emission = _system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = _system.shape;
            shape.enabled = false;
            ParticleSystem.CustomDataModule custom = _system.customData;
            custom.enabled = true;
            custom.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
            _renderer.SetActiveVertexStreams(STREAMS);

            if (_asset == null)
            {
                return;
            }

            _renderer.sharedMaterial = _asset.Material;
            _renderer.enabled = _asset.Material != null;
            main.startRotation3D = _asset.AlignToPath;
            _renderer.renderMode = ParticleSystemRenderMode.Billboard;
            _renderer.alignment = _asset.AlignToPath ? ParticleSystemRenderSpace.World : ParticleSystemRenderSpace.View;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
        }

        private void UpdateParent(float deltaTime)
        {
            Vector3 position = transform.position;
            Vector3 velocity = _hasParent && deltaTime > 0f ? (position - _lastPosition) / deltaTime : Vector3.zero;
            _simulation.UpdateParent(Matrix4x4.TRS(position, transform.rotation, Vector3.one), velocity);
            _lastPosition = position;
            _hasParent = true;
        }

        private void Render()
        {
            int count = _simulation.Count;
            XRayParticle[] particles = _simulation.Particles;
            Camera view = Camera.main;
            Vector3 eye = view != null ? view.transform.position : transform.position;
            _custom.Clear();

            for (int i = 0; i < count; i++)
            {
                XRayParticle particle = particles[i];
                ParticleSystem.Particle target = _buffer[i];
                target.position = particle.Position;
                target.velocity = particle.Velocity;
                if (_asset.AlignToPath)
                {
                    Align(particle, eye, ref target);
                }
                else
                {
                    target.startSize3D = particle.Size;
                    target.rotation = particle.Rotation.x * Mathf.Rad2Deg;
                }

                target.startColor = new Color(particle.Color.x, particle.Color.y, particle.Color.z, particle.Color.w);
                target.startLifetime = RENDER_LIFETIME;
                target.remainingLifetime = RENDER_LIFETIME;
                _buffer[i] = target;
                _custom.Add(new Vector4(Mathf.Floor(particle.Frame), 0f, 0f, 0f));
            }

            _system.SetParticles(_buffer, count);
            _system.SetCustomParticleData(_custom, ParticleSystemCustomData.Custom1);
        }

        private void Align(XRayParticle particle, Vector3 eye, ref ParticleSystem.Particle target)
        {
            Vector3 velocity = particle.Velocity;
            float speed = velocity.magnitude;
            float width = particle.Size.x;
            float height = particle.Size.y;
            if (_asset.UsesVelocityScale)
            {
                width += 2f * speed * _asset.VelocityScale.x;
                height += 2f * speed * _asset.VelocityScale.y;
            }

            Quaternion rotation;
            if (speed < MIN_SPEED && _asset.WorldAlign)
            {
                Quaternion world = Quaternion.Euler(_asset.DefaultRotation * Mathf.Rad2Deg);
                rotation = Quaternion.LookRotation(world * Vector3.up, world * Vector3.forward);
            }
            else if (speed >= MIN_SPEED && _asset.FaceAlign)
            {
                Vector3 forward = velocity / speed;
                Vector3 up = Mathf.Abs(Vector3.Dot(Vector3.up, forward)) > PARALLEL_LIMIT ? Vector3.forward : Vector3.up;
                rotation = Quaternion.LookRotation(forward, up) * Quaternion.AngleAxis(particle.Rotation.x * Mathf.Rad2Deg, Vector3.forward);
            }
            else
            {
                Vector3 direction = speed >= MIN_SPEED ? velocity / speed : DefaultDirection();
                Vector3 normal = Vector3.ProjectOnPlane(eye - particle.Position, direction);
                if (normal.sqrMagnitude < MIN_SPEED)
                {
                    normal = Vector3.Cross(direction, Mathf.Abs(Vector3.Dot(Vector3.up, direction)) > PARALLEL_LIMIT ? Vector3.forward : Vector3.up);
                }

                rotation = Quaternion.LookRotation(normal, direction);
            }

            target.startSize3D = new Vector3(width, height, particle.Size.z);
            target.rotation3D = rotation.eulerAngles;
        }

        private Vector3 DefaultDirection()
        {
            float heading = -_asset.DefaultRotation.y;
            float pitch = -_asset.DefaultRotation.x;
            return new Vector3(-Mathf.Cos(pitch) * Mathf.Sin(heading), Mathf.Sin(pitch), Mathf.Cos(pitch) * Mathf.Cos(heading));
        }
    }
}

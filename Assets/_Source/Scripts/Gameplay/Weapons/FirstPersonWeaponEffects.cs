using System.Collections.Generic;
using RaidIntoDarkness.Effects;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.VFX;

namespace RaidIntoDarkness.Weapons
{
    public sealed class FirstPersonWeaponEffects
    {
        private const int MAX_INSTANCES = 8;
        private const float MUZZLE_LIGHT_RANGE = 5f;
        private const float MUZZLE_LIGHT_DURATION = 0.2f;
        private const float MUZZLE_LIGHT_INTENSITY = 200f;

        private readonly Transform _root;
        private readonly Dictionary<GameObject, List<Instance>> _pools = new Dictionary<GameObject, List<Instance>>();
        private Light _muzzleLight;
        private float _muzzleLightTimeLeft;

        public FirstPersonWeaponEffects(Transform root)
        {
            _root = root;
        }

        public Instance Play(GameObject prefab, Transform follow, Vector3 position, Quaternion rotation)
        {
            Instance player = Acquire(prefab);
            if (player == null)
            {
                return null;
            }

            Transform transform = player.Transform;
            transform.SetParent(follow != null ? follow : _root, false);
            transform.localScale = Vector3.one;
            player.SetPose(position, rotation);
            player.Play(follow != null);
            return player;
        }

        public void PlayMuzzleLight(Transform muzzle)
        {
            if (_muzzleLight == null)
            {
                GameObject lightObject = new GameObject("Muzzle Flash Light");
                lightObject.SetActive(false);
                lightObject.AddHDLight(LightType.Point).affectsVolumetric = false;
                _muzzleLight = lightObject.GetComponent<Light>();
                _muzzleLight.lightUnit = LightUnit.Candela;
                _muzzleLight.lightmapBakeType = LightmapBakeType.Realtime;
                _muzzleLight.shadows = LightShadows.None;
                _muzzleLight.range = MUZZLE_LIGHT_RANGE;
                _muzzleLight.color = new Color(0.6f, 0.5f, 0.3f);
            }

            _muzzleLight.transform.SetParent(muzzle, false);
            _muzzleLight.transform.localPosition = Vector3.zero;
            _muzzleLight.intensity = MUZZLE_LIGHT_INTENSITY;
            _muzzleLightTimeLeft = MUZZLE_LIGHT_DURATION;
            _muzzleLight.gameObject.SetActive(true);
        }

        public void TickMuzzleLight(float deltaTime)
        {
            if (_muzzleLightTimeLeft <= 0f || _muzzleLight == null)
            {
                return;
            }

            _muzzleLightTimeLeft -= deltaTime;
            if (_muzzleLightTimeLeft <= 0f)
            {
                StopMuzzleLight();
                return;
            }

            _muzzleLight.intensity = MUZZLE_LIGHT_INTENSITY * (_muzzleLightTimeLeft / MUZZLE_LIGHT_DURATION);
        }

        public void StopAll()
        {
            StopMuzzleLight();
            foreach (List<Instance> pool in _pools.Values)
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    if (pool[i].Transform != null)
                    {
                        pool[i].Stop();
                        pool[i].Transform.SetParent(_root, false);
                    }
                }
            }
        }

        public void Detach(Transform follow)
        {
            if (_muzzleLight != null && _muzzleLight.transform.parent == follow)
            {
                StopMuzzleLight();
                _muzzleLight.transform.SetParent(_root, false);
            }

            foreach (List<Instance> pool in _pools.Values)
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    if (pool[i].Transform != null && pool[i].Transform.parent == follow)
                    {
                        pool[i].Stop();
                        pool[i].Transform.SetParent(_root, true);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_muzzleLight != null)
            {
                StopMuzzleLight();
                Object.Destroy(_muzzleLight.gameObject);
            }

            foreach (List<Instance> pool in _pools.Values)
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    if (pool[i].Transform != null)
                    {
                        Object.Destroy(pool[i].Transform.gameObject);
                    }
                }
            }

            _pools.Clear();
        }

        private void StopMuzzleLight()
        {
            _muzzleLightTimeLeft = 0f;
            if (_muzzleLight != null)
            {
                _muzzleLight.gameObject.SetActive(false);
            }
        }

        private Instance Acquire(GameObject prefab)
        {
            if (prefab == null)
            {
                return null;
            }

            if (!_pools.TryGetValue(prefab, out List<Instance> pool))
            {
                pool = new List<Instance>();
                _pools.Add(prefab, pool);
            }

            Instance oldest = null;
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                if (pool[i].Transform == null)
                {
                    pool.RemoveAt(i);
                    continue;
                }

                if (!pool[i].IsPlaying)
                {
                    return pool[i];
                }

                if (oldest == null || pool[i].CurrentTime > oldest.CurrentTime)
                {
                    oldest = pool[i];
                }
            }

            if (pool.Count >= MAX_INSTANCES)
            {
                oldest.Stop();
                return oldest;
            }

            GameObject instance = Object.Instantiate(prefab, _root, false);
            instance.name = prefab.name;
            XRayParticleGroupPlayer group = instance.GetComponent<XRayParticleGroupPlayer>();
            VisualEffect visualEffect = group == null ? instance.GetComponent<VisualEffect>() : null;
            if (group == null && (visualEffect == null || visualEffect.visualEffectAsset == null || !visualEffect.enabled))
            {
                Object.Destroy(instance);
                return null;
            }

            Instance player = new Instance(instance.transform, group, visualEffect);
            pool.Add(player);
            return player;
        }

        public sealed class Instance
        {
            private const int VFX_STARTUP_FRAMES = 2;

            private static readonly int DAVFX_SINGLE_BURST = Shader.PropertyToID("Single Burst Mode");
            private static readonly int DAVFX_BURST_DELAY = Shader.PropertyToID("Burst Delay");
            private static readonly Quaternion DAVFX_MUZZLE_ROTATION = Quaternion.Euler(0f, -90f, 0f);

            private readonly XRayParticleGroupPlayer _group;
            private readonly VisualEffect _visualEffect;
            private readonly bool _usesDavfxMuzzleConvention;

            private int _startFrame;
            private float _startTime;
            private bool _hasPlayed;

            public Transform Transform { get; }
            public bool IsPlaying => _group != null ? _group.IsPlaying : _visualEffect != null && _visualEffect.isActiveAndEnabled && (Time.frameCount - _startFrame <= VFX_STARTUP_FRAMES || _visualEffect.HasAnySystemAwake());
            internal float CurrentTime => _group != null ? _group.CurrentTime : Time.time - _startTime;

            internal Instance(Transform transform, XRayParticleGroupPlayer group, VisualEffect visualEffect)
            {
                Transform = transform;
                _group = group;
                _visualEffect = visualEffect;
                _usesDavfxMuzzleConvention = visualEffect != null && visualEffect.HasBool(DAVFX_SINGLE_BURST) && visualEffect.HasFloat(DAVFX_BURST_DELAY);
                if (_usesDavfxMuzzleConvention)
                {
                    visualEffect.SetBool(DAVFX_SINGLE_BURST, true);
                }
            }

            internal void SetPose(Vector3 position, Quaternion rotation)
            {
                if (!_usesDavfxMuzzleConvention)
                {
                    Transform.SetPositionAndRotation(position, rotation);
                    return;
                }

                Transform parent = Transform.parent;
                if (parent == null)
                {
                    Transform.SetPositionAndRotation(position, rotation * DAVFX_MUZZLE_ROTATION);
                    return;
                }

                Vector3 forward = parent.InverseTransformVector(rotation * Vector3.forward);
                Vector3 up = parent.InverseTransformVector(rotation * Vector3.up);
                Transform.SetLocalPositionAndRotation(parent.InverseTransformPoint(position), Quaternion.LookRotation(forward, up) * DAVFX_MUZZLE_ROTATION);
            }

            internal void Play(bool follow)
            {
                if (_group != null)
                {
                    _group.Play(follow);
                    return;
                }

                _startFrame = Time.frameCount;
                _startTime = Time.time;
                if (!_visualEffect.gameObject.activeSelf)
                {
                    _visualEffect.gameObject.SetActive(true);
                }
                else if (_hasPlayed)
                {
                    _visualEffect.Reinit();
                }

                _hasPlayed = true;
            }

            internal void Stop()
            {
                if (_group != null)
                {
                    _group.Stop(false);
                }
                else if (_visualEffect != null)
                {
                    _visualEffect.gameObject.SetActive(false);
                }
            }
        }
    }
}

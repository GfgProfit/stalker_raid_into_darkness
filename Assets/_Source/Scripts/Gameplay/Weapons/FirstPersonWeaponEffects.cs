using System.Collections.Generic;
using RaidIntoDarkness.Effects;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace RaidIntoDarkness.Weapons
{
    public sealed class FirstPersonWeaponEffects
    {
        private const int MAX_INSTANCES = 8;
        private const float MUZZLE_LIGHT_RANGE = 5f;
        private const float MUZZLE_LIGHT_DURATION = 0.2f;
        private const float MUZZLE_LIGHT_INTENSITY = 200f;

        private readonly Transform _root;
        private readonly Dictionary<GameObject, List<XRayParticleGroupPlayer>> _pools = new Dictionary<GameObject, List<XRayParticleGroupPlayer>>();
        private Light _muzzleLight;
        private float _muzzleLightTimeLeft;

        public FirstPersonWeaponEffects(Transform root)
        {
            _root = root;
        }

        public XRayParticleGroupPlayer Play(GameObject prefab, Transform follow, Vector3 position, Quaternion rotation)
        {
            XRayParticleGroupPlayer player = Acquire(prefab);
            if (player == null)
            {
                return null;
            }

            Transform transform = player.transform;
            transform.SetParent(follow != null ? follow : _root, false);
            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = Vector3.one;
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
            foreach (List<XRayParticleGroupPlayer> pool in _pools.Values)
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    if (pool[i] != null)
                    {
                        pool[i].Stop(false);
                        pool[i].transform.SetParent(_root, false);
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

            foreach (List<XRayParticleGroupPlayer> pool in _pools.Values)
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    if (pool[i] != null && pool[i].transform.parent == follow)
                    {
                        pool[i].Stop(false);
                        pool[i].transform.SetParent(_root, true);
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

            foreach (List<XRayParticleGroupPlayer> pool in _pools.Values)
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    if (pool[i] != null)
                    {
                        Object.Destroy(pool[i].gameObject);
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

        private XRayParticleGroupPlayer Acquire(GameObject prefab)
        {
            if (prefab == null)
            {
                return null;
            }

            if (!_pools.TryGetValue(prefab, out List<XRayParticleGroupPlayer> pool))
            {
                pool = new List<XRayParticleGroupPlayer>();
                _pools.Add(prefab, pool);
            }

            XRayParticleGroupPlayer oldest = null;
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                if (pool[i] == null)
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
                oldest.Stop(false);
                return oldest;
            }

            GameObject instance = Object.Instantiate(prefab, _root, false);
            instance.name = prefab.name;
            XRayParticleGroupPlayer player = instance.GetComponent<XRayParticleGroupPlayer>();
            if (player == null)
            {
                Object.Destroy(instance);
                return null;
            }

            pool.Add(player);
            return player;
        }
    }
}

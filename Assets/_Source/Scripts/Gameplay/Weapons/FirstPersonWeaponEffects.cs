using System.Collections.Generic;
using RaidIntoDarkness.Effects;
using UnityEngine;

namespace RaidIntoDarkness.Weapons
{
    public sealed class FirstPersonWeaponEffects
    {
        private const int MAX_INSTANCES = 8;

        private readonly Transform _root;
        private readonly Dictionary<GameObject, List<XRayParticleGroupPlayer>> _pools = new Dictionary<GameObject, List<XRayParticleGroupPlayer>>();

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

        public void StopAll()
        {
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

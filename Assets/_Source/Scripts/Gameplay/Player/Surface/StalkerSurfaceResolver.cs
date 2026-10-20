using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Player
{
    public sealed class StalkerSurfaceResolver
    {
        private readonly Dictionary<Collider, StalkerSurface> _components = new Dictionary<Collider, StalkerSurface>();
        private readonly Dictionary<PhysicsMaterial, StalkerSurfaceType> _materials = new Dictionary<PhysicsMaterial, StalkerSurfaceType>();
        private readonly Dictionary<TerrainLayer, StalkerSurfaceType> _layers = new Dictionary<TerrainLayer, StalkerSurfaceType>();
        private readonly StalkerSurfaceAudioLibrary _library;

        public StalkerSurfaceResolver(StalkerSurfaceAudioLibrary library)
        {
            _library = library;
            if (_library == null)
            {
                return;
            }

            foreach (StalkerPhysicsMaterialSurface mapping in _library.PhysicsMaterials)
            {
                if (mapping.Material != null)
                {
                    _materials[mapping.Material] = mapping.Type;
                }
            }

            foreach (StalkerTerrainLayerSurface mapping in _library.TerrainLayers)
            {
                if (mapping.Layer != null)
                {
                    _layers[mapping.Layer] = mapping.Type;
                }
            }
        }

        public StalkerSurfaceAudioLibrary Library => _library;

        public StalkerSurfaceType Resolve(Collider collider, Vector3 point, out StalkerSurfaceSource source)
        {
            if (!_components.TryGetValue(collider, out StalkerSurface surface))
            {
                surface = collider.GetComponentInParent<StalkerSurface>();
                _components.Add(collider, surface);
            }

            if (surface != null)
            {
                source = StalkerSurfaceSource.Component;
                return surface.Type;
            }

            PhysicsMaterial material = collider.sharedMaterial;
            if (material != null && _materials.TryGetValue(material, out StalkerSurfaceType type))
            {
                source = StalkerSurfaceSource.PhysicsMaterial;
                return type;
            }

            if (collider is TerrainCollider && TryResolveTerrain(collider.GetComponent<Terrain>(), point, out type))
            {
                source = StalkerSurfaceSource.TerrainLayer;
                return type;
            }

            source = StalkerSurfaceSource.Fallback;
            return _library != null ? _library.FallbackSurface : StalkerSurfaceType.Earth;
        }

        private bool TryResolveTerrain(Terrain terrain, Vector3 point, out StalkerSurfaceType type)
        {
            type = StalkerSurfaceType.Default;
            if (terrain == null || terrain.terrainData == null)
            {
                return false;
            }

            TerrainData data = terrain.terrainData;
            Vector3 local = point - terrain.transform.position;
            int x = Mathf.Clamp(Mathf.FloorToInt(local.x / data.size.x * data.alphamapWidth), 0, data.alphamapWidth - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(local.z / data.size.z * data.alphamapHeight), 0, data.alphamapHeight - 1);
            float[,,] weights = data.GetAlphamaps(x, z, 1, 1);
            TerrainLayer[] layers = data.terrainLayers;
            int best = -1;
            float bestWeight = 0.0f;
            for (int i = 0; i < weights.GetLength(2) && i < layers.Length; i++)
            {
                if (weights[0, 0, i] > bestWeight)
                {
                    bestWeight = weights[0, 0, i];
                    best = i;
                }
            }

            return best >= 0 && layers[best] != null && _layers.TryGetValue(layers[best], out type);
        }
    }
}

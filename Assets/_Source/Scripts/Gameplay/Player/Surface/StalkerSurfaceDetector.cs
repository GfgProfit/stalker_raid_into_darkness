using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Player
{
    public enum StalkerSurfaceSource
    {
        None,
        Component,
        PhysicsMaterial,
        TerrainLayer,
        Fallback
    }

    [DisallowMultipleComponent]
    public sealed class StalkerSurfaceDetector : MonoBehaviour
    {
        [SerializeField] private StalkerSurfaceAudioLibrary _library;
        [SerializeField] private LayerMask _mask = ~0;

        private readonly Dictionary<Collider, StalkerSurface> _components = new Dictionary<Collider, StalkerSurface>();
        private readonly Dictionary<PhysicsMaterial, StalkerSurfaceType> _materials = new Dictionary<PhysicsMaterial, StalkerSurfaceType>();
        private readonly Dictionary<TerrainLayer, StalkerSurfaceType> _layers = new Dictionary<TerrainLayer, StalkerSurfaceType>();
        private bool _initialized;

        public StalkerSurfaceAudioLibrary Library => _library;
        public StalkerSurfaceType Current { get; private set; }
        public StalkerSurfaceSource CurrentSource { get; private set; }
        public Collider CurrentCollider { get; private set; }
        public bool HasGround { get; private set; }

        public void Initialize(StalkerSurfaceAudioLibrary library, LayerMask mask)
        {
            _library = library;
            _mask = mask;
            _initialized = false;
            EnsureMappings();
        }

        public StalkerSurfaceType Detect()
        {
            EnsureMappings();
            float start = _library != null ? _library.RayStartHeight : 1.0f;
            float length = _library != null ? _library.RayLength : 1.1f;
            Vector3 origin = transform.position + Vector3.up * start;
            HasGround = Physics.Raycast(origin, Vector3.down, out RaycastHit hit, length, _mask, QueryTriggerInteraction.Ignore);
            if (!HasGround)
            {
                CurrentCollider = null;
                CurrentSource = StalkerSurfaceSource.None;
                return Current;
            }

            CurrentCollider = hit.collider;
            Current = Resolve(hit, out StalkerSurfaceSource source);
            CurrentSource = source;
            return Current;
        }

        public StalkerSurfaceType Resolve(RaycastHit hit, out StalkerSurfaceSource source)
        {
            Collider collider = hit.collider;
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

            if (collider is TerrainCollider && TryResolveTerrain(collider.GetComponent<Terrain>(), hit.point, out type))
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

        private void EnsureMappings()
        {
            if (_initialized || _library == null)
            {
                return;
            }

            _materials.Clear();
            _layers.Clear();
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

            _initialized = true;
        }
    }
}

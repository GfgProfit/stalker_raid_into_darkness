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

        private StalkerSurfaceResolver _resolver;

        public StalkerSurfaceAudioLibrary Library => _library;
        public StalkerSurfaceType Current { get; private set; }
        public StalkerSurfaceSource CurrentSource { get; private set; }
        public Collider CurrentCollider { get; private set; }
        public bool HasGround { get; private set; }

        public void Initialize(StalkerSurfaceAudioLibrary library, LayerMask mask)
        {
            _library = library;
            _mask = mask;
            _resolver = null;
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
            EnsureMappings();
            return _resolver.Resolve(hit.collider, hit.point, out source);
        }

        private void EnsureMappings()
        {
            if (_resolver != null && _resolver.Library == _library)
            {
                return;
            }

            _resolver = new StalkerSurfaceResolver(_library);
        }
    }
}

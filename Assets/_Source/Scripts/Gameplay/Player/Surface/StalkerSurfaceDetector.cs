using System.Collections.Generic;
using UnityEngine;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Player
{
    public enum StalkerSurfaceSource
    {
        None,
        Component,
        PhysicsMaterial,
        TerrainLayer,
        Fallback,
        XRayMaterial
    }

    [DisallowMultipleComponent]
    public sealed class StalkerSurfaceDetector : MonoBehaviour
    {
        [SerializeField] private StalkerSurfaceAudioLibrary _library;
        [SerializeField] private LayerMask _mask = ~0;

        private readonly List<XRayPassableContact> _passableContacts = new List<XRayPassableContact>();
        private StalkerSurfaceResolver _resolver;
        private CharacterController _controller;

        public StalkerSurfaceAudioLibrary Library => _library;
        public StalkerSurfaceType Current { get; private set; }
        public StalkerSurfaceSource CurrentSource { get; private set; }
        public Collider CurrentCollider { get; private set; }
        public bool HasGround { get; private set; }
        public string CurrentMaterialName => StalkerSurfaceTypes.GetMaterialName(Current);

        public void Initialize(StalkerSurfaceAudioLibrary library, LayerMask mask)
        {
            _library = library;
            _mask = mask;
            _resolver = null;
            _controller = GetComponent<CharacterController>();
            EnsureMappings();
        }

        public StalkerSurfaceType Detect()
        {
            EnsureMappings();
            float start = _library != null ? _library.RayStartHeight : 1.0f;
            float length = _library != null ? _library.RayLength : 1.1f;
            if (_controller != null)
            {
                Vector3 scale = transform.lossyScale;
                float radius = _controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                float slopeCosine = Mathf.Cos(Mathf.Clamp(_controller.slopeLimit, 0.0f, 89.0f) * Mathf.Deg2Rad);
                float slopeHeight = radius * (1.0f / slopeCosine - 1.0f);
                float skin = _controller.skinWidth * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                length = Mathf.Max(length, start + slopeHeight + skin);
            }

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
            if (source != StalkerSurfaceSource.Component)
            {
                ApplyPassableMaterial();
            }

            return Current;
        }

        private void ApplyPassableMaterial()
        {
            if (_controller == null || !_controller.enabled)
            {
                return;
            }

            Transform controllerTransform = _controller.transform;
            Vector3 scale = controllerTransform.lossyScale;
            float radius = _controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float halfAxis = Mathf.Max(0.0f, _controller.height * Mathf.Abs(scale.y) * 0.5f - radius);
            Vector3 center = controllerTransform.TransformPoint(_controller.center);
            Vector3 offset = controllerTransform.up * halfAxis;
            XRayLevelContactSource.GetPassableContacts(center - offset, center + offset, radius, _passableContacts);
            for (int i = 0; i < _passableContacts.Count; i++)
            {
                var material = _passableContacts[i].Material;
                if (!material.IsInjurious && StalkerSurfaceTypes.TryGetType(material.Name, out StalkerSurfaceType type))
                {
                    Current = type;
                    CurrentSource = StalkerSurfaceSource.XRayMaterial;
                }
            }
        }

        public StalkerSurfaceType Resolve(RaycastHit hit, out StalkerSurfaceSource source)
        {
            EnsureMappings();
            return _resolver.Resolve(hit, out source);
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

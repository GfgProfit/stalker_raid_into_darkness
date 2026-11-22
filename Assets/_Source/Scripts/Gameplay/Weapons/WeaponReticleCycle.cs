using UnityEngine;

namespace RaidIntoDarkness.Weapons
{
    [DisallowMultipleComponent]
    public sealed class WeaponReticleCycle : MonoBehaviour
    {
        private static readonly int RED_DOT_TEXTURE = Shader.PropertyToID("_RedDotTex");
        private static readonly int SCOPE_SDF_TEXTURE = Shader.PropertyToID("_ReticleSdf");
        private static readonly int DOT_CENTER = Shader.PropertyToID("_DotCenterWS");
        private static readonly int DOT_RIGHT = Shader.PropertyToID("_DotRightWS");
        private static readonly int DOT_UP = Shader.PropertyToID("_DotUpWS");

        [SerializeField] private Renderer _renderer;
        [SerializeField] private Texture2D[] _variants = System.Array.Empty<Texture2D>();
        [SerializeField] private bool _scopeSdf;
        [SerializeField, Min(0f)] private float _dotProjectionSize;

        private MaterialPropertyBlock _properties;
        private int _index;

        public int VariantCount => _variants.Length;
        public int VariantIndex => _index;

        private void LateUpdate()
        {
            if (_dotProjectionSize <= 0f || _renderer == null)
            {
                return;
            }

            Transform surface = _renderer.transform;
            Vector3 right = surface.TransformVector(Vector3.right);
            Vector3 up = surface.TransformVector(Vector3.up);
            _properties ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_properties, 0);
            _properties.SetVector(DOT_CENTER, surface.TransformPoint(_renderer.localBounds.center));
            _properties.SetVector(DOT_RIGHT, right / Mathf.Max(right.sqrMagnitude, 0.00000001f));
            _properties.SetVector(DOT_UP, up / Mathf.Max(up.sqrMagnitude, 0.00000001f));
            _renderer.SetPropertyBlock(_properties, 0);
        }

        public void Next()
        {
            if (_renderer == null || _variants.Length < 2)
            {
                return;
            }

            _index = (_index + 1) % _variants.Length;
            _properties ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_properties, 0);
            _properties.SetTexture(_scopeSdf ? SCOPE_SDF_TEXTURE : RED_DOT_TEXTURE, _variants[_index]);
            _renderer.SetPropertyBlock(_properties, 0);
        }
    }
}

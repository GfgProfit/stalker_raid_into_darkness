using UnityEngine;

namespace RaidIntoDarkness.FirstPerson
{
    internal sealed class FirstPersonContactBox
    {
        private const float CONTACT_MARGIN = 0.018f;

        private readonly Transform _bone;
        private readonly Bounds _localBounds;
        private readonly Renderer _renderer;
        private readonly bool _isHand;

        public FirstPersonContactBox(Transform bone, Bounds localBounds, Renderer renderer)
        {
            _bone = bone;
            _localBounds = localBounds;
            _renderer = renderer;
            _isHand = renderer == null;
        }

        public bool IsHand => _isHand;
        public Vector3 Center { get; private set; }
        public Vector3 Extents { get; private set; }
        public Quaternion Rotation { get; private set; }
        public Bounds Bounds { get; private set; }

        public bool Capture()
        {
            if (_bone == null || (!_isHand && (_renderer == null || !_renderer.enabled || _renderer.forceRenderingOff || !_renderer.gameObject.activeInHierarchy)))
            {
                return false;
            }

            Matrix4x4 matrix = _bone.localToWorldMatrix;
            Vector3 half = _localBounds.extents;
            Vector3 x = matrix.MultiplyVector(new Vector3(half.x, 0.0f, 0.0f));
            Vector3 y = matrix.MultiplyVector(new Vector3(0.0f, half.y, 0.0f));
            Vector3 z = matrix.MultiplyVector(new Vector3(0.0f, 0.0f, half.z));
            if (x.sqrMagnitude + y.sqrMagnitude + z.sqrMagnitude < 0.0000001f)
            {
                return false;
            }

            Center = matrix.MultiplyPoint3x4(_localBounds.center);
            Rotation = _bone.rotation;
            Quaternion inverse = Quaternion.Inverse(Rotation);
            Extents = Abs(inverse * x) + Abs(inverse * y) + Abs(inverse * z) + Vector3.one * CONTACT_MARGIN;
            Vector3 worldHalf = Abs(Rotation * new Vector3(Extents.x, 0.0f, 0.0f)) + Abs(Rotation * new Vector3(0.0f, Extents.y, 0.0f)) + Abs(Rotation * new Vector3(0.0f, 0.0f, Extents.z));
            Bounds = new Bounds(Center, worldHalf * 2.0f);
            return true;
        }

        public Vector3 SupportAgainst(Vector3 normal)
        {
            Vector3 localNormal = Quaternion.Inverse(Rotation) * normal;
            Vector3 vertex = new Vector3(SelectExtent(localNormal.x, Extents.x), SelectExtent(localNormal.y, Extents.y), SelectExtent(localNormal.z, Extents.z));
            return Center + Rotation * vertex;
        }

        private static float SelectExtent(float value, float extent) => Mathf.Abs(value) > 0.05f ? -Mathf.Sign(value) * extent : 0.0f;

        private static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }
}

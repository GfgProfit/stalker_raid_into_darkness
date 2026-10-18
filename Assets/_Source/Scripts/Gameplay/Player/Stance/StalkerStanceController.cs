using UnityEngine;

namespace RaidIntoDarkness.Player
{
    public readonly struct StalkerCollisionShape
    {
        public StalkerCollisionShape(float radius, float height, float cameraHeight)
        {
            Radius = radius;
            Height = height;
            CameraHeight = cameraHeight;
        }

        public float Radius { get; }
        public float Height { get; }
        public float CameraHeight { get; }
    }

    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public sealed class StalkerStanceController : MonoBehaviour
    {
        public const int STAND_BOX = 0;
        public const int CROUCH_BOX = 1;
        public const int LOW_CROUCH_BOX = 2;

        private const float HAT_RADIUS_DIVIDER = 1.2f;
        private const float MIN_CYLINDER_HEIGHT = 0.01f;

        private static readonly Collider[] OVERLAP_BUFFER = new Collider[16];

        [SerializeField] private StalkerMovementProfile _profile;

        private CharacterController _controller;
        private int _currentBox = -1;
        private float _currentCameraHeight = -1.0f;

        public int CurrentBox => _currentBox;
        public float CameraHeight => _currentCameraHeight;
        public float TargetCameraHeight => GetShape(_currentBox < 0 ? STAND_BOX : _currentBox).CameraHeight;
        public StalkerMovementProfile Profile => _profile;

        public void Initialize(StalkerMovementProfile profile)
        {
            _profile = profile;
            EnsureController();
            Apply(STAND_BOX);
            _currentCameraHeight = TargetCameraHeight;
        }

        public StalkerCollisionShape GetShape(int box)
        {
            StalkerActorValues actor = _profile.Actor;
            Vector3 halfSize = actor.GetBoxSize(box);
            float shift = _profile.Engine.CameraIntoCollisionShift;
            float sizeX = halfSize.x * 2.0f;
            float sizeY = halfSize.y * 2.0f + shift;
            float sizeZ = halfSize.z * 2.0f;
            float radius = Mathf.Min(sizeX, sizeZ) * 0.5f;
            float cylinder = Mathf.Max(sizeY - 2.0f * radius, MIN_CYLINDER_HEIGHT);
            float top = radius + cylinder + radius / HAT_RADIUS_DIVIDER;
            float cameraHeight = actor.CameraHeightFactor * (sizeY - shift);
            return new StalkerCollisionShape(Mathf.Min(radius, top * 0.5f), top, cameraHeight);
        }

        public bool TryActivateBox(int box)
        {
            if (box == _currentBox)
            {
                return true;
            }

            StalkerCollisionShape shape = GetShape(box);
            if (_currentBox >= 0 && shape.Height > GetShape(_currentBox).Height && !HasRoom(shape))
            {
                return false;
            }

            Apply(box);
            return true;
        }

        public bool HasRoom(int box)
        {
            return HasRoom(GetShape(box));
        }

        public void UpdateCameraHeight(float deltaTime)
        {
            float target = TargetCameraHeight;
            if (_currentCameraHeight < 0.0f)
            {
                _currentCameraHeight = target;
                return;
            }

            float blend = 1.0f - Mathf.Exp(-_profile.Engine.CameraHeightInterpolationSpeed * deltaTime);
            _currentCameraHeight = Mathf.Lerp(_currentCameraHeight, target, blend);
        }

        private bool HasRoom(StalkerCollisionShape shape)
        {
            EnsureController();
            float skin = _controller.skinWidth;
            Vector3 feet = transform.position;
            Vector3 bottom = feet + Vector3.up * (shape.Radius + skin);
            Vector3 top = feet + Vector3.up * Mathf.Max(shape.Height - shape.Radius, shape.Radius + skin);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, shape.Radius, OVERLAP_BUFFER, _profile.Unity.CollisionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (OVERLAP_BUFFER[i] != _controller && !OVERLAP_BUFFER[i].transform.IsChildOf(transform))
                {
                    return false;
                }
            }

            return true;
        }

        private void Apply(int box)
        {
            StalkerCollisionShape shape = GetShape(box);
            _controller.radius = shape.Radius;
            _controller.height = shape.Height;
            _controller.center = new Vector3(0.0f, shape.Height * 0.5f + _controller.skinWidth, 0.0f);
            _controller.stepOffset = Mathf.Min(_profile.Unity.StepOffset, shape.Height * 0.5f);
            _currentBox = box;
        }

        private void EnsureController()
        {
            if (_controller == null)
            {
                _controller = GetComponent<CharacterController>();
            }
        }
    }
}

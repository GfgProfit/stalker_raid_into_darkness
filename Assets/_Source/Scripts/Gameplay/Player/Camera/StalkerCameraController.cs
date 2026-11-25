using UnityEngine;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    public sealed class StalkerCameraController : MonoBehaviour
    {
        private const float MIN_HUD_FIELD_OF_VIEW = 0.1f;
        private const float MAX_HUD_FIELD_OF_VIEW = 1.0f;
        private const float LEAN_DISTANCE = 0.32f;
        private const float LEAN_ANGLE = 12.0f;
        private const float LEAN_SMOOTH_TIME = 0.11f;
        private const float LEAN_SETTLE_THRESHOLD = 0.001f;
        private const float LEAN_PROBE_RADIUS = 0.07f;
        private const float LEAN_PROBE_CLEARANCE = 0.005f;
        private const int LEAN_PROBE_CAPACITY = 16;

        [SerializeField] private Transform _cameraRoot;
        [SerializeField] private Camera _camera;
        [SerializeField] private Transform _hudRoot;
        [SerializeField] private StalkerCameraRecoil _recoil;
        [SerializeField] private bool _lockCursorOnStart = true;

        private readonly Collider[] _leanOverlaps = new Collider[LEAN_PROBE_CAPACITY];
        private readonly RaycastHit[] _leanHits = new RaycastHit[LEAN_PROBE_CAPACITY];

        private StalkerMovementProfile _profile;
        private float _hudFieldOfView;
        private float _weaponHudFieldOfView;
        private float _weaponHudAimFactor;
        private float _weaponHudFovScale = 1f;
        private float _yaw;
        private float _pitch;
        private float _lean;
        private float _leanVelocity;
        private bool _ladderClamp;
        private float _ladderYaw;

        public Transform CameraRoot => _cameraRoot;
        public Transform HudRoot => _hudRoot;
        public Camera Camera => _camera;
        public StalkerCameraRecoil Recoil => _recoil;
        public float YawDegrees => _yaw * Mathf.Rad2Deg;
        public float PitchDegrees => _pitch * Mathf.Rad2Deg;
        public Vector3 Direction => _cameraRoot != null ? _cameraRoot.forward : transform.forward;
        public bool CursorLocked => Cursor.lockState == CursorLockMode.Locked;
        public bool CursorReleased { get; private set; }
        public int CursorLockFrame { get; private set; } = -1;

        internal Vector3 CenteredPosition => _cameraRoot.position - GetLeanOffset(_lean);

        public float HudFieldOfView
        {
            get => _hudFieldOfView;
            set
            {
                _hudFieldOfView = Mathf.Clamp(value, MIN_HUD_FIELD_OF_VIEW, MAX_HUD_FIELD_OF_VIEW);
                ApplyHudFieldOfView();
            }
        }

        public void Initialize(StalkerMovementProfile profile)
        {
            _profile = profile;
            _yaw = transform.eulerAngles.y * Mathf.Deg2Rad;
            _pitch = 0.0f;
            if (_camera != null)
            {
                _camera.fieldOfView = profile.Camera.FieldOfView;
                _camera.nearClipPlane = profile.Camera.NearClip;
            }

            HudFieldOfView = profile.Camera.HudFieldOfView;

            if (_lockCursorOnStart && Application.isPlaying)
            {
                SetCursorLocked(true);
            }
        }

        public void SetWeaponZoom(float zoom)
        {
            if (_camera != null && _profile != null)
            {
                float halfTangent = Mathf.Tan(_profile.Camera.FieldOfView * 0.5f * Mathf.Deg2Rad);
                _camera.fieldOfView = 2f * Mathf.Atan(halfTangent / Mathf.Max(1f, zoom)) * Mathf.Rad2Deg;
                ApplyHudFieldOfView();
            }
        }

        public void SetWeaponHudFieldOfView(float fieldOfView, float aimFactor, float scale = 1f)
        {
            _weaponHudFieldOfView = float.IsFinite(fieldOfView) && fieldOfView > 0f ? Mathf.Clamp(fieldOfView, MIN_HUD_FIELD_OF_VIEW, MAX_HUD_FIELD_OF_VIEW) : 0f;
            _weaponHudAimFactor = float.IsFinite(aimFactor) ? Mathf.Clamp01(aimFactor) : 0f;
            _weaponHudFovScale = float.IsFinite(scale) ? Mathf.Clamp(scale, 0.5f, 1f) : 1f;
            ApplyHudFieldOfView();
        }

        public void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            CursorLockFrame = locked && CursorReleased ? Time.frameCount : CursorLockFrame;
            CursorReleased = !locked;
            Cursor.visible = !locked;
        }

        public void Look(Vector2 mouseDelta)
        {
            if (!CursorLocked)
            {
                return;
            }

            StalkerCameraSettings settings = _profile.Camera;
            float scale = settings.MouseSensitivity / settings.MouseScaleDivider;
            float vertical = mouseDelta.y * scale * settings.MouseSensitivityVertical * settings.VerticalScale * (settings.InvertMouse ? -1.0f : 1.0f);
            _yaw += mouseDelta.x * scale;
            _pitch = Mathf.Clamp(_pitch - vertical, settings.PitchLimits.x, settings.PitchLimits.y);
            if (_ladderClamp)
            {
                float relative = Mathf.DeltaAngle(_ladderYaw * Mathf.Rad2Deg, _yaw * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                _yaw = _ladderYaw + Mathf.Clamp(relative, settings.LadderYawLimits.x, settings.LadderYawLimits.y);
            }
        }

        public void SetLook(float yawDegrees, float pitchDegrees)
        {
            StalkerCameraSettings settings = _profile.Camera;
            _yaw = yawDegrees * Mathf.Deg2Rad;
            _pitch = Mathf.Clamp(pitchDegrees * Mathf.Deg2Rad, settings.PitchLimits.x, settings.PitchLimits.y);
        }

        public void SetLadder(StalkerLadder ladder)
        {
            _ladderClamp = ladder != null;
            if (ladder != null)
            {
                Vector3 facing = -ladder.Normal;
                _ladderYaw = Mathf.Atan2(facing.x, facing.z);
            }
        }

        public void Apply(float cameraHeight)
        {
            _lean = 0.0f;
            _leanVelocity = 0.0f;
            ApplyPose(cameraHeight);
        }

        internal void ApplyLook()
        {
            transform.rotation = Quaternion.Euler(0.0f, _yaw * Mathf.Rad2Deg, 0.0f);
            if (_cameraRoot != null)
            {
                Quaternion look = Quaternion.Euler(_pitch * Mathf.Rad2Deg, 0.0f, 0.0f);
                Quaternion recoil = _recoil != null ? _recoil.Rotation : Quaternion.identity;
                _cameraRoot.localRotation = look * recoil * Quaternion.AngleAxis(-_lean * LEAN_ANGLE, Vector3.forward);
            }
        }

        internal void ApplyLean(float cameraHeight, float input, float deltaTime)
        {
            GetLeanBounds(cameraHeight, input, out float minLean, out float maxLean);
            float target = Mathf.Clamp(input, minLean, maxLean);
            float current = Mathf.Clamp(_lean, minLean, maxLean);
            if (current != _lean)
            {
                _leanVelocity = 0.0f;
            }

            float next = deltaTime > 0.0f && (current != target || _leanVelocity != 0.0f) ? Mathf.SmoothDamp(current, target, ref _leanVelocity, LEAN_SMOOTH_TIME, Mathf.Infinity, deltaTime) : current;
            _lean = Mathf.Clamp(next, minLean, maxLean);
            if (_lean != next)
            {
                _leanVelocity = 0.0f;
            }

            if (Mathf.Abs(target - _lean) <= LEAN_SETTLE_THRESHOLD && Mathf.Abs(_leanVelocity) <= LEAN_SETTLE_THRESHOLD / LEAN_SMOOTH_TIME)
            {
                _lean = target;
                _leanVelocity = 0.0f;
            }

            ApplyPose(cameraHeight);
        }

        private void ApplyPose(float cameraHeight)
        {
            ApplyLook();
            if (_cameraRoot != null)
            {
                _cameraRoot.localPosition = new Vector3(_lean * LEAN_DISTANCE, cameraHeight, 0.0f);
            }

            ApplyHudFieldOfView();
        }

        private void GetLeanBounds(float cameraHeight, float input, out float minLean, out float maxLean)
        {
            minLean = 0.0f;
            maxLean = 0.0f;
            if (_cameraRoot == null || (_lean == 0.0f && input == 0.0f))
            {
                return;
            }

            PhysicsScene physicsScene = gameObject.scene.GetPhysicsScene();
            int collisionMask = _profile.Unity.CollisionMask;
            Vector3 origin = transform.TransformPoint(new Vector3(0.0f, cameraHeight, 0.0f));
            int overlapCount = physicsScene.OverlapSphere(origin, LEAN_PROBE_RADIUS, _leanOverlaps, collisionMask, QueryTriggerInteraction.Ignore);
            if (overlapCount == _leanOverlaps.Length)
            {
                return;
            }

            for (int i = 0; i < overlapCount; i++)
            {
                if (!_leanOverlaps[i].transform.IsChildOf(transform))
                {
                    return;
                }
            }

            if (_lean < 0.0f || input < 0.0f)
            {
                minLean = FindLeanLimit(physicsScene, origin, -1.0f, collisionMask);
            }

            if (_lean > 0.0f || input > 0.0f)
            {
                maxLean = FindLeanLimit(physicsScene, origin, 1.0f, collisionMask);
            }
        }

        private float FindLeanLimit(PhysicsScene physicsScene, Vector3 origin, float lean, int collisionMask)
        {
            Vector3 offset = GetLeanOffset(lean);
            float distance = offset.magnitude;
            if (distance <= Mathf.Epsilon)
            {
                return 0.0f;
            }

            int hitCount = physicsScene.SphereCast(origin, LEAN_PROBE_RADIUS, offset / distance, _leanHits, distance, collisionMask, QueryTriggerInteraction.Ignore);
            if (hitCount == _leanHits.Length)
            {
                return 0.0f;
            }

            float allowedDistance = distance;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = _leanHits[i];
                if (!hit.collider.transform.IsChildOf(transform))
                {
                    allowedDistance = Mathf.Min(allowedDistance, Mathf.Max(0.0f, hit.distance - LEAN_PROBE_CLEARANCE));
                }
            }

            return lean * (allowedDistance / distance);
        }

        private Vector3 GetLeanOffset(float lean)
        {
            return transform.TransformVector(new Vector3(lean * LEAN_DISTANCE, 0.0f, 0.0f));
        }

        private void ApplyHudFieldOfView()
        {
            if (_hudRoot == null || _camera == null || _profile == null)
            {
                return;
            }

            float hudAngleScale = _profile.Camera.HudFieldOfViewScale * _weaponHudFovScale * 0.5f * Mathf.Deg2Rad;
            float hudHalfTangent = Mathf.Tan(_hudFieldOfView * hudAngleScale);
            if (_weaponHudFieldOfView > 0f)
            {
                float targetHalfTangent = Mathf.Tan(_weaponHudFieldOfView * hudAngleScale);
                hudHalfTangent = Mathf.Lerp(hudHalfTangent, targetHalfTangent, _weaponHudAimFactor);
            }

            float scale = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / hudHalfTangent;
            _hudRoot.localScale = new Vector3(scale, scale, 1.0f);
        }
    }
}

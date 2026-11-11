using UnityEngine;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    public sealed class StalkerCameraController : MonoBehaviour
    {
        private const float MIN_HUD_FIELD_OF_VIEW = 0.1f;
        private const float MAX_HUD_FIELD_OF_VIEW = 1.0f;

        [SerializeField] private Transform _cameraRoot;
        [SerializeField] private Camera _camera;
        [SerializeField] private Transform _hudRoot;
        [SerializeField] private StalkerCameraRecoil _recoil;
        [SerializeField] private bool _lockCursorOnStart = true;

        private StalkerMovementProfile _profile;
        private float _hudFieldOfView;
        private float _weaponHudFieldOfView;
        private float _weaponHudAimFactor;
        private float _yaw;
        private float _pitch;
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

        public void SetWeaponHudFieldOfView(float fieldOfView, float aimFactor)
        {
            _weaponHudFieldOfView = float.IsFinite(fieldOfView) && fieldOfView > 0f ? Mathf.Clamp(fieldOfView, MIN_HUD_FIELD_OF_VIEW, MAX_HUD_FIELD_OF_VIEW) : 0f;
            _weaponHudAimFactor = float.IsFinite(aimFactor) ? Mathf.Clamp01(aimFactor) : 0f;
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
            transform.rotation = Quaternion.Euler(0.0f, _yaw * Mathf.Rad2Deg, 0.0f);
            if (_cameraRoot != null)
            {
                _cameraRoot.localPosition = new Vector3(0.0f, cameraHeight, 0.0f);
                _cameraRoot.localRotation = Quaternion.Euler(_pitch * Mathf.Rad2Deg, 0.0f, 0.0f) * (_recoil != null ? _recoil.Rotation : Quaternion.identity);
            }

            ApplyHudFieldOfView();
        }

        private void ApplyHudFieldOfView()
        {
            if (_hudRoot == null || _camera == null || _profile == null)
            {
                return;
            }

            float hudHalfTangent = Mathf.Tan(_hudFieldOfView * _profile.Camera.HudFieldOfViewScale * 0.5f * Mathf.Deg2Rad);
            if (_weaponHudFieldOfView > 0f)
            {
                float targetHalfTangent = Mathf.Tan(_weaponHudFieldOfView * _profile.Camera.HudFieldOfViewScale * 0.5f * Mathf.Deg2Rad);
                hudHalfTangent = Mathf.Lerp(hudHalfTangent, targetHalfTangent, _weaponHudAimFactor);
            }

            float scale = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / hudHalfTangent;
            _hudRoot.localScale = new Vector3(scale, scale, 1.0f);
        }
    }
}

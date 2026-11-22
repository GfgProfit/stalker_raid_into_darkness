using UnityEngine;
using UnityEngine.InputSystem;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    public sealed class StalkerPlayerInput : MonoBehaviour
    {
        private const string PLAYER_MAP = "Player";
        private const float AXIS_THRESHOLD = 0.5f;

        [SerializeField] private InputActionAsset _actions;

        private InputAction _move;
        private InputAction _look;
        private InputAction _jump;
        private InputAction _sprint;
        private InputAction _walk;
        private InputAction _crouch;
        private InputAction _lowCrouch;
        private InputAction _releaseCursor;
        private InputAction _captureCursor;
        private InputAction _fire;
        private InputAction _aim;
        private InputAction _reload;
        private InputAction _nextAmmo;
        private InputAction _fireMode;
        private InputAction _nextReticle;
        private InputAction _primaryWeapon;
        private InputAction _secondaryWeapon;
        private InputAction _bolt;

        public Vector2 LookDelta => _look != null ? _look.ReadValue<Vector2>() : Vector2.zero;
        public float ScopeZoomScroll => isActiveAndEnabled && Cursor.lockState == CursorLockMode.Locked && Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
        public bool ReleaseCursorPressed => WasPressed(_releaseCursor);
        public bool CaptureCursorPressed => WasPressed(_captureCursor);
        public bool FirePressed => WasPressed(_fire);
        public bool FireHeld => _fire != null && _fire.IsPressed();
        public bool FireReleased => WasReleased(_fire);
        public bool AimPressed => WasPressed(_aim);
        public bool AimReleased => WasReleased(_aim);
        public bool AimHeld => _aim != null && _aim.IsPressed();
        public bool ReloadPressed => WasPressed(_reload);
        public bool NextAmmoPressed => WasPressed(_nextAmmo);
        public bool FireModePressed => WasPressed(_fireMode);
        public bool NextReticlePressed => WasPressed(_nextReticle);
        public bool PrimaryWeaponPressed => WasPressed(_primaryWeapon);
        public bool SecondaryWeaponPressed => WasPressed(_secondaryWeapon);
        public bool BoltPressed => WasPressed(_bolt);
        public bool CrouchKeyPressed => WasPressed(_crouch) || WasPressed(_lowCrouch);
        public bool CrouchKeyReleased => WasReleased(_crouch) || WasReleased(_lowCrouch);
        public InputActionAsset Actions => _actions;

        private void OnEnable()
        {
            if (_actions == null)
            {
                Debug.LogError($"{nameof(StalkerPlayerInput)} on '{name}': no InputActionAsset assigned.", this);
                return;
            }

            InputActionMap player = _actions.FindActionMap(PLAYER_MAP, true);
            _move = player.FindAction("Move", true);
            _look = player.FindAction("Look", true);
            _jump = player.FindAction("Jump", true);
            _sprint = player.FindAction("Sprint", true);
            _walk = player.FindAction("Walk", true);
            _crouch = player.FindAction("Crouch", true);
            _lowCrouch = player.FindAction("LowCrouch", true);
            _releaseCursor = player.FindAction("ReleaseCursor", true);
            _captureCursor = player.FindAction("CaptureCursor", true);
            _fire = player.FindAction("Fire", true);
            _aim = player.FindAction("Aim", true);
            _reload = player.FindAction("Reload", true);
            _nextAmmo = player.FindAction("NextAmmo", true);
            _fireMode = player.FindAction("FireMode", true);
            _nextReticle = player.FindAction("NextReticle", false);
            _primaryWeapon = player.FindAction("PrimaryWeapon", true);
            _secondaryWeapon = player.FindAction("SecondaryWeapon", true);
            _bolt = player.FindAction("Bolt", false);

            player.Enable();
        }

        private void OnDisable()
        {
            if (_actions != null)
            {
                _actions.Disable();
            }
        }

        public StalkerMoveFlags Apply(StalkerMoveFlags wish, StalkerInputSettings settings)
        {
            if (_move == null)
            {
                return wish;
            }

            if (WasPressed(_sprint) && settings.SprintToggle)
            {
                wish = ClearForSprint(wish, settings);
                wish ^= StalkerMoveFlags.Sprint;
            }

            if (WasPressed(_crouch) && settings.CrouchToggle)
            {
                wish ^= StalkerMoveFlags.Crouch;
            }

            if (WasPressed(_walk) && settings.WalkToggle)
            {
                wish ^= StalkerMoveFlags.Accel;
            }

            Vector2 move = _move.ReadValue<Vector2>();
            if (move.y > AXIS_THRESHOLD)
            {
                wish |= StalkerMoveFlags.Forward;
            }

            if (move.y < -AXIS_THRESHOLD)
            {
                wish |= StalkerMoveFlags.Back;
            }

            if (move.x < -AXIS_THRESHOLD)
            {
                wish |= StalkerMoveFlags.LeftStrafe;
            }

            if (move.x > AXIS_THRESHOLD)
            {
                wish |= StalkerMoveFlags.RightStrafe;
            }

            if (_crouch.IsPressed() && !settings.CrouchToggle)
            {
                wish |= StalkerMoveFlags.Crouch;
            }

            if (_walk.IsPressed() && !settings.WalkToggle)
            {
                wish |= StalkerMoveFlags.Accel;
            }

            if (_lowCrouch.IsPressed())
            {
                wish |= StalkerMoveFlags.Crouch | StalkerMoveFlags.Accel;
            }

            if (_sprint.IsPressed() && !settings.SprintToggle)
            {
                wish = ClearForSprint(wish, settings) | StalkerMoveFlags.Sprint;
            }

            if (_jump.IsPressed())
            {
                wish |= StalkerMoveFlags.Jump;
            }
            else
            {
                wish &= ~StalkerMoveFlags.Jump;
            }

            return wish;
        }

        public StalkerMoveFlags ApplyBlocked(StalkerMoveFlags wish, StalkerInputSettings settings)
        {
            StalkerMoveFlags kept = StalkerMoveFlags.None;
            if (settings.CrouchToggle)
            {
                kept |= wish & StalkerMoveFlags.Crouch;
            }

            if (settings.WalkToggle)
            {
                kept |= wish & StalkerMoveFlags.Accel;
            }

            return kept;
        }

        private static StalkerMoveFlags ClearForSprint(StalkerMoveFlags wish, StalkerInputSettings settings)
        {
            if (settings.WalkToggle)
            {
                wish &= ~StalkerMoveFlags.Accel;
            }

            if (settings.CrouchToggle)
            {
                wish &= ~StalkerMoveFlags.Crouch;
            }

            return wish;
        }

        private static bool WasPressed(InputAction action)
        {
            return action != null && action.WasPressedThisFrame();
        }

        private static bool WasReleased(InputAction action)
        {
            return action != null && action.WasReleasedThisFrame();
        }
    }
}

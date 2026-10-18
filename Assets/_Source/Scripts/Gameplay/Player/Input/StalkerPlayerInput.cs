using UnityEngine;
using UnityEngine.InputSystem;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    public sealed class StalkerPlayerInput : MonoBehaviour
    {
        private const string PLAYER_MAP = "Player";
        private const string DEBUG_MAP = "Debug";
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
        private InputAction _toggleOverlay;
        private InputAction _toggleCombat;
        private InputAction _nextOutfit;
        private InputAction _previousOutfit;

        public Vector2 LookDelta => _look != null ? _look.ReadValue<Vector2>() : Vector2.zero;
        public bool ReleaseCursorPressed => WasPressed(_releaseCursor);
        public bool CaptureCursorPressed => WasPressed(_captureCursor);
        public bool ToggleOverlayPressed => WasPressed(_toggleOverlay);
        public bool ToggleCombatPressed => WasPressed(_toggleCombat);
        public bool CrouchKeyPressed => WasPressed(_crouch) || WasPressed(_lowCrouch);
        public bool CrouchKeyReleased => WasReleased(_crouch) || WasReleased(_lowCrouch);
        public bool NextOutfitPressed => WasPressed(_nextOutfit);
        public bool PreviousOutfitPressed => WasPressed(_previousOutfit);
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
            InputActionMap debug = _actions.FindActionMap(DEBUG_MAP, false);
            if (debug != null)
            {
                _toggleOverlay = debug.FindAction("ToggleOverlay");
                _toggleCombat = debug.FindAction("ToggleCombat");
                _nextOutfit = debug.FindAction("NextOutfit");
                _previousOutfit = debug.FindAction("PreviousOutfit");
                debug.Enable();
            }

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

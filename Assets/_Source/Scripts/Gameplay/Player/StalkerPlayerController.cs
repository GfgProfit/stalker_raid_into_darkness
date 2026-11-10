using RaidIntoDarkness.FirstPerson;
using UnityEngine;

namespace RaidIntoDarkness.Player
{
    [RequireComponent(typeof(StalkerMovementMotor))]
    [DisallowMultipleComponent]
    public sealed class StalkerPlayerController : MonoBehaviour
    {
        private const float MAX_FRAME_DELTA = 0.1f;

        [SerializeField] private StalkerMovementProfile _profile;
        [SerializeField] private StalkerMovementMotor _motor;
        [SerializeField] private StalkerPlayerInput _input;
        [SerializeField] private StalkerCameraController _camera;
        [SerializeField] private StalkerVisibleBody _visibleBody;
        [SerializeField] private StalkerBodyAnimationPlayer _bodyPlayer;
        [SerializeField] private FirstPersonRig _rig;
        [SerializeField] private FirstPersonPickupAnimator _pickup;
        [SerializeField] private FirstPersonHudAnimator _hudAnimator;
        [SerializeField] private StalkerScreenFade _screenFade;
        [SerializeField] private StalkerCameraEffects _cameraEffects;
        [SerializeField] private StalkerFootstepSynchronizer _footsteps;
        [SerializeField] private StalkerPlayerFoley _foley;
        [SerializeField] private StalkerSurfaceDetector _surfaceDetector;
        [SerializeField] private string _outfitSection = FirstPersonOutfitCatalog.NO_OUTFIT_SECTION;

        private string _appliedSection;
        private int _outfitIndex = -1;
        private float _savedHudFieldOfView = -1.0f;
        private CharacterController _characterController;
        private StalkerVegetationAudio _vegetationAudio;

        public StalkerMovementProfile Profile => _profile;
        public StalkerMovementMotor Motor => _motor;
        public StalkerCameraController CameraController => _camera;
        public StalkerVisibleBody VisibleBody => _visibleBody;
        public StalkerBodyAnimationPlayer BodyPlayer => _bodyPlayer;
        public FirstPersonRig Rig => _rig;
        public FirstPersonPickupAnimator Pickup => _pickup;
        public FirstPersonHudAnimator HudAnimator => _hudAnimator;
        public StalkerScreenFade ScreenFade => _screenFade;
        public StalkerCameraEffects CameraEffects => _cameraEffects;
        public StalkerFootstepSynchronizer Footsteps => _footsteps;
        public StalkerPlayerFoley Foley => _foley;
        public StalkerSurfaceDetector SurfaceDetector => _surfaceDetector;
        public StalkerVegetationAudio VegetationAudio => _vegetationAudio;
        public StalkerMovementState State => _motor.State;
        public string OutfitSection => _appliedSection;

        public StalkerMoveFlags ScriptedWish { get; set; }
        public bool GameplayInputBlocked { get; set; }

        public bool Combat
        {
            get => _visibleBody != null && _visibleBody.Combat;
            set
            {
                if (_visibleBody != null)
                {
                    _visibleBody.Combat = value;
                }
            }
        }

        private void Awake()
        {
            if (_profile == null)
            {
                Debug.LogError($"{nameof(StalkerPlayerController)} on '{name}': no {nameof(StalkerMovementProfile)} assigned.", this);
                enabled = false;
                return;
            }

            _motor.Initialize(_profile);
            _characterController = _motor.GetComponent<CharacterController>();
            _camera.Initialize(_profile);
            if (_surfaceDetector != null)
            {
                _surfaceDetector.Initialize(_surfaceDetector.Library, _profile.Unity.CollisionMask);
            }

            if (_foley != null)
            {
                _foley.Initialize();
                _vegetationAudio = new StalkerVegetationAudio(_foley);
            }

            _motor.JumpStarted += OnJumpStarted;
            _motor.Landed += OnLanded;
            _motor.ContactRestored += OnContactRestored;
            if (_footsteps != null)
            {
                _footsteps.Footstep += OnFootstep;
            }

            _visibleBody.Initialize(_profile);
            if (_rig != null)
            {
                _rig.OutfitApplied += OnOutfitApplied;
            }

            if (_hudAnimator != null)
            {
                _hudAnimator.Started += OnHudMotionStarted;
                _hudAnimator.Stopped += OnHudMotionStopped;
            }
        }

        private void Start()
        {
            ApplyOutfit(_outfitSection);
        }

        private void OnDestroy()
        {
            _vegetationAudio?.Dispose();
            if (_rig != null)
            {
                _rig.OutfitApplied -= OnOutfitApplied;
            }

            if (_hudAnimator != null)
            {
                _hudAnimator.Started -= OnHudMotionStarted;
                _hudAnimator.Stopped -= OnHudMotionStopped;
            }

            if (_motor != null)
            {
                _motor.JumpStarted -= OnJumpStarted;
                _motor.Landed -= OnLanded;
                _motor.ContactRestored -= OnContactRestored;
            }

            if (_footsteps != null)
            {
                _footsteps.Footstep -= OnFootstep;
            }
        }

        public bool ApplyOutfit(string section)
        {
            _outfitSection = section;
            bool applied = _rig != null && _rig.ApplyOutfit(section);
            if (!applied)
            {
                _appliedSection = section;
            }

            return applied;
        }

        private void Update()
        {
            float deltaTime = Mathf.Min(Time.deltaTime, MAX_FRAME_DELTA);
            if (!GameplayInputBlocked)
            {
                HandleDebugInput();
            }

            if (_outfitSection != _appliedSection && _rig != null && _rig.Catalog != null)
            {
                ApplyOutfit(_outfitSection);
            }

            if (_input != null && GameplayInputBlocked)
            {
                _motor.Wish = _input.ApplyBlocked(_motor.Wish, _profile.Input);
            }
            else if (_input != null)
            {
                if (_input.ReleaseCursorPressed)
                {
                    _camera.SetCursorLocked(false);
                }
                else if (_input.CaptureCursorPressed && !_camera.CursorLocked)
                {
                    _camera.SetCursorLocked(true);
                }

                _camera.Look(_input.LookDelta);
                HandleCrouchKey();
                _motor.Wish = _input.Apply(_motor.Wish, _profile.Input);
            }

            _motor.Wish |= ScriptedWish;
            _camera.SetLadder(_motor.RealFlags.Has(StalkerMoveFlags.Climb) ? _motor.Ladder : null);
            _camera.Apply(_motor.Stance.CameraHeight);
            _motor.SetView(_camera.YawDegrees, _camera.Direction);
            _motor.Tick(deltaTime);
            _vegetationAudio?.Tick(_characterController, _motor.Velocity);
            _motor.ClearHeldCommands();
            _camera.Apply(_motor.Stance.CameraHeight);
            _visibleBody.Tick(_motor.State, _camera, deltaTime);
            if (_footsteps != null)
            {
                _footsteps.Tick(_motor.State, _bodyPlayer, deltaTime);
            }

            if (_cameraEffects != null)
            {
                _cameraEffects.Tick(_motor.State, _motor.Condition.IsLimping(), _footsteps != null ? _footsteps.CyclePhase : -1.0f, deltaTime);
            }
        }

        private void HandleDebugInput()
        {
            if (_input == null)
            {
                return;
            }

            if (_input.ToggleCombatPressed)
            {
                Combat = !Combat;
            }

            if ((_input.NextOutfitPressed || _input.PreviousOutfitPressed) && _rig != null && _rig.Catalog != null && _rig.Catalog.Entries.Count > 0)
            {
                int count = _rig.Catalog.Entries.Count;
                _outfitIndex = (_outfitIndex + (_input.NextOutfitPressed ? 1 : count - 1)) % count;
                ApplyOutfit(_rig.Catalog.Entries[_outfitIndex].Section);
            }
        }

        private void HandleCrouchKey()
        {
            bool pressed = _input.CrouchKeyPressed;
            if (!pressed && !_input.CrouchKeyReleased)
            {
                return;
            }

            if (pressed && _foley != null)
            {
                _foley.PlayCrouchKey();
            }

            if (_cameraEffects != null)
            {
                _cameraEffects.OnCrouchKey(pressed);
            }
        }

        private void OnFootstep(StalkerFootstep step)
        {
            if (_foley != null)
            {
                _foley.PlayFootstep(step);
            }

            if (_surfaceDetector != null)
            {
                if (_foley == null)
                {
                    _surfaceDetector.Detect();
                }

                if (_surfaceDetector.HasGround)
                {
                    _motor.Condition.SetFootstepSurface(_surfaceDetector.Current);
                }
            }

            if (_cameraEffects != null)
            {
                _cameraEffects.OnFootstep(_motor.State, _motor.Condition.Power);
            }
        }

        private void OnJumpStarted()
        {
            if (_foley != null)
            {
                _foley.PlayJump();
            }

            if (_cameraEffects != null)
            {
                _cameraEffects.OnJump();
            }
        }

        private void OnLanded(float contactSpeed)
        {
            if (_foley != null)
            {
                _foley.PlayLanding(contactSpeed);
            }

            if (_cameraEffects != null)
            {
                _cameraEffects.OnLanded(contactSpeed);
            }
        }

        private void OnContactRestored(float contactSpeed)
        {
            if (_cameraEffects != null)
            {
                _cameraEffects.OnContact(contactSpeed);
            }
        }

        private void OnOutfitApplied(FirstPersonOutfitEntry entry, bool bodyChanged)
        {
            _appliedSection = entry.Section;
            _outfitSection = entry.Section;
            if (_rig.Catalog != null)
            {
                _outfitIndex = IndexOf(entry);
            }

            _visibleBody.SetLibrary(entry.BodyAnimations);
            if (bodyChanged || !_bodyPlayer.IsBound || _bodyPlayer.Body != _rig.Body)
            {
                _bodyPlayer.Bind(_rig.Body, entry.BodyAnimations);
            }
        }

        private void OnHudMotionStarted(FirstPersonHudMotion motion)
        {
            if (_cameraEffects != null && motion.CameraEffect != null)
            {
                _cameraEffects.PlayItemEffect(motion.CameraEffect);
            }

            if (_camera != null && motion.HudFieldOfView > 0.0f)
            {
                _savedHudFieldOfView = _camera.HudFieldOfView;
                _camera.HudFieldOfView = motion.HudFieldOfView;
            }
        }

        private void OnHudMotionStopped(FirstPersonHudMotion motion, bool completed)
        {
            if (!completed && _cameraEffects != null)
            {
                _cameraEffects.StopItemEffect();
            }

            if (_camera != null && _savedHudFieldOfView > 0.0f)
            {
                _camera.HudFieldOfView = _savedHudFieldOfView;
                _savedHudFieldOfView = -1.0f;
            }
        }

        private int IndexOf(FirstPersonOutfitEntry entry)
        {
            for (int i = 0; i < _rig.Catalog.Entries.Count; i++)
            {
                if (_rig.Catalog.Entries[i] == entry)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}

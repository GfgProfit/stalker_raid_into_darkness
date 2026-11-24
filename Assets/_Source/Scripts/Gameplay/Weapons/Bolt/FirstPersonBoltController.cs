using RaidIntoDarkness.FirstPerson;
using RaidIntoDarkness.Player;
using UnityEngine;

namespace RaidIntoDarkness.Weapons
{
    public enum FirstPersonBoltState
    {
        Hidden,
        Drawing,
        Ready,
        ThrowBegin,
        ThrowIdle,
        ThrowEnd,
        Bore,
        Holstering
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-10)]
    public sealed class FirstPersonBoltController : MonoBehaviour, IFirstPersonHandsOverlay
    {
        private const float TEMPORARY_REDRAW_DELAY = 0.5f;
        private const float BORE_DELAY = 20f;

        [SerializeField] private StalkerPlayerController _player;
        [SerializeField] private StalkerPlayerInput _input;
        [SerializeField] private FirstPersonWeaponController _weapons;
        [SerializeField] private BoltDefinition _definition;
        [SerializeField] private AudioSource _audio;

        private FirstPersonRig _rig;
        private FirstPersonBoltVisual _visual;
        private AnimationClip _loop;
        private float _loopSpeed = 1f;
        private float _stateTime;
        private float _stateDuration;
        private float _chargeTime;
        private float _idleTime;
        private float _redrawDelay;
        private float _savedFieldOfView;
        private bool _constantPower;
        private bool _releaseQueued;
        private bool _spawned;
        private bool _fieldOfViewSaved;
        private bool _requested;

        public FirstPersonBoltState State { get; private set; }
        public bool Selected { get; private set; }
        public float ThrowSpeed => _constantPower ? BoltThrowMath.CONSTANT_SPEED : BoltThrowMath.ChargeSpeed(_chargeTime);
        public GameObject Model => _visual != null ? _visual.Model : null;

        public void Select()
        {
            if (Selected)
            {
                Selected = false;
                Holster();
                return;
            }
            Selected = true;
            _requested = true;
            _redrawDelay = 0f;
        }

        public void Holster()
        {
            if (State == FirstPersonBoltState.Hidden || State == FirstPersonBoltState.Holstering)
            {
                return;
            }
            SetState(FirstPersonBoltState.Holstering, _definition.Holster, false);
            PlaySound(_definition.HolsterSound);
        }

        public bool BeginThrow(bool charged)
        {
            if (State != FirstPersonBoltState.Ready && State != FirstPersonBoltState.Bore)
            {
                return false;
            }
            _constantPower = !charged;
            _releaseQueued = !charged;
            _chargeTime = 0f;
            _spawned = false;
            _idleTime = 0f;
            SetState(FirstPersonBoltState.ThrowBegin, _definition.ThrowBegin, false);
            return true;
        }

        public void ReleaseThrow()
        {
            if (State == FirstPersonBoltState.ThrowBegin)
            {
                _releaseQueued = true;
            }
            else if (State == FirstPersonBoltState.ThrowIdle)
            {
                StartThrowEnd();
            }
        }

        public bool TryPlayOverlay(AnimationClip clip, float speed)
        {
            return _visual != null && State == FirstPersonBoltState.Ready && _visual.TryPlayOverlay(clip, speed);
        }

        public void StopOverlay()
        {
            if (_visual != null)
            {
                _visual.StopOverlay();
            }
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Selected = false;
            Hide();
            Unsubscribe();
        }

        private void Update()
        {
            bool inputAllowed = _player != null && _input != null && !_player.GameplayInputBlocked;
            if (inputAllowed)
            {
                if (_input.BoltPressed)
                {
                    Select();
                }
                else if (Selected && HasWeaponSelection())
                {
                    Selected = false;
                    Holster();
                }
            }
            StalkerCameraController camera = _player != null ? _player.CameraController : null;
            bool cursorLocked = camera == null || (!camera.CursorReleased && camera.CursorLockFrame != Time.frameCount);
            if (inputAllowed && cursorLocked && Selected)
            {
                if (_input.FirePressed)
                {
                    BeginThrow(false);
                }
                else if (_input.AimPressed)
                {
                    BeginThrow(true);
                }
                if (_input.AimReleased)
                {
                    ReleaseThrow();
                }
            }
            else if (State == FirstPersonBoltState.ThrowBegin || State == FirstPersonBoltState.ThrowIdle)
            {
                SetState(FirstPersonBoltState.Ready, _definition.Idle, true);
            }
            Tick(Time.deltaTime);
        }

        public void Tick(float deltaTime)
        {
            Subscribe();
            if (_definition == null || _rig == null)
            {
                return;
            }
            _redrawDelay = Mathf.Max(0f, _redrawDelay - deltaTime);
            if (State == FirstPersonBoltState.Hidden)
            {
                if (Selected && _redrawDelay <= 0f)
                {
                    TryDraw();
                }
                return;
            }
            if (_visual == null || !_visual.IsBound || _rig.HandsOwner != (object)this)
            {
                Hide();
                return;
            }
            _visual.Tick(deltaTime);
            _stateTime += deltaTime;
            switch (State)
            {
                case FirstPersonBoltState.Drawing:
                    if (_stateTime >= _stateDuration)
                    {
                        SetState(FirstPersonBoltState.Ready, _definition.Idle, true);
                    }
                    break;
                case FirstPersonBoltState.Ready:
                    TickIdle(deltaTime);
                    break;
                case FirstPersonBoltState.ThrowBegin:
                    if (_stateTime >= _stateDuration)
                    {
                        if (_releaseQueued)
                        {
                            StartThrowEnd();
                        }
                        else
                        {
                            SetState(FirstPersonBoltState.ThrowIdle, _definition.ThrowIdle, true);
                        }
                    }
                    break;
                case FirstPersonBoltState.ThrowIdle:
                    _chargeTime += deltaTime;
                    break;
                case FirstPersonBoltState.ThrowEnd:
                    if (!_spawned && _stateTime >= _definition.ThrowReleaseTime / _definition.GetMotionSpeed(_definition.ThrowEnd))
                    {
                        SpawnProjectile();
                    }
                    if (_stateTime >= _stateDuration)
                    {
                        StartDraw(false);
                    }
                    break;
                case FirstPersonBoltState.Bore:
                    if (_stateTime >= _stateDuration || IsMoving)
                    {
                        _idleTime = 0f;
                        SetState(FirstPersonBoltState.Ready, _definition.Idle, true);
                    }
                    break;
                case FirstPersonBoltState.Holstering:
                    if (_stateTime >= _stateDuration)
                    {
                        Hide();
                    }
                    break;
            }
        }

        private bool IsMoving => _player != null && _player.Motor != null && _player.State.IsMoving;

        private bool HasWeaponSelection()
        {
            return _weapons != null && ((_input.PrimaryWeaponPressed && _weapons.GetSlotWeapon(FirstPersonWeaponController.PRIMARY_SLOT) != null) || (_input.SecondaryWeaponPressed && _weapons.GetSlotWeapon(FirstPersonWeaponController.SECONDARY_SLOT) != null) || (_input.KnifeWeaponPressed && _weapons.GetSlotWeapon(FirstPersonWeaponController.KNIFE_SLOT) != null) || (_input.HandGrenadePressed && _weapons.GetSlotWeapon(FirstPersonWeaponController.HAND_GRENADE_SELECTION) != null));
        }

        private void TickIdle(float deltaTime)
        {
            bool sprinting = _player != null && _player.Motor != null && _player.State.IsSprinting;
            AnimationClip moving = _player != null && _player.Motor != null ? _player.State.IsCrouching ? _definition.MovingCrouch : _player.State.Flags.IsAccelerated() ? _definition.Moving : _definition.MovingSlow : _definition.Moving;
            AnimationClip clip = sprinting ? _definition.Sprint : IsMoving ? moving : _definition.Idle;
            if (clip == null)
            {
                clip = _definition.Idle;
            }
            float speed = _definition.HasDistinctMovement == false && clip == _definition.Moving && _player != null && _player.Motor != null ? _player.State.IsCrouching ? 0.7f : _player.State.Flags.IsAccelerated() ? 1f : 0.75f : 1f;
            if (_loop != clip || _loopSpeed != speed)
            {
                _loop = clip;
                _loopSpeed = speed;
                PlayVisual(clip, true, speed);
            }
            _idleTime = IsMoving ? 0f : _idleTime + deltaTime;
            if (_idleTime >= BORE_DELAY && _definition.Bore != null)
            {
                SetState(FirstPersonBoltState.Bore, _definition.Bore, false);
            }
        }

        private void TryDraw()
        {
            if (_rig.Hands == null || _definition.HudModel == null)
            {
                return;
            }
            if (!_rig.TryAcquireHands(this))
            {
                if (_requested || _rig.HandsOwner is FirstPersonWeaponController)
                {
                    _rig.RequestHands(this);
                }
                return;
            }
            _requested = false;
            _rig.SetHandsVisible(true);
            _visual = new FirstPersonBoltVisual(_rig, _definition);
            StalkerCameraController camera = _player.CameraController;
            if (camera != null)
            {
                _savedFieldOfView = camera.HudFieldOfView;
                _fieldOfViewSaved = true;
                camera.HudFieldOfView = _definition.HudFieldOfView;
            }
            _idleTime = 0f;
            StartDraw(true);
        }

        private void StartDraw(bool playSound)
        {
            AnimationClip clip = _definition.DrawAlternate != null && Random.Range(0, 2) == 1 ? _definition.DrawAlternate : _definition.Draw;
            SetState(FirstPersonBoltState.Drawing, clip, false);
            if (_definition.DrawCameraEffect != null && _player != null && _player.CameraEffects != null)
            {
                _player.CameraEffects.PlayItemEffect(_definition.DrawCameraEffect);
            }
            if (playSound)
            {
                PlaySound(_definition.DrawSound);
            }
        }

        private void StartThrowEnd()
        {
            SetState(FirstPersonBoltState.ThrowEnd, _definition.ThrowEnd, false);
        }

        private void SpawnProjectile()
        {
            _spawned = true;
            _visual.SetItemVisible(false);
            StalkerCameraController camera = _player.CameraController;
            Transform origin = camera != null && camera.CameraRoot != null ? camera.CameraRoot : _player.transform;
            BoltProjectile.Launch(_definition, _player, origin.position, origin.forward, ThrowSpeed);
        }

        private void SetState(FirstPersonBoltState state, AnimationClip clip, bool loop)
        {
            State = state;
            _stateTime = 0f;
            _stateDuration = clip != null ? clip.length / _definition.GetMotionSpeed(clip) : 0f;
            _loop = loop ? clip : null;
            _loopSpeed = 1f;
            if (_visual != null)
            {
                if (state == FirstPersonBoltState.Drawing)
                {
                    _visual.SetItemVisible(true);
                }
                PlayVisual(clip, loop);
            }
        }

        private void PlayVisual(AnimationClip clip, bool loop, float speed = 1f)
        {
            AnimationClip item = State == FirstPersonBoltState.Drawing ? clip == _definition.DrawAlternate ? _definition.ItemDrawAlternate : _definition.ItemDraw : State == FirstPersonBoltState.Bore ? _definition.ItemBore : clip == _definition.Idle ? _definition.ItemIdle : _definition.ItemFallback;
            _visual.Play(clip, item, _definition.GetMotionSpeed(clip) * speed, _definition.GetMotionSpeed(item) * speed, loop);
        }

        private void Hide()
        {
            if (_visual != null)
            {
                _visual.Dispose();
                _visual = null;
            }
            if (_fieldOfViewSaved && _player != null && _player.CameraController != null)
            {
                _player.CameraController.HudFieldOfView = _savedFieldOfView;
            }
            _fieldOfViewSaved = false;
            if (_rig != null && _rig.HandsOwner == (object)this)
            {
                _rig.SetHandsVisible(false);
                _rig.ReleaseHands(this);
            }
            State = FirstPersonBoltState.Hidden;
            _loop = null;
        }

        private void Subscribe()
        {
            FirstPersonRig rig = _player != null ? _player.Rig : null;
            if (_rig == rig)
            {
                return;
            }
            Hide();
            Unsubscribe();
            _rig = rig;
            if (_rig != null)
            {
                _rig.HandsRequested += OnHandsRequested;
            }
        }

        private void Unsubscribe()
        {
            if (_rig != null)
            {
                _rig.HandsRequested -= OnHandsRequested;
            }
            _rig = null;
        }

        private void OnHandsRequested(object requester)
        {
            if (requester != (object)this && _rig.HandsOwner == (object)this)
            {
                _redrawDelay = TEMPORARY_REDRAW_DELAY;
                Holster();
            }
        }

        private void PlaySound(AudioClip clip)
        {
            if (_audio != null && clip != null)
            {
                _audio.PlayOneShot(clip);
            }
        }
    }
}

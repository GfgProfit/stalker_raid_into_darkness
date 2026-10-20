using System;
using System.Collections.Generic;
using RaidIntoDarkness.Effects;
using RaidIntoDarkness.FirstPerson;
using RaidIntoDarkness.Player;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer;

namespace RaidIntoDarkness.Weapons
{
    public enum FirstPersonWeaponState
    {
        Hidden = 0,
        Drawing = 1,
        Ready = 2,
        Reloading = 3,
        SwitchingMode = 4,
        Holstering = 5
    }

    [DisallowMultipleComponent]
    public sealed class FirstPersonWeaponController : MonoBehaviour, IFirstPersonHandsOverlay
    {
        public const int PRIMARY_SLOT = 0;
        public const int SECONDARY_SLOT = 1;
        public const int SLOT_COUNT = 2;

        private const float MAX_FRAME_DELTA = 0.1f;
        private const float REDRAW_DELAY = 0.5f;
        private const float SHOT_FADE = 0.05f;
        private const float SOUND_VOLUME = 1.0f;

        [SerializeField] private StalkerPlayerController _player;
        [SerializeField] private StalkerPlayerInput _input;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private AudioSource _shotAudio;
        [SerializeField] private string _itemAnchorBone = FirstPersonHudAnimator.ITEM_ANCHOR_BONE;

        private readonly Slot[] _slots = { new Slot(), new Slot() };
        private readonly Dictionary<object, Ammo> _ammo = new Dictionary<object, Ammo>();
        private FirstPersonWeaponGraph _graph;
        private FirstPersonWeaponEffects _effects;
        private FirstPersonRig _subscribedRig;
        private GameObject _boundHands;
        private GameObject _model;
        private Transform _modelRoot;
        private Transform _muzzle;
        private bool _muzzleFromView;
        private XRayParticleGroupPlayer _flame;
        private Vector3 _savedHandsPosition;
        private Quaternion _savedHandsRotation;
        private FirstPersonWeaponMotionKind _loopKind;
        private bool _hasLoop;
        private bool _inSprint;
        private float _stateTime;
        private float _stateDuration;
        private float _actionUntil;
        private float _nextShotTime;
        private float _nextEmptyClickTime;
        private int _burstLeft;
        private int _pendingSlot = -1;
        private int _redrawSlot = -1;
        private float _handsFreeTime;
        private float _aim;
        private bool _aiming;
        private bool _aimEnding;
        private float _aimBlendTime;
        private float _aimBlendScale = 1.0f;
        private FirstPersonWeaponAsset _recoilSource;

        public event Action<int, int> AmmoChanged;
        public event Action<FirstPersonWeaponState> StateChanged;

        public FirstPersonWeaponState State { get; private set; }
        public int ActiveSlot { get; private set; } = -1;
        public FirstPersonWeaponAsset ActiveWeapon => ActiveSlot >= 0 ? _slots[ActiveSlot].Asset : null;
        public int Magazine => ActiveSlot >= 0 && _slots[ActiveSlot].Ammo != null ? _slots[ActiveSlot].Ammo.Magazine : 0;
        public int Reserve => ActiveSlot >= 0 && _slots[ActiveSlot].Ammo != null ? _slots[ActiveSlot].Ammo.Reserve : 0;
        public int FireMode => ActiveWeapon != null ? ActiveWeapon.FireModes[Mathf.Clamp(_slots[ActiveSlot].Ammo.FireModeIndex, 0, ActiveWeapon.FireModes.Count - 1)] : 0;
        public bool IsAiming => _aiming;
        public float AimFactor => _aim;
        public GameObject Model => _model;
        public Transform Muzzle => _muzzle;
        public FirstPersonWeaponMotionKind CurrentMotion { get; private set; }

        public FirstPersonWeaponAsset GetSlotWeapon(int slot)
        {
            return IsSlot(slot) ? _slots[slot].Asset : null;
        }

        public void SetSlotWeapon(int slot, FirstPersonWeaponAsset asset, object key)
        {
            if (!IsSlot(slot))
            {
                return;
            }

            Slot target = _slots[slot];
            object ammoKey = key ?? asset;
            if (target.Asset == asset && Equals(target.Key, ammoKey))
            {
                return;
            }

            target.Asset = asset;
            target.Key = ammoKey;
            target.Ammo = asset != null ? GetAmmo(ammoKey, asset) : null;
            if (_redrawSlot == slot && asset == null)
            {
                _redrawSlot = -1;
            }

            if (_pendingSlot == slot && asset == null)
            {
                _pendingSlot = -1;
            }

            if (ActiveSlot != slot)
            {
                return;
            }

            if (asset == null)
            {
                Hide();
                return;
            }

            _pendingSlot = slot;
            Holster();
        }

        public bool Select(int slot)
        {
            if (!IsSlot(slot) || _slots[slot].Asset == null)
            {
                return false;
            }

            _redrawSlot = -1;
            if (State == FirstPersonWeaponState.Hidden)
            {
                return Draw(slot);
            }

            if (ActiveSlot == slot)
            {
                _pendingSlot = -1;
                Holster();
                return true;
            }

            _pendingSlot = slot;
            Holster();
            return true;
        }

        public void Holster()
        {
            if (State == FirstPersonWeaponState.Hidden || State == FirstPersonWeaponState.Holstering)
            {
                return;
            }

            SetAiming(false);
            _burstLeft = 0;
            FirstPersonWeaponMotion motion = ActiveWeapon != null ? ActiveWeapon.Find(FirstPersonWeaponMotionKind.Holster, 0) : null;
            if (motion == null || _graph == null)
            {
                Hide();
                return;
            }

            PlayMotion(motion, ActiveWeapon.CrossFade, false, true);
            SetState(FirstPersonWeaponState.Holstering, motion.Duration);
        }

        public void Hide()
        {
            if (State == FirstPersonWeaponState.Hidden)
            {
                return;
            }

            SetAiming(false);
            _aim = 0.0f;
            DestroyVisuals(true);
            if (_player != null)
            {
                _player.Combat = false;
            }

            ActiveSlot = -1;
            SetState(FirstPersonWeaponState.Hidden, 0.0f);
            NotifyAmmo();
        }

        public bool TryPlayOverlay(AnimationClip clip, float speed)
        {
            return State != FirstPersonWeaponState.Hidden && _graph != null && _graph.PlayOverlay(clip, speed);
        }

        public void StopOverlay()
        {
            if (_graph != null)
            {
                _graph.StopOverlay();
            }
        }

        private void Awake()
        {
            _effects = new FirstPersonWeaponEffects(null);
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Hide();
            Unsubscribe();
        }

        private void OnDestroy()
        {
            DestroyVisuals(true);
            if (_effects != null)
            {
                _effects.Dispose();
            }
        }

        private void Update()
        {
            float deltaTime = Mathf.Min(Time.deltaTime, MAX_FRAME_DELTA);
            Subscribe();
            TickRecoil();
            bool inputAllowed = _input != null && _player != null && !_player.GameplayInputBlocked;
            if (inputAllowed)
            {
                HandleSlotInput();
            }

            TickRedraw(deltaTime);
            if (State == FirstPersonWeaponState.Hidden)
            {
                return;
            }

            FirstPersonRig rig = _player.Rig;
            if (rig == null || rig.Hands == null)
            {
                Hide();
                return;
            }

            if (rig.Hands != _boundHands)
            {
                RebuildVisuals();
            }

            StalkerCameraController camera = _player.CameraController;
            bool cursorLocked = camera == null || (!camera.CursorReleased && camera.CursorLockFrame != Time.frameCount);
            if (inputAllowed && cursorLocked)
            {
                HandleWeaponInput();
            }
            else
            {
                _burstLeft = 0;
                SetAiming(false);
            }

            TickState(deltaTime);
            if (State == FirstPersonWeaponState.Hidden)
            {
                return;
            }

            TickAim(deltaTime);
            if (_graph != null)
            {
                _graph.Evaluate(deltaTime);
            }

            ResetModelRoot();
            ApplyHandsOffset();
            UpdateFlame();
        }

        private void Subscribe()
        {
            FirstPersonRig rig = _player != null ? _player.Rig : null;
            if (rig == _subscribedRig)
            {
                return;
            }

            Unsubscribe();
            _subscribedRig = rig;
            if (rig != null)
            {
                rig.HandsRequested += OnHandsRequested;
            }
        }

        private void Unsubscribe()
        {
            if (_subscribedRig != null)
            {
                _subscribedRig.HandsRequested -= OnHandsRequested;
            }

            _subscribedRig = null;
        }

        private void OnHandsRequested(object requester)
        {
            if (requester == (object)this || State == FirstPersonWeaponState.Hidden || _subscribedRig == null || _subscribedRig.HandsOwner != (object)this)
            {
                return;
            }

            if (State != FirstPersonWeaponState.Holstering)
            {
                _redrawSlot = ActiveSlot;
                _pendingSlot = -1;
                Holster();
            }
        }

        private void HandleSlotInput()
        {
            if (_input.PrimaryWeaponPressed)
            {
                Select(PRIMARY_SLOT);
            }
            else if (_input.SecondaryWeaponPressed)
            {
                Select(SECONDARY_SLOT);
            }
        }

        private void TickRedraw(float deltaTime)
        {
            if (State != FirstPersonWeaponState.Hidden)
            {
                _handsFreeTime = 0.0f;
                return;
            }

            int slot = _pendingSlot >= 0 ? _pendingSlot : _redrawSlot;
            if (slot < 0)
            {
                return;
            }

            FirstPersonRig rig = _player != null ? _player.Rig : null;
            if (rig == null || rig.HandsBusy)
            {
                _handsFreeTime = 0.0f;
                return;
            }

            _handsFreeTime += deltaTime;
            if (_pendingSlot < 0 && _handsFreeTime < REDRAW_DELAY)
            {
                return;
            }

            _pendingSlot = -1;
            _redrawSlot = -1;
            if (!Draw(slot))
            {
                _redrawSlot = slot;
            }
        }

        private bool Draw(int slot)
        {
            FirstPersonWeaponAsset asset = IsSlot(slot) ? _slots[slot].Asset : null;
            FirstPersonRig rig = _player != null ? _player.Rig : null;
            if (asset == null || asset.HudModel == null || rig == null || rig.Hands == null)
            {
                return false;
            }

            if (!rig.TryAcquireHands(this))
            {
                _redrawSlot = slot;
                return false;
            }

            ActiveSlot = slot;
            _recoilSource = asset;
            rig.SetHandsVisible(true);
            BuildVisuals(rig.Hands, asset);
            _player.Combat = true;
            _aim = 0.0f;
            _aiming = false;
            _aimEnding = false;
            _hasLoop = false;
            _inSprint = false;
            FirstPersonWeaponMotion motion = asset.Find(FirstPersonWeaponMotionKind.Draw, 0);
            if (motion != null)
            {
                PlayMotion(motion, 0.0f, false, true);
                SetState(FirstPersonWeaponState.Drawing, motion.Duration);
            }
            else
            {
                SetState(FirstPersonWeaponState.Ready, 0.0f);
            }

            NotifyAmmo();
            return true;
        }

        private void HandleWeaponInput()
        {
            FirstPersonWeaponAsset asset = ActiveWeapon;
            Ammo ammo = _slots[ActiveSlot].Ammo;
            bool ready = State == FirstPersonWeaponState.Ready;
            bool sprinting = _player.State.IsSprinting;
            SetAiming(_input.AimHeld && ready && !sprinting);
            if (!ready)
            {
                _burstLeft = 0;
                return;
            }

            if (_input.ReloadPressed && ammo.Magazine < asset.MagazineSize && ammo.Reserve > 0)
            {
                StartReload(asset, ammo);
                return;
            }

            if (_input.FireModePressed && asset.FireModes.Count > 1)
            {
                StartFireModeSwitch(asset, ammo);
                return;
            }

            if (_input.FirePressed && !sprinting)
            {
                int mode = FireMode;
                _burstLeft = mode == FirstPersonWeaponAsset.AUTO_FIRE_MODE ? int.MaxValue : Mathf.Max(1, mode);
                if (ammo.Magazine <= 0)
                {
                    _burstLeft = 0;
                    PlayEmptyClick(asset);
                }
            }

            if (!_input.FireHeld || sprinting)
            {
                _burstLeft = 0;
            }

            if (_burstLeft > 0 && Time.time >= _nextShotTime)
            {
                if (ammo.Magazine <= 0)
                {
                    _burstLeft = 0;
                    return;
                }

                Shoot(asset, ammo);
            }
        }

        private void Shoot(FirstPersonWeaponAsset asset, Ammo ammo)
        {
            ammo.Magazine--;
            _burstLeft--;
            _nextShotTime = Time.time + asset.ShotInterval;
            FirstPersonWeaponMotion motion = _aiming ? asset.FindRandom(FirstPersonWeaponMotionKind.ShootAim) : null;
            if (motion == null)
            {
                motion = asset.FindRandom(FirstPersonWeaponMotionKind.Shoot);
            }

            if (motion != null)
            {
                PlayMotion(motion, SHOT_FADE, false, false);
                _actionUntil = Time.time + motion.Duration;
            }

            PlayShotSounds(asset, ammo.Magazine);
            Vector3 recoil = asset.Recoil * (_player.State.IsCrouching ? 0.5f : 1.0f);
            if (_player.CameraController != null && _player.CameraController.Recoil != null)
            {
                _player.CameraController.Recoil.Shoot(recoil.x, recoil.y, recoil.z);
            }

            SpawnShotEffects(asset);
            NotifyAmmo();
        }

        private void PlayShotSounds(FirstPersonWeaponAsset asset, int magazine)
        {
            AudioSource source = _shotAudio != null ? _shotAudio : _audio;
            if (source == null)
            {
                return;
            }

            IReadOnlyList<AudioClip> shots = asset.ShotSounds;
            if (shots.Count > 0)
            {
                PlayOneShot(source, shots[UnityEngine.Random.Range(0, shots.Count)]);
            }

            if (magazine == 0)
            {
                PlayOneShot(source, asset.LastShellSound);
            }
            else if (magazine <= asset.LowAmmoThreshold)
            {
                PlayOneShot(source, asset.LowAmmoSound);
            }
        }

        private void PlayEmptyClick(FirstPersonWeaponAsset asset)
        {
            if (Time.time < _nextEmptyClickTime)
            {
                return;
            }

            _nextEmptyClickTime = Time.time + asset.EmptyClickInterval;
            PlayOneShot(_shotAudio != null ? _shotAudio : _audio, asset.EmptySound);
        }

        private void SpawnShotEffects(FirstPersonWeaponAsset asset)
        {
            Transform muzzle = _muzzle != null ? _muzzle : _modelRoot;
            if (muzzle == null || _effects == null)
            {
                return;
            }

            Quaternion rotation = GetMuzzleRotation(muzzle);
            _flame = _effects.Play(asset.FlameParticles, muzzle, muzzle.position, rotation);
            _effects.Play(asset.SmokeParticles, null, muzzle.position, rotation);
        }

        private Quaternion GetMuzzleRotation(Transform muzzle)
        {
            Transform axis = _muzzleFromView ? muzzle : _modelRoot != null ? _modelRoot : _model.transform;
            Vector3 forward = axis.TransformVector(Vector3.forward);
            return forward.sqrMagnitude > 0.0f ? Quaternion.LookRotation(forward, Vector3.up) : axis.rotation;
        }

        private void UpdateFlame()
        {
            if (_flame == null || _muzzle == null || !_flame.IsPlaying || _flame.transform.parent != _muzzle)
            {
                return;
            }

            _flame.transform.SetPositionAndRotation(_muzzle.position, GetMuzzleRotation(_muzzle));
        }

        private void StartReload(FirstPersonWeaponAsset asset, Ammo ammo)
        {
            FirstPersonWeaponMotion motion = ammo.Magazine <= 0 ? asset.Find(FirstPersonWeaponMotionKind.ReloadEmpty, 0) : null;
            if (motion == null)
            {
                motion = asset.Find(FirstPersonWeaponMotionKind.Reload, 0);
            }

            SetAiming(false);
            _burstLeft = 0;
            if (motion == null)
            {
                FinishReload(asset, ammo);
                return;
            }

            PlayMotion(motion, asset.CrossFade, false, true);
            SetState(FirstPersonWeaponState.Reloading, motion.Duration);
        }

        private void FinishReload(FirstPersonWeaponAsset asset, Ammo ammo)
        {
            int taken = Mathf.Min(asset.MagazineSize - ammo.Magazine, ammo.Reserve);
            ammo.Magazine += taken;
            ammo.Reserve -= taken;
            NotifyAmmo();
        }

        private void StartFireModeSwitch(FirstPersonWeaponAsset asset, Ammo ammo)
        {
            ammo.FireModeIndex = (ammo.FireModeIndex + 1) % asset.FireModes.Count;
            _burstLeft = 0;
            FirstPersonWeaponMotion motion = asset.Find(FirstPersonWeaponMotionKind.FireMode, 0);
            if (motion == null)
            {
                return;
            }

            PlayMotion(motion, asset.CrossFade, false, true);
            SetState(FirstPersonWeaponState.SwitchingMode, motion.Duration);
        }

        private void TickState(float deltaTime)
        {
            _stateTime += deltaTime;
            FirstPersonWeaponAsset asset = ActiveWeapon;
            if (asset == null)
            {
                Hide();
                return;
            }

            switch (State)
            {
                case FirstPersonWeaponState.Ready:
                    TickReady(asset);
                    return;
                case FirstPersonWeaponState.Hidden:
                    return;
            }

            if (_stateTime < _stateDuration)
            {
                return;
            }

            switch (State)
            {
                case FirstPersonWeaponState.Holstering:
                    Hide();
                    return;
                case FirstPersonWeaponState.Reloading:
                    FinishReload(asset, _slots[ActiveSlot].Ammo);
                    break;
            }

            _hasLoop = false;
            _actionUntil = 0.0f;
            SetState(FirstPersonWeaponState.Ready, 0.0f);
            TickReady(asset);
        }

        private void TickReady(FirstPersonWeaponAsset asset)
        {
            if (Time.time < _actionUntil)
            {
                return;
            }

            StalkerMovementState movement = _player.State;
            if (movement.IsSprinting && movement.IsMoving)
            {
                if (!_inSprint)
                {
                    _inSprint = true;
                    if (PlayTransition(asset, FirstPersonWeaponMotionKind.SprintStart))
                    {
                        return;
                    }
                }

                PlayLoop(asset, FirstPersonWeaponMotionKind.Sprint);
                return;
            }

            if (_inSprint)
            {
                _inSprint = false;
                if (PlayTransition(asset, FirstPersonWeaponMotionKind.SprintEnd))
                {
                    return;
                }
            }

            PlayLoop(asset, SelectIdle(asset, movement));
        }

        private FirstPersonWeaponMotionKind SelectIdle(FirstPersonWeaponAsset asset, StalkerMovementState movement)
        {
            if (_aiming)
            {
                return movement.IsMoving && asset.HasMotion(FirstPersonWeaponMotionKind.MovingAim) ? FirstPersonWeaponMotionKind.MovingAim : FirstPersonWeaponMotionKind.IdleAim;
            }

            if (!movement.IsMoving)
            {
                return Magazine <= 0 && asset.HasMotion(FirstPersonWeaponMotionKind.IdleEmpty) ? FirstPersonWeaponMotionKind.IdleEmpty : FirstPersonWeaponMotionKind.Idle;
            }

            if (movement.IsCrouching && asset.HasMotion(FirstPersonWeaponMotionKind.MovingCrouch))
            {
                return FirstPersonWeaponMotionKind.MovingCrouch;
            }

            if (movement.Flags.Has(StalkerMoveFlags.Accel) && asset.HasMotion(FirstPersonWeaponMotionKind.MovingSlow))
            {
                return FirstPersonWeaponMotionKind.MovingSlow;
            }

            return asset.HasMotion(FirstPersonWeaponMotionKind.Moving) ? FirstPersonWeaponMotionKind.Moving : FirstPersonWeaponMotionKind.Idle;
        }

        private bool PlayTransition(FirstPersonWeaponAsset asset, FirstPersonWeaponMotionKind kind)
        {
            FirstPersonWeaponMotion motion = asset.Find(kind, 0);
            if (motion == null)
            {
                return false;
            }

            PlayMotion(motion, asset.CrossFade, false, false);
            _hasLoop = false;
            _actionUntil = Time.time + motion.Duration;
            return true;
        }

        private void PlayLoop(FirstPersonWeaponAsset asset, FirstPersonWeaponMotionKind kind)
        {
            if (_hasLoop && _loopKind == kind && CurrentMotion == kind)
            {
                return;
            }

            FirstPersonWeaponMotion motion = asset.Find(kind, 0) ?? asset.Find(FirstPersonWeaponMotionKind.Idle, 0);
            if (motion == null)
            {
                return;
            }

            PlayMotion(motion, asset.CrossFade, true, false);
            _loopKind = kind;
            _hasLoop = true;
            CurrentMotion = kind;
        }

        private void PlayMotion(FirstPersonWeaponMotion motion, float fade, bool loop, bool withEffects)
        {
            CurrentMotion = motion.Kind;
            if (_graph != null)
            {
                _graph.Play(motion.Hands, motion.Weapon, motion.Speed, fade, loop);
            }

            if (motion.CameraEffect != null && _player.CameraEffects != null)
            {
                _player.CameraEffects.PlayItemEffect(motion.CameraEffect);
            }

            if (withEffects && _audio != null && motion.Sound != null)
            {
                _audio.Stop();
                _audio.clip = motion.Sound;
                _audio.Play();
            }
        }

        private void SetState(FirstPersonWeaponState state, float duration)
        {
            _stateTime = 0.0f;
            _stateDuration = duration;
            if (State == state)
            {
                return;
            }

            State = state;
            StateChanged?.Invoke(state);
        }

        private void SetAiming(bool aiming)
        {
            if (_aiming == aiming)
            {
                return;
            }

            FirstPersonWeaponAsset asset = ActiveWeapon;
            float startDuration = asset != null && asset.AimStart != null ? asset.AimStart.Duration : 0.0f;
            float endDuration = asset != null && asset.AimEnd != null ? asset.AimEnd.Duration : 0.0f;
            if (aiming)
            {
                float endProgress = _aimEnding && endDuration > 0.0f ? Mathf.Clamp01(_aimBlendTime / endDuration) : 1.0f;
                _aimBlendTime = _aimEnding ? (1.0f - endProgress) * _aimBlendScale * startDuration : 0.0f;
                _aimBlendScale = 1.0f;
                _aimEnding = false;
            }
            else
            {
                _aimBlendScale = startDuration > 0.0f ? Mathf.Clamp01(_aimBlendTime / startDuration) : 1.0f;
                _aimBlendTime = 0.0f;
                _aimEnding = true;
            }

            _aiming = aiming;
            _hasLoop = false;
        }

        private void TickAim(float deltaTime)
        {
            FirstPersonWeaponAsset asset = ActiveWeapon;
            float aimTime = asset != null ? asset.AimTime : 0.25f;
            _aim = Mathf.MoveTowards(_aim, _aiming ? 1.0f : 0.0f, deltaTime / aimTime);
            _aimBlendTime += deltaTime;
        }

        private void ApplyHandsOffset()
        {
            FirstPersonWeaponAsset asset = ActiveWeapon;
            if (_boundHands == null || asset == null)
            {
                return;
            }

            Quaternion aimRotation = FirstPersonWeaponAsset.ToAimRotation(asset.AimOffsetRotation * _aim);
            Vector3 position = asset.AimOffsetPosition * _aim + aimRotation * asset.HandsPosition;
            Quaternion rotation = aimRotation * asset.HandsRotation;
            if (EvaluateAimBlend(asset, out Vector3 blendPosition, out Quaternion blendRotation))
            {
                position += rotation * blendPosition;
                rotation *= blendRotation;
            }

            _boundHands.transform.localPosition = position;
            _boundHands.transform.localRotation = rotation;
        }

        private bool EvaluateAimBlend(FirstPersonWeaponAsset asset, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            XRayAnmAsset anm;
            float time = _aimBlendTime;
            if (_aiming)
            {
                anm = asset.AimStart;
                if (anm == null || time >= anm.Duration)
                {
                    anm = asset.AimIdle != null ? asset.AimIdle : anm;
                    time = anm != null && anm == asset.AimIdle ? 0.0f : time;
                }
            }
            else if (_aimEnding)
            {
                anm = asset.AimEnd;
                if (anm == null || time >= anm.Duration)
                {
                    _aimEnding = false;
                    return false;
                }
            }
            else
            {
                return false;
            }

            if (anm == null || !anm.Evaluate(Mathf.Min(time, anm.Duration), out position, out rotation))
            {
                return false;
            }

            if (_aimEnding && _aimBlendScale < 1.0f)
            {
                position *= _aimBlendScale;
                rotation = Quaternion.Slerp(Quaternion.identity, rotation, _aimBlendScale);
            }

            return true;
        }

        private void TickRecoil()
        {
            FirstPersonWeaponAsset asset = ActiveWeapon != null ? ActiveWeapon : _recoilSource;
            StalkerCameraRecoil recoil = _player != null && _player.CameraController != null ? _player.CameraController.Recoil : null;
            if (asset != null && recoil != null)
            {
                recoil.Tick(asset.RecoilReturnSpeed, asset.RecoilSnappiness);
            }
        }

        private void BuildVisuals(GameObject hands, FirstPersonWeaponAsset asset)
        {
            DestroyVisuals(false);
            _boundHands = hands;
            _savedHandsPosition = hands.transform.localPosition;
            _savedHandsRotation = hands.transform.localRotation;
            Transform anchor = FindBone(hands, _itemAnchorBone);
            _model = Instantiate(asset.HudModel, anchor != null ? anchor : hands.transform, false);
            _model.transform.localPosition = Vector3.zero;
            _model.transform.localRotation = Quaternion.identity;
            foreach (Renderer renderer in _model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            _modelRoot = FindModelRoot(_model);
            FirstPersonWeaponView view = _model.GetComponent<FirstPersonWeaponView>();
            _muzzleFromView = view != null && view.Muzzle != null;
            _muzzle = _muzzleFromView ? view.Muzzle : FindBone(_model, asset.MuzzleBone);
            Animator handsAnimator = GetAnimator(hands);
            Animator weaponAnimator = GetAnimator(_model);
            _graph = new FirstPersonWeaponGraph($"{name}.FirstPersonWeapon", handsAnimator, weaponAnimator, FirstPersonWeaponGraph.CreateMask(hands.transform, asset.OverlayBones));
            _hasLoop = false;
        }

        private void RebuildVisuals()
        {
            FirstPersonWeaponAsset asset = ActiveWeapon;
            FirstPersonRig rig = _player.Rig;
            if (asset == null || rig.Hands == null || rig.HandsOwner != (object)this)
            {
                Hide();
                return;
            }

            rig.SetHandsVisible(true);
            BuildVisuals(rig.Hands, asset);
            _hasLoop = false;
            _actionUntil = 0.0f;
            SetState(FirstPersonWeaponState.Ready, 0.0f);
        }

        private void DestroyVisuals(bool releaseHands)
        {
            if (_graph != null)
            {
                _graph.Destroy();
                _graph = null;
            }

            if (_effects != null && _muzzle != null)
            {
                _effects.Detach(_muzzle);
            }

            if (_model != null)
            {
                Destroy(_model);
            }

            _model = null;
            _modelRoot = null;
            _muzzle = null;
            _muzzleFromView = false;
            _flame = null;
            FirstPersonRig rig = _player != null ? _player.Rig : null;
            if (_boundHands != null)
            {
                _boundHands.transform.localPosition = _savedHandsPosition;
                _boundHands.transform.localRotation = _savedHandsRotation;
            }

            if (releaseHands && rig != null && rig.HandsOwner == (object)this)
            {
                rig.SetHandsVisible(false);
                rig.ReleaseHands(this);
            }

            _boundHands = null;
        }

        private void ResetModelRoot()
        {
            if (_modelRoot != null)
            {
                _modelRoot.localPosition = Vector3.zero;
                _modelRoot.localRotation = Quaternion.identity;
            }
        }

        private void NotifyAmmo()
        {
            AmmoChanged?.Invoke(Magazine, Reserve);
        }

        private Ammo GetAmmo(object key, FirstPersonWeaponAsset asset)
        {
            if (!_ammo.TryGetValue(key, out Ammo ammo))
            {
                ammo = new Ammo { Magazine = asset.MagazineSize, Reserve = asset.ReserveAmmo };
                _ammo.Add(key, ammo);
            }

            return ammo;
        }

        private static bool IsSlot(int slot)
        {
            return slot >= 0 && slot < SLOT_COUNT;
        }

        private static void PlayOneShot(AudioSource source, AudioClip clip)
        {
            if (source != null && clip != null)
            {
                source.PlayOneShot(clip, SOUND_VOLUME);
            }
        }

        private static Animator GetAnimator(GameObject target)
        {
            Animator animator = target.GetComponent<Animator>();
            if (animator == null)
            {
                animator = target.AddComponent<Animator>();
            }

            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return animator;
        }

        private static Transform FindModelRoot(GameObject model)
        {
            XRayModel xray = model.GetComponent<XRayModel>();
            if (xray == null)
            {
                return null;
            }

            for (int i = 0; i < xray.Bones.Count; i++)
            {
                if (xray.Bones[i] != null && xray.Bones[i].parent == model.transform)
                {
                    return xray.Bones[i];
                }
            }

            return null;
        }

        private static Transform FindBone(GameObject root, string boneName)
        {
            if (root == null || string.IsNullOrEmpty(boneName))
            {
                return null;
            }

            XRayModel model = root.GetComponent<XRayModel>();
            Transform bone = model != null ? model.FindBone(boneName) : null;
            if (bone != null)
            {
                return bone;
            }

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == boneName)
                {
                    return child;
                }
            }

            return null;
        }

        private sealed class Slot
        {
            public FirstPersonWeaponAsset Asset { get; set; }
            public object Key { get; set; }
            public Ammo Ammo { get; set; }
        }

        private sealed class Ammo
        {
            public int Magazine { get; set; }
            public int Reserve { get; set; }
            public int FireModeIndex { get; set; }
        }
    }
}

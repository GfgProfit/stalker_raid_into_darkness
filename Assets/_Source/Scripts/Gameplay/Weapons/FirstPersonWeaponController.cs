using System;
using System.Collections.Generic;
using RaidIntoDarkness.Effects;
using RaidIntoDarkness.FirstPerson;
using RaidIntoDarkness.Player;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Weapons
{
    [DisallowMultipleComponent]
    public sealed class FirstPersonWeaponController : MonoBehaviour, IFirstPersonHandsOverlay
    {
        public const int PRIMARY_SLOT = 0;
        public const int SECONDARY_SLOT = 1;
        public const int KNIFE_SLOT = 2;
        public const int HAND_GRENADE_SELECTION = 3;
        public const int SLOT_COUNT = 4;

        private const float MAX_FRAME_DELTA = 0.1f;
        private const float REDRAW_DELAY = 0.5f;
        private const float SHOT_FADE = 0.05f;
        private const float PISTOL_CAMERA_DEPTH_OFFSET = 0.04f;
        private const float LAUNCHER_AIM_SHOT_BLEND_TIME = 0.22f;
        private const float RELOAD_HUD_FOV_BLEND_TIME = 0.25f;
        private const float FIRE_MODE_INPUT_RELEASE_RATIO = 0.85f;
        private const float SOUND_VOLUME = 1.0f;
        private const int MELEE_HIT_BUFFER_SIZE = 16;
        private const string GRENADE_AIM_POINT = "GrenadeAimPoint";

        [SerializeField] private StalkerPlayerController _player;
        [SerializeField] private StalkerPlayerInput _input;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private AudioSource _shotAudio;
        [SerializeField] private string _itemAnchorBone = FirstPersonHudAnimator.ITEM_ANCHOR_BONE;

        private readonly FirstPersonWeaponSlot[] _slots = { new FirstPersonWeaponSlot(), new FirstPersonWeaponSlot(), new FirstPersonWeaponSlot(), new FirstPersonWeaponSlot() };
        private readonly Dictionary<object, FirstPersonWeaponInstanceState> _weaponStates = new Dictionary<object, FirstPersonWeaponInstanceState>();
        private readonly List<MonoBehaviour> _opticsBindings = new List<MonoBehaviour>();
        private readonly List<WeaponReticleCycle> _reticleBindings = new List<WeaponReticleCycle>();
        private IFirstPersonWeaponAmmoSource _ammoSource;
        private FirstPersonWeaponGraph _graph;
        private FirstPersonWeaponEffects _effects;
        private FirstPersonWeaponMotion _activeMeleeAttack;
        private RaycastHit[] _meleeHits;
        private bool _meleeContactPending;
        private bool _handGrenadeReleaseRequested;
        private bool _handGrenadeReleased;
        private bool _handGrenadeVisible;
        private (FirstPersonWeaponAsset Asset, object Key)? _handGrenadeAfterThrow;
        private Transform _handGrenadeAnchor;
        private Transform _handGrenadeThrowHand;
        private Renderer[] _handGrenadeRenderers;
        private WeaponWorldCrosshair _worldCrosshair;
        private FirstPersonRig _subscribedRig;
        private GameObject _boundHands;
        private GameObject _model;
        private FirstPersonWeaponView _weaponView;
        private FirstPersonRevolverView _revolverView;
        private FirstPersonMagazineRoundsView _magazineRoundsView;
        private int _reloadIncomingRounds;
        private Transform _modelRoot;
        private Transform _grenadeAimPoint;
        private Transform _muzzle;
        private bool _muzzleFromView;
        private bool _ballisticsWarningLogged;
        private FirstPersonWeaponEffects.Instance _flame;
        private Vector3 _savedHandsPosition;
        private Quaternion _savedHandsRotation;
        private FirstPersonWeaponMotionKind _loopKind;
        private AnimationClip _loopWeaponClip;
        private bool _hasLoop;
        private bool _inSprint;
        private float _stateTime;
        private float _stateDuration;
        private float _underbarrelRoundRevealTime;
        private Vector2 _underbarrelShellTimes;
        private float _actionUntil;
        private float _emptyWeaponAt = float.PositiveInfinity;
        private float _nextShotTime;
        private float _nextEmptyClickTime;
        private int _burstLeft;
        private int _pendingSlot = -1;
        private int _redrawSlot = -1;
        private float _handsFreeTime;
        private float _aim;
        private float _aimCameraRoll;
        private float _aimCameraVelocity;
        private float _reloadHudFovBlend;
        private bool _aiming;
        private bool _aimedLauncherShot;
        private bool _developmentAimOverride;
        private FirstPersonWeaponAimPhase _aimPhase;
        private float _aimBlendTime;
        private float _hipFireDispersionMultiplier = GameConfig.DEFAULT_HIP_FIRE_DISPERSION_MULTIPLIER;
        private FirstPersonWeaponAsset _recoilSource;

        public event Action<int, int> AmmoChanged;
        public event Action<FirstPersonWeaponState> StateChanged;
        public event Action<GameObject> VisualCreated;
        public event Action HandGrenadeSelectionRequested;
        public event Action FireModeChanged;
        public event Action<Vector3> BulletImpacted;

        public FirstPersonWeaponState State { get; private set; }
        public bool IsReloading => State is FirstPersonWeaponState.Reloading or FirstPersonWeaponState.LoadingGrenade;
        public int ActiveSlot { get; private set; } = -1;
        public FirstPersonWeaponAsset ActiveWeapon => ActiveSlot >= 0 ? _slots[ActiveSlot].Asset : null;
        public int Magazine => ActiveSlot >= 0 && _ammoSource != null ? _ammoSource.GetMagazine(_slots[ActiveSlot].Key) : 0;
        public int Reserve => ActiveSlot >= 0 && _ammoSource != null ? _ammoSource.GetReserve(_slots[ActiveSlot].Key) : 0;
        public string AmmoSection => ActiveSlot >= 0 && _ammoSource != null ? _ammoSource.GetAmmoSection(_slots[ActiveSlot].Key) : string.Empty;
        public string AmmoName => ActiveSlot >= 0 && _ammoSource != null ? _ammoSource.GetAmmoName(_slots[ActiveSlot].Key) : string.Empty;
        public int FireMode => ActiveWeapon != null ? ActiveWeapon.FireModes[Mathf.Clamp(_slots[ActiveSlot].WeaponState.FireModeIndex, 0, ActiveWeapon.FireModes.Count - 1)] : 0;
        public bool IsAiming => _aiming;
        public bool DevelopmentAimOverride
        {
            get => _developmentAimOverride && (Application.isEditor || Debug.isDebugBuild);
            set => _developmentAimOverride = value;
        }
        public float AimFactor => _aim;
        public Vector3 AimOffsetPosition => ActiveWeapon != null ? _slots[ActiveSlot].AimOffsetPosition ?? ActiveWeapon.AimOffsetPosition : Vector3.zero;
        public GameObject Model => _model;
        public GameObject HandsModel => _boundHands;
        public float ScopeZoomScroll => _player != null && !_player.GameplayInputBlocked && _input != null ? _input.ScopeZoomScroll : 0f;
        public Transform Muzzle => _muzzle;
        public float ShotDispersionDegrees => ActiveWeapon is { } asset && _ammoSource != null && !IsGrenadeMode ? asset.GetShotDispersionDegrees(_ammoSource.GetDispersionDegrees(_slots[ActiveSlot].Key), _aiming, _hipFireDispersionMultiplier) : 0f;
        public float ShotMaximumDistance => ActiveSlot >= 0 && _ammoSource != null ? _ammoSource.GetFireDistance(_slots[ActiveSlot].Key) * (_ammoSource.GetBallistics(_slots[ActiveSlot].Key)?.DistanceMultiplier ?? 1f) : 0f;
        public bool IsSilenced => ActiveSlot >= 0 && _slots[ActiveSlot].Silenced;
        public bool HasGrenadeLauncher => ActiveSlot >= 0 && _slots[ActiveSlot].HasGrenadeLauncher;
        public bool IsGrenadeMode => ActiveSlot >= 0 && _slots[ActiveSlot].GrenadeMode;
        public FirstPersonWeaponMotionKind CurrentMotion { get; private set; }

        public void GetHudAmmo(out int loaded, out int reserve, out string name)
        {
            if (ActiveWeapon?.IsHandGrenade == true && _ammoSource != null)
            {
                _ammoSource.GetHandGrenadeStock(_slots[ActiveSlot].Key, out loaded, out name);
                reserve = 0;
                return;
            }

            if (IsGrenadeMode && _ammoSource != null)
            {
                _ammoSource.GetGrenadeAmmo(_slots[ActiveSlot].Key, out loaded, out reserve, out name);
                return;
            }

            loaded = Magazine;
            reserve = Reserve;
            name = AmmoName;
        }

        public FirstPersonWeaponAsset GetSlotWeapon(int slot)
        {
            return IsSlot(slot) ? _slots[slot].Asset : null;
        }

        public void SetAmmoSource(IFirstPersonWeaponAmmoSource source)
        {
            if (_ammoSource != null)
            {
                _ammoSource.Changed -= NotifyAmmo;
            }

            _ammoSource = source;

            if (_ammoSource != null)
            {
                _ammoSource.Changed += NotifyAmmo;
            }

            NotifyAmmo();
        }

        public int GetSelectedFireModeIndex(object weaponKey) => weaponKey != null && _weaponStates.TryGetValue(weaponKey, out FirstPersonWeaponInstanceState state) ? state.FireModeIndex : 0;

        public void SetSlotSilencer(int slot, bool installed)
        {
            if (IsSlot(slot) == false || _slots[slot].Silenced == installed)
            {
                return;
            }

            _slots[slot].Silenced = installed;

            if (ActiveSlot == slot && _model != null)
            {
                if (_muzzle != null)
                {
                    _effects.Detach(_muzzle);
                }

                _flame = null;
                ApplySilencerVisual();
                if (_slots[slot].Asset?.HasAttachmentMotions == true)
                {
                    _hasLoop = false;
                }
            }
        }

        public void SetSlotAimOffset(int slot, Vector3? position)
        {
            if (IsSlot(slot))
            {
                _slots[slot].AimOffsetPosition = position;
            }
        }

        public void SetSlotScope(int slot, bool installed)
        {
            if (!IsSlot(slot) || _slots[slot].HasScope == installed)
            {
                return;
            }

            _slots[slot].HasScope = installed;
            if (ActiveSlot == slot && _slots[slot].Asset?.HasAttachmentMotions == true)
            {
                _hasLoop = false;
            }
        }

        public void SetSlotRecoilMultipliers(int slot, Vector3 recoilMultiplier, float recoilSnappinessMultiplier)
        {
            if (IsSlot(slot))
            {
                FirstPersonWeaponSlot target = _slots[slot];
                target.RecoilMultiplier = recoilMultiplier;
                target.RecoilSnappinessMultiplier = recoilSnappinessMultiplier;
            }
        }

        public void SetSlotGrenadeLauncher(int slot, bool installed)
        {
            if (!IsSlot(slot) || _slots[slot].HasGrenadeLauncher == installed)
            {
                return;
            }

            FirstPersonWeaponSlot target = _slots[slot];
            target.HasGrenadeLauncher = installed;
            bool wasGrenadeMode = target.GrenadeMode;
            target.GrenadeMode = false;
            if (ActiveSlot != slot)
            {
                return;
            }

            if (wasGrenadeMode)
            {
                ResetAimForModeChange();
                NotifyAmmo();
            }

            _hasLoop = false;
            if (State is FirstPersonWeaponState.SwitchingGrenadeMode or FirstPersonWeaponState.LoadingGrenade)
            {
                StopGrenadeActionSound();
                SetState(FirstPersonWeaponState.Ready, 0f);
            }

            UpdateUnderbarrelRound();
        }

        public void SetSlotWeapon(int slot, FirstPersonWeaponAsset asset, object key) => SetSlotWeapon(slot, asset, key, false);

        public void SetSlotWeapon(int slot, FirstPersonWeaponAsset asset, object key, bool isPistol)
        {
            if (!IsSlot(slot))
            {
                return;
            }

            FirstPersonWeaponSlot target = _slots[slot];
            object ammoKey = key ?? asset;
            if (slot == HAND_GRENADE_SELECTION && ActiveSlot == slot && State == FirstPersonWeaponState.ThrowingHandGrenade && _handGrenadeReleased)
            {
                _handGrenadeAfterThrow = (asset, ammoKey);
                return;
            }

            target.IsPistol = asset != null && isPistol;
            if (target.Asset == asset && Equals(target.Key, ammoKey))
            {
                return;
            }

            target.Asset = asset;
            target.Key = ammoKey;
            target.WeaponState = asset != null ? GetWeaponState(ammoKey) : null;
            target.RecoilMultiplier = Vector3.one;
            target.RecoilSnappinessMultiplier = 1f;
            target.GrenadeMode = false;
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
            if (IsReloading)
            {
                Hide();
                return;
            }

            Holster();
        }

        public bool Select(int slot)
        {
            if (IsReloading || !IsSlot(slot) || _slots[slot].Asset == null)
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
            if (IsReloading || State == FirstPersonWeaponState.Hidden || State == FirstPersonWeaponState.Holstering)
            {
                return;
            }

            StopGrenadeActionSound();
            if (State == FirstPersonWeaponState.ThrowingHandGrenade)
            {
                RestoreHandGrenadeModelAnchor();
            }

            SetAiming(false);
            _burstLeft = 0;
            FirstPersonWeaponMotion motion = ActiveWeapon != null ? FindMotion(ActiveWeapon, FirstPersonWeaponMotionKind.Holster) : null;
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
            ResetAimCamera();

            if (State == FirstPersonWeaponState.Hidden)
            {
                return;
            }

            StopGrenadeActionSound();
            _aiming = false;
            _aim = 0.0f;
            DestroyVisuals(true);
            if (_player != null)
            {
                _player.Combat = false;
            }

            ActiveSlot = -1;
            if (_handGrenadeAfterThrow is { } next)
            {
                _handGrenadeAfterThrow = null;
                SetSlotWeapon(HAND_GRENADE_SELECTION, next.Asset, next.Key);
            }

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

        public bool TryGetMuzzleRay(out Ray ray)
        {
            ray = default;
            if (!_muzzleFromView || _muzzle == null)
            {
                return false;
            }

            Vector3 origin = _muzzle.position;
            Vector3 direction = _muzzle.TransformVector(Vector3.forward);
            if (!float.IsFinite(origin.x) || !float.IsFinite(origin.y) || !float.IsFinite(origin.z) || !float.IsFinite(direction.sqrMagnitude) || !float.IsFinite(direction.x) || !float.IsFinite(direction.y) || !float.IsFinite(direction.z) || direction.sqrMagnitude < 0.000001f)
            {
                return false;
            }

            ray = new Ray(origin, direction.normalized);
            return true;
        }

        internal bool TryGetGrenadeAimRay(out Ray ray)
        {
            ray = default;
            Camera camera = _player != null && _player.CameraController != null ? _player.CameraController.Camera : null;
            if (camera == null)
            {
                return false;
            }

            Vector3 viewport = new Vector3(0.5f, 0.5f, 0f);
            if (IsGrenadeMode && _aiming && _grenadeAimPoint != null)
            {
                Vector3 sight = camera.WorldToViewportPoint(_grenadeAimPoint.position);
                if (sight.z > camera.nearClipPlane && float.IsFinite(sight.z) && float.IsFinite(sight.x) && float.IsFinite(sight.y))
                {
                    viewport.x = Mathf.Lerp(viewport.x, sight.x, _aim);
                    viewport.y = Mathf.Lerp(viewport.y, sight.y, _aim);
                }
            }

            ray = camera.ViewportPointToRay(viewport);
            return true;
        }

        private void Awake()
        {
            _hipFireDispersionMultiplier = GameConfig.Load()?.HipFireDispersionMultiplier ?? GameConfig.DEFAULT_HIP_FIRE_DISPERSION_MULTIPLIER;
            _effects = new FirstPersonWeaponEffects(null);
            _worldCrosshair = GetComponent<WeaponWorldCrosshair>();
            if (_worldCrosshair == null)
            {
                _worldCrosshair = gameObject.AddComponent<WeaponWorldCrosshair>();
            }

            _worldCrosshair.Initialize(this, _player);
        }

        private void OnEnable()
        {
            if (_worldCrosshair != null)
            {
                _worldCrosshair.enabled = true;
            }

            Subscribe();
        }

        private void OnDisable()
        {
            if (_worldCrosshair != null)
            {
                _worldCrosshair.enabled = false;
            }

            Hide();
            Unsubscribe();
        }

        private void OnDestroy()
        {
            SetAmmoSource(null);
            DestroyVisuals(true);
            if (_effects != null)
            {
                _effects.Dispose();
            }
        }

        private void Update()
        {
            float deltaTime = Mathf.Min(Time.deltaTime, MAX_FRAME_DELTA);
            _effects.TickMuzzleLight(Time.deltaTime);
            Subscribe();
            TickRecoil();
            bool inputAllowed = _input != null && _player != null && !_player.GameplayInputBlocked;
            if (inputAllowed && !IsReloading)
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
                if (State == FirstPersonWeaponState.Hidden)
                {
                    return;
                }
            }

            StalkerCameraController camera = _player.CameraController;
            bool cursorLocked = camera == null || (!camera.CursorReleased && camera.CursorLockFrame != Time.frameCount);
            bool fireGrenade = false;
            if (inputAllowed && cursorLocked)
            {
                HandleWeaponInput(out fireGrenade);
            }
            else
            {
                _burstLeft = 0;
                SetAiming(DevelopmentAimOverride && State == FirstPersonWeaponState.Ready && !IsGrenadeMode && ActiveWeapon is { IsMelee: false, IsHandGrenade: false });
            }

            TickState(deltaTime);
            if (State == FirstPersonWeaponState.Hidden)
            {
                return;
            }

            TickAim(deltaTime);
            TickEmptyWeapon();
            if (_graph != null)
            {
                _graph.Evaluate(deltaTime);
            }

            if (State == FirstPersonWeaponState.LoadingGrenade)
            {
                UpdateUnderbarrelRound();
            }

            if (ActiveWeapon?.IsStandaloneLauncher == true)
            {
                bool reloading = State == FirstPersonWeaponState.Reloading;
                _weaponView?.UpdateLauncherChambers(Magazine, reloading && CurrentMotion == FirstPersonWeaponMotionKind.LauncherReloadInsert);
                if (reloading)
                {
                    _weaponView?.SetLauncherRoundVisible(true);
                }
            }

            if (_revolverView != null || _magazineRoundsView != null)
            {
                int loaded = Magazine;
                bool reloading = State == FirstPersonWeaponState.Reloading;
                if (_revolverView != null)
                {
                    FirstPersonWeaponInstanceState weaponState = _slots[ActiveSlot].WeaponState;
                    weaponState.SpentChambers = _revolverView.RefreshRounds(loaded, reloading, _stateTime, weaponState.SpentChambers, _reloadIncomingRounds);
                }

                float progress = reloading && _stateDuration > 0f ? _stateTime / _stateDuration : 0f;
                _magazineRoundsView?.Refresh(loaded, reloading, CurrentMotion == FirstPersonWeaponMotionKind.ReloadEmpty, progress, _reloadIncomingRounds);
            }

            TickMeleeAttack();
            ResetModelRoot();
            ApplyHandsOffset();
            TickHandGrenade();
            UpdateFlame();
            if (fireGrenade)
            {
                ShootGrenade(ActiveWeapon);
            }
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
            if (IsReloading || requester == (object)this || State == FirstPersonWeaponState.Hidden || _subscribedRig == null || _subscribedRig.HandsOwner != (object)this)
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
            else if (_input.KnifeWeaponPressed)
            {
                Select(KNIFE_SLOT);
            }
            else if (_input.HandGrenadePressed)
            {
                HandGrenadeSelectionRequested?.Invoke();
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
            ResetAimCamera();
            _aimPhase = FirstPersonWeaponAimPhase.Hip;
            _aimBlendTime = 0f;
            _hasLoop = false;
            _inSprint = false;
            FirstPersonWeaponMotion motion = FindMotion(asset, FirstPersonWeaponMotionKind.Draw);
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

        private void HandleWeaponInput(out bool fireGrenade)
        {
            fireGrenade = false;
            FirstPersonWeaponAsset asset = ActiveWeapon;
            bool ready = State == FirstPersonWeaponState.Ready;
            bool sprinting = _player.State.IsSprinting;
            if (asset.IsHandGrenade)
            {
                SetAiming(false);
                if (ready && !sprinting && _input.FirePressed)
                {
                    _handGrenadeReleaseRequested = false;
                    _handGrenadeReleased = false;
                    PlayHandGrenadeMotion(asset, FirstPersonWeaponMotionKind.HandGrenadePrepare, FirstPersonWeaponState.PreparingHandGrenade, false);
                }

                if (State is (FirstPersonWeaponState.PreparingHandGrenade or FirstPersonWeaponState.HoldingHandGrenade) && !_input.FireHeld)
                {
                    _handGrenadeReleaseRequested = true;
                }

                return;
            }

            if (asset.IsMelee)
            {
                SetAiming(false);
                if (ready && !sprinting && _activeMeleeAttack == null && Time.time >= _actionUntil && (_input.FirePressed || _input.AimPressed))
                {
                    PlayMeleeAttack(asset, _input.FirePressed ? FirstPersonWeaponMotionKind.MeleePrimary : FirstPersonWeaponMotionKind.MeleeSecondary);
                }

                return;
            }

            bool holdShotAim = _aimedLauncherShot && Time.time < _actionUntil;
            bool aimRequested = (_input.AimHeld || DevelopmentAimOverride) && (DevelopmentAimOverride || !sprinting);
            SetAiming(ready && (holdShotAim || aimRequested));
            if (!ready)
            {
                _burstLeft = 0;
                return;
            }

            if (_input.GrenadeModePressed && TrySwitchGrenadeMode())
            {
                return;
            }

            if (IsGrenadeMode)
            {
                if (Time.time >= _actionUntil)
                {
                    if (_input.ReloadPressed)
                    {
                        TryStartGrenadeReload();
                    }
                    else
                    {
                        fireGrenade = _input.FirePressed && !sprinting;
                    }
                }

                return;
            }

            if (_input.NextReticlePressed)
            {
                foreach (WeaponReticleCycle reticle in _reticleBindings)
                {
                    if (reticle != null && reticle.isActiveAndEnabled)
                    {
                        reticle.Next();
                    }
                }
            }

            if (_input.NextAmmoPressed && _ammoSource != null && _ammoSource.TrySwitchAmmo(_slots[ActiveSlot].Key))
            {
                StartReload(asset);
                NotifyAmmo();
                return;
            }

            if (_input.ReloadPressed && _ammoSource != null && _ammoSource.TryStartReload(_slots[ActiveSlot].Key))
            {
                StartReload(asset);
                NotifyAmmo();
                return;
            }

            if (_input.FireModePressed && asset.FireModes.Count > 1)
            {
                StartFireModeSwitch(asset, _slots[ActiveSlot].WeaponState);
                return;
            }

            bool fixedBurst = asset.HasFixedTwoRoundBurst && FireMode == 2;
            if (_input.FirePressed && !sprinting && (!fixedBurst || _burstLeft <= 0))
            {
                int mode = FireMode;
                _burstLeft = mode == FirstPersonWeaponAsset.AUTO_FIRE_MODE ? int.MaxValue : Mathf.Max(1, mode);
                if (Magazine <= 0)
                {
                    _burstLeft = 0;
                    PlayEmptyClick(asset);
                }
            }

            if (sprinting || (!_input.FireHeld && !fixedBurst))
            {
                _burstLeft = 0;
            }

            if (_burstLeft > 0 && Time.time >= _nextShotTime)
            {
                if (Magazine <= 0)
                {
                    _burstLeft = 0;
                    return;
                }

                Shoot(asset);
            }
        }

        public bool TryGetHandGrenadeThrowRay(out Ray ray)
        {
            ray = default;
            Camera camera = _player != null && _player.CameraController != null ? _player.CameraController.Camera : null;
            if (camera == null || ActiveWeapon?.IsHandGrenade != true || _model == null || !_handGrenadeVisible)
            {
                return false;
            }

            ray = new Ray(_model.transform.position, camera.transform.forward);
            return true;
        }

        private void PlayHandGrenadeMotion(FirstPersonWeaponAsset asset, FirstPersonWeaponMotionKind kind, FirstPersonWeaponState state, bool loop)
        {
            FirstPersonWeaponMotion motion = FindMotion(asset, kind);
            if (motion?.Hands == null)
            {
                SetState(FirstPersonWeaponState.Ready, 0f);
                return;
            }

            _hasLoop = false;
            PlayMotion(motion, kind == FirstPersonWeaponMotionKind.Draw ? 0f : asset.CrossFade, loop, true);
            SetState(state, motion.Duration);
        }

        private void TickHandGrenade()
        {
            if (State is (FirstPersonWeaponState.Drawing or FirstPersonWeaponState.Ready) && ActiveWeapon?.IsHandGrenade == true && !_handGrenadeVisible)
            {
                SetHandGrenadeVisible(true);
            }

            if (State is not (FirstPersonWeaponState.PreparingHandGrenade or FirstPersonWeaponState.HoldingHandGrenade or FirstPersonWeaponState.ThrowingHandGrenade))
            {
                return;
            }

            FirstPersonWeaponAsset asset = ActiveWeapon;
            FirstPersonWeaponMotion motion = FindMotion(asset, CurrentMotion);
            double clipTime = _graph.HandsClipTime;
            if (State == FirstPersonWeaponState.HoldingHandGrenade || State == FirstPersonWeaponState.PreparingHandGrenade && clipTime >= motion.Hands.length)
            {
                if (_handGrenadeReleaseRequested)
                {
                    if (_handGrenadeThrowHand != null)
                    {
                        _model.transform.SetParent(_handGrenadeThrowHand, true);
                    }

                    PlayHandGrenadeMotion(asset, FirstPersonWeaponMotionKind.HandGrenadeThrow, FirstPersonWeaponState.ThrowingHandGrenade, false);
                }
                else if (State == FirstPersonWeaponState.PreparingHandGrenade)
                {
                    PlayHandGrenadeMotion(asset, FirstPersonWeaponMotionKind.HandGrenadeHold, FirstPersonWeaponState.HoldingHandGrenade, true);
                }

                return;
            }

            if (State != FirstPersonWeaponState.ThrowingHandGrenade)
            {
                return;
            }

            float releaseTime = Mathf.Min(asset.HandGrenadeReleaseFrame / motion.Hands.frameRate, motion.Hands.length);
            if (!_handGrenadeReleased && clipTime >= releaseTime)
            {
                _handGrenadeReleased = true;
                ReleaseHandGrenade(asset);
            }

            if (State == FirstPersonWeaponState.ThrowingHandGrenade && clipTime >= motion.Hands.length)
            {
                if (TryFinishHandGrenadeSwitch())
                {
                    return;
                }

                RestoreHandGrenadeModelAnchor();
                PlayHandGrenadeMotion(asset, FirstPersonWeaponMotionKind.Draw, FirstPersonWeaponState.Drawing, false);
            }
        }

        private bool TryFinishHandGrenadeSwitch()
        {
            if (!_handGrenadeAfterThrow.HasValue)
            {
                return false;
            }

            Hide();
            if (_pendingSlot < 0 && _redrawSlot < 0 && Draw(HAND_GRENADE_SELECTION))
            {
                _graph.Evaluate(0f);
                ResetModelRoot();
                ApplyHandsOffset();
                SetHandGrenadeVisible(true);
            }

            return true;
        }

        private void RestoreHandGrenadeModelAnchor()
        {
            if (_model == null || _handGrenadeAnchor == null)
            {
                return;
            }

            Transform visual = _model.transform;
            visual.SetParent(_handGrenadeAnchor, false);
            visual.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            visual.localScale = ActiveWeapon.HudModel.transform.localScale;
        }

        private void ReleaseHandGrenade(FirstPersonWeaponAsset asset)
        {
            if (asset.HandGrenade?.IsConfigured != true || !TryGetHandGrenadeThrowRay(out Ray release) || _ammoSource == null)
            {
                return;
            }

            GameObject held = _model;
            if (!_ammoSource.TryConsumeHandGrenade(_slots[ActiveSlot].Key))
            {
                return;
            }

            int mask = asset.Ballistics != null ? asset.Ballistics.BallisticHitMask.value : Physics.DefaultRaycastLayers;
            GrenadeProjectile.LaunchThrown(asset.HandGrenadeVisual, asset.HandGrenade, release, asset.HandGrenadeThrowSpeed, mask, _player.transform.root, held.transform, _audio != null ? _audio.outputAudioMixerGroup : null);
            if (_model == held)
            {
                SetHandGrenadeVisible(false);
            }
            else
            {
                held.SetActive(false);
            }

            NotifyAmmo();
        }

        private void SetHandGrenadeVisible(bool visible)
        {
            _handGrenadeVisible = visible;
            if (_handGrenadeRenderers == null)
            {
                return;
            }

            foreach (Renderer renderer in _handGrenadeRenderers)
            {
                if (renderer != null)
                {
                    renderer.forceRenderingOff = !visible;
                }
            }
        }

        private void PlayMeleeAttack(FirstPersonWeaponAsset asset, FirstPersonWeaponMotionKind kind)
        {
            FirstPersonWeaponMotion motion = FindMotion(asset, kind);
            if (motion?.Hands == null || _graph == null)
            {
                return;
            }

            PlayMotion(motion, SHOT_FADE, false, true);
            _hasLoop = false;
            _activeMeleeAttack = motion;
            _meleeContactPending = motion.MeleeDamageHp > 0f && motion.MeleeReach > 0f && motion.MeleeContactTime > 0f && motion.MeleeContactTime <= motion.Hands.length;
        }

        private void TickMeleeAttack()
        {
            FirstPersonWeaponMotion motion = _activeMeleeAttack;
            if (motion == null)
            {
                return;
            }

            double clipTime = _graph.HandsClipTime;
            if (_meleeContactPending && clipTime >= motion.MeleeContactTime)
            {
                _meleeContactPending = false;
                ApplyMeleeHit(motion);
            }

            if (clipTime >= motion.Hands.length)
            {
                _activeMeleeAttack = null;
            }
        }

        private void ApplyMeleeHit(FirstPersonWeaponMotion motion)
        {
            Camera camera = _player.CameraController != null ? _player.CameraController.Camera : null;
            if (camera == null)
            {
                return;
            }

            Ray ray = new Ray(camera.transform.position, camera.transform.forward);
            int mask = ActiveWeapon.Ballistics != null ? ActiveWeapon.Ballistics.BallisticHitMask.value : Physics.DefaultRaycastLayers;
            _meleeHits ??= new RaycastHit[MELEE_HIT_BUFFER_SIZE];
            RaycastHit[] hits = _meleeHits;
            int count = Physics.RaycastNonAlloc(ray, hits, motion.MeleeReach, mask, QueryTriggerInteraction.Ignore);
            if (count == hits.Length)
            {
                hits = Physics.RaycastAll(ray, motion.MeleeReach, mask, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }

            Transform owner = _player.transform.root;
            Collider closest = null;
            float closestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == null || hit.distance >= closestDistance || hit.collider.transform.IsChildOf(owner))
                {
                    continue;
                }

                closest = hit.collider;
                closestDistance = hit.distance;
            }

            if (closest != null)
            {
                closest.GetComponentInParent<IHealthDamageReceiver>()?.TryApplyHealthDamage(motion.MeleeDamageHp);
            }
        }

        private void ShootGrenade(FirstPersonWeaponAsset asset)
        {
            if (_ammoSource == null || !IsGrenadeMode)
            {
                return;
            }

            object weaponKey = _slots[ActiveSlot].Key;
            GrenadeAmmoData ammo = _ammoSource.GetGrenadeShot(weaponKey, out GameObject visual);
            if (visual == null && ammo == null)
            {
                _ammoSource.GetGrenadeAmmo(weaponKey, out int loaded, out _, out _);
                if (loaded == 0)
                {
                    PlayEmptyClick(asset, false);
                    return;
                }
            }

            Transform muzzle = _weaponView != null ? _weaponView.GrenadeMuzzle : null;
            if (muzzle == null || visual == null || ammo == null || !ammo.IsConfigured || !TryGetGrenadeAimRay(out Ray aimRay))
            {
                Debug.LogError($"{asset.name}: grenade firing requires a launcher muzzle, camera, and configured grenade ammo.", this);
                return;
            }

            Vector3 muzzleDirection = muzzle.TransformVector(Vector3.forward).normalized;
            if (!float.IsFinite(muzzleDirection.sqrMagnitude) || muzzleDirection.sqrMagnitude < 0.5f)
            {
                Debug.LogError($"{asset.name}: grenade muzzle direction is invalid.", this);
                return;
            }

            Ray muzzleRay = new Ray(muzzle.position, muzzleDirection);
            Transform weapon = _model.transform;
            if (!_ammoSource.TryShootGrenade(weaponKey))
            {
                return;
            }

            NotifyAmmo();
            int mask = asset.Ballistics != null ? asset.Ballistics.BallisticHitMask.value : Physics.DefaultRaycastLayers;
            AudioSource source = _shotAudio != null ? _shotAudio : _audio;
            GrenadeProjectile.Launch(visual, ammo, muzzleRay, aimRay, mask, _player.transform.root, weapon, source != null ? source.outputAudioMixerGroup : null);
            FirstPersonWeaponMotion motion = asset.FindGrenadeShot(_aiming, GetMotionMode());
            if (motion != null)
            {
                PlayMotion(motion, SHOT_FADE, false, false);
                _actionUntil = Time.time + motion.Duration;
            }

            PlayOneShot(source, motion?.Sound != null ? motion.Sound : ammo.LaunchSound);
        }

        private void Shoot(FirstPersonWeaponAsset asset)
        {
            bool fired = asset.IsStandaloneLauncher ? TryShootStandaloneLauncher(asset) : TryShootBallistic(asset);
            if (!fired)
            {
                _burstLeft = 0;
                return;
            }

            _burstLeft--;
            _nextShotTime = Time.time + asset.GetShotInterval(FireMode);
            if (_revolverView != null && _revolverView.RetainsSpentCases)
            {
                _slots[ActiveSlot].WeaponState.SpentChambers |= 1 << Magazine;
            }

            FirstPersonWeaponMotionMode mode = GetMotionMode();
            FirstPersonWeaponMotion motion = Magazine == 0 ? _revolverView?.FindLastShot(_aiming) : null;
            motion ??= _aiming ? asset.FindRandom(FirstPersonWeaponMotionKind.ShootAim, mode) : null;
            if (motion == null)
            {
                motion = asset.FindRandom(FirstPersonWeaponMotionKind.Shoot, mode);
            }

            if (motion != null)
            {
                PlayMotion(motion, SHOT_FADE, false, false);
                _actionUntil = Time.time + motion.Duration;
                _aimedLauncherShot = asset.IsStandaloneLauncher && _aiming;

                if (Magazine <= 0 && FindMotion(asset, FirstPersonWeaponMotionKind.IdleEmpty)?.Weapon != null)
                {
                    float holdTime = motion.Weapon != null && motion.Weapon.frameRate > 0f ? asset.LastShotHoldFrame / motion.Weapon.frameRate : 0f;
                    _emptyWeaponAt = Time.time + holdTime / motion.Speed;
                }
            }

            PlayShotSounds(asset, Magazine);
            Vector3 recoil = Vector3.Scale(asset.Recoil, _slots[ActiveSlot].RecoilMultiplier) * (_player.State.IsCrouching ? 0.5f : 1.0f);
            if (_player.CameraController != null && _player.CameraController.Recoil != null)
            {
                _player.CameraController.Recoil.Shoot(recoil.x, recoil.y, recoil.z);
            }

            SpawnShotEffects(asset);
            NotifyAmmo();
        }

        private bool TryShootBallistic(FirstPersonWeaponAsset asset)
        {
            BallisticAmmoData ammo = _ammoSource != null ? _ammoSource.GetBallistics(_slots[ActiveSlot].Key) : null;
            float bulletSpeed = _ammoSource?.GetBulletSpeed(_slots[ActiveSlot].Key) ?? 0f;
            float fireDistance = _ammoSource?.GetFireDistance(_slots[ActiveSlot].Key) ?? 0f;
            float dispersion = ShotDispersionDegrees;

            if (!TryGetMuzzleRay(out Ray muzzleRay) || ammo == null || !ammo.IsConfigured || bulletSpeed <= 0f || fireDistance <= 0f || asset.Ballistics == null || asset.Ballistics.BulletPrefab == null)
            {
                if (!_ballisticsWarningLogged)
                {
                    _ballisticsWarningLogged = true;
                    Debug.LogError($"{asset.name}: configure loaded ammo ballistics, bullet prefab and {(IsSilenced ? "SilencedMuzzlePoint" : "MuzzlePoint")} before firing.", this);
                }

                return false;
            }

            Vector3 launchPosition = muzzleRay.origin;
            Quaternion launchRotation = Quaternion.LookRotation(muzzleRay.direction, Vector3.up);

            if (_ammoSource == null || _ammoSource.TryShoot(_slots[ActiveSlot].Key) == false)
            {
                return false;
            }

            SpawnProjectiles(asset.Ballistics, ammo, launchPosition, launchRotation, bulletSpeed, fireDistance, dispersion);
            return true;
        }

        private bool TryShootStandaloneLauncher(FirstPersonWeaponAsset asset)
        {
            object key = _slots[ActiveSlot].Key;
            GameObject visual = null;
            GrenadeAmmoData ammo = _ammoSource?.GetLauncherShot(key, out visual);
            float speed = _ammoSource?.GetBulletSpeed(key) ?? 0f;
            if (!TryGetMuzzleRay(out Ray muzzle) || ammo?.IsConfigured != true || visual == null || speed <= 0f || !float.IsFinite(speed))
            {
                if (!_ballisticsWarningLogged)
                {
                    _ballisticsWarningLogged = true;
                    Debug.LogError($"{asset.name}: configure the launcher muzzle and loaded explosive ammunition before firing.", this);
                }

                return false;
            }

            Ray aim = muzzle;
            if (!ammo.FliesStraight && _aiming && !TryGetGrenadeAimRay(out aim))
            {
                return false;
            }

            if (!ammo.FliesStraight && !_aiming && asset.UsesStandaloneHipDispersion)
            {
                float tangent = Mathf.Tan(ShotDispersionDegrees * Mathf.Deg2Rad);
                aim.direction = GetDispersedDirection(Quaternion.LookRotation(muzzle.direction, Vector3.up), tangent);
            }

            if (!_ammoSource.TryShoot(key))
            {
                return false;
            }

            int mask = asset.Ballistics != null ? asset.Ballistics.BallisticHitMask.value : Physics.DefaultRaycastLayers;
            AudioSource sound = _shotAudio != null ? _shotAudio : _audio;
            GrenadeProjectile.LaunchStandalone(visual, ammo, muzzle, aim, speed, mask, _player.transform.root, _model.transform, sound != null ? sound.outputAudioMixerGroup : null);
            return true;
        }

        private void SpawnProjectiles(WeaponBallisticsSettings settings, BallisticAmmoData ammo, Vector3 position, Quaternion rotation, float speed, float distance, float dispersionDegrees)
        {
            float tangent = Mathf.Tan(dispersionDegrees * Mathf.Deg2Rad);
            Transform owner = _player != null ? _player.transform.root : transform.root;

            for (int i = 0; i < ammo.ProjectileCount; i++)
            {
                Vector3 direction = GetDispersedDirection(rotation, tangent);
                GameObject projectileObject = Instantiate(settings.BulletPrefab, position, rotation);
                BallisticProjectile projectile = projectileObject.GetComponent<BallisticProjectile>();

                if (projectile == null)
                {
                    projectile = projectileObject.AddComponent<BallisticProjectile>();
                }

                if (BulletImpacted != null)
                {
                    projectile.Impacted += NotifyBulletImpact;
                }

                projectile.Initialize(settings, ammo, position, direction, speed, distance * ammo.DistanceMultiplier, owner, _model.transform);
            }
        }

        private void NotifyBulletImpact(Vector3 position)
        {
            BulletImpacted?.Invoke(position);
        }

        private static Vector3 GetDispersedDirection(Quaternion rotation, float tangent)
        {
            Vector2 dispersion = UnityEngine.Random.insideUnitCircle * tangent;
            return rotation * new Vector3(dispersion.x, dispersion.y, 1f).normalized;
        }

        private void PlayShotSounds(FirstPersonWeaponAsset asset, int magazine)
        {
            AudioSource source = _shotAudio != null ? _shotAudio : _audio;
            if (source == null)
            {
                return;
            }

            IReadOnlyList<AudioClip> shots = IsSilenced ? asset.SilencedShotSounds : asset.ShotSounds;
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

        private void PlayEmptyClick(FirstPersonWeaponAsset asset, bool animate = true)
        {
            bool revolverShotPlaying = animate && _revolverView != null && CurrentMotion is (FirstPersonWeaponMotionKind.Shoot or FirstPersonWeaponMotionKind.ShootAim) && Time.time < _actionUntil;
            if (revolverShotPlaying || Time.time < _nextEmptyClickTime)
            {
                return;
            }

            _nextEmptyClickTime = Time.time + asset.EmptyClickInterval;
            FirstPersonWeaponMotion emptyClick = animate ? FindMotion(asset, FirstPersonWeaponMotionKind.EmptyClick) : null;
            if (emptyClick != null)
            {
                PlayMotion(emptyClick, SHOT_FADE, false, false);
                _actionUntil = Time.time + emptyClick.Duration;
                PlayOneShot(_shotAudio != null ? _shotAudio : _audio, asset.EmptySound);
                return;
            }

            FirstPersonWeaponMotion motion = animate ? asset.FindRandom(FirstPersonWeaponMotionKind.Shoot, GetMotionMode()) : null;

            if (motion != null)
            {
                PlayLoop(asset, SelectIdle(asset, _player.State));
                CurrentMotion = motion.Kind;
                _emptyWeaponAt = float.PositiveInfinity;
                _hasLoop = false;

                if (_graph != null)
                {
                    _graph.PlayEmptyShot(motion.Hands, motion.Speed, SHOT_FADE, asset.EmptyShotStrength);
                }

                _actionUntil = Time.time + motion.Duration;
            }

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
            Transform flashFollow = _revolverView != null && _revolverView.AnchorFlashAtShotPose ? null : muzzle;
            _flame = _effects.Play(IsSilenced ? asset.SilencedFlameParticles : asset.FlameParticles, flashFollow, muzzle.position, rotation);
            if (_flame != null)
            {
                if (flashFollow == null && _flame.Transform.TryGetComponent(out XRayParticleGroupPlayer group))
                {
                    group.RenderInitialFrame();
                }

                _effects.PlayMuzzleLight(flashFollow != null ? muzzle : _flame.Transform);
            }

            _effects.Play(IsSilenced ? asset.SilencedSmokeParticles : asset.SmokeParticles, null, muzzle.position, rotation);
        }

        private Quaternion GetMuzzleRotation(Transform muzzle)
        {
            Transform axis = _muzzleFromView ? muzzle : _modelRoot != null ? _modelRoot : _model.transform;
            Vector3 forward = axis.TransformVector(Vector3.forward);
            return forward.sqrMagnitude > 0.0f ? Quaternion.LookRotation(forward, Vector3.up) : axis.rotation;
        }

        private void UpdateFlame()
        {
            if (_flame == null || _muzzle == null || !_flame.IsPlaying || _flame.Transform.parent != _muzzle)
            {
                return;
            }

            _flame.SetPose(_muzzle.position, GetMuzzleRotation(_muzzle));
        }

        public bool TrySwitchGrenadeMode()
        {
            if (State != FirstPersonWeaponState.Ready || !HasGrenadeLauncher || ActiveWeapon == null)
            {
                return false;
            }

            bool entering = !IsGrenadeMode;
            FirstPersonWeaponMotionKind kind = entering ? FirstPersonWeaponMotionKind.GrenadeModeEnter : FirstPersonWeaponMotionKind.GrenadeModeExit;
            FirstPersonWeaponMotion motion = ActiveWeapon.Find(kind, 0, GetMotionMode(true));
            if (motion == null)
            {
                return false;
            }

            ResetAimForModeChange();
            _burstLeft = 0;
            _slots[ActiveSlot].GrenadeMode = entering;
            PlayMotion(motion, ActiveWeapon.CrossFade, false, true);
            SetState(FirstPersonWeaponState.SwitchingGrenadeMode, motion.Duration);
            return true;
        }

        public bool TryStartGrenadeReload()
        {
            if (State != FirstPersonWeaponState.Ready || !IsGrenadeMode || _ammoSource?.CanReloadGrenade(_slots[ActiveSlot].Key) != true)
            {
                return false;
            }

            FirstPersonWeaponMotion motion = FindMotion(ActiveWeapon, FirstPersonWeaponMotionKind.GrenadeReload);
            if (motion == null)
            {
                return false;
            }

            SetAiming(false);
            float revealTime = _weaponView != null ? _weaponView.GetUnderbarrelRoundRevealTime(motion) : float.PositiveInfinity;
            _underbarrelRoundRevealTime = Mathf.Max(ActiveWeapon.CrossFade, revealTime);
            _underbarrelShellTimes = _weaponView != null ? _weaponView.GetUnderbarrelShellTimes(motion) : Vector2.zero;
            _underbarrelShellTimes.x = Mathf.Max(ActiveWeapon.CrossFade, _underbarrelShellTimes.x);
            PlayMotion(motion, ActiveWeapon.CrossFade, false, true);
            SetState(FirstPersonWeaponState.LoadingGrenade, motion.Duration);
            return true;
        }

        private FirstPersonWeaponMotion FindMotion(FirstPersonWeaponAsset asset, FirstPersonWeaponMotionKind kind) => asset.Find(kind, 0, GetMotionMode());

        private FirstPersonWeaponMotionMode GetMotionMode(bool grenadeAction = false)
        {
            FirstPersonWeaponMotionMode mode = ActiveSlot >= 0 && _slots[ActiveSlot].HasScope ? FirstPersonWeaponMotionMode.Scoped : FirstPersonWeaponMotionMode.None;
            if (grenadeAction || IsGrenadeMode)
            {
                return mode | FirstPersonWeaponMotionMode.GrenadeMode;
            }

            if (HasGrenadeLauncher)
            {
                return mode | FirstPersonWeaponMotionMode.WithGrenadeLauncher;
            }

            return IsSilenced ? mode | FirstPersonWeaponMotionMode.Silenced : mode;
        }

        private void StartReload(FirstPersonWeaponAsset asset)
        {
            FirstPersonWeaponMotion motion = FindMotion(asset, FirstPersonWeaponMotionKind.LauncherReloadOpen);
            motion ??= Magazine <= 0 ? FindMotion(asset, FirstPersonWeaponMotionKind.ReloadEmpty) : null;
            motion ??= _revolverView?.FindPartialReload(asset.MagazineSize - Magazine);
            if (motion == null)
            {
                motion = FindMotion(asset, FirstPersonWeaponMotionKind.Reload);
            }

            SetAiming(false);
            _burstLeft = 0;
            if (_revolverView != null || _magazineRoundsView != null)
            {
                _reloadIncomingRounds = Mathf.Min(asset.MagazineSize, Magazine + Reserve);
                _revolverView?.BeginReload();
            }

            if (motion == null)
            {
                FinishReload(asset);
                return;
            }

            PlayMotion(motion, asset.CrossFade, false, true);
            SetState(FirstPersonWeaponState.Reloading, motion.Duration);
        }

        private void FinishReload(FirstPersonWeaponAsset asset)
        {
            _graph?.CompleteMagazineSwap();
            if (_ammoSource != null)
            {
                _ammoSource.Reload(_slots[ActiveSlot].Key, asset.MagazineSize);
            }

            if (_revolverView != null && _revolverView.RetainsSpentCases)
            {
                _slots[ActiveSlot].WeaponState.SpentChambers = 0;
            }

            NotifyAmmo();
        }

        private bool AdvanceLauncherReload(FirstPersonWeaponAsset asset)
        {
            if (_ammoSource == null)
            {
                return false;
            }

            object key = _slots[ActiveSlot].Key;
            if (CurrentMotion == FirstPersonWeaponMotionKind.LauncherReloadInsert)
            {
                _ammoSource.Reload(key, Magazine + 1);
                NotifyAmmo();
            }

            FirstPersonWeaponMotion next = Magazine < asset.MagazineSize && _ammoSource.GetReserve(key) > 0 ? asset.Find(FirstPersonWeaponMotionKind.LauncherReloadInsert, Magazine) : null;
            next ??= FindMotion(asset, FirstPersonWeaponMotionKind.LauncherReloadClose);
            if (next == null)
            {
                return false;
            }

            PlayMotion(next, asset.CrossFade, false, true);
            SetState(FirstPersonWeaponState.Reloading, next.Duration);
            return true;
        }

        private void StartFireModeSwitch(FirstPersonWeaponAsset asset, FirstPersonWeaponInstanceState weaponState)
        {
            weaponState.FireModeIndex = (weaponState.FireModeIndex + 1) % asset.FireModes.Count;
            FireModeChanged?.Invoke();
            _burstLeft = 0;
            FirstPersonWeaponMotion motion = FindMotion(asset, FirstPersonWeaponMotionKind.FireMode);
            if (motion == null)
            {
                return;
            }

            PlayMotion(motion, asset.CrossFade, false, true);
            float duration = motion.Duration;
            _actionUntil = Time.time + duration;
            SetState(FirstPersonWeaponState.SwitchingMode, duration * FIRE_MODE_INPUT_RELEASE_RATIO);
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
                case FirstPersonWeaponState.PreparingHandGrenade:
                case FirstPersonWeaponState.HoldingHandGrenade:
                case FirstPersonWeaponState.ThrowingHandGrenade:
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
                    if (CurrentMotion is FirstPersonWeaponMotionKind.LauncherReloadOpen or FirstPersonWeaponMotionKind.LauncherReloadInsert)
                    {
                        if (AdvanceLauncherReload(asset))
                        {
                            return;
                        }
                    }
                    else if (CurrentMotion != FirstPersonWeaponMotionKind.LauncherReloadClose)
                    {
                        FinishReload(asset);
                    }

                    break;
                case FirstPersonWeaponState.LoadingGrenade:
                    _ammoSource?.ReloadGrenade(_slots[ActiveSlot].Key);
                    break;
            }

            _hasLoop = false;
            if (State != FirstPersonWeaponState.SwitchingMode)
            {
                _actionUntil = 0.0f;
            }

            SetState(FirstPersonWeaponState.Ready, 0.0f);
            TickReady(asset);
        }

        private void TickReady(FirstPersonWeaponAsset asset)
        {
            if (_activeMeleeAttack != null || Time.time < _actionUntil)
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
                return FirstPersonWeaponMotionKind.Idle;
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
            FirstPersonWeaponMotion motion = FindMotion(asset, kind);
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
            FirstPersonWeaponMotion motion = FindMotion(asset, kind) ?? FindMotion(asset, FirstPersonWeaponMotionKind.Idle);
            if (motion == null)
            {
                return;
            }

            AnimationClip weaponClip = FindEmptyWeaponClip(motion) ?? motion.Weapon;

            if (_hasLoop && _loopKind == kind && CurrentMotion == kind)
            {
                if (_loopWeaponClip != weaponClip)
                {
                    _loopWeaponClip = weaponClip;

                    if (_graph != null)
                    {
                        _graph.PlayWeapon(weaponClip, motion.WeaponSpeed, Magazine <= 0 ? 0f : asset.CrossFade, true);
                    }
                }

                return;
            }

            float fade = _aimedLauncherShot ? Mathf.Max(asset.CrossFade, LAUNCHER_AIM_SHOT_BLEND_TIME) : asset.CrossFade;
            PlayMotion(motion, fade, true, false);
            _loopWeaponClip = weaponClip;
            _loopKind = kind;
            _hasLoop = true;
            CurrentMotion = kind;
        }

        private AnimationClip FindEmptyWeaponClip(FirstPersonWeaponMotion motion)
        {
            if (Magazine > 0)
            {
                return null;
            }

            if (motion.EmptyWeapon != null)
            {
                return motion.EmptyWeapon;
            }

            if (IsGrenadeMode || motion.Kind is FirstPersonWeaponMotionKind.Shoot or FirstPersonWeaponMotionKind.ShootAim or FirstPersonWeaponMotionKind.Reload or FirstPersonWeaponMotionKind.ReloadEmpty or FirstPersonWeaponMotionKind.Misfire or FirstPersonWeaponMotionKind.GrenadeModeExit)
            {
                return null;
            }

            return FindMotion(ActiveWeapon, FirstPersonWeaponMotionKind.IdleEmpty)?.Weapon;
        }

        private void PlayMotion(FirstPersonWeaponMotion motion, float fade, bool loop, bool withEffects)
        {
            bool leavingReload = CurrentMotion is FirstPersonWeaponMotionKind.Reload or FirstPersonWeaponMotionKind.ReloadEmpty;
            bool startingReload = motion.Kind is FirstPersonWeaponMotionKind.Reload or FirstPersonWeaponMotionKind.ReloadEmpty;
            bool replaceLauncherPose = ActiveWeapon?.IsStandaloneLauncher == true && (startingReload || leavingReload);
            _activeMeleeAttack = null;
            _aimedLauncherShot = false;
            CurrentMotion = motion.Kind;
            _emptyWeaponAt = float.PositiveInfinity;
            if (_graph != null)
            {
                AnimationClip emptyClip = FindEmptyWeaponClip(motion);
                _graph.Play(motion.Hands, emptyClip ?? motion.Weapon, motion.Speed, fade, loop, emptyClip != null || replaceLauncherPose ? 0f : fade, motion.WeaponSpeed);
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

        private void StopGrenadeActionSound()
        {
            if (_audio != null && State is FirstPersonWeaponState.SwitchingGrenadeMode or FirstPersonWeaponState.LoadingGrenade)
            {
                _audio.Stop();
            }
        }

        private void TickEmptyWeapon()
        {
            if (Time.time < _emptyWeaponAt)
            {
                return;
            }

            _emptyWeaponAt = float.PositiveInfinity;

            if (_graph == null || ActiveWeapon == null || Magazine > 0)
            {
                return;
            }

            FirstPersonWeaponMotion empty = FindMotion(ActiveWeapon, FirstPersonWeaponMotionKind.IdleEmpty);

            if (empty?.Weapon != null)
            {
                _graph.PlayWeapon(empty.Weapon, empty.Speed, 0f, true);
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

            if (state != FirstPersonWeaponState.Reloading && ActiveWeapon?.IsStandaloneLauncher == true)
            {
                _weaponView?.SetLauncherRoundVisible(Magazine > 0);
            }

            bool grenadeReloadChanged = State == FirstPersonWeaponState.LoadingGrenade || state == FirstPersonWeaponState.LoadingGrenade;
            State = state;
            if (grenadeReloadChanged)
            {
                UpdateUnderbarrelRound();
            }

            StateChanged?.Invoke(state);
        }

        private void SetAiming(bool aiming)
        {
            if (aiming && _player != null && _player.Rig != null && _player.Rig.IsWeaponAimObstructed(this))
            {
                aiming = false;
            }

            if (_aiming == aiming)
            {
                return;
            }

            _aiming = aiming;
            FirstPersonWeaponAsset asset = ActiveWeapon;

            if (asset == null)
            {
                return;
            }

            bool grenadeMode = IsGrenadeMode;
            if (!grenadeMode && _audio != null)
            {
                IReadOnlyList<AudioClip> sounds = aiming ? asset.AimInSounds : asset.AimOutSounds;
                if (sounds.Count > 0)
                {
                    _audio.Stop();
                    _audio.clip = sounds[UnityEngine.Random.Range(0, sounds.Count)];
                    _audio.Play();
                }
            }

            asset.GetAimClips(grenadeMode, out XRayAnmAsset start, out _, out XRayAnmAsset end);
            if (aiming && _aimPhase == FirstPersonWeaponAimPhase.Hip)
            {
                _aimPhase = start != null ? FirstPersonWeaponAimPhase.Entering : end != null ? FirstPersonWeaponAimPhase.Leaving : FirstPersonWeaponAimPhase.Aimed;
                _aimBlendTime = _aimPhase == FirstPersonWeaponAimPhase.Leaving ? end.Duration : 0f;
            }
            else if (!aiming && _aimPhase == FirstPersonWeaponAimPhase.Aimed)
            {
                _aimPhase = end != null ? FirstPersonWeaponAimPhase.Leaving : start != null ? FirstPersonWeaponAimPhase.Entering : FirstPersonWeaponAimPhase.Hip;
                _aimBlendTime = _aimPhase == FirstPersonWeaponAimPhase.Entering ? start.Duration : 0f;
            }
        }

        private void TickAim(float deltaTime)
        {
            FirstPersonWeaponAsset asset = ActiveWeapon;
            float aimTime = asset != null ? asset.AimTime : 0.25f;
            _aim = Mathf.MoveTowards(_aim, _aiming ? 1f : 0f, deltaTime / aimTime);
            bool grenadeMode = IsGrenadeMode;
            if (!grenadeMode)
            {
                TickAimCamera(asset, deltaTime);
            }

            if (asset == null)
            {
                return;
            }

            asset.GetAimClips(grenadeMode, out XRayAnmAsset start, out _, out XRayAnmAsset end);
            XRayAnmAsset clip = _aimPhase == FirstPersonWeaponAimPhase.Entering ? start : _aimPhase == FirstPersonWeaponAimPhase.Leaving ? end : null;

            if (clip == null)
            {
                _aimBlendTime += deltaTime;
                return;
            }

            bool entering = _aimPhase == FirstPersonWeaponAimPhase.Entering;
            bool forward = entering == _aiming;
            _aimBlendTime = Mathf.Clamp(_aimBlendTime + (forward ? deltaTime : -deltaTime), 0f, clip.Duration);

            if (forward && _aimBlendTime >= clip.Duration || !forward && _aimBlendTime <= 0f)
            {
                _aimPhase = _aiming ? FirstPersonWeaponAimPhase.Aimed : FirstPersonWeaponAimPhase.Hip;
                _aimBlendTime = 0f;
            }
        }

        private void TickAimCamera(FirstPersonWeaponAsset asset, float deltaTime)
        {
            if (_player != null && _player.CameraController != null)
            {
                float zoom = asset != null ? asset.AimZoom : 1f;
                _player.CameraController.SetWeaponZoom(Mathf.Lerp(1f, zoom, _aim));
                float hudFieldOfView = asset != null ? asset.IronSightHudFieldOfView : 0f;
                foreach (MonoBehaviour behaviour in _opticsBindings)
                {
                    if (behaviour != null && behaviour is IFirstPersonWeaponOptics optics && optics.UsesOpticalMagnification)
                    {
                        hudFieldOfView = optics.AimHudFieldOfView;
                        break;
                    }
                }

                float reloadHudFovScale = 1f;
                float reduction = asset != null ? asset.ReloadHudFovReduction : 0f;
                if (reduction > 0f)
                {
                    float targetBlend = State == FirstPersonWeaponState.Reloading ? 1f : 0f;
                    _reloadHudFovBlend = Mathf.MoveTowards(_reloadHudFovBlend, targetBlend, deltaTime / RELOAD_HUD_FOV_BLEND_TIME);
                    reloadHudFovScale = 1f - reduction * Mathf.SmoothStep(0f, 1f, _reloadHudFovBlend);
                }

                _player.CameraController.SetWeaponHudFieldOfView(hudFieldOfView, _aim, reloadHudFovScale);
            }

            float target = asset != null ? asset.AimCameraRoll * 4f * _aim * (1f - _aim) * (_aiming ? 1f : -1f) : 0f;
            float blendTime = asset != null ? asset.AimCameraBlendTime : 0.15f;
            _aimCameraRoll = Mathf.SmoothDamp(_aimCameraRoll, target, ref _aimCameraVelocity, blendTime, Mathf.Infinity, deltaTime);

            if (Mathf.Abs(_aimCameraRoll - target) < 0.0001f && Mathf.Abs(_aimCameraVelocity) < 0.001f)
            {
                _aimCameraRoll = target;
                _aimCameraVelocity = 0f;
            }

            if (_player != null && _player.CameraEffects != null)
            {
                _player.CameraEffects.SetWeaponAimRotation(Quaternion.AngleAxis(_aimCameraRoll, Vector3.forward));
            }
        }

        private void ResetAimForModeChange()
        {
            _aiming = false;
            _aim = 0f;
            _aimPhase = FirstPersonWeaponAimPhase.Hip;
            _aimBlendTime = 0f;
            ResetAimCamera();
        }

        private void ResetAimCamera()
        {
            _aimCameraRoll = 0f;
            _aimCameraVelocity = 0f;
            _reloadHudFovBlend = 0f;
            _aimedLauncherShot = false;

            if (_player != null && _player.CameraController != null)
            {
                _player.CameraController.SetWeaponZoom(1f);
                _player.CameraController.SetWeaponHudFieldOfView(0f, 0f);
            }

            if (_player != null && _player.CameraEffects != null)
            {
                _player.CameraEffects.SetWeaponAimRotation(Quaternion.identity);
            }
        }

        private void ApplyHandsOffset()
        {
            FirstPersonWeaponAsset asset = ActiveWeapon;
            if (_boundHands == null || asset == null)
            {
                return;
            }

            bool grenadeMode = IsGrenadeMode;
            FirstPersonWeaponSlot slot = _slots[ActiveSlot];
            bool scoped = slot.HasScope;
            Vector3 aimPosition = grenadeMode ? asset.GetGrenadeAimOffset(scoped) : AimOffsetPosition;
            Vector3 aimEuler = grenadeMode ? asset.GrenadeAimOffsetRotation : asset.GetRifleAimRotation(scoped);
            Quaternion aimRotation = FirstPersonWeaponAsset.ToAimRotation(aimEuler * _aim);
            Vector3 position = aimPosition * _aim + aimRotation * asset.HandsPosition;
            position.z -= (_weaponView == null ? 0f : _weaponView.HipBackOffset) * (1f - _aim);
            Quaternion rotation = aimRotation * asset.HandsRotation;
            if (EvaluateAimBlend(asset, grenadeMode, out Vector3 blendPosition, out Quaternion blendRotation))
            {
                position += rotation * blendPosition;
                rotation *= blendRotation;
            }

            Transform hands = _boundHands.transform;
            Transform parent = hands.parent;
            Camera camera = _player != null && _player.CameraController != null ? _player.CameraController.Camera : null;
            Vector2 idleSway = asset.AimIdleSwayDegrees;
            if (idleSway != Vector2.zero && !grenadeMode && _aimPhase == FirstPersonWeaponAimPhase.Aimed && CurrentMotion == FirstPersonWeaponMotionKind.IdleAim)
            {
                float phase = _aimBlendTime * Mathf.PI * 0.5f;
                float recovery = (Time.time - _actionUntil) / LAUNCHER_AIM_SHOT_BLEND_TIME;
                float strength = _aim * Mathf.Clamp01(Mathf.Min(_aimBlendTime / asset.AimTime, recovery));
                float pitch = Mathf.Sin(phase) * idleSway.x * strength;
                float yaw = Mathf.Sin(phase * 0.75f) * idleSway.y * strength;
                if (asset.IsStandaloneLauncher && !scoped && asset.IronSightHudFieldOfView > 0f && camera != null && parent != null)
                {
                    Vector3 pivot = parent.InverseTransformPoint(camera.transform.position);
                    Vector3 axis = parent.InverseTransformVector(camera.transform.forward).normalized;
                    Quaternion roll = Quaternion.AngleAxis(pitch + yaw, axis);
                    position = pivot + roll * (position - pivot);
                    rotation = roll * rotation;
                }
                else
                {
                    rotation *= Quaternion.Euler(pitch, yaw, 0f);
                }
            }

            if (slot.IsPistol)
            {
                float cameraDepth = Mathf.Lerp(PISTOL_CAMERA_DEPTH_OFFSET, -PISTOL_CAMERA_DEPTH_OFFSET, _aim);
                if (camera != null)
                {
                    Vector3 cameraOffset = camera.transform.forward * cameraDepth;
                    position += parent != null ? parent.InverseTransformVector(cameraOffset) : cameraOffset;
                }
                else
                {
                    position.z += cameraDepth;
                }
            }

            hands.SetLocalPositionAndRotation(position, rotation);
        }

        private bool EvaluateAimBlend(FirstPersonWeaponAsset asset, bool grenadeMode, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            XRayAnmAsset clip;
            float time = _aimBlendTime;
            asset.GetAimClips(grenadeMode, out XRayAnmAsset start, out XRayAnmAsset idle, out XRayAnmAsset end);

            switch (_aimPhase)
            {
                case FirstPersonWeaponAimPhase.Entering:
                    clip = start;
                    break;
                case FirstPersonWeaponAimPhase.Leaving:
                    clip = end;
                    break;
                case FirstPersonWeaponAimPhase.Aimed:
                    clip = idle != null ? idle : start != null ? start : end;

                    if (clip != null)
                    {
                        time = clip == idle && clip.Duration > 0f ? time % clip.Duration : clip == start ? clip.Duration : 0f;
                    }

                    break;
                default:
                    clip = start == null && end == null && _aim > 0f ? idle : null;
                    time = 0f;
                    break;
            }

            if (clip == null || !clip.Evaluate(time, out position, out rotation))
            {
                return false;
            }

            if (start == null && end == null)
            {
                position *= _aim;
                rotation = Quaternion.Slerp(Quaternion.identity, rotation, _aim);
            }

            return true;
        }

        private void TickRecoil()
        {
            FirstPersonWeaponAsset active = ActiveWeapon;
            FirstPersonWeaponAsset asset = active != null ? active : _recoilSource;
            StalkerCameraRecoil recoil = _player != null && _player.CameraController != null ? _player.CameraController.Recoil : null;
            if (asset != null && recoil != null)
            {
                float snappiness = active != null ? asset.RecoilSnappiness * _slots[ActiveSlot].RecoilSnappinessMultiplier : asset.RecoilSnappiness;
                recoil.Tick(asset.RecoilReturnSpeed, snappiness);
            }
        }

        private void BuildVisuals(GameObject hands, FirstPersonWeaponAsset asset)
        {
            DestroyVisuals(false);
            _boundHands = hands;
            _savedHandsPosition = hands.transform.localPosition;
            _savedHandsRotation = hands.transform.localRotation;
            Transform anchor = FindBone(hands, _itemAnchorBone);
            if (asset.IsHandGrenade)
            {
                _handGrenadeAnchor = anchor != null ? anchor : hands.transform;
                _handGrenadeThrowHand = FindBone(hands, "r_hand");
            }

            _model = Instantiate(asset.HudModel, anchor != null ? anchor : hands.transform, false);
            _weaponView = _model.GetComponent<FirstPersonWeaponView>();
            _revolverView = _model.GetComponent<FirstPersonRevolverView>();
            _magazineRoundsView = _model.GetComponent<FirstPersonMagazineRoundsView>();
            _grenadeAimPoint = _weaponView != null && _weaponView.GrenadeMuzzle != null ? FindBone(_model, GRENADE_AIM_POINT) : null;
            _model.transform.localPosition = Vector3.zero;
            _model.transform.localRotation = Quaternion.identity;
            Renderer[] renderers = _model.GetComponentsInChildren<Renderer>(true);
            if (asset.IsHandGrenade)
            {
                _handGrenadeRenderers = renderers;
                SetHandGrenadeVisible(false);
            }

            foreach (Renderer renderer in renderers)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;

                if (renderer is SkinnedMeshRenderer skinned)
                {
                    skinned.updateWhenOffscreen = true;
                }
            }

            _modelRoot = FindModelRoot(_model);
            ApplySilencerVisual();
            VisualCreated?.Invoke(_model);
            BindOptics();
            Animator handsAnimator = GetAnimator(hands);
            Animator weaponAnimator = GetAnimator(_model);
            _graph = new FirstPersonWeaponGraph($"{name}.FirstPersonWeapon", handsAnimator, weaponAnimator, FirstPersonWeaponGraph.CreateMask(hands.transform, asset.OverlayBones), _weaponView);
            _hasLoop = false;
            _weaponView?.UpdateMagazineRounds(Magazine);
            UpdateUnderbarrelRound();
        }

        private void BindOptics()
        {
            _model.GetComponentsInChildren(true, _reticleBindings);
            Camera viewer = _player != null && _player.CameraController != null ? _player.CameraController.Camera : null;
            foreach (MonoBehaviour behaviour in _model.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null && behaviour is IFirstPersonWeaponOptics optics)
                {
                    _opticsBindings.Add(behaviour);
                    optics.Bind(this, viewer);
                }
            }
        }

        private void ApplySilencerVisual()
        {
            _ballisticsWarningLogged = false;
            if (_weaponView != null)
            {
                _weaponView.SetSilencer(IsSilenced);
            }

            Transform muzzle = _weaponView != null ? IsSilenced ? _weaponView.SilencedMuzzle : _weaponView.Muzzle : null;
            _muzzleFromView = muzzle != null;
            _muzzle = _muzzleFromView ? muzzle : FindBone(_model, ActiveWeapon.MuzzleBone);
            FirstPersonRig rig = _player != null ? _player.Rig : null;
            if (rig != null)
            {
                bool hasBarrel = ActiveWeapon is { IsMelee: false, IsHandGrenade: false };
                rig.SetWeaponObstruction(this, hasBarrel ? _model : null, hasBarrel ? _muzzle : null, _muzzleFromView);
            }
        }

        private void RebuildVisuals()
        {
            if (TryFinishHandGrenadeSwitch())
            {
                return;
            }

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
            _activeMeleeAttack = null;
            foreach (MonoBehaviour behaviour in _opticsBindings)
            {
                if (behaviour != null && behaviour is IFirstPersonWeaponOptics optics)
                {
                    optics.Bind(null, null);
                }
            }

            _opticsBindings.Clear();
            _reticleBindings.Clear();
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
            _handGrenadeAnchor = null;
            _handGrenadeThrowHand = null;
            _handGrenadeRenderers = null;
            _handGrenadeVisible = false;
            _weaponView = null;
            _revolverView = null;
            _magazineRoundsView = null;
            _modelRoot = null;
            _grenadeAimPoint = null;
            _muzzle = null;
            _muzzleFromView = false;
            _flame = null;
            FirstPersonRig rig = _player != null ? _player.Rig : null;
            if (_boundHands != null)
            {
                _boundHands.transform.localPosition = _savedHandsPosition;
                _boundHands.transform.localRotation = _savedHandsRotation;
            }

            if (rig != null)
            {
                rig.SetWeaponObstruction(this, null, null, false);
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
            _weaponView?.UpdateMagazineRounds(Magazine);
            if (State != FirstPersonWeaponState.Reloading && ActiveWeapon?.IsStandaloneLauncher == true)
            {
                _weaponView?.SetLauncherRoundVisible(Magazine > 0);
            }

            if (State != FirstPersonWeaponState.LoadingGrenade)
            {
                UpdateUnderbarrelRound();
            }

            AmmoChanged?.Invoke(Magazine, Reserve);
        }

        private void UpdateUnderbarrelRound()
        {
            if (_weaponView == null || _weaponView.GrenadeMuzzle == null || ActiveWeapon?.IsStandaloneLauncher != false)
            {
                return;
            }

            bool roundVisible = false;
            bool shellVisible = false;
            if (HasGrenadeLauncher && _ammoSource != null)
            {
                if (State == FirstPersonWeaponState.LoadingGrenade)
                {
                    roundVisible = _stateTime >= _underbarrelRoundRevealTime;
                    shellVisible = _stateTime >= _underbarrelShellTimes.x && _stateTime < _underbarrelShellTimes.y;
                }
                else
                {
                    _ammoSource.GetGrenadeAmmo(_slots[ActiveSlot].Key, out int loaded, out _, out _);
                    roundVisible = loaded > 0;
                }
            }

            _weaponView.SetUnderbarrelRoundsVisible(roundVisible, shellVisible);
        }

        private FirstPersonWeaponInstanceState GetWeaponState(object key)
        {
            if (!_weaponStates.TryGetValue(key, out FirstPersonWeaponInstanceState weaponState))
            {
                weaponState = new FirstPersonWeaponInstanceState();
                _weaponStates.Add(key, weaponState);
            }

            return weaponState;
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
    }
}

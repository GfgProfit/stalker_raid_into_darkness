using System;
using System.Collections.Generic;
using UnityEngine;
using XRay.Importer;

namespace RaidIntoDarkness.Weapons
{
    [Flags]
    internal enum FirstPersonWeaponMotionMode
    {
        None = 0,
        Silenced = 1,
        Scoped = 2,
        WithGrenadeLauncher = 4,
        GrenadeMode = 8
    }

    [CreateAssetMenu(menuName = "Raid Into Darkness/Weapons/First Person Weapon", fileName = "FirstPersonWeapon")]
    public sealed class FirstPersonWeaponAsset : ScriptableObject
    {
        public const int AUTO_FIRE_MODE = -1;
        public const string DEFAULT_MUZZLE_BONE = "wpn_silencer";

        [Header("Model")]
        [SerializeField] private GameObject _hudModel;
        [SerializeField] private bool _isMelee;
        [SerializeField] private bool _isStandaloneLauncher;
        [SerializeField] private GameObject _handGrenadeVisual;
        [SerializeField] private GrenadeAmmoData _handGrenade;
        [SerializeField] [Min(1)] private int _handGrenadeReleaseFrame = 1;
        [SerializeField] [Min(0.1f)] private float _handGrenadeThrowSpeed = 16f;
        [Tooltip("Hud section 'hands_position_16x9'.")]
        [SerializeField] private Vector3 _handsPosition;
        [Tooltip("Hud section 'hands_orientation_16x9': heading, pitch, bank in degrees.")]
        [SerializeField] private Vector3 _handsOrientation;
        [Tooltip("Hud section 'aim_hud_offset_pos_16x9', camera space.")]
        [SerializeField] private Vector3 _aimOffsetPosition;
        [Tooltip("Hud section 'aim_hud_offset_rot_16x9' in radians.")]
        [SerializeField] private Vector3 _aimOffsetRotation;
        [SerializeField] private Vector3 _ironSightAimRotationOffset;
        [Tooltip("Weapon section 'zoom_rotate_time'.")]
        [SerializeField] [Min(0.01f)] private float _aimTime = 0.25f;
        [SerializeField] [Range(-10f, 10f)] private float _aimCameraRoll = 1f;
        [SerializeField] [Min(0.01f)] private float _aimCameraBlendTime = 0.15f;
        [SerializeField] [Range(1f, 2f)] private float _aimZoom = 1.15f;
        [SerializeField] [Range(0f, 1f)] private float _ironSightHudFieldOfView;
        [SerializeField] [Range(0f, 0.5f)] private float _reloadHudFovReduction;
        [SerializeField] private string _muzzleBone = DEFAULT_MUZZLE_BONE;
        [Tooltip("Hands bones animated by the left-hand pickup overlay.")]
        [SerializeField] private string[] _overlayBones = Array.Empty<string>();
        [Header("Animations")]
        [SerializeField] private List<FirstPersonWeaponMotion> _motions = new List<FirstPersonWeaponMotion>();
        [SerializeField] private List<FirstPersonWeaponMotion> _withGrenadeLauncherMotions = new List<FirstPersonWeaponMotion>();
        [SerializeField] private List<FirstPersonWeaponMotion> _grenadeModeMotions = new List<FirstPersonWeaponMotion>();
        [SerializeField] private List<AttachmentMotionSet> _attachmentMotions = new List<AttachmentMotionSet>();
        [SerializeField] private XRayAnmAsset _aimStart;
        [SerializeField] private XRayAnmAsset _aimIdle;
        [SerializeField] private Vector2 _aimIdleSwayDegrees;
        [SerializeField] private XRayAnmAsset _aimEnd;
        [SerializeField] [Min(0.0f)] private float _crossFade = 0.15f;
        [Header("Grenade Launcher Aim")]
        [SerializeField] private Vector3 _grenadeAimOffsetPosition;
        [SerializeField] private Vector3 _grenadeAimOffsetRotation;
        [SerializeField] private bool _useScopedGrenadeAimOffset;
        [SerializeField] private Vector3 _scopedGrenadeAimOffsetPosition;
        [SerializeField] private XRayAnmAsset _grenadeAimStart;
        [SerializeField] private XRayAnmAsset _grenadeAimIdle;
        [SerializeField] private XRayAnmAsset _grenadeAimEnd;
        [Header("Fire")]
        [SerializeField] [Min(1)] private int _roundsPerMinute = 600;
        [SerializeField] [Min(0)] private int _twoRoundBurstRoundsPerMinute;
        [SerializeField] [Min(1)] private int _emptyClicksPerMinute = 200;
        [SerializeField] private int[] _fireModes = { 1, AUTO_FIRE_MODE };
        [SerializeField] [Min(1)] private int _magazineSize = 30;
        [SerializeField] private GameObject _flameParticles;
        [SerializeField] private GameObject _smokeParticles;
        [SerializeField] private GameObject _silencedFlameParticles;
        [SerializeField] private GameObject _silencedSmokeParticles;
        [SerializeField] [Min(0)] private int _lastShotHoldFrame;
        [SerializeField] [Range(0f, 1f)] private float _emptyShotStrength = 0.15f;
        [SerializeField] [HideInInspector] private WeaponBallisticsSettings _ballistics = new WeaponBallisticsSettings();
        [Header("Recoil")]
        [SerializeField] private Vector3 _recoil = new Vector3(4.0f, 4.0f, 2.0f);
        [SerializeField] [Range(0.0f, 50.0f)] private float _recoilSnappiness = 13.0f;
        [SerializeField] [Range(0.0f, 50.0f)] private float _recoilReturnSpeed = 9.0f;
        [Header("Sounds")]
        [SerializeField] private AudioClip[] _shotSounds = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] _silencedShotSounds = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] _aimInSounds = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] _aimOutSounds = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip _emptySound;
        [SerializeField] private AudioClip _lastShellSound;
        [SerializeField] private AudioClip _lowAmmoSound;
        [SerializeField] [Min(0)] private int _lowAmmoThreshold = 5;

        public GameObject HudModel => _hudModel;
        public bool IsMelee => _isMelee;
        public bool IsStandaloneLauncher => _isStandaloneLauncher;
        public bool IsHandGrenade => _handGrenadeVisual != null;
        public GameObject HandGrenadeVisual => _handGrenadeVisual;
        public GrenadeAmmoData HandGrenade => _handGrenade;
        public int HandGrenadeReleaseFrame => _handGrenadeReleaseFrame;
        public float HandGrenadeThrowSpeed => _handGrenadeThrowSpeed;
        public Vector3 HandsPosition => _handsPosition;
        public Vector3 HandsOrientation => _handsOrientation;
        public Quaternion HandsRotation => Quaternion.Euler(_handsOrientation.y, _handsOrientation.x, _handsOrientation.z);
        public Vector3 AimOffsetPosition => _aimOffsetPosition;
        public Vector3 AimOffsetRotation => _aimOffsetRotation;
        public float AimTime => _aimTime;
        public float AimCameraRoll => _aimCameraRoll;
        public float AimCameraBlendTime => Mathf.Max(0.01f, _aimCameraBlendTime);
        public float AimZoom => Mathf.Clamp(_aimZoom, 1f, 2f);
        public float IronSightHudFieldOfView => _ironSightHudFieldOfView;
        public float ReloadHudFovReduction => Mathf.Clamp(_reloadHudFovReduction, 0f, 0.5f);
        public string MuzzleBone => _muzzleBone;
        public IReadOnlyList<string> OverlayBones => _overlayBones ?? Array.Empty<string>();
        public IReadOnlyList<FirstPersonWeaponMotion> Motions => _motions;
        public XRayAnmAsset AimStart => _aimStart;
        public XRayAnmAsset AimIdle => _aimIdle;
        public Vector2 AimIdleSwayDegrees => _aimIdleSwayDegrees;
        public XRayAnmAsset AimEnd => _aimEnd;
        public Vector3 GrenadeAimOffsetPosition => _grenadeAimOffsetPosition;
        public Vector3 GrenadeAimOffsetRotation => _grenadeAimOffsetRotation;
        public float CrossFade => _crossFade;
        public int RoundsPerMinute => _roundsPerMinute;
        public float ShotInterval => 60.0f / Mathf.Max(1, _roundsPerMinute);
        public float EmptyClickInterval => 60.0f / Mathf.Max(1, _emptyClicksPerMinute);
        public IReadOnlyList<int> FireModes => _fireModes != null && _fireModes.Length > 0 ? _fireModes : new[] { 1 };
        public int MagazineSize => _magazineSize;
        public GameObject FlameParticles => _flameParticles;
        public GameObject SmokeParticles => _smokeParticles;
        public GameObject SilencedFlameParticles => _silencedFlameParticles;
        public GameObject SilencedSmokeParticles => _silencedSmokeParticles;
        public int LastShotHoldFrame => _lastShotHoldFrame;
        public float EmptyShotStrength => Mathf.Clamp01(_emptyShotStrength);
        public WeaponBallisticsSettings Ballistics => _ballistics;
        public Vector3 Recoil => _recoil;
        public float RecoilSnappiness => _recoilSnappiness;
        public float RecoilReturnSpeed => _recoilReturnSpeed;
        public IReadOnlyList<AudioClip> ShotSounds => _shotSounds ?? Array.Empty<AudioClip>();
        public IReadOnlyList<AudioClip> SilencedShotSounds => _silencedShotSounds ?? Array.Empty<AudioClip>();
        public IReadOnlyList<AudioClip> AimInSounds => _aimInSounds ?? Array.Empty<AudioClip>();
        public IReadOnlyList<AudioClip> AimOutSounds => _aimOutSounds ?? Array.Empty<AudioClip>();
        public AudioClip EmptySound => _emptySound;
        public AudioClip LastShellSound => _lastShellSound;
        public AudioClip LowAmmoSound => _lowAmmoSound;
        public int LowAmmoThreshold => _lowAmmoThreshold;
        internal bool HasFixedTwoRoundBurst => _twoRoundBurstRoundsPerMinute > 0;
        internal bool HasAttachmentMotions => _attachmentMotions?.Count > 0;

        internal Vector3 GetRifleAimRotation(bool scoped)
        {
            return scoped ? _aimOffsetRotation : _aimOffsetRotation + _ironSightAimRotationOffset;
        }

        internal Vector3 GetGrenadeAimOffset(bool scoped)
        {
            return scoped && _useScopedGrenadeAimOffset ? _scopedGrenadeAimOffsetPosition : _grenadeAimOffsetPosition;
        }

        internal float GetShotInterval(int fireMode)
        {
            return fireMode == 2 && HasFixedTwoRoundBurst ? 60.0f / _twoRoundBurstRoundsPerMinute : ShotInterval;
        }

        public static Quaternion ToAimRotation(Vector3 radians)
        {
            Vector3 degrees = radians * Mathf.Rad2Deg;
            return Quaternion.AngleAxis(degrees.z, Vector3.forward) * Quaternion.AngleAxis(degrees.y, Vector3.up) * Quaternion.AngleAxis(degrees.x, Vector3.right);
        }

        internal void GetAimClips(bool grenadeMode, out XRayAnmAsset start, out XRayAnmAsset idle, out XRayAnmAsset end)
        {
            start = grenadeMode ? _grenadeAimStart : _aimStart;
            idle = grenadeMode ? _grenadeAimIdle : _aimIdle;
            end = grenadeMode ? _grenadeAimEnd : _aimEnd;
        }

        public bool HasMotion(FirstPersonWeaponMotionKind kind)
        {
            return Find(kind, 0) != null;
        }

        public FirstPersonWeaponMotion Find(FirstPersonWeaponMotionKind kind, int variant, bool hasGrenadeLauncher = false, bool grenadeMode = false)
        {
            FirstPersonWeaponMotionMode mode = grenadeMode ? FirstPersonWeaponMotionMode.GrenadeMode : hasGrenadeLauncher ? FirstPersonWeaponMotionMode.WithGrenadeLauncher : FirstPersonWeaponMotionMode.None;
            return Find(kind, variant, mode);
        }

        public FirstPersonWeaponMotion FindRandom(FirstPersonWeaponMotionKind kind, bool hasGrenadeLauncher = false)
        {
            return FindRandom(kind, hasGrenadeLauncher ? FirstPersonWeaponMotionMode.WithGrenadeLauncher : FirstPersonWeaponMotionMode.None);
        }

        internal FirstPersonWeaponMotion Find(FirstPersonWeaponMotionKind kind, int variant, FirstPersonWeaponMotionMode mode)
        {
            return FindIn(GetAttachmentMotions(kind, mode), kind, variant) ?? FindIn(GetModeMotions(mode), kind, variant) ?? FindIn(_motions, kind, variant);
        }

        internal FirstPersonWeaponMotion FindGrenadeShot(bool aiming, FirstPersonWeaponMotionMode mode)
        {
            FirstPersonWeaponMotion aimedShot = aiming ? FindIn(GetAttachmentMotions(FirstPersonWeaponMotionKind.ShootAim, mode), FirstPersonWeaponMotionKind.ShootAim, 0) ?? FindIn(_grenadeModeMotions, FirstPersonWeaponMotionKind.ShootAim, 0) : null;
            return aimedShot ?? FindIn(GetAttachmentMotions(FirstPersonWeaponMotionKind.Shoot, mode), FirstPersonWeaponMotionKind.Shoot, 0) ?? FindIn(_grenadeModeMotions, FirstPersonWeaponMotionKind.Shoot, 0);
        }

        internal FirstPersonWeaponMotion FindRandom(FirstPersonWeaponMotionKind kind, FirstPersonWeaponMotionMode mode)
        {
            List<FirstPersonWeaponMotion> motions = GetAttachmentMotions(kind, mode) ?? GetModeMotions(mode);
            int count = CountIn(motions, kind);
            if (count == 0)
            {
                motions = _motions;
                count = CountIn(motions, kind);
            }

            return count > 0 ? FindIn(motions, kind, UnityEngine.Random.Range(0, count)) : null;
        }

        public int CountMotions(FirstPersonWeaponMotionKind kind) => CountIn(_motions, kind);

        private List<FirstPersonWeaponMotion> GetAttachmentMotions(FirstPersonWeaponMotionKind kind, FirstPersonWeaponMotionMode mode)
        {
            if (!HasAttachmentMotions || mode == FirstPersonWeaponMotionMode.None)
            {
                return null;
            }

            List<FirstPersonWeaponMotion> exact = FindAttachmentMotions(kind, mode);
            FirstPersonWeaponMotionMode unscoped = mode & ~FirstPersonWeaponMotionMode.Scoped;
            return exact ?? (unscoped != mode && unscoped != FirstPersonWeaponMotionMode.None ? FindAttachmentMotions(kind, unscoped) : null);
        }

        private List<FirstPersonWeaponMotion> FindAttachmentMotions(FirstPersonWeaponMotionKind kind, FirstPersonWeaponMotionMode mode)
        {
            foreach (AttachmentMotionSet set in _attachmentMotions)
            {
                if (set != null && set.Mode == mode && CountIn(set.Motions, kind) > 0)
                {
                    return set.Motions;
                }
            }

            return null;
        }

        private List<FirstPersonWeaponMotion> GetModeMotions(FirstPersonWeaponMotionMode mode)
        {
            if ((mode & FirstPersonWeaponMotionMode.GrenadeMode) != 0)
            {
                return _grenadeModeMotions;
            }

            return (mode & FirstPersonWeaponMotionMode.WithGrenadeLauncher) != 0 ? _withGrenadeLauncherMotions : null;
        }

        [Serializable]
        private sealed class AttachmentMotionSet
        {
            [SerializeField] private FirstPersonWeaponMotionMode _mode;
            [SerializeField] private List<FirstPersonWeaponMotion> _motions = new List<FirstPersonWeaponMotion>();

            public FirstPersonWeaponMotionMode Mode => _mode;
            public List<FirstPersonWeaponMotion> Motions => _motions;
        }

        private static FirstPersonWeaponMotion FindIn(List<FirstPersonWeaponMotion> motions, FirstPersonWeaponMotionKind kind, int variant)
        {
            int index = 0;
            for (int i = 0; motions != null && i < motions.Count; i++)
            {
                if (motions[i] != null && motions[i].Kind == kind && motions[i].Hands != null)
                {
                    if (index == variant)
                    {
                        return motions[i];
                    }

                    index++;
                }
            }

            return null;
        }

        private static int CountIn(List<FirstPersonWeaponMotion> motions, FirstPersonWeaponMotionKind kind)
        {
            int count = 0;
            for (int i = 0; motions != null && i < motions.Count; i++)
            {
                if (motions[i] != null && motions[i].Kind == kind && motions[i].Hands != null)
                {
                    count++;
                }
            }

            return count;
        }

        public void SetModel(GameObject hudModel, Vector3 handsPosition, Vector3 handsOrientation, Vector3 aimOffsetPosition, Vector3 aimOffsetRotation, float aimTime, string muzzleBone, string[] overlayBones)
        {
            _hudModel = hudModel;
            _handsPosition = handsPosition;
            _handsOrientation = handsOrientation;
            _aimOffsetPosition = aimOffsetPosition;
            _aimOffsetRotation = aimOffsetRotation;
            _aimTime = aimTime;
            _muzzleBone = muzzleBone;
            _overlayBones = overlayBones ?? Array.Empty<string>();
        }

        public void SetAnimations(List<FirstPersonWeaponMotion> motions, XRayAnmAsset aimStart, XRayAnmAsset aimIdle, XRayAnmAsset aimEnd)
        {
            _motions = motions ?? new List<FirstPersonWeaponMotion>();
            _aimStart = aimStart;
            _aimIdle = aimIdle;
            _aimEnd = aimEnd;
        }

        public void SetFire(int roundsPerMinute, int emptyClicksPerMinute, int[] fireModes, int magazineSize, GameObject flameParticles, GameObject smokeParticles)
        {
            _roundsPerMinute = roundsPerMinute;
            _emptyClicksPerMinute = emptyClicksPerMinute;
            _fireModes = fireModes;
            _magazineSize = magazineSize;
            _flameParticles = flameParticles;
            _smokeParticles = smokeParticles;
        }

        public void SetSounds(AudioClip[] shotSounds, AudioClip emptySound, AudioClip lastShellSound, AudioClip lowAmmoSound, int lowAmmoThreshold)
        {
            _shotSounds = shotSounds ?? Array.Empty<AudioClip>();
            _emptySound = emptySound;
            _lastShellSound = lastShellSound;
            _lowAmmoSound = lowAmmoSound;
            _lowAmmoThreshold = lowAmmoThreshold;
        }
    }
}

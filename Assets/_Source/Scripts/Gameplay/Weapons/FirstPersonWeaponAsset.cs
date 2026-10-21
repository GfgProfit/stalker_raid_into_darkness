using System;
using System.Collections.Generic;
using UnityEngine;
using XRay.Importer;

namespace RaidIntoDarkness.Weapons
{
    [CreateAssetMenu(menuName = "Raid Into Darkness/Weapons/First Person Weapon", fileName = "FirstPersonWeapon")]
    public sealed class FirstPersonWeaponAsset : ScriptableObject
    {
        public const int AUTO_FIRE_MODE = -1;
        public const string DEFAULT_MUZZLE_BONE = "wpn_silencer";

        [Header("Model")]
        [SerializeField] private GameObject _hudModel;
        [Tooltip("Hud section 'hands_position_16x9'.")]
        [SerializeField] private Vector3 _handsPosition;
        [Tooltip("Hud section 'hands_orientation_16x9': heading, pitch, bank in degrees.")]
        [SerializeField] private Vector3 _handsOrientation;
        [Tooltip("Hud section 'aim_hud_offset_pos_16x9', camera space.")]
        [SerializeField] private Vector3 _aimOffsetPosition;
        [Tooltip("Hud section 'aim_hud_offset_rot_16x9' in radians.")]
        [SerializeField] private Vector3 _aimOffsetRotation;
        [Tooltip("Weapon section 'zoom_rotate_time'.")]
        [SerializeField] [Min(0.01f)] private float _aimTime = 0.25f;
        [SerializeField] private string _muzzleBone = DEFAULT_MUZZLE_BONE;
        [Tooltip("Hands bones animated by the left-hand pickup overlay.")]
        [SerializeField] private string[] _overlayBones = Array.Empty<string>();
        [Header("Animations")]
        [SerializeField] private List<FirstPersonWeaponMotion> _motions = new List<FirstPersonWeaponMotion>();
        [SerializeField] private XRayAnmAsset _aimStart;
        [SerializeField] private XRayAnmAsset _aimIdle;
        [SerializeField] private XRayAnmAsset _aimEnd;
        [SerializeField] [Min(0.0f)] private float _crossFade = 0.15f;
        [Header("Fire")]
        [SerializeField] [Min(1)] private int _roundsPerMinute = 600;
        [SerializeField] [Min(1)] private int _emptyClicksPerMinute = 200;
        [SerializeField] private int[] _fireModes = { 1, AUTO_FIRE_MODE };
        [SerializeField] [Min(1)] private int _magazineSize = 30;
        [SerializeField] private GameObject _flameParticles;
        [SerializeField] private GameObject _smokeParticles;
        [Header("Recoil")]
        [SerializeField] private Vector3 _recoil = new Vector3(4.0f, 4.0f, 2.0f);
        [SerializeField] [Range(0.0f, 50.0f)] private float _recoilSnappiness = 13.0f;
        [SerializeField] [Range(0.0f, 50.0f)] private float _recoilReturnSpeed = 9.0f;
        [Header("Sounds")]
        [SerializeField] private AudioClip[] _shotSounds = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip _emptySound;
        [SerializeField] private AudioClip _lastShellSound;
        [SerializeField] private AudioClip _lowAmmoSound;
        [SerializeField] [Min(0)] private int _lowAmmoThreshold = 5;

        public GameObject HudModel => _hudModel;
        public Vector3 HandsPosition => _handsPosition;
        public Vector3 HandsOrientation => _handsOrientation;
        public Quaternion HandsRotation => Quaternion.Euler(_handsOrientation.y, _handsOrientation.x, _handsOrientation.z);
        public Vector3 AimOffsetPosition => _aimOffsetPosition;
        public Vector3 AimOffsetRotation => _aimOffsetRotation;
        public float AimTime => _aimTime;
        public string MuzzleBone => _muzzleBone;
        public IReadOnlyList<string> OverlayBones => _overlayBones ?? Array.Empty<string>();
        public IReadOnlyList<FirstPersonWeaponMotion> Motions => _motions;
        public XRayAnmAsset AimStart => _aimStart;
        public XRayAnmAsset AimIdle => _aimIdle;
        public XRayAnmAsset AimEnd => _aimEnd;
        public float CrossFade => _crossFade;
        public int RoundsPerMinute => _roundsPerMinute;
        public float ShotInterval => 60.0f / Mathf.Max(1, _roundsPerMinute);
        public float EmptyClickInterval => 60.0f / Mathf.Max(1, _emptyClicksPerMinute);
        public IReadOnlyList<int> FireModes => _fireModes != null && _fireModes.Length > 0 ? _fireModes : new[] { 1 };
        public int MagazineSize => _magazineSize;
        public GameObject FlameParticles => _flameParticles;
        public GameObject SmokeParticles => _smokeParticles;
        public Vector3 Recoil => _recoil;
        public float RecoilSnappiness => _recoilSnappiness;
        public float RecoilReturnSpeed => _recoilReturnSpeed;
        public IReadOnlyList<AudioClip> ShotSounds => _shotSounds ?? Array.Empty<AudioClip>();
        public AudioClip EmptySound => _emptySound;
        public AudioClip LastShellSound => _lastShellSound;
        public AudioClip LowAmmoSound => _lowAmmoSound;
        public int LowAmmoThreshold => _lowAmmoThreshold;

        public static Quaternion ToAimRotation(Vector3 radians)
        {
            Vector3 degrees = radians * Mathf.Rad2Deg;
            return Quaternion.AngleAxis(degrees.z, Vector3.forward) * Quaternion.AngleAxis(degrees.y, Vector3.up) * Quaternion.AngleAxis(degrees.x, Vector3.right);
        }

        public bool HasMotion(FirstPersonWeaponMotionKind kind)
        {
            return Find(kind, 0) != null;
        }

        public FirstPersonWeaponMotion Find(FirstPersonWeaponMotionKind kind, int variant)
        {
            int index = 0;
            for (int i = 0; i < _motions.Count; i++)
            {
                if (_motions[i] != null && _motions[i].Kind == kind && _motions[i].Hands != null)
                {
                    if (index == variant)
                    {
                        return _motions[i];
                    }

                    index++;
                }
            }

            return null;
        }

        public FirstPersonWeaponMotion FindRandom(FirstPersonWeaponMotionKind kind)
        {
            int count = CountMotions(kind);
            return count > 0 ? Find(kind, UnityEngine.Random.Range(0, count)) : null;
        }

        public int CountMotions(FirstPersonWeaponMotionKind kind)
        {
            int count = 0;
            for (int i = 0; i < _motions.Count; i++)
            {
                if (_motions[i] != null && _motions[i].Kind == kind && _motions[i].Hands != null)
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

using System;
using UnityEngine;
using XRay.Importer;

namespace RaidIntoDarkness.Weapons
{
    public enum FirstPersonWeaponMotionKind
    {
        Draw = 0,
        Holster = 1,
        Idle = 2,
        IdleAim = 3,
        Moving = 4,
        MovingAim = 5,
        MovingSlow = 6,
        MovingCrouch = 7,
        SprintStart = 8,
        Sprint = 9,
        SprintEnd = 10,
        Shoot = 11,
        ShootAim = 12,
        Reload = 13,
        ReloadEmpty = 14,
        FireMode = 15,
        Misfire = 16,
        IdleEmpty = 17,
        GrenadeModeEnter = 18,
        GrenadeModeExit = 19,
        GrenadeReload = 20
    }

    [Serializable]
    public sealed class FirstPersonWeaponMotion
    {
        private const float MIN_SPEED = 0.01f;

        [SerializeField] private FirstPersonWeaponMotionKind _kind;
        [SerializeField] private string _handsName;
        [SerializeField] private AnimationClip _hands;
        [SerializeField] private AnimationClip _weapon;
        [SerializeField] private AnimationClip _emptyWeapon;
        [SerializeField] private XRayAnmAsset _cameraEffect;
        [SerializeField] private AudioClip _sound;
        [SerializeField] private float _speed = 1.0f;

        public FirstPersonWeaponMotion(FirstPersonWeaponMotionKind kind, string handsName, AnimationClip hands, AnimationClip weapon, XRayAnmAsset cameraEffect, AudioClip sound, float speed)
        {
            _kind = kind;
            _handsName = handsName;
            _hands = hands;
            _weapon = weapon;
            _cameraEffect = cameraEffect;
            _sound = sound;
            _speed = speed;
        }

        public FirstPersonWeaponMotionKind Kind => _kind;
        public string HandsName => _handsName;
        public AnimationClip Hands => _hands;
        public AnimationClip Weapon => _weapon;
        public AnimationClip EmptyWeapon => _emptyWeapon;
        public XRayAnmAsset CameraEffect => _cameraEffect;
        public AudioClip Sound => _sound;
        public float Speed => Mathf.Max(MIN_SPEED, _speed);
        public float Duration => _hands != null ? _hands.length / Speed : 0.0f;
    }
}

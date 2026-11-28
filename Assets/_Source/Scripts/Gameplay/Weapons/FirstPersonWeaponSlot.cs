using UnityEngine;

namespace RaidIntoDarkness.Weapons
{
    internal sealed class FirstPersonWeaponSlot
    {
        public FirstPersonWeaponAsset Asset { get; set; }
        public object Key { get; set; }
        public FirstPersonWeaponInstanceState WeaponState { get; set; }
        public bool IsPistol { get; set; }
        public bool Silenced { get; set; }
        public bool HasScope { get; set; }
        public bool HasGrenadeLauncher { get; set; }
        public Vector3 RecoilMultiplier { get; set; } = Vector3.one;
        public float RecoilSnappinessMultiplier { get; set; } = 1f;
        public bool GrenadeMode { get; set; }
        public Vector3? AimOffsetPosition { get; set; }
        public float AimDepthOffset { get; set; }
    }
}

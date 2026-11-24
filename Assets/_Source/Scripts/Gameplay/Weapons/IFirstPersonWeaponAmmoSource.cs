using System;
using UnityEngine;

namespace RaidIntoDarkness.Weapons
{
    public interface IFirstPersonWeaponAmmoSource
    {
        event Action Changed;

        int GetMagazine(object weaponKey);
        int GetReserve(object weaponKey);
        string GetAmmoSection(object weaponKey);
        string GetAmmoName(object weaponKey);
        void GetGrenadeAmmo(object weaponKey, out int loaded, out int reserve, out string name);
        void GetHandGrenadeStock(object weaponKey, out int count, out string name)
        {
            count = 0;
            name = string.Empty;
        }

        GrenadeAmmoData GetGrenadeShot(object weaponKey, out GameObject visual);
        BallisticAmmoData GetBallistics(object weaponKey);
        float GetBulletSpeed(object weaponKey);
        float GetFireDistance(object weaponKey);
        float GetDispersionDegrees(object weaponKey);
        bool TryShoot(object weaponKey);
        bool TryShootGrenade(object weaponKey);
        bool TryConsumeHandGrenade(object weaponKey) => false;
        bool TryStartReload(object weaponKey);
        bool TrySwitchAmmo(object weaponKey);
        bool CanReloadGrenade(object weaponKey);
        bool ReloadGrenade(object weaponKey);
        void Reload(object weaponKey, int capacity);
    }
}

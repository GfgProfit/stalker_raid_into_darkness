using System;

namespace RaidIntoDarkness.Weapons
{
    public interface IFirstPersonWeaponAmmoSource
    {
        event Action Changed;

        int GetMagazine(object weaponKey);
        int GetReserve(object weaponKey);
        string GetAmmoSection(object weaponKey);
        string GetAmmoName(object weaponKey);
        bool TryShoot(object weaponKey);
        bool TryStartReload(object weaponKey);
        bool TrySwitchAmmo(object weaponKey);
        void Reload(object weaponKey, int capacity);
    }
}

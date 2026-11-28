using System;
using System.Globalization;

namespace RaidIntoDarkness.Inventory.Domain
{
    public static class WeaponMagazineState
    {
        public const string MAGAZINE_STATE = "weapon_magazine";
        public const string AMMO_SECTION_STATE = "weapon_ammo_section";

        public static int GetLoadedRounds(ItemInstance weapon)
        {
            WeaponStatsComponent stats = weapon?.Definition.GetComponent<WeaponStatsComponent>();

            if (stats == null || stats.SupportsAmmo(weapon.GetState(AMMO_SECTION_STATE)) == false)
            {
                return 0;
            }

            return int.TryParse(weapon.GetState(MAGAZINE_STATE), NumberStyles.Integer, CultureInfo.InvariantCulture, out int magazine) ? Math.Clamp(magazine, 0, stats.MagazineSize) : 0;
        }
    }
}

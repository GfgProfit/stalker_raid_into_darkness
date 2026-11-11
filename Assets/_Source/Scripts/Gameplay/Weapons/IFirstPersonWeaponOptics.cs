using UnityEngine;

namespace RaidIntoDarkness.Weapons
{
    public interface IFirstPersonWeaponOptics
    {
        bool UsesOpticalMagnification { get; }

        void Bind(FirstPersonWeaponController controller, Camera viewer);
    }
}

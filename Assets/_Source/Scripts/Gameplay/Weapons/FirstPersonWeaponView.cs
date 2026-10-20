using UnityEngine;

namespace RaidIntoDarkness.Weapons
{
    [DisallowMultipleComponent]
    public sealed class FirstPersonWeaponView : MonoBehaviour
    {
        [Tooltip("Muzzle flash and smoke spawn point; its forward axis is the flame direction.")]
        [SerializeField] private Transform _muzzle;

        public Transform Muzzle => _muzzle;

        public void SetMuzzle(Transform muzzle)
        {
            _muzzle = muzzle;
        }
    }
}

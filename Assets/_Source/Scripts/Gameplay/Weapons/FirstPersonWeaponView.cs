using UnityEngine;

namespace RaidIntoDarkness.Weapons
{
    [DisallowMultipleComponent]
    public sealed class FirstPersonWeaponView : MonoBehaviour
    {
        [Tooltip("Muzzle flash and smoke spawn point; its forward axis is the flame direction.")]
        [SerializeField] private Transform _muzzle;
        [SerializeField] private GameObject _silencerVisual;
        [SerializeField] private Transform _silencedMuzzle;

        public Transform Muzzle => _muzzle;
        public GameObject SilencerVisual => _silencerVisual;
        public Transform SilencedMuzzle => _silencedMuzzle != null ? _silencedMuzzle : _muzzle;

        public void SetMuzzle(Transform muzzle)
        {
            _muzzle = muzzle;
        }

        public void SetSilencer(bool installed)
        {
            if (_silencerVisual != null)
            {
                _silencerVisual.SetActive(installed);
            }
        }
    }
}

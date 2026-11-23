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
        [SerializeField] private GameObject _launcherRound;

        public Transform Muzzle => _muzzle;
        public GameObject SilencerVisual => _silencerVisual;
        public Transform SilencedMuzzle => _silencedMuzzle;

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

        public void SetLauncherRoundVisible(bool visible)
        {
            if (_launcherRound != null)
            {
                _launcherRound.SetActive(visible);
            }
        }
    }
}

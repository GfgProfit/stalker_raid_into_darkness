using RaidIntoDarkness.Player;
using UnityEngine;
using XRay.Importer;

namespace RaidIntoDarkness.Weapons
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class AnomalyHudImpact : MonoBehaviour
    {
        [SerializeField] private StalkerAnomalyResponse _response;
        [SerializeField] private FirstPersonWeaponController _weapon;
        [SerializeField] private Transform _target;

        private XRayAnmAsset _motion;
        private float _time;

        private void OnEnable()
        {
            if (_response != null)
            {
                _response.Impact += PlayImpact;
            }
        }

        private void OnDisable()
        {
            if (_response != null)
            {
                _response.Impact -= PlayImpact;
            }
            ResetPose();
        }

        private void PlayImpact(AnomalyInfluenceProfile profile)
        {
            if (_target == null || profile == null || profile.ImpactAnimations == null || profile.ImpactAnimations.Length == 0 || (_weapon != null && (_weapon.IsAiming || _weapon.AimFactor > 0.0001f)))
            {
                return;
            }
            _motion = profile.ImpactAnimations[Random.Range(0, profile.ImpactAnimations.Length)];
            _time = 0f;
        }

        private void LateUpdate()
        {
            if (_motion == null)
            {
                return;
            }
            _time += Time.deltaTime;
            if (_target == null || _time > _motion.Duration || _motion.Evaluate(_time, out Vector3 position, out Quaternion rotation) == false)
            {
                ResetPose();
                return;
            }
            _target.localPosition = position;
            _target.localRotation = rotation;
        }

        private void ResetPose()
        {
            _motion = null;
            _time = 0f;
            if (_target != null)
            {
                _target.localPosition = Vector3.zero;
                _target.localRotation = Quaternion.identity;
            }
        }
    }
}

using System;
using UnityEngine;

namespace RaidIntoDarkness.Weapons
{
    [DisallowMultipleComponent]
    public sealed class FirstPersonRevolverView : MonoBehaviour
    {
        private const float CASE_EJECTION_DISTANCE_SQUARED = 0.0081f;
        private const float ROUND_CASE_JOIN_DISTANCE_SQUARED = 0.0001f;

        [SerializeField] private FirstPersonWeaponMotion[] _partialReloadMotions = Array.Empty<FirstPersonWeaponMotion>();
        [SerializeField] private FirstPersonWeaponMotion _lastShot;
        [SerializeField] private FirstPersonWeaponMotion _aimedLastShot;
        [SerializeField] private Transform _cylinder;
        [SerializeField] private Transform[] _rounds = Array.Empty<Transform>();
        [SerializeField] private Transform[] _cases = Array.Empty<Transform>();
        [SerializeField] private GameObject _speedloader;
        [SerializeField] private Transform _speedloaderRounds;
        [SerializeField] [Min(0f)] private float _reloadVisibilityRadius = 0.22f;
        [SerializeField] [Min(0f)] private float _partialReloadInsertTime = 0.25f;
        [SerializeField] [Min(0f)] private float _speedloaderInsertTime;
        [SerializeField] private bool _retainSpentCases;
        [SerializeField] private bool _anchorFlashAtShotPose;

        private int _ejectedCases;
        private int _incomingRounds;

        internal bool RetainsSpentCases => _retainSpentCases;
        internal bool AnchorFlashAtShotPose => _anchorFlashAtShotPose;

        internal void BeginReload()
        {
            _ejectedCases = 0;
            _incomingRounds = 0;
        }

        internal FirstPersonWeaponMotion FindPartialReload(int missing)
        {
            return missing > 0 && missing <= _partialReloadMotions.Length ? _partialReloadMotions[missing - 1] : null;
        }

        internal FirstPersonWeaponMotion FindLastShot(bool aiming)
        {
            FirstPersonWeaponMotion motion = aiming ? _aimedLastShot : _lastShot;
            return motion?.Hands != null ? motion : null;
        }

        internal int RefreshRounds(int loaded, bool reloading, float reloadTime, int spentChambers, int incoming)
        {
            Matrix4x4 worldToLocal = transform.worldToLocalMatrix;
            Vector3 cylinder = worldToLocal.MultiplyPoint3x4(_cylinder.position);
            float radiusSquared = _reloadVisibilityRadius * _reloadVisibilityRadius;
            bool usingSpeedloader = _speedloaderRounds != null && loaded <= 1;
            if (_retainSpentCases)
            {
                RefreshTrackedChambers(loaded, reloading, reloadTime, spentChambers, incoming, worldToLocal, cylinder, radiusSquared, usingSpeedloader);
            }
            else
            {
                for (int i = 0; i < _rounds.Length; i++)
                {
                    var round = _rounds[i];
                    bool visible = reloading ? (i < loaded || reloadTime >= 0.25f) && (worldToLocal.MultiplyPoint3x4(round.position) - cylinder).sqrMagnitude <= radiusSquared : i < loaded;
                    SetVisible(round, visible);
                }

                for (int i = 0; i < _cases.Length; i++)
                {
                    var casing = _cases[i];
                    bool visible = reloading ? (worldToLocal.MultiplyPoint3x4(casing.position) - cylinder).sqrMagnitude <= radiusSquared : i < loaded;
                    SetVisible(casing, visible);
                }
            }

            if (_speedloader != null && _speedloader.activeSelf != reloading)
            {
                _speedloader.SetActive(reloading);
            }

            if (_speedloaderRounds != null)
            {
                SetVisible(_speedloaderRounds, reloading && usingSpeedloader && _incomingRounds == 0 && incoming == _rounds.Length && (worldToLocal.MultiplyPoint3x4(_speedloaderRounds.position) - cylinder).sqrMagnitude <= radiusSquared);
            }

            return _retainSpentCases && reloading ? spentChambers & ~_ejectedCases : spentChambers;
        }

        private void RefreshTrackedChambers(int loaded, bool reloading, float reloadTime, int spentChambers, int incoming, Matrix4x4 worldToLocal, Vector3 cylinder, float radiusSquared, bool usingSpeedloader)
        {
            float insertTime = usingSpeedloader ? _speedloaderInsertTime : _partialReloadInsertTime;
            for (int i = 0; i < _rounds.Length; i++)
            {
                Transform round = _rounds[i];
                Transform casing = _cases[i];
                int chamber = 1 << i;
                bool live = i < loaded;
                bool spent = (spentChambers & chamber) != 0;

                if (reloading && (!live || usingSpeedloader))
                {
                    Vector3 casePosition = worldToLocal.MultiplyPoint3x4(casing.position);
                    Vector3 roundPosition = worldToLocal.MultiplyPoint3x4(round.position);
                    if ((spent || live) && (casePosition - cylinder).sqrMagnitude > CASE_EJECTION_DISTANCE_SQUARED)
                    {
                        _ejectedCases |= chamber;
                    }

                    bool ejected = (_ejectedCases & chamber) != 0;
                    spent &= !ejected;
                    live &= !ejected;
                    if (!live && !spent && i < incoming && reloadTime >= insertTime && (roundPosition - cylinder).sqrMagnitude <= radiusSquared && (roundPosition - casePosition).sqrMagnitude <= ROUND_CASE_JOIN_DISTANCE_SQUARED)
                    {
                        _incomingRounds |= chamber;
                    }

                    live |= (_incomingRounds & chamber) != 0;
                }

                SetVisible(round, live);
                SetVisible(casing, live || spent);
            }
        }

        private static void SetVisible(Transform bone, bool visible)
        {
            Vector3 scale = visible ? Vector3.one : Vector3.zero;
            if (bone.localScale != scale)
            {
                bone.localScale = scale;
            }
        }
    }
}

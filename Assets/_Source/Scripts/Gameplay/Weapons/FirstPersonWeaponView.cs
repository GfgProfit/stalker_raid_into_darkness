using System;
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
        [SerializeField] private Transform _grenadeMuzzle;
        [SerializeField] private GameObject _launcherRound;
        [SerializeField] [Min(0)] private int _underbarrelRoundRevealFrame;
        [SerializeField] private Transform _underbarrelRoundBone;
        [SerializeField] private Transform _underbarrelShellBone;
        [SerializeField] private Vector2Int _underbarrelShellFrames;
        [SerializeField] private Transform[] _launcherChambers = Array.Empty<Transform>();
        [SerializeField] private Transform[] _launcherReloadRounds = Array.Empty<Transform>();
        [SerializeField] private Transform[] _unfoldedLauncherSights = Array.Empty<Transform>();
        [SerializeField] private Transform _fixedLauncherPart;
        [SerializeField] private Quaternion _fixedLauncherPartRotation = Quaternion.identity;
        [SerializeField] private GameObject _magazineSwapObjects;
        [SerializeField] private AnimationClip[] _magazineSwapClips = Array.Empty<AnimationClip>();
        [SerializeField] private SkinnedMeshRenderer[] _installedMagazineRenderers = Array.Empty<SkinnedMeshRenderer>();
        [SerializeField] private SkinnedMeshRenderer[] _replacementMagazineRenderers = Array.Empty<SkinnedMeshRenderer>();
        [SerializeField] private MagazineBonePair[] _magazineBonePairs = Array.Empty<MagazineBonePair>();
        [SerializeField] private Renderer _installedMagazineRound;
        [SerializeField] private Renderer _replacementMagazineRound;
        [SerializeField] [Min(0f)] private float _hipBackOffset;

        private bool _magazineRolesSwapped;
        private bool _magazineSwapping;
        private bool _magazineHasRounds;
        private bool _outgoingMagazineHasRounds;

        public Transform Muzzle => _muzzle;
        public GameObject SilencerVisual => _silencerVisual;
        public Transform SilencedMuzzle => _silencedMuzzle;
        public Transform GrenadeMuzzle => _grenadeMuzzle;
        internal float HipBackOffset => _hipBackOffset;

        public void SetMuzzle(Transform muzzle)
        {
            _muzzle = muzzle;
        }

        internal bool UsesMagazineSwap(AnimationClip clip)
        {
            return _magazineSwapObjects != null && _installedMagazineRenderers.Length > 0 && _replacementMagazineRenderers.Length > 0 && Array.IndexOf(_magazineSwapClips, clip) >= 0;
        }

        internal void SetMagazineSwapObjectsVisible(bool swapping)
        {
            if (_magazineSwapObjects == null)
            {
                return;
            }

            if (swapping && !_magazineSwapping)
            {
                _outgoingMagazineHasRounds = _magazineHasRounds;
            }

            _magazineSwapping = swapping;
            bool showInstalled = swapping || !_magazineRolesSwapped;
            foreach (SkinnedMeshRenderer renderer in _installedMagazineRenderers)
            {
                if (renderer != null && renderer.enabled != showInstalled)
                {
                    renderer.enabled = showInstalled;
                }
            }

            bool showReplacement = swapping || _magazineRolesSwapped;
            if (_magazineSwapObjects.activeSelf != showReplacement)
            {
                _magazineSwapObjects.SetActive(showReplacement);
            }

            SetMagazineRoundVisibility();
        }

        internal void UpdateMagazineRounds(int loaded)
        {
            _magazineHasRounds = loaded > 0;
            SetMagazineRoundVisibility();
        }

        private void SetMagazineRoundVisibility()
        {
            bool installedHasRounds = _magazineSwapping ? _outgoingMagazineHasRounds : _magazineHasRounds;
            if (_installedMagazineRound != null)
            {
                _installedMagazineRound.enabled = _magazineRolesSwapped ? _magazineSwapping : installedHasRounds;
            }

            if (_replacementMagazineRound != null)
            {
                _replacementMagazineRound.enabled = _magazineRolesSwapped ? installedHasRounds : _magazineSwapping;
            }
        }

        internal void SwapMagazineRoles()
        {
            if (_magazineBonePairs.Length == 0)
            {
                return;
            }

            SwapMagazineBones(_installedMagazineRenderers);
            SwapMagazineBones(_replacementMagazineRenderers);
            _magazineRolesSwapped = !_magazineRolesSwapped;
        }

        private void SwapMagazineBones(SkinnedMeshRenderer[] renderers)
        {
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                Transform[] bones = renderer.bones;
                for (int i = 0; i < bones.Length; i++)
                {
                    bones[i] = GetPairedBone(bones[i]);
                }

                renderer.bones = bones;
                Transform root = GetPairedBone(renderer.rootBone);
                if (root != renderer.rootBone)
                {
                    renderer.rootBone = root;
                }
            }
        }

        private Transform GetPairedBone(Transform bone)
        {
            foreach (MagazineBonePair pair in _magazineBonePairs)
            {
                if (pair.Installed == null || pair.Replacement == null)
                {
                    continue;
                }

                if (bone == pair.Installed)
                {
                    return pair.Replacement;
                }

                if (bone == pair.Replacement)
                {
                    return pair.Installed;
                }
            }

            return bone;
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
            if (_launcherRound != null && _launcherRound.activeSelf != visible)
            {
                _launcherRound.SetActive(visible);
            }
        }

        internal float GetUnderbarrelRoundRevealTime(FirstPersonWeaponMotion motion)
        {
            AnimationClip clip = motion.Weapon;
            return clip != null && clip.frameRate > 0f ? _underbarrelRoundRevealFrame / (clip.frameRate * motion.WeaponSpeed) : float.PositiveInfinity;
        }

        internal Vector2 GetUnderbarrelShellTimes(FirstPersonWeaponMotion motion)
        {
            AnimationClip clip = motion.Weapon;
            return _underbarrelShellBone != null && clip != null && clip.frameRate > 0f ? (Vector2)_underbarrelShellFrames / (clip.frameRate * motion.WeaponSpeed) : Vector2.zero;
        }

        internal void SetUnderbarrelRoundsVisible(bool roundVisible, bool shellVisible)
        {
            if (_underbarrelRoundBone != null)
            {
                SetBoneVisible(_underbarrelRoundBone, roundVisible);
            }

            if (_underbarrelShellBone != null)
            {
                SetBoneVisible(_underbarrelShellBone, shellVisible);
            }

            SetLauncherRoundVisible(roundVisible || shellVisible);
        }

        private static void SetBoneVisible(Transform bone, bool visible)
        {
            Vector3 scale = visible ? Vector3.one : Vector3.zero;
            if (bone.localScale != scale)
            {
                bone.localScale = scale;
            }
        }

        internal void UpdateLauncherChambers(int loaded, bool inserting)
        {
            for (int i = 0; i < _launcherChambers.Length; i++)
            {
                _launcherChambers[i].localScale = i < loaded ? Vector3.one : Vector3.zero;
            }

            foreach (Transform round in _launcherReloadRounds)
            {
                round.localScale = inserting ? Vector3.one : Vector3.zero;
            }
        }

        internal void ApplyLauncherBoneOverrides()
        {
            foreach (Transform sight in _unfoldedLauncherSights)
            {
                if (sight != null)
                {
                    sight.localRotation = Quaternion.identity;
                }
            }

            if (_fixedLauncherPart != null)
            {
                _fixedLauncherPart.localRotation = _fixedLauncherPartRotation;
            }
        }
    }
}

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
        [SerializeField] private GameObject _magazineSwapObjects;
        [SerializeField] private AnimationClip[] _magazineSwapClips = Array.Empty<AnimationClip>();
        [SerializeField] private SkinnedMeshRenderer[] _installedMagazineRenderers = Array.Empty<SkinnedMeshRenderer>();
        [SerializeField] private SkinnedMeshRenderer[] _replacementMagazineRenderers = Array.Empty<SkinnedMeshRenderer>();
        [SerializeField] private MagazineBonePair[] _magazineBonePairs = Array.Empty<MagazineBonePair>();

        private bool _magazineRolesSwapped;

        public Transform Muzzle => _muzzle;
        public GameObject SilencerVisual => _silencerVisual;
        public Transform SilencedMuzzle => _silencedMuzzle;
        public Transform GrenadeMuzzle => _grenadeMuzzle;

        public void SetMuzzle(Transform muzzle)
        {
            _muzzle = muzzle;
        }

        internal bool UsesMagazineSwap(AnimationClip clip)
        {
            return _magazineSwapObjects != null && _installedMagazineRenderers.Length > 0 && _replacementMagazineRenderers.Length > 0 && _magazineBonePairs.Length > 0 && Array.IndexOf(_magazineSwapClips, clip) >= 0;
        }

        internal void SetMagazineSwapObjectsVisible(bool swapping)
        {
            if (_magazineSwapObjects == null)
            {
                return;
            }

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
        }

        internal void SwapMagazineRoles()
        {
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
            if (_launcherRound != null)
            {
                _launcherRound.SetActive(visible);
            }
        }

        [Serializable]
        private struct MagazineBonePair
        {
            [SerializeField] private Transform _installed;
            [SerializeField] private Transform _replacement;

            public Transform Installed => _installed;
            public Transform Replacement => _replacement;
        }
    }
}

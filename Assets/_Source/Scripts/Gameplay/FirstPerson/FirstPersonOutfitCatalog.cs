using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.FirstPerson
{
    public enum FirstPersonOutfitResolution
    {
        Resolved,
        BodyFallbackNoLegsSection,
        BodyFallbackMissingModel,
        HandsMissing,
        BodyMissing
    }

    [Serializable]
    public sealed class FirstPersonOutfitEntry
    {
        [SerializeField] private string _section;
        [SerializeField] private string _displayName;
        [SerializeField] private string _hudSection;
        [SerializeField] private string _handsVirtualPath;
        [SerializeField] private GameObject _handsModel;
        [SerializeField] private string _handsSource;
        [SerializeField] private string _legsSection;
        [SerializeField] private string _bodyVirtualPath;
        [SerializeField] private GameObject _bodyModel;
        [SerializeField] private string _bodySource;
        [SerializeField] private BodyAnimationLibrary _bodyAnimations;
        [SerializeField] private bool _isExoskeleton;
        [SerializeField] private FirstPersonOutfitResolution _resolution;

        public FirstPersonOutfitEntry(string section, string displayName, string hudSection, string handsVirtualPath, GameObject handsModel, string handsSource, string legsSection, string bodyVirtualPath, GameObject bodyModel, string bodySource, BodyAnimationLibrary bodyAnimations, bool isExoskeleton, FirstPersonOutfitResolution resolution)
        {
            _section = section;
            _displayName = displayName;
            _hudSection = hudSection;
            _handsVirtualPath = handsVirtualPath;
            _handsModel = handsModel;
            _handsSource = handsSource;
            _legsSection = legsSection;
            _bodyVirtualPath = bodyVirtualPath;
            _bodyModel = bodyModel;
            _bodySource = bodySource;
            _bodyAnimations = bodyAnimations;
            _isExoskeleton = isExoskeleton;
            _resolution = resolution;
        }

        public string Section => _section;
        public string DisplayName => _displayName;
        public string HudSection => _hudSection;
        public string HandsVirtualPath => _handsVirtualPath;
        public GameObject HandsModel => _handsModel;
        public string HandsSource => _handsSource;
        public string LegsSection => _legsSection;
        public string BodyVirtualPath => _bodyVirtualPath;
        public GameObject BodyModel => _bodyModel;
        public string BodySource => _bodySource;
        public BodyAnimationLibrary BodyAnimations => _bodyAnimations;
        public bool IsExoskeleton => _isExoskeleton;
        public FirstPersonOutfitResolution Resolution => _resolution;
    }

    [CreateAssetMenu(menuName = "Raid Into Darkness/First Person/Outfit Catalog", fileName = "OutfitCatalog")]
    public sealed class FirstPersonOutfitCatalog : ScriptableObject
    {
        public const string NO_OUTFIT_SECTION = "actor";

        [SerializeField] private List<FirstPersonOutfitEntry> _entries = new List<FirstPersonOutfitEntry>();

        public IReadOnlyList<FirstPersonOutfitEntry> Entries => _entries;

        public bool TryGet(string section, out FirstPersonOutfitEntry entry)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i].Section, section, StringComparison.OrdinalIgnoreCase))
                {
                    entry = _entries[i];
                    return true;
                }
            }

            entry = null;
            return false;
        }

        public FirstPersonOutfitEntry GetOrDefault(string section)
        {
            if (!string.IsNullOrEmpty(section) && TryGet(section, out FirstPersonOutfitEntry entry))
            {
                return entry;
            }

            return TryGet(NO_OUTFIT_SECTION, out FirstPersonOutfitEntry fallback) ? fallback : null;
        }

        public void SetEntries(List<FirstPersonOutfitEntry> entries)
        {
            _entries = entries;
        }
    }
}

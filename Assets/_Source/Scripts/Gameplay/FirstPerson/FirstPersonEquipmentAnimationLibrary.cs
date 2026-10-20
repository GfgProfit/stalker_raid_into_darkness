using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.FirstPerson
{
    [CreateAssetMenu(menuName = "Raid Into Darkness/First Person/Equipment Animation Library", fileName = "EquipmentAnimationLibrary")]
    public sealed class FirstPersonEquipmentAnimationLibrary : ScriptableObject
    {
        public const string OUTFIT_SECTION = "outfit_animation_hud";
        public const string OUTFIT_FAST_SECTION = "outfit_animation_fast_hud";
        public const string HEADGEAR_SUFFIX = "_headgear_hud";
        public const string HEADGEAR_FALLBACK_SECTION = "gasmask_headgear_hud";
        public const string BACKPACK_EQUIP_PREFIX = "outfit_animation_backpack_equip_";
        public const string BACKPACK_UNEQUIP_PREFIX = "outfit_animation_backpack_unequip_";
        public const string HUD_SUFFIX = "_hud";
        public const string DEFAULT_COMMUNITY = "stalker";
        public const string ANM_EQUIP = "anm_equip";
        public const string ANM_UNEQUIP = "anm_unequip";
        public const string ANM_USE = "anm_use";

        [SerializeField] private List<FirstPersonHudMotion> _motions = new List<FirstPersonHudMotion>();
        [Tooltip("Headgear hud sections with fade_use = true.")]
        [SerializeField] private List<string> _fadeSections = new List<string>();
        [Tooltip("items\\items\\headgear_blacklist.ltx: headgear equipped without animation.")]
        [SerializeField] private List<string> _headgearBlacklist = new List<string>();
        [Tooltip("MCM fddar/outfit_anm/enable_outfit_equip.")]
        [SerializeField] private bool _outfitEnabled = true;
        [Tooltip("MCM fddar/outfit_anm/enable_backpack_equip.")]
        [SerializeField] private bool _backpackEnabled = true;
        [Tooltip("MCM fddar/headgear_anm/enable.")]
        [SerializeField] private bool _headgearEnabled = true;
        [Tooltip("MCM fddar/outfit_anm/memory: recently worn outfits use the short animation.")]
        [SerializeField] private bool _outfitMemory = true;
        [Tooltip("MCM fddar/outfit_anm/memory_size.")]
        [SerializeField] [Min(0)] private int _outfitMemorySize = 3;
        [Tooltip("MCM fddar/outfit_anm/always_short.")]
        [SerializeField] private bool _alwaysShort;
        [Tooltip("MCM fddar/headgear_anm/speed.")]
        [SerializeField] [Min(0.01f)] private float _headgearSpeed = 1.0f;

        public IReadOnlyList<FirstPersonHudMotion> Motions => _motions;
        public IReadOnlyList<string> FadeSections => _fadeSections;
        public IReadOnlyList<string> HeadgearBlacklist => _headgearBlacklist;
        public bool OutfitEnabled => _outfitEnabled;
        public bool BackpackEnabled => _backpackEnabled;
        public bool HeadgearEnabled => _headgearEnabled;
        public bool OutfitMemory => _outfitMemory;
        public int OutfitMemorySize => _outfitMemorySize;
        public bool AlwaysShort => _alwaysShort;
        public float HeadgearSpeed => _headgearSpeed;

        public void SetData(List<FirstPersonHudMotion> motions, List<string> fadeSections, List<string> headgearBlacklist)
        {
            _motions = motions ?? new List<FirstPersonHudMotion>();
            _fadeSections = fadeSections ?? new List<string>();
            _headgearBlacklist = headgearBlacklist ?? new List<string>();
        }

        public FirstPersonHudMotion Find(string section, string key)
        {
            string id = FirstPersonHudMotion.MakeId(section, key);
            for (int i = 0; i < _motions.Count; i++)
            {
                if (string.Equals(_motions[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return _motions[i];
                }
            }

            return null;
        }

        public bool HasSection(string section)
        {
            string prefix = section + "|";
            for (int i = 0; i < _motions.Count; i++)
            {
                if (_motions[i].Id != null && _motions[i].Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public bool UsesFade(string section)
        {
            return Contains(_fadeSections, section);
        }

        public bool IsHeadgearBlacklisted(string itemSection)
        {
            return Contains(_headgearBlacklist, itemSection);
        }

        public string GetHeadgearSection(string itemSection)
        {
            string section = itemSection + HEADGEAR_SUFFIX;
            return HasSection(section) ? section : HEADGEAR_FALLBACK_SECTION;
        }

        public string GetBackpackSection(string prefix, string community)
        {
            string section = prefix + community + HUD_SUFFIX;
            return HasSection(section) ? section : prefix + DEFAULT_COMMUNITY + HUD_SUFFIX;
        }

        private static bool Contains(List<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

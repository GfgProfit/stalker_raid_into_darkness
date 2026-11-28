using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory
{
    [Serializable]
    public sealed class SlotRule
    {
        [SerializeField] private List<string> _acceptedCategories = new();
        [SerializeField] private List<string> _requiredTags = new();
        [SerializeField] private List<string> _forbiddenTags = new();
        [SerializeField] private string _validatorId;
        [SerializeField] private List<SlotId> _requiresOccupiedSlots = new();
        [SerializeField] private List<SlotId> _blockedByOccupiedSlots = new();
        [SerializeField] private bool _startsLocked;

        public IReadOnlyList<string> AcceptedCategories => _acceptedCategories;
        public IReadOnlyList<string> RequiredTags => _requiredTags;
        public IReadOnlyList<string> ForbiddenTags => _forbiddenTags;
        public string ValidatorId => _validatorId ?? string.Empty;
        public IReadOnlyList<SlotId> RequiresOccupiedSlots => _requiresOccupiedSlots ?? (IReadOnlyList<SlotId>)Array.Empty<SlotId>();
        public IReadOnlyList<SlotId> BlockedByOccupiedSlots => _blockedByOccupiedSlots ?? (IReadOnlyList<SlotId>)Array.Empty<SlotId>();
        public bool StartsLocked => _startsLocked;

        public static SlotRule Create(IEnumerable<string> categories = null, IEnumerable<string> requiredTags = null, IEnumerable<string> forbiddenTags = null, string validatorId = null, IEnumerable<SlotId> requiresOccupied = null, IEnumerable<SlotId> blockedByOccupied = null, bool startsLocked = false)
        {
            var rule = new SlotRule();
            AddRange(rule._acceptedCategories, categories);
            AddRange(rule._requiredTags, requiredTags);
            AddRange(rule._forbiddenTags, forbiddenTags);

            if (requiresOccupied != null)
            {
                rule._requiresOccupiedSlots.AddRange(requiresOccupied);
            }

            if (blockedByOccupied != null)
            {
                rule._blockedByOccupiedSlots.AddRange(blockedByOccupied);
            }

            rule._validatorId = validatorId;
            rule._startsLocked = startsLocked;
            return rule;
        }

        public bool AcceptsDefinition(ItemDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            if (_acceptedCategories != null && _acceptedCategories.Count > 0)
            {
                bool categoryMatch = false;

                for (int i = 0; i < _acceptedCategories.Count; i++)
                {
                    if (definition.IsCategory(_acceptedCategories[i]))
                    {
                        categoryMatch = true;
                        break;
                    }
                }

                if (categoryMatch == false)
                {
                    return false;
                }
            }

            if (_requiredTags != null && _requiredTags.Count > 0)
            {
                bool tagMatch = false;

                for (int i = 0; i < _requiredTags.Count; i++)
                {
                    if (definition.HasTag(_requiredTags[i]))
                    {
                        tagMatch = true;
                        break;
                    }
                }

                if (tagMatch == false)
                {
                    return false;
                }
            }

            if (_forbiddenTags != null)
            {
                for (int i = 0; i < _forbiddenTags.Count; i++)
                {
                    if (definition.HasTag(_forbiddenTags[i]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static void AddRange(List<string> target, IEnumerable<string> values)
        {
            if (values == null)
            {
                return;
            }

            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value) == false)
                {
                    target.Add(value.Trim());
                }
            }
        }
    }
}

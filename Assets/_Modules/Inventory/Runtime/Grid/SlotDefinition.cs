using System;
using System.Collections.Generic;
using UnityEngine;

namespace Inventory
{
    public enum SlotMode
    {
        Grid = 0,
        Single = 1
    }

    [Serializable]
    public sealed class SlotRule
    {
        [SerializeField] private List<string> _acceptedCategories = new();
        [SerializeField] private List<string> _requiredTags = new();
        [SerializeField] private List<string> _forbiddenTags = new();
        [SerializeField] private string _validatorId;
        [SerializeField] private List<string> _requiresOccupiedSlots = new();
        [SerializeField] private List<string> _blockedByOccupiedSlots = new();
        [SerializeField] private bool _startsLocked;

        public IReadOnlyList<string> AcceptedCategories => _acceptedCategories;
        public IReadOnlyList<string> RequiredTags => _requiredTags;
        public IReadOnlyList<string> ForbiddenTags => _forbiddenTags;
        public string ValidatorId => _validatorId ?? string.Empty;
        public IReadOnlyList<string> RequiresOccupiedSlots => _requiresOccupiedSlots;
        public IReadOnlyList<string> BlockedByOccupiedSlots => _blockedByOccupiedSlots;
        public bool StartsLocked => _startsLocked;

        public static SlotRule Create(IEnumerable<string> categories = null, IEnumerable<string> requiredTags = null, IEnumerable<string> forbiddenTags = null, string validatorId = null, IEnumerable<string> requiresOccupied = null, IEnumerable<string> blockedByOccupied = null, bool startsLocked = false)
        {
            var rule = new SlotRule();
            AddRange(rule._acceptedCategories, categories);
            AddRange(rule._requiredTags, requiredTags);
            AddRange(rule._forbiddenTags, forbiddenTags);
            AddRange(rule._requiresOccupiedSlots, requiresOccupied);
            AddRange(rule._blockedByOccupiedSlots, blockedByOccupied);
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

    public sealed class SlotDefinition
    {
        public SlotDefinition(string id, string displayName, RectInt bounds, SlotMode mode, SlotRule rule, string group = null)
        {
            Id = id ?? string.Empty;
            DisplayName = string.IsNullOrEmpty(displayName) ? Id : displayName;
            Bounds = bounds;
            Mode = mode;
            Rule = rule ?? new SlotRule();
            Group = group ?? string.Empty;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public RectInt Bounds { get; }
        public SlotMode Mode { get; }
        public SlotRule Rule { get; }
        public string Group { get; }
        public int X => Bounds.x;
        public int Y => Bounds.y;
        public int Width => Bounds.width;
        public int Height => Bounds.height;

        public bool Contains(Vector2Int cell) => cell.x >= Bounds.xMin && cell.y >= Bounds.yMin && cell.x < Bounds.xMax && cell.y < Bounds.yMax;
    }
}

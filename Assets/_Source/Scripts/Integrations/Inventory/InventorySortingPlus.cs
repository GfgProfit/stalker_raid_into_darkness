using System;
using System.Collections.Generic;
using Inventory;
using Inventory.UI;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory
{
    public sealed class InventorySortingPlus
    {
        public const int FAVORITES_ORDER = -2;
        public const int WEAPONS_ORDER = 1;
        public const int OUTFITS_ORDER = 2;
        public const int ARTIFACTS_ORDER = 3;
        public const int DEVICES_ORDER = 4;
        public const int TOOLS_ORDER = 5;
        public const int MEDICAL_ORDER = 6;
        public const int FOODS_ORDER = 7;
        public const int QUEST_ORDER = 8;
        public const int AMMO_ORDER = 9;
        public const int MUTANT_PARTS_ORDER = 10;
        public const int MISC_ORDER = 11;
        public const int UPGRADES_ORDER = 12;
        public const int PARTS_ORDER = 13;
        public const int UNKNOWN_ORDER = 42;
        public const int JUNK_ORDER = 100;

        private static readonly Color _favoriteTint = new(176f / 255f, 238f / 255f, 26f / 255f, 10f / 255f);
        private static readonly Color _junkTint = new(204f / 255f, 0f, 0f, 10f / 255f);

        private readonly InventoryModel _model;
        private readonly List<string> _containerIds;
        private readonly HashSet<string> _favorites = new(StringComparer.Ordinal);
        private readonly HashSet<string> _junk = new(StringComparer.Ordinal);
        private readonly List<ItemInstance> _order = new();
        private readonly List<int> _groups = new();
        private readonly Sprite _favoriteBadge;
        private readonly Sprite _junkBadge;

        public InventorySortingPlus(InventoryModel model, IEnumerable<string> containerIds, Sprite favoriteBadge, Sprite junkBadge)
        {
            _model = model;
            _containerIds = new List<string>(containerIds);
            _favoriteBadge = favoriteBadge;
            _junkBadge = junkBadge;
        }

        public IReadOnlyCollection<string> Favorites => _favorites;
        public IReadOnlyCollection<string> Junk => _junk;

        public event Action Changed;

        public bool IsFavorite(ItemDefinition definition) => definition != null && _favorites.Contains(definition.DefinitionId);
        public bool IsJunk(ItemDefinition definition) => definition != null && _junk.Contains(definition.DefinitionId);
        public bool IsSorted(string containerId) => _containerIds.Contains(containerId);

        public void SetFavorite(ItemDefinition definition, bool state)
        {
            if (definition == null)
            {
                return;
            }

            if (state)
            {
                _favorites.Add(definition.DefinitionId);
                _junk.Remove(definition.DefinitionId);
            }
            else
            {
                _favorites.Remove(definition.DefinitionId);
            }

            Changed?.Invoke();
        }

        public void SetJunk(ItemDefinition definition, bool state)
        {
            if (definition == null)
            {
                return;
            }

            if (state)
            {
                _junk.Add(definition.DefinitionId);
                _favorites.Remove(definition.DefinitionId);
            }
            else
            {
                _junk.Remove(definition.DefinitionId);
            }

            Changed?.Invoke();
        }

        public void ToggleFavorite(ItemDefinition definition) => SetFavorite(definition, IsFavorite(definition) == false);
        public void ToggleJunk(ItemDefinition definition) => SetJunk(definition, IsJunk(definition) == false);

        public InventoryItemDecoration Decorate(ItemInstance item)
        {
            if (item == null || IsSorted(item.ContainerId) == false)
            {
                return InventoryItemDecoration.None;
            }

            if (IsFavorite(item.Definition))
            {
                return new InventoryItemDecoration(_favoriteTint, _favoriteBadge);
            }

            return IsJunk(item.Definition) ? new InventoryItemDecoration(_junkTint, _junkBadge) : InventoryItemDecoration.None;
        }

        public int GetOrder(ItemDefinition definition)
        {
            if (IsFavorite(definition))
            {
                return FAVORITES_ORDER;
            }

            return IsJunk(definition) ? JUNK_ORDER : GetKindOrder(definition);
        }

        public static int GetKindOrder(ItemDefinition definition)
        {
            if (definition == null)
            {
                return UNKNOWN_ORDER;
            }

            if (definition.IsCategory("Weapon") || definition.IsCategory("Attachment"))
            {
                return WEAPONS_ORDER;
            }

            if (definition.IsCategory("Armor") || definition.IsCategory("Head") || definition.IsCategory("Backpack"))
            {
                return OUTFITS_ORDER;
            }

            if (definition.IsCategory("Artifact"))
            {
                return ARTIFACTS_ORDER;
            }

            if (definition.IsCategory("Accessory") || definition.IsCategory("Device") || definition.IsCategory("Pda") || definition.IsCategory("Binocular"))
            {
                return DEVICES_ORDER;
            }

            if (definition.IsCategory("Tool"))
            {
                return TOOLS_ORDER;
            }

            if (definition.HasTag("Medical"))
            {
                return MEDICAL_ORDER;
            }

            if (definition.HasTag("Food") || definition.HasTag("Drink"))
            {
                return FOODS_ORDER;
            }

            if (definition.IsCategory("Quest"))
            {
                return QUEST_ORDER;
            }

            if (definition.IsCategory("Ammo"))
            {
                return AMMO_ORDER;
            }

            if (definition.IsCategory("MutantPart"))
            {
                return MUTANT_PARTS_ORDER;
            }

            if (definition.IsCategory("Misc"))
            {
                return MISC_ORDER;
            }

            if (definition.IsCategory("Upgrade"))
            {
                return UPGRADES_ORDER;
            }

            return definition.IsCategory("Part") ? PARTS_ORDER : UNKNOWN_ORDER;
        }

        public int Compare(ItemInstance left, ItemInstance right)
        {
            int order = GetOrder(left.Definition).CompareTo(GetOrder(right.Definition));

            if (order != 0)
            {
                return order;
            }

            int width = right.Definition.Width.CompareTo(left.Definition.Width);

            if (width != 0)
            {
                return width;
            }

            int height = right.Definition.Height.CompareTo(left.Definition.Height);

            if (height != 0)
            {
                return height;
            }

            int section = string.CompareOrdinal(GetSortName(left.Definition), GetSortName(right.Definition));

            if (section != 0)
            {
                return section;
            }

            int amount = right.Amount.CompareTo(left.Amount);
            return amount != 0 ? amount : string.CompareOrdinal(left.InstanceId, right.InstanceId);
        }

        private static string GetSortName(ItemDefinition definition)
        {
            if (definition.IsCategory("Weapon"))
            {
                string weaponSection = definition.GetComponent<WeaponStatsComponent>()?.Section;
                return string.IsNullOrEmpty(weaponSection) ? definition.name : weaponSection;
            }

            string section = definition.IsCategory(AnomalyEquipmentProtection.OUTFIT_CATEGORY) ? definition.GetComponent<EquipmentStatsComponent>()?.Section : null;
            return string.IsNullOrEmpty(section) ? definition.name : section;
        }

        public void ArrangeAll()
        {
            foreach (string containerId in _containerIds)
            {
                Arrange(containerId);
            }
        }

        private void Arrange(string containerId)
        {
            InventoryContainer container = _model.GetContainer(containerId);

            if (container == null)
            {
                return;
            }

            _order.Clear();
            _order.AddRange(container.Items);
            _order.Sort(Compare);
            _groups.Clear();

            for (int i = 0; i < _order.Count; i++)
            {
                _groups.Add(GetOrder(_order[i].Definition));
            }

            _model.TryArrange(containerId, _order, _groups);
        }
    }
}

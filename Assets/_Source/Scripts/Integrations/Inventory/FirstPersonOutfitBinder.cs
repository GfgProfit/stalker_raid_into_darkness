using System;
using Inventory;
using Inventory.UI;
using RaidIntoDarkness.FirstPerson;
using RaidIntoDarkness.Player;

namespace RaidIntoDarkness.Integrations.Inventory
{
    public sealed class FirstPersonOutfitBinder : IDisposable
    {
        private readonly InventoryWindow _window;
        private readonly InventoryModel _model;
        private readonly StalkerPlayerController _player;
        private string _section;

        public FirstPersonOutfitBinder(InventoryWindow window, StalkerPlayerController player)
        {
            _window = window;
            _player = player;
            _model = window.Model;
            _model.ItemEquipped += OnEquipmentChanged;
            _model.ItemUnequipped += OnEquipmentChanged;
        }

        public void Refresh()
        {
            ItemInstance outfit = EquipmentStatsFormatter.FindEquipped(_window, EquipmentStatsFormatter.OUTFIT_CATEGORY);
            EquipmentStatsComponent stats = outfit != null ? outfit.Definition.GetComponent<EquipmentStatsComponent>() : null;
            string section = stats != null && stats.Section.Length > 0 ? stats.Section : FirstPersonOutfitCatalog.NO_OUTFIT_SECTION;

            if (_player == null || string.Equals(_section, section, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_player.ApplyOutfit(section))
            {
                _section = section;
            }
        }

        public void Dispose()
        {
            _model.ItemEquipped -= OnEquipmentChanged;
            _model.ItemUnequipped -= OnEquipmentChanged;
        }

        private void OnEquipmentChanged(ItemInstance item, SlottedContainer container, SlotDefinition slot)
        {
            if (item != null && item.Definition.IsCategory(EquipmentStatsFormatter.OUTFIT_CATEGORY))
            {
                Refresh();
            }
        }
    }
}

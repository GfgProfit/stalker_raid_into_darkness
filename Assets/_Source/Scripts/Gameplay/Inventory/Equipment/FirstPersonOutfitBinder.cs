using RaidIntoDarkness.Inventory.Domain;
using System;
using RaidIntoDarkness.FirstPerson;
using RaidIntoDarkness.Player;

namespace RaidIntoDarkness.Inventory.Infrastructure
{
    public sealed class FirstPersonOutfitBinder : IDisposable
    {
        private readonly InventoryModel _model;
        private readonly string _equipmentContainerId;
        private readonly StalkerPlayerController _player;
        private string _section;

        public FirstPersonOutfitBinder(InventoryModel model, string equipmentContainerId, StalkerPlayerController player)
        {
            _model = model;
            _equipmentContainerId = equipmentContainerId;
            _player = player;
            _model.ItemEquipped += OnEquipmentChanged;
            _model.ItemUnequipped += OnEquipmentChanged;
        }

        public void Refresh()
        {
            ItemInstance outfit = EquipmentItemStats.FindEquipped(_model.GetContainer(_equipmentContainerId), AnomalyEquipmentProtection.OUTFIT_CATEGORY);
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
            if (container.Id == _equipmentContainerId && item != null && item.Definition.IsCategory(AnomalyEquipmentProtection.OUTFIT_CATEGORY))
            {
                Refresh();
            }
        }
    }
}

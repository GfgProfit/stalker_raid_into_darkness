using System;
using R3;
using RaidIntoDarkness.Inventory.Application;
using RaidIntoDarkness.Inventory.Domain;

namespace RaidIntoDarkness.Inventory.UI
{
    internal sealed class InventoryWindowViewModel : IDisposable
    {
        private readonly InventoryModel _model;
        private readonly string _equipmentId;
        private readonly string _primaryId;
        private readonly Subject<(InventoryWindowCommand Kind, ItemInstance Item, InventoryResult Result, bool PlaySound, int Amount, InventoryContainer Target)> _completed = new();

        public InventoryWindowViewModel(InventoryModel model, string equipmentId, string primaryId)
        {
            _model = model;
            _equipmentId = equipmentId;
            _primaryId = primaryId;
        }

        public Observable<(InventoryWindowCommand Kind, ItemInstance Item, InventoryResult Result, bool PlaySound, int Amount, InventoryContainer Target)> Completed => _completed;
        public IEquipmentAnimationRequests Equipment { get; set; }
        public Func<ItemInstance, InventoryResult> WorldDrop { get; set; }
        public bool CanDropInWorld => WorldDrop != null;

        public bool CanSplit(ItemInstance item) => InventoryItemOperations.CanSplit(_model, item);

        public void Use(ItemInstance item)
        {
            if (item != null)
            {
                _completed.OnNext((InventoryWindowCommand.Use, item, InventoryItemOperations.Use(_model, item), true, 0, null));
            }
        }

        public void Equip(ItemInstance item, SlotDefinition slot, bool animate)
        {
            var command = EquipmentInventoryCommands.RequestEquip(_model, item, _equipmentId, _primaryId, slot, animate ? Equipment : null);
            if (command.Dispatched && command.Queued == false)
            {
                _completed.OnNext((InventoryWindowCommand.Equip, item, command.Result, true, 0, null));
            }
        }

        public InventoryResult Unequip(ItemInstance item, InventoryUnequipTarget target, bool animate, bool playSound)
        {
            var command = EquipmentInventoryCommands.RequestUnequip(_model, item, target, animate ? Equipment : null);
            if (command.Dispatched && command.Queued == false)
            {
                _completed.OnNext((InventoryWindowCommand.Unequip, item, command.Result, playSound, 0, null));
            }

            return command.Result;
        }

        public void Split(ItemInstance item, int amount)
        {
            _completed.OnNext((InventoryWindowCommand.Split, item, InventoryItemOperations.Split(_model, item, amount), false, amount, null));
        }

        public void Transfer(ItemInstance item, InventoryContainer target)
        {
            if (target == null)
            {
                return;
            }

            var transfer = InventoryItemOperations.Transfer(_model, item, target, _equipmentId, Equipment);
            if (transfer.Queued == false)
            {
                _completed.OnNext((InventoryWindowCommand.Transfer, item, transfer.Result, transfer.WasEquipped, 0, target));
            }
        }

        public InventoryResult Drop(ItemInstance item, bool animate)
        {
            var command = InventoryItemOperations.RequestDrop(_model, item, _equipmentId, animate ? Equipment : null, WorldDrop);
            if (command.Dispatched && command.Queued == false)
            {
                _completed.OnNext((InventoryWindowCommand.Drop, item, command.Result, true, 0, null));
            }

            return command.Result;
        }

        public void Dispose() => _completed.Dispose();
    }
}

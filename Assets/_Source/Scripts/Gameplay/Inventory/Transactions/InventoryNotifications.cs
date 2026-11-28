using System;
using System.Collections.Generic;

namespace RaidIntoDarkness.Inventory.Domain
{
    internal sealed class InventoryNotifications
    {
        private readonly List<InventoryChange> _pendingChanges = new();
        private readonly List<EquipEvent> _pendingEquipEvents = new();
        private readonly List<(OutfitSlotRules Rules, ItemInstance Item, int Amount)> _pendingDrops = new();
        private readonly List<(int EquipEnd, int ChangeEnd, int DropEnd)> _notificationBatches = new();
        private readonly InventoryObservers<Action<InventoryChange>> _changed = new();
        private readonly InventoryObservers<Action<ItemInstance, SlottedContainer, SlotDefinition>> _equipped = new();
        private readonly InventoryObservers<Action<ItemInstance, SlottedContainer, SlotDefinition>> _unequipped = new();

        private bool _dispatching;

        public event Action<InventoryChange> Changed
        {
            add => _changed.Add(value);
            remove => _changed.Remove(value);
        }

        public event Action<ItemInstance, SlottedContainer, SlotDefinition> ItemEquipped
        {
            add => _equipped.Add(value);
            remove => _equipped.Remove(value);
        }

        public event Action<ItemInstance, SlottedContainer, SlotDefinition> ItemUnequipped
        {
            add => _unequipped.Add(value);
            remove => _unequipped.Remove(value);
        }

        public void Add(InventoryChange change) => _pendingChanges.Add(change);
        public void Add(EquipEvent equipEvent) => _pendingEquipEvents.Add(equipEvent);
        public void AddDrop(OutfitSlotRules rules, ItemInstance item, int amount) => _pendingDrops.Add((rules, item, amount));

        public void PublishCommittedBatch()
        {
            var end = (_pendingEquipEvents.Count, _pendingChanges.Count, _pendingDrops.Count);
            if (end == (_notificationBatches.Count == 0 ? default : _notificationBatches[^1]))
            {
                return;
            }

            _notificationBatches.Add(end);
            if (_dispatching)
            {
                return;
            }

            _dispatching = true;
            try
            {
                int equipIndex = 0;
                int changeIndex = 0;
                int dropIndex = 0;
                for (int batchIndex = 0; batchIndex < _notificationBatches.Count; batchIndex++)
                {
                    var batch = _notificationBatches[batchIndex];
                    for (; equipIndex < batch.EquipEnd; equipIndex++)
                    {
                        EquipEvent equipEvent = _pendingEquipEvents[equipIndex];
                        var observers = equipEvent.Equipped ? _equipped : _unequipped;
                        observers.Publish(equipEvent, static (observer, value) => observer(value.Item, value.Container, value.Slot), static value => $"{(value.Equipped ? nameof(ItemEquipped) : nameof(ItemUnequipped))} for item '{value.Item.InstanceId}', container '{value.Container.Id}', slot '{value.Slot.Id}'");
                    }

                    for (; changeIndex < batch.ChangeEnd; changeIndex++)
                    {
                        _changed.Publish(_pendingChanges[changeIndex], static (observer, value) => observer(value), static value => $"{value.Kind} for item '{value.Item?.InstanceId}', container '{value.ContainerId}'");
                    }

                    for (; dropIndex < batch.DropEnd; dropIndex++)
                    {
                        var drop = _pendingDrops[dropIndex];
                        drop.Rules.ReportDrop(drop.Item, drop.Amount);
                    }
                }
            }
            finally
            {
                _pendingEquipEvents.Clear();
                _pendingChanges.Clear();
                _pendingDrops.Clear();
                _notificationBatches.Clear();
                _dispatching = false;
            }
        }
    }
}

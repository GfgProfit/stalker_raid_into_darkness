using System;
using R3;
using RaidIntoDarkness.Player;

namespace RaidIntoDarkness.Inventory.UI
{
    internal sealed class InventoryConditionStatusViewModel
    {
        public InventoryConditionStatusViewModel(Observable<StalkerStatKind> changes, Func<StalkerStatKind, (float Value, float Maximum, float Capacity)> read)
        {
            Values = changes.Select(kind => Project(kind, read(kind)));
        }

        public Observable<(string Id, float Current, float Maximum)> Values { get; }

        internal static (string Id, float Current, float Maximum) Project(StalkerStatKind kind, (float Value, float Maximum, float Capacity) value)
        {
            if (float.IsFinite(value.Capacity) == false || value.Capacity <= 0f)
            {
                return default;
            }

            string id = kind switch
            {
                StalkerStatKind.Health => InventoryWindowController.STATUS_HEALTH,
                StalkerStatKind.Stamina => InventoryWindowController.STATUS_POWER,
                StalkerStatKind.Hunger => InventoryWindowController.STATUS_HUNGER,
                StalkerStatKind.Thirst => InventoryWindowController.STATUS_THIRST,
                StalkerStatKind.Sleep => InventoryWindowController.STATUS_SLEEP,
                _ => null
            };
            return (id, value.Value / value.Capacity, value.Maximum / value.Capacity);
        }
    }
}

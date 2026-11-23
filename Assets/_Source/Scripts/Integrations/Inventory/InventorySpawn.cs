using System;
using System.Globalization;
using Inventory;
using Inventory.UI;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory
{
    internal static class InventorySpawn
    {
        public static int Grant(PlayerInventoryRuntime inventory, InventoryWindow window, ItemDefinition definition, int quantity, int condition, out InventoryFailure failure)
        {
            InventoryModel model = window != null ? window.Model : null;
            if (inventory == null || !inventory.isActiveAndEnabled || inventory.IsRestoring || model == null || model != inventory.Model || model.GetContainer(window.PrimaryContainerId) == null)
            {
                failure = InventoryFailure.ContainerNotFound;
                return 0;
            }
            if (definition == null || quantity < 1)
            {
                failure = InventoryFailure.InvalidItem;
                return 0;
            }

            string conditionState = condition < 100 ? (Math.Clamp(condition, 0, 100) / 100f).ToString("R", CultureInfo.InvariantCulture) : null;
            int added = 0;
            failure = InventoryFailure.None;
            while (added < quantity)
            {
                ItemInstance item;
                try
                {
                    item = model.CreateItem(definition, Math.Min(quantity - added, definition.MaxStack));
                    if (conditionState != null)
                    {
                        item.SetState(EquipmentStatsFormatter.CONDITION_STATE, conditionState);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, inventory);
                    failure = InventoryFailure.InvalidItem;
                    break;
                }

                InventoryResult result = model.TryAddAuto(item, window.PrimaryContainerId);
                if (!result.Success)
                {
                    failure = result.Failure;
                    break;
                }
                added += result.Amount;
            }

            return added;
        }
    }
}

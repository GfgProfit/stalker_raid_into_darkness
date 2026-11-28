using RaidIntoDarkness.Inventory.Domain;
using System;
using System.Globalization;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Application
{
    internal static class InventorySpawn
    {
        public static int Grant(InventoryModel model, string containerId, ItemDefinition definition, int quantity, int condition, out InventoryFailure failure)
        {
            if (model?.GetContainer(containerId) == null)
            {
                failure = InventoryFailure.ContainerNotFound;
                return 0;
            }
            if (definition == null || quantity < 1)
            {
                failure = InventoryFailure.InvalidItem;
                return 0;
            }

            string conditionState = condition < 100 && !definition.IsCategory("Grenade") ? (Math.Clamp(condition, 0, 100) / 100f).ToString("R", CultureInfo.InvariantCulture) : null;
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
                        item.SetState(AnomalyEquipmentProtection.CONDITION_STATE, conditionState);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    failure = InventoryFailure.InvalidItem;
                    break;
                }

                InventoryResult result = model.TryAddAuto(item, containerId);
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

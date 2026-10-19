using System;
using System.Collections.Generic;
using UnityEngine;

namespace Inventory
{
    [Serializable]
    public sealed class InventoryStateEntryData
    {
        public string Key;
        public string Value;
    }

    [Serializable]
    public sealed class InventoryItemData
    {
        public string InstanceId;
        public string DefinitionId;
        public int Amount;
        public int X;
        public int Y;
        public bool Rotated;
        public string SlotId;
        public List<string> VisualVariant = new();
        public List<InventoryStateEntryData> State = new();
    }

    [Serializable]
    public sealed class InventoryContainerData
    {
        public string ContainerId;
        public List<string> LockedSlots = new();
        public List<string> UnlockedSlots = new();
        public List<InventoryItemData> Items = new();
    }

    [Serializable]
    public sealed class InventorySaveData
    {
        public const int CURRENT_VERSION = 1;

        public int Version = CURRENT_VERSION;
        public List<InventoryContainerData> Containers = new();
        public List<InventoryItemData> Unresolved = new();
    }

    public sealed class InventoryLoadReport
    {
        public readonly List<InventoryItemData> UnknownDefinitions = new();
        public readonly List<InventoryItemData> UnplacedItems = new();
        public readonly List<string> MissingContainers = new();
        public int LoadedItems;
        public bool IsClean => UnknownDefinitions.Count == 0 && UnplacedItems.Count == 0 && MissingContainers.Count == 0;
    }

    public static class InventorySerializer
    {
        public static InventorySaveData Capture(InventoryModel model, IEnumerable<string> containerIds = null)
        {
            var data = new InventorySaveData();
            var filter = containerIds == null ? null : new HashSet<string>(containerIds, StringComparer.Ordinal);

            foreach (InventoryContainer container in model.Containers)
            {
                if (filter != null && filter.Contains(container.Id) == false)
                {
                    continue;
                }

                var containerData = new InventoryContainerData { ContainerId = container.Id };

                if (container is SlottedContainer slotted)
                {
                    foreach (SlotDefinition slot in slotted.Slots)
                    {
                        bool locked = slotted.IsSlotLocked(slot.Id);

                        if (locked && slot.Rule.StartsLocked == false)
                        {
                            containerData.LockedSlots.Add(slot.Id);
                        }
                        else if (locked == false && slot.Rule.StartsLocked)
                        {
                            containerData.UnlockedSlots.Add(slot.Id);
                        }
                    }
                }

                foreach (ItemInstance item in container.Items)
                {
                    containerData.Items.Add(CaptureItem(item));
                }

                data.Containers.Add(containerData);
            }

            data.Unresolved.AddRange(model.UnresolvedItems);
            return data;
        }

        public static string ToJson(InventoryModel model, bool prettyPrint = true) => JsonUtility.ToJson(Capture(model), prettyPrint);

        public static InventoryLoadReport Restore(InventoryModel model, InventorySaveData data, bool clearExisting = true)
        {
            var report = new InventoryLoadReport();

            if (data == null)
            {
                return report;
            }

            if (data.Version > InventorySaveData.CURRENT_VERSION)
            {
                Debug.LogWarning($"Inventory save version {data.Version} is newer than supported version {InventorySaveData.CURRENT_VERSION}; unknown fields are ignored.");
            }

            if (clearExisting)
            {
                model.Clear();
            }

            if (data.Unresolved != null)
            {
                report.UnknownDefinitions.AddRange(data.Unresolved);
            }

            foreach (InventoryContainerData containerData in data.Containers)
            {
                InventoryContainer container = model.GetContainer(containerData.ContainerId);

                if (container == null)
                {
                    report.MissingContainers.Add(containerData.ContainerId);
                    report.UnplacedItems.AddRange(containerData.Items);
                    continue;
                }

                if (container is SlottedContainer slotted)
                {
                    foreach (string slotId in containerData.LockedSlots)
                    {
                        model.RestoreSlotLock(slotted, slotId, true);
                    }

                    foreach (string slotId in containerData.UnlockedSlots)
                    {
                        model.RestoreSlotLock(slotted, slotId, false);
                    }
                }

                foreach (InventoryItemData itemData in SortForRestore(container, containerData.Items))
                {
                    if (model.Resolver == null || model.Resolver.TryResolve(itemData.DefinitionId, out ItemDefinition definition) == false)
                    {
                        report.UnknownDefinitions.Add(itemData);
                        continue;
                    }

                    if (model.GetItem(itemData.InstanceId) != null)
                    {
                        report.UnplacedItems.Add(itemData);
                        continue;
                    }

                    var item = new ItemInstance(definition, itemData.Amount, itemData.InstanceId);
                    item.SetVisualVariant(itemData.VisualVariant);

                    foreach (InventoryStateEntryData entry in itemData.State)
                    {
                        item.SetState(entry.Key, entry.Value);
                    }

                    if (model.RestoreItem(item, container, new Vector2Int(itemData.X, itemData.Y), itemData.Rotated))
                    {
                        report.LoadedItems++;
                    }
                    else
                    {
                        report.UnplacedItems.Add(itemData);
                    }
                }
            }

            model.AddUnresolved(report.UnknownDefinitions);
            return report;
        }

        public static InventoryLoadReport FromJson(InventoryModel model, string json, bool clearExisting = true)
        {
            InventorySaveData data = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<InventorySaveData>(json);
            return Restore(model, data, clearExisting);
        }

        public static InventoryItemData CaptureItem(ItemInstance item)
        {
            var itemData = new InventoryItemData
            {
                InstanceId = item.InstanceId,
                DefinitionId = item.DefinitionId,
                Amount = item.Amount,
                X = item.Location.Position.x,
                Y = item.Location.Position.y,
                Rotated = item.Location.Rotated,
                SlotId = item.Location.SlotId
            };

            itemData.VisualVariant.AddRange(item.VisualVariant);

            foreach (KeyValuePair<string, string> pair in item.State)
            {
                itemData.State.Add(new InventoryStateEntryData { Key = pair.Key, Value = pair.Value });
            }

            itemData.State.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
            return itemData;
        }

        private static IEnumerable<InventoryItemData> SortForRestore(InventoryContainer container, List<InventoryItemData> items)
        {
            if (container is not SlottedContainer slotted)
            {
                return items;
            }

            var sorted = new List<InventoryItemData>(items);
            sorted.Sort((left, right) => GetDependencyDepth(slotted, left.SlotId).CompareTo(GetDependencyDepth(slotted, right.SlotId)));
            return sorted;
        }

        private static int GetDependencyDepth(SlottedContainer container, string slotId)
        {
            SlotDefinition slot = container.GetSlot(slotId);
            return slot == null ? 0 : slot.Rule.RequiresOccupiedSlots.Count;
        }
    }
}

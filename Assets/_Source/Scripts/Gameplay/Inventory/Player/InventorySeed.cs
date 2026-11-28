using RaidIntoDarkness.Inventory.Domain;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Application
{
    [CreateAssetMenu(menuName = "Raid Into Darkness/Inventory/Inventory Seed", fileName = "InventorySeed")]
    public sealed class InventorySeed : ScriptableObject
    {
        [SerializeField] private List<InventorySeedEntry> _entries = new();

        public IReadOnlyList<InventorySeedEntry> Entries => _entries;

        public void SetEntries(IEnumerable<InventorySeedEntry> entries) => _entries = new List<InventorySeedEntry>(entries);

        public int Apply(InventoryModel model)
        {
            int added = 0;

            foreach (InventorySeedEntry entry in _entries)
            {
                if (entry.Definition == null)
                {
                    continue;
                }

                ItemInstance item = model.CreateItem(entry.Definition, entry.Amount);
                var variant = new List<string>();

                foreach (ItemDefinition part in entry.VisualVariant)
                {
                    if (part != null)
                    {
                        variant.Add(part.DefinitionId);
                    }
                }

                if (variant.Count > 0)
                {
                    item.SetVisualVariant(variant);
                }

                if (entry.Condition < 1f && !entry.Definition.IsCategory("Grenade"))
                {
                    item.SetState(AnomalyEquipmentProtection.CONDITION_STATE, entry.Condition.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                InventoryResult result = entry.AutoPlace ? model.TryAddAuto(item, entry.ContainerId, false) : model.TryAdd(item, entry.ContainerId, entry.Position, entry.Rotated);

                if (result.Success)
                {
                    added++;
                }
                else
                {
                    Debug.LogWarning($"Inventory seed could not place {entry.Definition.DisplayName} x{entry.Amount} into '{entry.ContainerId}' at {entry.Position}: {result.Failure}.", this);
                }
            }

            return added;
        }
    }
}

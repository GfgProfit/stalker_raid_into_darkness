using RaidIntoDarkness.Inventory.Domain;
using System.Collections.Generic;
using System.Linq;
using RaidIntoDarkness.Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Tests
{
    public sealed class InventoryEquipmentConfigurationTests
    {
        private const string EQUIPMENT_LAYOUT = "Assets/_Source/Settings/Inventory/Layouts/EquipmentLayout.asset";
        private const string PISTOL_DEFINITION = "Assets/_Source/Resources/Items/Weapons/Pistols/wpn_apb/wpn_pm_actor_definition.asset";

        [Test]
        public void EquipmentLayoutBindsEverySlotAndPreservesItsBounds()
        {
            var layout = AssetDatabase.LoadAssetAtPath<InventorySlotLayout>(EQUIPMENT_LAYOUT);
            Assert.That(layout, Is.Not.Null);
            Assert.That(layout.TryBuildSlots(out List<SlotDefinition> slots, out int width, out int height, out string error), Is.True, error);
            var expected = new Dictionary<SlotId, RectInt>
            {
                { SlotId.Head, new RectInt(0, 0, 2, 2) },
                { SlotId.Device, new RectInt(2, 0, 2, 1) },
                { SlotId.Pda, new RectInt(4, 0, 2, 1) },
                { SlotId.Outfit, new RectInt(2, 1, 2, 3) },
                { SlotId.Detector, new RectInt(4, 1, 2, 1) },
                { SlotId.Backpack, new RectInt(0, 2, 2, 2) },
                { SlotId.Binocular, new RectInt(4, 2, 2, 1) },
                { SlotId.Knife, new RectInt(4, 3, 2, 1) },
                { SlotId.Primary, new RectInt(0, 4, 5, 2) },
                { SlotId.Secondary, new RectInt(0, 6, 5, 2) },
                { SlotId.QuickSlot1, new RectInt(0, 8, 1, 1) },
                { SlotId.QuickSlot2, new RectInt(1, 8, 1, 1) },
                { SlotId.QuickSlot3, new RectInt(2, 8, 1, 1) },
                { SlotId.QuickSlot4, new RectInt(3, 8, 1, 1) },
                { SlotId.Belt1, new RectInt(0, 9, 1, 1) },
                { SlotId.Belt2, new RectInt(1, 9, 1, 1) },
                { SlotId.Belt3, new RectInt(2, 9, 1, 1) },
                { SlotId.Belt4, new RectInt(3, 9, 1, 1) },
                { SlotId.Belt5, new RectInt(4, 9, 1, 1) }
            };

            Assert.That(width, Is.EqualTo(6));
            Assert.That(height, Is.EqualTo(10));
            Assert.That(slots.ToDictionary(slot => slot.Id, slot => slot.Bounds), Is.EqualTo(expected));
            Assert.That(slots.Where(slot => slot.Rule.StartsLocked).Select(slot => slot.Id), Is.EquivalentTo(new[] { SlotId.Belt1, SlotId.Belt2, SlotId.Belt3, SlotId.Belt4, SlotId.Belt5 }));
        }

        [Test]
        public void PistolDefinitionPreservesAuthoredEquipPriority()
        {
            var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(PISTOL_DEFINITION);
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.TryGetComponent(out EquippableItemComponent equippable), Is.True);
            Assert.That(equippable.PreferredSlotIds, Is.EqualTo(new[] { SlotId.Secondary, SlotId.Knife, SlotId.Primary }));
        }
    }
}

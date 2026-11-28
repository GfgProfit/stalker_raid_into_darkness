using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Core.Tests
{
    public sealed class SlottedContainerTests
    {
        private const string MASK = "HH.AA.PP\nHH.AA.SS\n12.V....";

        private InventoryTestItems _items;
        private InventoryModel _model;
        private SlottedContainer _equipment;
        private ItemDefinition _helmet;
        private ItemDefinition _armor;
        private ItemDefinition _rifle;
        private ItemDefinition _pistol;
        private ItemDefinition _medkit;

        [SetUp]
        public void SetUp()
        {
            _items = new InventoryTestItems();
            _helmet = _items.Create("helmet", new Vector2Int(2, 2), 1, 1f, "Helmet");
            _armor = _items.Create("armor", new Vector2Int(2, 3), 1, 6f, "Armor");
            _rifle = _items.Create("rifle", new Vector2Int(4, 2), 1, 3.5f, "Weapon");
            _pistol = _items.Create("pistol", new Vector2Int(2, 1), 1, 1f, "Weapon", true, "Sidearm");
            _medkit = _items.Create("medkit", Vector2Int.one, 5, 0.2f, "Medical");
            _model = new InventoryModel(_items.Database);
            _model.AddContainer(new GridContainer("Backpack", 10, 8, "Player"));
            InventorySlotLayout layout = _items.Track(ScriptableObject.CreateInstance<InventorySlotLayout>());
            layout.SetData(MASK, CreateBindings());
            _equipment = _model.AddContainer(SlottedContainer.FromLayout("Equipment", layout, true, "Player"));
        }

        [TearDown]
        public void TearDown()
        {
            _items.Dispose();
        }

        [Test]
        public void MaskBuildsRectangularSlots()
        {
            Assert.That(_equipment.Width, Is.EqualTo(8));
            Assert.That(_equipment.Height, Is.EqualTo(3));
            Assert.That(_equipment.Slots.Count, Is.EqualTo(7));
            Assert.That(_equipment.GetSlot(SlotId.Head).Bounds, Is.EqualTo(new RectInt(0, 0, 2, 2)));
            Assert.That(_equipment.GetSlot(SlotId.Outfit).Bounds, Is.EqualTo(new RectInt(3, 0, 2, 2)));
            Assert.That(_equipment.GetSlot(SlotId.Primary).Bounds, Is.EqualTo(new RectInt(6, 0, 2, 1)));
            Assert.That(_equipment.GetSlotAt(new Vector2Int(1, 1)).Id, Is.EqualTo(SlotId.Head));
            Assert.That(_equipment.GetSlotAt(new Vector2Int(2, 0)), Is.Null);
            Assert.That(_equipment.GetSlot(SlotId.Device).Id, Is.EqualTo(SlotId.Device));
        }

        [TestCase("AA\nA.", "filled rectangle")]
        [TestCase("AAA\nABA", "filled rectangle")]
        [TestCase("...\n...", "no slots")]
        public void InvalidMasksAreRejected(string mask, string expectedError)
        {
            bool parsed = SlotMaskParser.TryParse(mask, null, out _, out _, out _, out string error);

            Assert.That(parsed, Is.False);
            StringAssert.Contains(expectedError, error);
        }

        [Test]
        public void DuplicateSlotIdsAreRejected()
        {
            var bindings = new List<SlotLayoutBinding>
            {
                new('A', SlotId.Head, "A", SlotMode.Single, null),
                new('B', SlotId.Head, "B", SlotMode.Single, null)
            };

            Assert.That(SlotMaskParser.TryParse("AB", bindings, out _, out _, out _, out string error), Is.False);
            StringAssert.Contains("more than one", error);
        }

        [Test]
        public void MaskRequiresOneExplicitBindingPerKey()
        {
            SlotLayoutBinding[] bindings = { new('A', SlotId.Head, "A", SlotMode.Single, null) };

            Assert.That(SlotMaskParser.TryParse("AB", bindings, out _, out _, out _, out string missingError), Is.False);
            StringAssert.Contains("explicit slot binding", missingError);

            SlotLayoutBinding[] repeated = { bindings[0], new('A', SlotId.Outfit, "B", SlotMode.Single, null) };

            Assert.That(SlotMaskParser.TryParse("A", repeated, out _, out _, out _, out string duplicateError), Is.False);
            StringAssert.Contains("more than one slot binding", duplicateError);
        }

        [TestCase(SlotId.None)]
        [TestCase((SlotId)999)]
        public void BothCreationPathsRejectInvalidIds(SlotId id)
        {
            SlotLayoutBinding[] bindings = { new('A', id, "A", SlotMode.Single, null) };
            SlotDefinition[] definitions = { new(id, "A", new RectInt(0, 0, 1, 1), SlotMode.Single, null) };

            Assert.That(SlotMaskParser.TryParse("A", bindings, out _, out _, out _, out string error), Is.False);
            StringAssert.Contains("invalid", error);
            Assert.That(() => new SlottedContainer("Other", definitions, 1, 1, false), Throws.TypeOf<ArgumentException>().With.Message.Contains(error));
        }

        [Test]
        public void ProgrammaticCreationRejectsRepeatedIdsWithinAContainer()
        {
            var left = new SlotDefinition(SlotId.Head, "left", new RectInt(0, 0, 1, 1), SlotMode.Single, null);
            var right = new SlotDefinition(SlotId.Head, "right", new RectInt(1, 0, 1, 1), SlotMode.Single, null);

            Assert.That(() => new SlottedContainer("Other", new[] { left, right }, 2, 1, false), Throws.TypeOf<ArgumentException>().With.Message.Contains("more than one"));
            var other = new SlottedContainer("Other", new[] { left }, 1, 1, false);
            Assert.That(other.GetSlot(SlotId.Head), Is.SameAs(left));
            Assert.That(_equipment.GetSlot(SlotId.Head), Is.Not.SameAs(left));
        }

        [Test]
        public void BothCreationPathsRejectMissingRequiredSlot()
        {
            SlotRule rule = SlotRule.Create(requiresOccupied: new[] { SlotId.Outfit });
            SlotLayoutBinding[] bindings = { new('A', SlotId.Head, "A", SlotMode.Single, rule) };
            SlotDefinition[] definitions = { new(SlotId.Head, "A", new RectInt(0, 0, 1, 1), SlotMode.Single, rule) };

            Assert.That(SlotMaskParser.TryParse("A", bindings, out _, out _, out _, out string error), Is.False);
            StringAssert.Contains("depends on invalid slot", error);
            Assert.That(() => new SlottedContainer("Other", definitions, 1, 1, false), Throws.TypeOf<ArgumentException>().With.Message.Contains(error));
        }

        [Test]
        public void SingleSlotTakesWholeSlotAndRejectsWrongCategory()
        {
            ItemInstance helmet = _model.CreateItem(_helmet);
            ItemInstance rifle = _model.CreateItem(_rifle);
            _model.TryAdd(rifle, "Backpack", Vector2Int.zero, false);

            Assert.That(_model.TryAdd(helmet, "Equipment", new Vector2Int(1, 1), true).Success, Is.True);
            Assert.That(helmet.Location.SlotId, Is.EqualTo(SlotId.Head));
            Assert.That(helmet.Location.Position, Is.EqualTo(Vector2Int.zero));
            Assert.That(helmet.Location.Rotated, Is.False);
            Assert.That(_model.TryAdd(_model.CreateItem(_helmet), "Equipment", Vector2Int.one, false).Failure, Is.EqualTo(InventoryFailure.Occupied));
            Assert.That(_model.TryMove(rifle.InstanceId, "Equipment", new Vector2Int(3, 0), false).Failure, Is.EqualTo(InventoryFailure.SlotRejected));
            Assert.That(_model.TryMove(rifle.InstanceId, "Equipment", new Vector2Int(2, 0), false).Failure, Is.EqualTo(InventoryFailure.NoSlot));
            Assert.That(_model.TryMove(rifle.InstanceId, "Equipment", new Vector2Int(7, 0), false).Success, Is.True);
            Assert.That(_equipment.GetSlotOccupant(SlotId.Primary), Is.SameAs(rifle));
        }

        [Test]
        public void TagRuleRequiresTag()
        {
            ItemInstance rifle = _model.CreateItem(_rifle);
            ItemInstance pistol = _model.CreateItem(_pistol);
            _model.TryAdd(rifle, "Backpack", Vector2Int.zero, false);
            _model.TryAdd(pistol, "Backpack", new Vector2Int(0, 3), false);

            Assert.That(_model.TryMove(rifle.InstanceId, "Equipment", new Vector2Int(6, 1), false).Failure, Is.EqualTo(InventoryFailure.SlotRejected));
            Assert.That(_model.TryMove(pistol.InstanceId, "Equipment", new Vector2Int(6, 1), false).Success, Is.True);
        }

        [Test]
        public void DependentSlotRequiresAndBlocksRelease()
        {
            ItemInstance armor = _model.CreateItem(_armor);
            ItemInstance medkit = _model.CreateItem(_medkit, 2);
            _model.TryAdd(armor, "Backpack", Vector2Int.zero, false);
            _model.TryAdd(medkit, "Backpack", new Vector2Int(5, 0), false);

            Assert.That(_equipment.IsSlotAvailable(SlotId.Belt1), Is.False);
            Assert.That(_model.TryMove(medkit.InstanceId, "Equipment", new Vector2Int(0, 2), false).Failure, Is.EqualTo(InventoryFailure.SlotUnavailable));
            Assert.That(_model.TryEquip(armor.InstanceId, "Equipment").Success, Is.True);
            Assert.That(_equipment.IsSlotAvailable(SlotId.Belt1), Is.True);
            Assert.That(_model.TryMove(medkit.InstanceId, "Equipment", new Vector2Int(0, 2), false).Success, Is.True);
            Assert.That(_model.TryTransfer(armor.InstanceId, "Backpack").Failure, Is.EqualTo(InventoryFailure.DependentSlotOccupied));
            Assert.That(_model.TryRemove(armor.InstanceId).Failure, Is.EqualTo(InventoryFailure.DependentSlotOccupied));
            Assert.That(armor.ContainerId, Is.EqualTo("Equipment"));
        }

        [Test]
        public void BlockedSlotIsUnavailableWhileBlockerIsOccupied()
        {
            ItemInstance helmet = _model.CreateItem(_helmet);
            ItemInstance pistol = _model.CreateItem(_pistol);
            _model.TryAdd(helmet, "Equipment", Vector2Int.zero, false);
            _model.TryAdd(pistol, "Backpack", Vector2Int.zero, false);

            Assert.That(_equipment.IsSlotAvailable(SlotId.Secondary), Is.True);
            Assert.That(_model.TryAdd(_model.CreateItem(_rifle), "Equipment", new Vector2Int(6, 0), false).Success, Is.True);
            Assert.That(_equipment.IsSlotAvailable(SlotId.Secondary), Is.False);
            Assert.That(_model.TryMove(pistol.InstanceId, "Equipment", new Vector2Int(6, 1), false).Failure, Is.EqualTo(InventoryFailure.SlotUnavailable));
        }

        [Test]
        public void LockedSlotCanBeUnlockedAndCannotBeLockedWhileOccupied()
        {
            ItemInstance medkit = _model.CreateItem(_medkit);
            _model.TryAdd(medkit, "Backpack", Vector2Int.zero, false);
            var changes = new List<InventoryChange>();
            _model.Changed += changes.Add;

            Assert.That(_equipment.IsSlotLocked(SlotId.Belt2), Is.True);
            Assert.That(_model.TryMove(medkit.InstanceId, "Equipment", new Vector2Int(1, 2), false).Failure, Is.EqualTo(InventoryFailure.SlotUnavailable));
            Assert.That(_model.TrySetSlotLocked("Equipment", SlotId.Belt2, false).Success, Is.True);
            Assert.That(changes.Exists(change => change.Kind == InventoryChangeKind.SlotStateChanged && change.SlotId == SlotId.Belt2), Is.True);
            Assert.That(_model.TryMove(medkit.InstanceId, "Equipment", new Vector2Int(1, 2), false).Success, Is.True);
            Assert.That(_model.TrySetSlotLocked("Equipment", SlotId.Belt2, true).Failure, Is.EqualTo(InventoryFailure.Occupied));
        }

        [Test]
        public void SlotValidatorIsConsulted()
        {
            ItemInstance medkit = _model.CreateItem(_medkit);
            _model.TryAdd(medkit, "Backpack", Vector2Int.zero, false);
            var validator = new ToggleSlotValidator();
            _model.RegisterSlotValidator("custom", validator);

            Assert.That(_model.TryMove(medkit.InstanceId, "Equipment", new Vector2Int(3, 2), false).Failure, Is.EqualTo(InventoryFailure.ValidatorRejected));
            validator.Allow = true;
            Assert.That(_model.TryMove(medkit.InstanceId, "Equipment", new Vector2Int(3, 2), false).Success, Is.True);
        }

        [Test]
        public void EquipEventsAreRaised()
        {
            ItemInstance helmet = _model.CreateItem(_helmet);
            _model.TryAdd(helmet, "Backpack", Vector2Int.zero, false);
            SlotId equippedSlot = SlotId.None;
            SlotId unequippedSlot = SlotId.None;
            _model.ItemEquipped += (item, container, slot) => equippedSlot = slot.Id;
            _model.ItemUnequipped += (item, container, slot) => unequippedSlot = slot.Id;

            Assert.That(_model.TryEquip(helmet.InstanceId, "Equipment").Success, Is.True);
            Assert.That(equippedSlot, Is.EqualTo(SlotId.Head));
            Assert.That(_model.TryTransfer(helmet.InstanceId, "Backpack").Success, Is.True);
            Assert.That(unequippedSlot, Is.EqualTo(SlotId.Head));
        }

        [Test]
        public void OtherOccupantsKeepAGridSlotDependencySatisfied()
        {
            SlotDefinition[] slots =
            {
                new(SlotId.Belt1, "Belt", new RectInt(0, 0, 2, 1), SlotMode.Grid, null),
                new(SlotId.QuickSlot1, "Quick", new RectInt(2, 0, 1, 1), SlotMode.Single, SlotRule.Create(requiresOccupied: new[] { SlotId.Belt1 }))
            };
            var container = _model.AddContainer(new SlottedContainer("GridWithDependency", slots, 3, 1, false));
            ItemInstance first = _model.CreateItem(_medkit);
            ItemInstance second = _model.CreateItem(_medkit);
            ItemInstance dependent = _model.CreateItem(_medkit);
            Assert.That(_model.TryAdd(first, container.Id, Vector2Int.zero, false).Success, Is.True);
            Assert.That(_model.TryAdd(second, container.Id, Vector2Int.right, false).Success, Is.True);
            Assert.That(_model.TryAdd(dependent, container.Id, new Vector2Int(2, 0), false).Success, Is.True);

            Assert.That(_model.TryRemove(first.InstanceId).Success, Is.True);
            Assert.That(container.GetSlotOccupant(SlotId.Belt1), Is.SameAs(second));
            Assert.That(_model.TryRemove(second.InstanceId).Failure, Is.EqualTo(InventoryFailure.DependentSlotOccupied));
        }

        [Test]
        public void RejectedSwapKeepsBothItemsPlacedAndRaisesNoEvents()
        {
            ItemInstance displaced = _model.CreateItem(_rifle);
            ItemInstance incoming = _model.CreateItem(_rifle);
            Vector2Int slot = _equipment.GetSlot(SlotId.Primary).Bounds.position;
            Assert.That(_model.TryAdd(displaced, _equipment.Id, slot, false).Success, Is.True);
            Assert.That(_model.TryAdd(incoming, "Backpack", Vector2Int.zero, false).Success, Is.True);
            _model.AddPlacementValidator(new TestPlacementValidator((item, container, _) => container != _equipment));
            var changes = new List<InventoryChange>();
            _model.Changed += changes.Add;

            Assert.That(_model.CanSwapIntoSlot(incoming, _equipment.Id, slot, "Backpack"), Is.False);
            Assert.That(_model.TrySwapIntoSlot(incoming, _equipment.Id, slot, "Backpack").Failure, Is.EqualTo(InventoryFailure.ValidatorRejected));
            Assert.That(_equipment.GetSlotOccupant(SlotId.Primary), Is.SameAs(displaced));
            Assert.That(_model.GetContainer("Backpack").GetItemAt(Vector2Int.zero), Is.SameAs(incoming));
            Assert.That(changes, Is.Empty);
        }

        [Test]
        public void SwapRaisesChangesAfterTheReplacementIsCommitted()
        {
            ItemInstance displaced = _model.CreateItem(_rifle);
            ItemInstance incoming = _model.CreateItem(_rifle);
            Vector2Int slot = _equipment.GetSlot(SlotId.Primary).Bounds.position;
            Assert.That(_model.TryAdd(displaced, _equipment.Id, slot, false).Success, Is.True);
            Assert.That(_model.TryAdd(incoming, "Backpack", Vector2Int.zero, false).Success, Is.True);
            var seen = new List<ItemInstance>();
            _model.ItemUnequipped += (_, container, definition) => seen.Add(container.GetSlotOccupant(definition.Id));

            Assert.That(_model.CanSwapIntoSlot(incoming, _equipment.Id, slot, "Backpack"), Is.True);
            Assert.That(_equipment.GetSlotOccupant(SlotId.Primary), Is.SameAs(displaced));
            Assert.That(_model.TryEquipItem(incoming, _equipment.Id, SlotId.Primary, "Backpack").Success, Is.True);
            Assert.That(_equipment.GetSlotOccupant(SlotId.Primary), Is.SameAs(incoming));
            Assert.That(displaced.ContainerId, Is.EqualTo("Backpack"));
            Assert.That(seen, Is.EqualTo(new[] { incoming }));
        }

        [Test]
        public void SwapWithinSameContainerReturnsTheDisplacedItemToTheFormerSlot()
        {
            SlotDefinition[] slots =
            {
                new(SlotId.Primary, "Primary", new RectInt(0, 0, 1, 1), SlotMode.Single, null),
                new(SlotId.Secondary, "Secondary", new RectInt(1, 0, 1, 1), SlotMode.Single, null)
            };
            var target = _model.AddContainer(new SlottedContainer("SwapSlots", slots, 2, 1, true));
            ItemInstance displaced = _model.CreateItem(_rifle);
            ItemInstance incoming = _model.CreateItem(_rifle);
            Assert.That(_model.TryAdd(displaced, target.Id, Vector2Int.zero, false).Success, Is.True);
            Assert.That(_model.TryAdd(incoming, target.Id, Vector2Int.right, false).Success, Is.True);

            Assert.That(_model.TrySwapIntoSlot(incoming, target.Id, Vector2Int.zero, "Backpack").Success, Is.True);
            Assert.That(target.GetSlotOccupant(SlotId.Primary), Is.SameAs(incoming));
            Assert.That(target.GetSlotOccupant(SlotId.Secondary), Is.SameAs(displaced));
            Assert.That(_model.GetContainer("Backpack").Items, Is.Empty);
        }

        private static List<SlotLayoutBinding> CreateBindings()
        {
            return new List<SlotLayoutBinding>
            {
                new('H', SlotId.Head, "Head", SlotMode.Single, SlotRule.Create(new[] { "Helmet" })),
                new('A', SlotId.Outfit, "Armor", SlotMode.Single, SlotRule.Create(new[] { "Armor" })),
                new('P', SlotId.Primary, "Primary", SlotMode.Single, SlotRule.Create(new[] { "Weapon" })),
                new('S', SlotId.Secondary, "Secondary", SlotMode.Single, SlotRule.Create(new[] { "Weapon" }, new[] { "Sidearm" }, null, null, null, new[] { SlotId.Primary })),
                new('1', SlotId.Belt1, "Pocket 1", SlotMode.Single, SlotRule.Create(null, null, null, null, new[] { SlotId.Outfit })),
                new('2', SlotId.Belt2, "Pocket 2", SlotMode.Grid, SlotRule.Create(startsLocked: true)),
                new('V', SlotId.Device, "Validated", SlotMode.Single, SlotRule.Create(validatorId: "custom"))
            };
        }
    }
}

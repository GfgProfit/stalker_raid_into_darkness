using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Inventory.Tests
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
            Assert.That(_equipment.GetSlot("Head").Bounds, Is.EqualTo(new RectInt(0, 0, 2, 2)));
            Assert.That(_equipment.GetSlot("Armor").Bounds, Is.EqualTo(new RectInt(3, 0, 2, 2)));
            Assert.That(_equipment.GetSlot("Primary").Bounds, Is.EqualTo(new RectInt(6, 0, 2, 1)));
            Assert.That(_equipment.GetSlotAt(new Vector2Int(1, 1)).Id, Is.EqualTo("Head"));
            Assert.That(_equipment.GetSlotAt(new Vector2Int(2, 0)), Is.Null);
            Assert.That(_equipment.GetSlot("V").Id, Is.EqualTo("V"));
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
                new('A', "Same", "A", SlotMode.Single, null),
                new('B', "Same", "B", SlotMode.Single, null)
            };

            Assert.That(SlotMaskParser.TryParse("AB", bindings, out _, out _, out _, out string error), Is.False);
            StringAssert.Contains("more than one", error);
        }

        [Test]
        public void SingleSlotTakesWholeSlotAndRejectsWrongCategory()
        {
            ItemInstance helmet = _model.CreateItem(_helmet);
            ItemInstance rifle = _model.CreateItem(_rifle);
            _model.TryAdd(rifle, "Backpack", Vector2Int.zero, false);

            Assert.That(_model.TryAdd(helmet, "Equipment", new Vector2Int(1, 1), true).Success, Is.True);
            Assert.That(helmet.Location.SlotId, Is.EqualTo("Head"));
            Assert.That(helmet.Location.Position, Is.EqualTo(Vector2Int.zero));
            Assert.That(helmet.Location.Rotated, Is.False);
            Assert.That(_model.TryAdd(_model.CreateItem(_helmet), "Equipment", Vector2Int.one, false).Failure, Is.EqualTo(InventoryFailure.Occupied));
            Assert.That(_model.TryMove(rifle.InstanceId, "Equipment", new Vector2Int(3, 0), false).Failure, Is.EqualTo(InventoryFailure.SlotRejected));
            Assert.That(_model.TryMove(rifle.InstanceId, "Equipment", new Vector2Int(2, 0), false).Failure, Is.EqualTo(InventoryFailure.NoSlot));
            Assert.That(_model.TryMove(rifle.InstanceId, "Equipment", new Vector2Int(7, 0), false).Success, Is.True);
            Assert.That(_equipment.GetSlotOccupant("Primary"), Is.SameAs(rifle));
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

            Assert.That(_equipment.IsSlotAvailable("Pocket1"), Is.False);
            Assert.That(_model.TryMove(medkit.InstanceId, "Equipment", new Vector2Int(0, 2), false).Failure, Is.EqualTo(InventoryFailure.SlotUnavailable));
            Assert.That(_model.TryEquip(armor.InstanceId, "Equipment").Success, Is.True);
            Assert.That(_equipment.IsSlotAvailable("Pocket1"), Is.True);
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

            Assert.That(_equipment.IsSlotAvailable("Secondary"), Is.True);
            Assert.That(_model.TryAdd(_model.CreateItem(_rifle), "Equipment", new Vector2Int(6, 0), false).Success, Is.True);
            Assert.That(_equipment.IsSlotAvailable("Secondary"), Is.False);
            Assert.That(_model.TryMove(pistol.InstanceId, "Equipment", new Vector2Int(6, 1), false).Failure, Is.EqualTo(InventoryFailure.SlotUnavailable));
        }

        [Test]
        public void LockedSlotCanBeUnlockedAndCannotBeLockedWhileOccupied()
        {
            ItemInstance medkit = _model.CreateItem(_medkit);
            _model.TryAdd(medkit, "Backpack", Vector2Int.zero, false);
            var changes = new List<InventoryChange>();
            _model.Changed += changes.Add;

            Assert.That(_equipment.IsSlotLocked("Pocket2"), Is.True);
            Assert.That(_model.TryMove(medkit.InstanceId, "Equipment", new Vector2Int(1, 2), false).Failure, Is.EqualTo(InventoryFailure.SlotUnavailable));
            Assert.That(_model.TrySetSlotLocked("Equipment", "Pocket2", false).Success, Is.True);
            Assert.That(changes.Exists(change => change.Kind == InventoryChangeKind.SlotStateChanged && change.SlotId == "Pocket2"), Is.True);
            Assert.That(_model.TryMove(medkit.InstanceId, "Equipment", new Vector2Int(1, 2), false).Success, Is.True);
            Assert.That(_model.TrySetSlotLocked("Equipment", "Pocket2", true).Failure, Is.EqualTo(InventoryFailure.Occupied));
        }

        [Test]
        public void SlotValidatorIsConsulted()
        {
            ItemInstance medkit = _model.CreateItem(_medkit);
            _model.TryAdd(medkit, "Backpack", Vector2Int.zero, false);
            var validator = new ToggleValidator();
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
            string equippedSlot = null;
            string unequippedSlot = null;
            _model.ItemEquipped += (item, container, slot) => equippedSlot = slot.Id;
            _model.ItemUnequipped += (item, container, slot) => unequippedSlot = slot.Id;

            Assert.That(_model.TryEquip(helmet.InstanceId, "Equipment").Success, Is.True);
            Assert.That(equippedSlot, Is.EqualTo("Head"));
            Assert.That(_model.TryTransfer(helmet.InstanceId, "Backpack").Success, Is.True);
            Assert.That(unequippedSlot, Is.EqualTo("Head"));
        }

        private static List<SlotLayoutBinding> CreateBindings()
        {
            return new List<SlotLayoutBinding>
            {
                new('H', "Head", "Head", SlotMode.Single, SlotRule.Create(new[] { "Helmet" })),
                new('A', "Armor", "Armor", SlotMode.Single, SlotRule.Create(new[] { "Armor" })),
                new('P', "Primary", "Primary", SlotMode.Single, SlotRule.Create(new[] { "Weapon" })),
                new('S', "Secondary", "Secondary", SlotMode.Single, SlotRule.Create(new[] { "Weapon" }, new[] { "Sidearm" }, null, null, null, new[] { "Primary" })),
                new('1', "Pocket1", "Pocket 1", SlotMode.Single, SlotRule.Create(null, null, null, null, new[] { "Armor" })),
                new('2', "Pocket2", "Pocket 2", SlotMode.Grid, SlotRule.Create(startsLocked: true)),
                new('V', null, "Validated", SlotMode.Single, SlotRule.Create(validatorId: "custom"))
            };
        }

        private sealed class ToggleValidator : ISlotValidator
        {
            public bool Allow { get; set; }

            public bool CanPlace(ItemInstance item, SlottedContainer container, SlotDefinition slot) => Allow;
        }
    }
}

using NUnit.Framework;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Core.Tests
{
    public sealed class ExpandableGridTests
    {
        private InventoryTestItems _items;

        [SetUp]
        public void SetUp()
        {
            _items = new InventoryTestItems();
        }

        [TearDown]
        public void TearDown()
        {
            _items.Dispose();
        }

        [Test]
        public void AutomaticPlacementUsesEmptyCellsThenGrowsWholeSteps()
        {
            ItemDefinition definition = _items.Create("item", Vector2Int.one, 1, 1f, "Misc");
            var model = new InventoryModel(_items.Database);
            GridContainer backpack = model.AddContainer(new GridContainer("Backpack", 2, 1, "Player", null, 2));
            GridContainer stash = model.AddContainer(new GridContainer("Stash", 2, 1));

            for (int i = 0; i < 7; i++)
            {
                ItemInstance item = model.CreateItem(definition);
                Assert.That(model.TryAddAuto(item, backpack.Id).Success, Is.True);
                Assert.That(item.Location.Position, Is.EqualTo(new Vector2Int(i % 2, i / 2)));
                Assert.That(backpack.Height, Is.EqualTo(i < 2 ? 1 : i < 6 ? 3 : 5));
            }

            Assert.That(model.TryAddAuto(model.CreateItem(definition), stash.Id).Success, Is.True);
            Assert.That(model.TryAddAuto(model.CreateItem(definition), stash.Id).Success, Is.True);
            Assert.That(model.TryAddAuto(model.CreateItem(definition), stash.Id).Failure, Is.EqualTo(InventoryFailure.NoSpace));
            Assert.That(stash.Height, Is.EqualTo(1));
        }

        [Test]
        public void PreviewingTwoReturnedItemsDoesNotGrowTheBackpack()
        {
            ItemDefinition itemDefinition = _items.Create("item", Vector2Int.one, 1, 1f, "Misc");
            ItemDefinition returnedDefinition = _items.Create("returned", Vector2Int.one, 1, 1f, "Misc");
            ItemDefinition stackDefinition = _items.Create("stack", Vector2Int.one, 5, 1f, "Misc");
            var model = new InventoryModel(_items.Database);
            GridContainer backpack = model.AddContainer(new GridContainer("Backpack", 1, 1, "Player", null, 2));
            GridContainer source = model.AddContainer(new GridContainer("Source", 1, 1));
            ItemInstance item = model.CreateItem(itemDefinition);
            ItemInstance returned = model.CreateItem(returnedDefinition);
            ItemInstance stack = model.CreateItem(stackDefinition);
            var updated = new ItemInstance(itemDefinition, item.Amount, item.InstanceId);
            Assert.That(model.TryAddAuto(item, source.Id).Success, Is.True);
            Assert.That(model.TryAddAuto(model.CreateItem(itemDefinition), backpack.Id).Success, Is.True);

            Assert.That(model.TryReconfigureItem(item, updated, Vector2Int.zero, returned: returned, returnContainerId: backpack.Id, apply: false, returnedStack: stack).Success, Is.True);
            Assert.That(backpack.Height, Is.EqualTo(1));
            Assert.That(backpack.Items.Count, Is.EqualTo(1));

            Assert.That(model.TryReconfigureItem(item, updated, Vector2Int.zero, returned: returned, returnContainerId: backpack.Id, returnedStack: stack).Success, Is.True);
            Assert.That(backpack.Height, Is.EqualTo(3));
            Assert.That(backpack.GetItemAt(new Vector2Int(0, 1)), Is.SameAs(returned));
            Assert.That(backpack.GetItemAt(new Vector2Int(0, 2)), Is.SameAs(stack));
        }

        [Test]
        public void ExplicitPlacementBelowTheOriginalGridGrowsWholeSteps()
        {
            ItemDefinition definition = _items.Create("item", Vector2Int.one, 1, 1f, "Misc");
            var model = new InventoryModel(_items.Database);
            GridContainer backpack = model.AddContainer(new GridContainer("Backpack", 2, 1, "Player", null, 2));
            ItemInstance item = model.CreateItem(definition);
            var position = new Vector2Int(1, 7);
            Assert.That(model.TryAdd(item, backpack.Id, position, false).Success, Is.True);

            Assert.That(backpack.Height, Is.EqualTo(9));
            Assert.That(model.GetItem(item.InstanceId).Location.Position, Is.EqualTo(position));
            Assert.That(backpack.GetItemAt(position), Is.SameAs(item));
            Assert.That(backpack.GetItemAt(new Vector2Int(0, 8)), Is.Null);
        }
    }
}

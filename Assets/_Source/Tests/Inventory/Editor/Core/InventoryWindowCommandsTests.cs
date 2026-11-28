using NUnit.Framework;
using RaidIntoDarkness.Inventory.Domain;
using RaidIntoDarkness.Inventory.UI;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Core.Tests
{
    public sealed class InventoryWindowCommandsTests
    {
        [Test]
        public void ImmediateItemCommandsCommitOnceAndPreserveStateOnWorldDropFailure()
        {
            var definitions = new InventoryTestItems();
            var windowObject = new GameObject("InventoryCommandsTest", typeof(RectTransform));
            try
            {
                ItemDefinition consumable = definitions.Create("consumable", Vector2Int.one, maxStack: 8);
                consumable.AddComponent(new ConsumableItemComponent());
                var model = new InventoryModel(definitions.Database);
                GridContainer backpack = model.AddContainer(new GridContainer("Backpack", 4, 1));
                GridContainer stash = model.AddContainer(new GridContainer("Stash", 2, 1));
                ItemInstance stack = model.CreateItem(consumable, 4);
                Assert.That(model.TryAdd(stack, backpack.Id, Vector2Int.zero, false).Success, Is.True);
                using var commands = new InventoryWindowViewModel(model, "Equipment", backpack.Id);
                InventoryWindow window = windowObject.AddComponent<InventoryWindow>();
                window.Bind(model, null);
                int uses = 0;
                window.ItemUsed += _ => uses++;
                int notifications = 0;
                model.Changed += _ => notifications++;

                window.UseItem(stack);
                Assert.That(stack.Amount, Is.EqualTo(3));
                Assert.That(notifications, Is.EqualTo(1));
                Assert.That(uses, Is.EqualTo(1));
                Assert.That(window.LastStatus, Is.Not.Empty);

                commands.Split(stack, 1);
                Assert.That(stack.Amount, Is.EqualTo(2));
                Assert.That(backpack.Items.Count, Is.EqualTo(2));

                commands.Transfer(stack, stash);
                Assert.That(stash.Contains(stack), Is.True);

                int beforeDrop = notifications;
                int worldAttempts = 0;
                commands.WorldDrop = _ =>
                {
                    worldAttempts++;
                    return InventoryResult.Fail(InventoryFailure.NoSpace);
                };
                Assert.That(commands.Drop(stack, false).Failure, Is.EqualTo(InventoryFailure.NoSpace));
                Assert.That(worldAttempts, Is.EqualTo(1));
                Assert.That(notifications, Is.EqualTo(beforeDrop));
                Assert.That(stash.Contains(stack), Is.True);

                commands.WorldDrop = null;
                Assert.That(commands.Drop(stack, false).Success, Is.True);
                Assert.That(model.ContainsItem(stack), Is.False);
                Assert.That(notifications, Is.EqualTo(beforeDrop + 1));
            }
            finally
            {
                Object.DestroyImmediate(windowObject);
                definitions.Dispose();
            }
        }
    }
}


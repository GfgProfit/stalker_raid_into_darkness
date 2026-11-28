using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RaidIntoDarkness.Inventory.Tests
{
    public sealed class WorldItemPickupTests
    {
        [UnityTest]
        public IEnumerator PickupWithoutAWindowRetriesAfterFailureAndRejectsReentryFromAMerge()
        {
            var root = new GameObject(nameof(WorldItemPickupTests));
            var worldObject = new GameObject("Item to pick up");
            var layout = ScriptableObject.CreateInstance<InventorySlotLayout>();
            var definition = ScriptableObject.CreateInstance<ItemDefinition>();

            try
            {
                definition.Configure("stackable", "Misc", Vector2Int.one, 0f, 10, false);
                layout.SetData("H", new[] { new SlotLayoutBinding('H', SlotId.Head, "Head", SlotMode.Single, SlotRule.Create()) });
                var interactor = root.AddComponent<WorldItemInteractor>();
                var runtime = root.AddComponent<PlayerInventoryRuntime>();
                runtime.Configure(new InventoryGridSettings("Bag", "Player", "Bag", 1, 1, 0), new InventoryGridSettings("Stash", "World", "Stash", 1, 1, 0), "Equipment", "Equipment", layout, null, null, null, interactor);
                Assert.That(runtime.EnsureInitialized(), Is.True);

                InventoryModel model = runtime.Model;
                ItemInstance stack = model.CreateItem(definition, 10);
                Assert.That(model.TryAddAuto(stack, "Bag").Success, Is.True);
                var worldItem = worldObject.AddComponent<WorldItem>();
                worldItem.Configure(definition, 2);

                Assert.That(interactor.TryPickUp(worldItem), Is.False);
                Assert.That(worldObject.activeSelf, Is.True);
                Assert.That(stack.Amount, Is.EqualTo(10));
                Assert.That(model.TryRemove(stack.InstanceId, 5).Success, Is.True);

                bool? reentrantPickup = null;
                model.Changed += change =>
                {
                    if (change.Kind == InventoryChangeKind.AmountChanged && change.Item == stack && reentrantPickup.HasValue == false)
                    {
                        reentrantPickup = interactor.TryPickUp(worldItem);
                    }
                };

                Assert.That(interactor.TryPickUp(worldItem), Is.True);
                Assert.That(reentrantPickup, Is.False);
                Assert.That(stack.Amount, Is.EqualTo(7));
                Assert.That(worldObject == null || worldObject.activeSelf == false, Is.True);
                yield return null;
                Assert.That(worldObject == null, Is.True);
            }
            finally
            {
                Object.Destroy(root);
                if (worldObject != null)
                {
                    Object.Destroy(worldObject);
                }

                Object.Destroy(layout);
                Object.Destroy(definition);
            }
        }
    }
}

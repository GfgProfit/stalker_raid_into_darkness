using System.Collections;
using System.Text.RegularExpressions;
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
                WorldItemInteractor interactor = CreateBoundInteractor(root, layout, out InventoryModel model);
                ItemInstance stack = model.CreateItem(definition, 9);
                Assert.That(model.TryAddAuto(stack, "Bag").Success, Is.True);
                var worldItem = worldObject.AddComponent<WorldItem>();
                worldItem.Configure(definition, 2);
                int notifications = 0;
                model.Changed += _ => notifications++;

                Assert.That(interactor.TryPickUp(worldItem), Is.False);
                Assert.That(worldObject.activeSelf, Is.True);
                Assert.That(worldItem.Amount, Is.EqualTo(2));
                Assert.That(stack.Amount, Is.EqualTo(9));
                Assert.That(notifications, Is.Zero);
                Assert.That(model.TryRemove(stack.InstanceId, 4).Success, Is.True);

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

        [UnityTest]
        public IEnumerator PickupCompletesWhenItemAddedSubscriberThrows() => VerifyCommittedPickupAfterSubscriberThrows(false);

        [UnityTest]
        public IEnumerator PickupCompletesWhenStackMergeSubscriberThrows() => VerifyCommittedPickupAfterSubscriberThrows(true);

        private static IEnumerator VerifyCommittedPickupAfterSubscriberThrows(bool merge)
        {
            var root = new GameObject(nameof(WorldItemPickupTests));
            var worldObject = new GameObject("Item to pick up");
            var layout = ScriptableObject.CreateInstance<InventorySlotLayout>();
            var definition = ScriptableObject.CreateInstance<ItemDefinition>();

            try
            {
                definition.Configure("stackable", "Misc", Vector2Int.one, 0f, 10, false);
                WorldItemInteractor interactor = CreateBoundInteractor(root, layout, out InventoryModel model);
                ItemInstance retained = model.CreateItem(definition, merge ? 5 : 2);
                var worldItem = worldObject.AddComponent<WorldItem>();
                if (merge)
                {
                    Assert.That(model.TryAddAuto(retained, "Bag").Success, Is.True);
                    worldItem.Configure(definition, 2);
                }
                else
                {
                    retained.SetState("condition", "0.8");
                    worldItem.Configure(retained);
                }

                int expected = merge ? 7 : 2;
                InventoryChangeKind expectedChange = merge ? InventoryChangeKind.AmountChanged : InventoryChangeKind.ItemAdded;
                int observedAmount = 0;
                int notifications = 0;
                bool? attemptedDuringNotification = null;
                model.Changed += change =>
                {
                    if (change.Kind == expectedChange && change.Item == retained)
                    {
                        observedAmount = change.Item.Amount;
                        notifications++;
                        attemptedDuringNotification = interactor.TryPickUp(worldItem);
                        throw new System.InvalidOperationException("Pickup test subscriber threw after inventory commit.");
                    }
                };

                LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: Pickup test subscriber threw after inventory commit"));
                Assert.That(interactor.TryPickUp(worldItem), Is.True);

                Assert.That(notifications, Is.EqualTo(1));
                Assert.That(observedAmount, Is.EqualTo(expected));
                Assert.That(attemptedDuringNotification, Is.False);
                Assert.That(model.GetItem(retained.InstanceId), Is.SameAs(retained));
                Assert.That(model.ItemCount, Is.EqualTo(1));
                Assert.That(retained.Amount, Is.EqualTo(expected));
                if (merge == false)
                {
                    Assert.That(retained.GetState("condition"), Is.EqualTo("0.8"));
                }

                Assert.That(worldObject == null || worldObject.activeSelf == false, Is.True);
                Assert.That(interactor.TryPickUp(worldItem), Is.False);
                Assert.That(retained.Amount, Is.EqualTo(expected));
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

        private static WorldItemInteractor CreateBoundInteractor(GameObject root, InventorySlotLayout layout, out InventoryModel model)
        {
            layout.SetData("H", new[] { new SlotLayoutBinding('H', SlotId.Head, "Head", SlotMode.Single, SlotRule.Create()) });
            var interactor = root.AddComponent<WorldItemInteractor>();
            var runtime = root.AddComponent<PlayerInventoryRuntime>();
            runtime.Configure(new InventoryGridSettings("Bag", "Player", "Bag", 1, 1, 0), new InventoryGridSettings("Stash", "World", "Stash", 1, 1, 0), "Equipment", "Equipment", layout, null, null, null, interactor);
            Assert.That(runtime.EnsureInitialized(), Is.True);
            model = runtime.Model;
            return interactor;
        }
    }
}

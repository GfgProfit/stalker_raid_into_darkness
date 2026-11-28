using NUnit.Framework;
using RaidIntoDarkness.Inventory.Application;
using RaidIntoDarkness.Inventory.Domain;
using RaidIntoDarkness.Inventory.UI;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Core.Tests
{
    public sealed class InventoryItemIconBindingTests
    {
        [Test]
        public void RebindingTheSameIconCancelsTheOldProviderAndIgnoresItsCompletion()
        {
            var items = new InventoryTestItems();
            Sprite displayed = null;
            var binding = new InventoryItemIconBinding(sprite => displayed = sprite);

            try
            {
                ItemDefinition definition = items.Create("rebind-icon", Vector2Int.one);
                Texture2D texture = items.Track(new Texture2D(2, 2));
                Sprite oldIcon = items.Track(Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero));
                Sprite newIcon = items.Track(Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one));
                var request = new ItemIconRequest(definition, null, 1, 1, ItemIconProfileType.Default);
                var first = new DeferredTestItemIconProvider();
                var second = new DeferredTestItemIconProvider();

                binding.Request(request, first, false);
                binding.Request(request, second, false);
                Assert.That(first.Cancellation.IsCancellationRequested, Is.True);

                first.Complete(oldIcon);
                Assert.That(displayed, Is.Null);
                Assert.That(binding.IsLoading, Is.True);

                second.Complete(newIcon);
                Assert.That(displayed, Is.SameAs(newIcon));
                Assert.That(binding.IsLoading, Is.False);
            }
            finally
            {
                binding.Reset();
                items.Dispose();
            }
        }
    }
}


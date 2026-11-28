using System;

namespace RaidIntoDarkness.Inventory.Core.Tests
{
    internal sealed class TestPlacementValidator : IInventoryPlacementValidator
    {
        private readonly Func<ItemInstance, InventoryContainer, ItemPlacement, bool> _canPlace;

        public TestPlacementValidator(Func<ItemInstance, InventoryContainer, ItemPlacement, bool> canPlace)
        {
            _canPlace = canPlace;
        }

        public bool CanPlace(ItemInstance item, InventoryContainer container, ItemPlacement placement) => _canPlace(item, container, placement);
    }
}

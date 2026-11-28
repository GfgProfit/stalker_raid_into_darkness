using System;
using R3;

namespace Inventory.UI
{
    internal sealed class InventoryDropAreaViewModel : IDisposable
    {
        private readonly Func<ItemInstance, InventoryResult> _drop;
        private readonly ReactiveProperty<bool> _visible = new(false);

        public InventoryDropAreaViewModel(Func<ItemInstance, InventoryResult> drop)
        {
            _drop = drop;
        }

        public bool IsVisible => _visible.Value;
        public Observable<bool> Visibility => _visible;

        public void SetVisible(bool visible) => _visible.Value = visible;

        public InventoryResult Drop(ItemInstance item) => _drop(item);

        public void Dispose() => _visible.Dispose();
    }
}

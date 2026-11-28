using R3;

namespace RaidIntoDarkness.Inventory.UI
{
    internal sealed class InventorySplitDialogViewModel
    {
        private readonly InventoryModel _model;
        private readonly ItemInstance _item;
        private readonly InventoryContainer _source;

        public InventorySplitDialogViewModel(InventoryModel model, ItemInstance item)
        {
            _model = model;
            _item = item;
            _source = model.GetContainer(item.ContainerId);
        }

        public Observable<int> MaximumAmountChanges => Observable
            .FromEvent<InventoryChange>(handler => _model.Changed += handler, handler => _model.Changed -= handler)
            .Where(change => change.Item == _item || change.Kind == InventoryChangeKind.Cleared)
            .Select(_ => GetMaximumAmount())
            .DistinctUntilChanged();

        public int GetMaximumAmount() => IsItemAvailable() ? _item.Amount - 1 : 0;

        private bool IsItemAvailable()
        {
            if (_source == null || _model.ContainsItem(_item) == false)
            {
                return false;
            }

            InventoryContainer current = _model.GetContainer(_item.ContainerId);
            return current == _source || (current != null && string.IsNullOrEmpty(_source.OwnerId) == false && current.OwnerId == _source.OwnerId);
        }
    }
}

using System;
using Inventory;
using R3;

namespace RaidIntoDarkness.Integrations.Inventory
{
    internal sealed class InventoryBackpackLayoutViewModel : IDisposable
    {
        private readonly GridContainer _backpack;
        private readonly InventoryModel _model;
        private readonly ReactiveProperty<int> _rows;

        public InventoryBackpackLayoutViewModel(GridContainer backpack)
        {
            _backpack = backpack;
            _model = backpack.Model;
            _rows = new ReactiveProperty<int>(backpack.Height);
            _model.Changed += OnInventoryChanged;
        }

        public ReadOnlyReactiveProperty<int> Rows => _rows;

        public void Dispose()
        {
            _model.Changed -= OnInventoryChanged;
            _rows.Dispose();
        }

        private void OnInventoryChanged(InventoryChange change)
        {
            if (change.Affects(_backpack.Id) && _rows.Value != _backpack.Height)
            {
                _rows.Value = _backpack.Height;
            }
        }
    }
}

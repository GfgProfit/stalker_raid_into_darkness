using System;
using R3;
using RaidIntoDarkness.Inventory.Application;
using RaidIntoDarkness.Inventory.Domain;

namespace RaidIntoDarkness.Inventory.UI
{
    internal sealed class InventoryWorldItemBinding : IDisposable
    {
        private readonly InventoryWindow _window;
        private readonly Func<ItemInstance, InventoryResult> _drop;
        private readonly CompositeDisposable _subscriptions = new();

        public InventoryWorldItemBinding(InventoryWindow window, IWorldItemInteraction interaction, Func<ItemInstance, InventoryResult> drop)
        {
            _window = window;
            _drop = drop;
            _subscriptions.Add(Observable.FromEvent<string>(handler => interaction.HintChanged += handler, handler => interaction.HintChanged -= handler).Subscribe(SetHint));
            _subscriptions.Add(Observable.FromEvent<bool>(handler => interaction.DropAvailabilityChanged += handler, handler => interaction.DropAvailabilityChanged -= handler).Subscribe(SetDropAvailable));
            _subscriptions.Add(Observable.FromEvent<(ItemInstance Item, InventoryResult Result)>(handler => interaction.PickupCompleted += handler, handler => interaction.PickupCompleted -= handler).Subscribe(ReportPickup));
            SetHint(interaction.ActivePickupBinding);
            SetDropAvailable(interaction.CanDrop);
        }

        public void Dispose()
        {
            _subscriptions.Dispose();
            SetDropAvailable(false);
            SetHint(null);
        }

        private void SetHint(string inputBinding)
        {
            if (_window == null)
            {
                return;
            }

            if (inputBinding == null)
            {
                _window.ClearHint();
            }
            else
            {
                _window.ShowHint(_window.FormatLabel("hint_pick_up", "[{0}] Pick up", inputBinding));
            }
        }

        private void SetDropAvailable(bool available)
        {
            if (_window == null)
            {
                return;
            }

            _window.BindWorldDrop(available ? _drop : null);
        }

        private void ReportPickup((ItemInstance Item, InventoryResult Result) pickup)
        {
            if (_window == null)
            {
                return;
            }

            if (pickup.Result.Success)
            {
                _window.ShowStatus(_window.FormatLabel("status_picked", "Picked up {0}", pickup.Item.Definition.DisplayName));
            }
            else
            {
                _window.ShowStatus(_window.Label("failure_" + pickup.Result.Failure, pickup.Result.Failure.ToString()));
            }
        }
    }
}

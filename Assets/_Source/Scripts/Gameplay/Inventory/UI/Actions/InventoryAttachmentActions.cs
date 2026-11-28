using System;
using System.Collections.Generic;
using RaidIntoDarkness.Inventory;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.UI
{
    public sealed class InventoryAttachmentActions : IDisposable
    {
        private readonly InventoryWindow _window;
        private readonly InventoryWeaponAttachments _attachments;
        private readonly List<string> _actionIds = new();

        public InventoryAttachmentActions(InventoryWindow window, InventoryWeaponAttachments attachments)
        {
            _window = window;
            _attachments = attachments;
            _window.ItemDropHandler = attachments;
            _attachments.Detached += OnDetached;
            Register(new InventoryDelegateAction(InventoryWeaponAttachments.DETACH_ACTION, 21, context => "Снять глушитель", context => _window.ReportAttachmentResult(_attachments.Detach(context.Item), null), context => _attachments.GetSilencer(context.Item) != null, context => _attachments.Detach(context.Item, false).Success));
            Register(new InventoryDelegateAction(InventoryWeaponAttachments.DETACH_SCOPE_ACTION, 22, context => "Снять прицел", context => _window.ReportAttachmentResult(_attachments.DetachScope(context.Item), null), context => _attachments.GetScope(context.Item) != null, context => _attachments.DetachScope(context.Item, false).Success));
            Register(new InventoryDelegateAction(InventoryWeaponAttachments.DETACH_GRENADE_LAUNCHER_ACTION, 23, context => "Снять подствольник", context => _window.ReportAttachmentResult(_attachments.DetachGrenadeLauncher(context.Item), null), context => _attachments.GetGrenadeLauncher(context.Item) != null, context => _attachments.DetachGrenadeLauncher(context.Item, false).Success));

            if (_window.Model.TryGetContainer(_window.EquipmentContainerId, out SlottedContainer equipment) == false)
            {
                return;
            }

            foreach (SlotDefinition slot in equipment.Slots)
            {
                SlotId slotId = slot.Id;
                string actionId = InstallActionId(slotId);
                Register(new InventoryDelegateAction(actionId, 20, context => "Поставить на: " + equipment.GetSlotOccupant(slotId)?.Definition.ShortName, context => _window.ReportAttachmentResult(_attachments.Install(context.Item, equipment.GetSlotOccupant(slotId)), context.Item), context => _attachments.CanOfferInstall(context.Item, equipment.GetSlotOccupant(slotId)), context => _attachments.Install(context.Item, equipment.GetSlotOccupant(slotId), false).Success));
            }
        }

        public static string InstallActionId(SlotId slotId) => FormattableString.Invariant($"{InventoryWeaponAttachments.ATTACH_ACTION}{(int)slotId}");

        public void Dispose()
        {
            _attachments.Detached -= OnDetached;
            if (_window == null)
            {
                return;
            }

            if (_window.ItemDropHandler == _attachments)
            {
                _window.ItemDropHandler = null;
            }

            foreach (string actionId in _actionIds)
            {
                _window.Actions.Unregister(actionId);
            }

            _actionIds.Clear();
        }

        private void Register(InventoryDelegateAction action)
        {
            _window.Actions.Register(action);
            _actionIds.Add(action.Id);
        }

        private void OnDetached(AudioClip clip)
        {
            if (_window != null)
            {
                _window.PlayClip(clip, 1f);
            }
        }
    }
}

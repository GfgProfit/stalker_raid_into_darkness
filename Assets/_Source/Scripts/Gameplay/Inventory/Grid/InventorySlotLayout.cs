using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Domain
{
    [CreateAssetMenu(menuName = "Inventory/Slot Layout", fileName = "SlotLayout")]
    public sealed class InventorySlotLayout : ScriptableObject
    {
        [SerializeField] [TextArea(2, 16)] private string _slotMask = "AAB\nAAC";
        [SerializeField] private List<SlotLayoutBinding> _bindings = new()
        {
            new('A', SlotId.Head, "A", SlotMode.Grid, null),
            new('B', SlotId.Outfit, "B", SlotMode.Grid, null),
            new('C', SlotId.Backpack, "C", SlotMode.Grid, null)
        };

        public string SlotMask => _slotMask ?? string.Empty;
        public IReadOnlyList<SlotLayoutBinding> Bindings => _bindings;

        public void SetData(string slotMask, IEnumerable<SlotLayoutBinding> bindings)
        {
            _slotMask = slotMask;
            _bindings = bindings == null ? new List<SlotLayoutBinding>() : new List<SlotLayoutBinding>(bindings);
        }

        public bool TryBuildSlots(out List<SlotDefinition> slots, out int width, out int height, out string error) => SlotMaskParser.TryParse(SlotMask, _bindings, out slots, out width, out height, out error);
    }
}

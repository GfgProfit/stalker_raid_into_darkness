using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Inventory
{
    public enum ItemIconProfileType
    {
        Default = 0,
        Slot = 1
    }

    public readonly struct ItemIconRequest
    {
        public ItemIconRequest(ItemDefinition definition, IReadOnlyList<string> variantIds, int width, int height, ItemIconProfileType profileType)
        {
            Definition = definition;
            VariantIds = variantIds ?? Array.Empty<string>();
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            ProfileType = profileType;
        }

        public ItemDefinition Definition { get; }
        public IReadOnlyList<string> VariantIds { get; }
        public int Width { get; }
        public int Height { get; }
        public ItemIconProfileType ProfileType { get; }

        public static ItemIconRequest ForItem(ItemInstance item) => new(item.Definition, item.VisualVariant, item.GetSize(false).x, item.GetSize(false).y, ItemIconProfileType.Default);
        public static ItemIconRequest ForSlot(ItemInstance item, int slotWidth, int slotHeight) => new(item.Definition, item.VisualVariant, slotWidth, slotHeight, ItemIconProfileType.Slot);
    }

    public interface IItemIconProvider
    {
        bool TryGetIcon(in ItemIconRequest request, out Sprite icon);
        void RequestIcon(in ItemIconRequest request, Action<Sprite> onReady, CancellationToken cancellationToken);
    }
}

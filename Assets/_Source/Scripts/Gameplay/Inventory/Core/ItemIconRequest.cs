using RaidIntoDarkness.Inventory.Domain;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Application
{
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

        public static ItemIconRequest ForItem(ItemInstance item)
        {
            Vector2Int size = item.GetSize(false);
            return new ItemIconRequest(item.Definition, GetVariantIds(item), size.x, size.y, ItemIconProfileType.Default);
        }

        public static ItemIconRequest ForSlot(ItemInstance item, int slotWidth, int slotHeight) => new(item.Definition, GetVariantIds(item), slotWidth, slotHeight, ItemIconProfileType.Slot);

        private static IReadOnlyList<string> GetVariantIds(ItemInstance item)
        {
            List<string> variants = null;

            foreach (ItemDefinitionComponent component in item.Definition.Components)
            {
                string id = component?.GetIconVariantId(item);

                if (!string.IsNullOrEmpty(id))
                {
                    variants ??= new List<string>(item.VisualVariant);
                    variants.Add(id);
                }
            }

            return variants != null ? variants : item.VisualVariant;
        }
    }
}

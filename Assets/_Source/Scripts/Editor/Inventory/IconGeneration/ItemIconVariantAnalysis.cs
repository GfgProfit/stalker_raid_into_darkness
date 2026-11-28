using System;
using System.Collections.Generic;
using RaidIntoDarkness.Inventory.IconGeneration;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Editor
{
    internal sealed class ItemIconVariantAnalysis
    {
        public readonly List<ItemIconBakeVariant> Variants = new();
        public readonly Dictionary<ItemDefinition, int> VariantCountsByItem = new();
        public readonly List<ItemDefinition> ExcessiveVariantItems = new();

        public int DefaultVariantCount => Variants.FindAll(variant => variant.ProfileType == ItemIconProfileType.Default).Count;
        public int SlotVariantCount => Variants.Count - DefaultVariantCount;

        public long EstimatedTextureBytes
        {
            get
            {
                long bytes = 0L;

                foreach (ItemIconBakeVariant variant in Variants)
                {
                    bytes += variant.EstimatedTextureBytes;
                }

                return bytes;
            }
        }
    }
}

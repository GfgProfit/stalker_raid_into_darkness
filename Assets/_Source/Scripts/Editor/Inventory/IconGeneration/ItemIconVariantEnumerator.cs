using System;
using System.Collections.Generic;
using RaidIntoDarkness.Inventory.IconGeneration;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Editor
{
    internal static class ItemIconVariantEnumerator
    {
        private static Dictionary<string, ItemDefinition> _definitionsById;
        private static int _definitionsFrame = -1;

        public static ItemIconVariantAnalysis Enumerate(IReadOnlyList<ItemDefinition> definitions, ItemIconGeneratorSettings settings)
        {
            InvalidateDefinitions();
            var analysis = new ItemIconVariantAnalysis();
            var unique = new HashSet<ItemIconBakeVariant>();

            foreach (ItemDefinition definition in definitions)
            {
                if (definition == null || definition.CanGenerateIcon == false || ItemDefinitionIdValidator.IsValidId(definition.DefinitionId) == false)
                {
                    continue;
                }

                int before = analysis.Variants.Count;

                foreach (string[] variant in ItemIconVariantEnumeration.EnumerateVariants(definition))
                {
                    Vector2Int sizeDelta = ItemIconVariantEnumeration.CalculateSizeDelta(definition, variant);
                    AddUnique(analysis.Variants, unique, new ItemIconBakeVariant(definition, variant, Mathf.Max(1, definition.Width + sizeDelta.x), Mathf.Max(1, definition.Height + sizeDelta.y), ItemIconProfileType.Default, settings.IconRenderScale));

                    foreach (ItemIconSlotProfile slotProfile in settings.PrewarmSlotProfiles)
                    {
                        if (slotProfile.Accepts(definition))
                        {
                            AddUnique(analysis.Variants, unique, new ItemIconBakeVariant(definition, variant, slotProfile.Width, slotProfile.Height, ItemIconProfileType.Slot, settings.IconRenderScale));
                        }
                    }
                }

                int count = analysis.Variants.Count - before;
                analysis.VariantCountsByItem[definition] = count;

                if (count > ItemIconVariantEnumeration.LARGE_VARIANT_WARNING)
                {
                    analysis.ExcessiveVariantItems.Add(definition);
                }
            }

            analysis.Variants.Sort(ItemIconBakeVariant.Compare);
            return analysis;
        }

        public static ItemIconVariantAnalysis Enumerate(ItemDefinition definition, ItemIconGeneratorSettings settings) => Enumerate(definition == null ? Array.Empty<ItemDefinition>() : new[] { definition }, settings);

        public static void InvalidateDefinitions()
        {
            _definitionsById = null;
            _definitionsFrame = -1;
        }

        public static Dictionary<string, ItemDefinition> GetDefinitionsById()
        {
            if (_definitionsById != null && _definitionsFrame == Time.frameCount)
            {
                return _definitionsById;
            }

            _definitionsById = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);

            foreach (ItemDefinition definition in ItemDefinitionIdValidator.LoadAllDefinitions())
            {
                if (string.IsNullOrEmpty(definition.DefinitionId) == false)
                {
                    _definitionsById[definition.DefinitionId] = definition;
                }
            }

            _definitionsFrame = Time.frameCount;
            return _definitionsById;
        }

        private static void AddUnique(List<ItemIconBakeVariant> variants, HashSet<ItemIconBakeVariant> unique, ItemIconBakeVariant variant)
        {
            if (unique.Add(variant))
            {
                variants.Add(variant);
            }
        }
    }
}

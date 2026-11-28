using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.IconGeneration
{
    public static class ItemIconVisualConfigurators
    {
        private static readonly List<IItemIconVisualConfigurator> _global = new();

        public static IReadOnlyList<IItemIconVisualConfigurator> Global => _global;

        public static void Register(IItemIconVisualConfigurator configurator)
        {
            if (configurator != null && _global.Contains(configurator) == false)
            {
                _global.Add(configurator);
            }
        }

        public static void Unregister(IItemIconVisualConfigurator configurator) => _global.Remove(configurator);

        public static void Apply(GameObject visualRoot, ItemDefinition definition, IReadOnlyList<string> effectiveVariantIds)
        {
            if (visualRoot == null)
            {
                return;
            }

            ItemIconVariantPart.ApplyAll(visualRoot.GetComponentsInChildren<ItemIconVariantPart>(true), effectiveVariantIds);

            foreach (IItemIconVisualConfigurator configurator in visualRoot.GetComponentsInChildren<IItemIconVisualConfigurator>(true))
            {
                if (configurator is not ItemIconVariantPart)
                {
                    configurator.ConfigureIconVisual(visualRoot, definition, effectiveVariantIds);
                }
            }

            for (int i = 0; i < _global.Count; i++)
            {
                _global[i].ConfigureIconVisual(visualRoot, definition, effectiveVariantIds);
            }
        }
    }
}

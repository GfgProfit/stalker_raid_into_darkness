using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.IconGeneration
{
    public interface IItemIconVisualConfigurator
    {
        void ConfigureIconVisual(GameObject visualRoot, ItemDefinition definition, IReadOnlyList<string> effectiveVariantIds);
    }
}

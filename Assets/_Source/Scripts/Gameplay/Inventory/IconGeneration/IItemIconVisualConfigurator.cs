using RaidIntoDarkness.Inventory.Domain;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Infrastructure.IconGeneration
{
    public interface IItemIconVisualConfigurator
    {
        void ConfigureIconVisual(GameObject visualRoot, ItemDefinition definition, IReadOnlyList<string> effectiveVariantIds);
    }
}

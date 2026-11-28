using System.Collections.Generic;

namespace RaidIntoDarkness.Inventory.IconGeneration
{
    internal static class ItemIconStableKeyBuilder
    {
        public static IconCacheKey Build(ItemDefinition definition, IReadOnlyList<string> variantIds, ItemIconGeneratorSettings settings, IconRenderProfile renderProfile)
        {
            string itemId = definition == null ? string.Empty : definition.DefinitionId;
            string variantKey = ItemIconVariantKeyCache.GetCanonicalKey(variantIds);
            ulong visualSignature = ItemIconHashBuilder.BuildStableHash(definition, settings, renderProfile);
            return new IconCacheKey(itemId, variantKey, renderProfile.CellWidth, renderProfile.CellHeight, renderProfile.ProfileType, visualSignature);
        }
    }
}

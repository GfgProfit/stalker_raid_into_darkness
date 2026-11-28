namespace RaidIntoDarkness.Inventory.IconGeneration
{
    internal readonly struct PrewarmRequest
    {
        public PrewarmRequest(ItemDefinition definition, string[] variantIds, IconRenderProfile renderProfile, IconCacheKey key)
        {
            Definition = definition;
            VariantIds = variantIds;
            RenderProfile = renderProfile;
            Key = key;
        }

        public ItemDefinition Definition { get; }
        public string[] VariantIds { get; }
        public IconRenderProfile RenderProfile { get; }
        public IconCacheKey Key { get; }
    }
}

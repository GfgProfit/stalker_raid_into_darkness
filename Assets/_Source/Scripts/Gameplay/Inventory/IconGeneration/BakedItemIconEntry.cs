using RaidIntoDarkness.Inventory.Domain;
using RaidIntoDarkness.Inventory.Application;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Infrastructure.IconGeneration
{
    [Serializable]
    public sealed class BakedItemIconEntry
    {
        [SerializeField] private string _itemId;
        [SerializeField] private List<string> _variantIds = new();
        [SerializeField] private int _width;
        [SerializeField] private int _height;
        [SerializeField] private ItemIconProfileType _profileType;
        [SerializeField] private string _visualSignature;
        [SerializeField] private string _spriteResourcePath;
        [SerializeField] private string _bakeSignature;
        [SerializeField] private string _sourceItemAssetGuid;
        [SerializeField] private long _estimatedTextureBytes;

        [NonSerialized] private string _canonicalVariantIds;
        [NonSerialized] private Sprite _loadedSprite;

        public BakedItemIconEntry(string itemId, IReadOnlyList<string> variantIds, int width, int height, ItemIconProfileType profileType, ulong visualSignature, string spriteResourcePath, string bakeSignature, string sourceItemAssetGuid, long estimatedTextureBytes)
        {
            _itemId = itemId ?? string.Empty;
            _variantIds = variantIds == null ? new List<string>() : new List<string>(variantIds);
            _variantIds.Sort(StringComparer.Ordinal);
            _width = Mathf.Max(1, width);
            _height = Mathf.Max(1, height);
            _profileType = profileType;
            _visualSignature = ItemIconStableHash.ToHex(visualSignature);
            _spriteResourcePath = spriteResourcePath ?? string.Empty;
            _bakeSignature = bakeSignature ?? string.Empty;
            _sourceItemAssetGuid = sourceItemAssetGuid ?? string.Empty;
            _estimatedTextureBytes = Math.Max(0L, estimatedTextureBytes);
        }

        public string ItemId => _itemId ?? string.Empty;
        public IReadOnlyList<string> VariantIds => _variantIds;
        public int Width => Mathf.Max(1, _width);
        public int Height => Mathf.Max(1, _height);
        public ItemIconProfileType ProfileType => _profileType;
        public string VisualSignature => _visualSignature ?? string.Empty;
        public string SpriteResourcePath => _spriteResourcePath ?? string.Empty;
        public string BakeSignature => _bakeSignature ?? string.Empty;
        public string SourceItemAssetGuid => _sourceItemAssetGuid ?? string.Empty;
        public long EstimatedTextureBytes => Math.Max(0L, _estimatedTextureBytes);
        public ulong StableKeyHash => TryBuildKey(out IconCacheKey key) ? key.StableHash : 0UL;

        public static BakedItemIconEntry Create(ItemDefinition definition, IReadOnlyList<string> variantIds, int width, int height, ItemIconProfileType profileType, ItemIconGeneratorSettings settings, string spriteResourcePath, string bakeSignature, string sourceItemAssetGuid, long estimatedTextureBytes)
        {
            IconRenderProfile renderProfile = IconRenderProfile.Create(definition, width, height, profileType, settings);
            IconCacheKey key = ItemIconStableKeyBuilder.Build(definition, variantIds, settings, renderProfile);
            return new BakedItemIconEntry(key.ItemId, ItemIconVariantKeyCache.BuildNormalizedIds(variantIds), key.Width, key.Height, key.ProfileType, key.VisualSignature, spriteResourcePath, bakeSignature, sourceItemAssetGuid, estimatedTextureBytes);
        }

        internal bool TryBuildKey(out IconCacheKey key)
        {
            if (string.IsNullOrEmpty(ItemId) || ItemIconStableHash.TryParseHex(VisualSignature, out ulong visualSignature) == false)
            {
                key = default;
                return false;
            }

            _canonicalVariantIds ??= ItemIconVariantKeyCache.BuildCanonicalKey(_variantIds);
            key = new IconCacheKey(ItemId, _canonicalVariantIds, Width, Height, ProfileType, visualSignature);
            return true;
        }

        internal bool TryLoadSprite(out Sprite sprite)
        {
            if (_loadedSprite == null && string.IsNullOrEmpty(SpriteResourcePath) == false)
            {
                _loadedSprite = Resources.Load<Sprite>(SpriteResourcePath);
            }

            sprite = _loadedSprite;
            return sprite != null;
        }
    }
}

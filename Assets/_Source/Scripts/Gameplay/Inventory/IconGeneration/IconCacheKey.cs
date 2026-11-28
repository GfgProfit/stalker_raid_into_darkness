using RaidIntoDarkness.Inventory.Application;
using System;

namespace RaidIntoDarkness.Inventory.Infrastructure.IconGeneration
{
    internal readonly struct IconCacheKey : IEquatable<IconCacheKey>
    {
        private readonly string _itemId;
        private readonly string _variantIds;
        private readonly int _width;
        private readonly int _height;
        private readonly ItemIconProfileType _profileType;
        private readonly ulong _visualSignature;
        private readonly ulong _stableHash;

        public IconCacheKey(string itemId, string variantIds, int width, int height, ItemIconProfileType profileType, ulong visualSignature)
        {
            _itemId = itemId ?? string.Empty;
            _variantIds = variantIds ?? string.Empty;
            _width = width;
            _height = height;
            _profileType = profileType;
            _visualSignature = visualSignature;
            _stableHash = ItemIconStableHash.BuildKeyHash(_itemId, _variantIds, _width, _height, _profileType, _visualSignature);
        }

        public string ItemId => _itemId;
        public string VariantIds => _variantIds;
        public int Width => _width;
        public int Height => _height;
        public ItemIconProfileType ProfileType => _profileType;
        public ulong VisualSignature => _visualSignature;
        public ulong StableHash => _stableHash;

        public bool Equals(IconCacheKey other)
        {
            return _stableHash == other._stableHash && _width == other._width && _height == other._height && _profileType == other._profileType && _visualSignature == other._visualSignature && string.Equals(_itemId, other._itemId, StringComparison.Ordinal) && string.Equals(_variantIds, other._variantIds, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is IconCacheKey other && Equals(other);
        public override int GetHashCode() => unchecked((int)(_stableHash ^ (_stableHash >> 32)));
    }
}

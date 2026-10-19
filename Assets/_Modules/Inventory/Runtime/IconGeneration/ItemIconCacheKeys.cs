using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Inventory.IconGeneration
{
    internal sealed class IconCacheEntry
    {
        private IconCacheEntry(Sprite sprite, Texture2D texture, bool ownsSprite, bool ownsTexture)
        {
            Sprite = sprite;
            Texture = texture;
            OwnsSprite = ownsSprite;
            OwnsTexture = ownsTexture;
        }

        public Sprite Sprite { get; }
        public Texture2D Texture { get; }
        public bool OwnsSprite { get; }
        public bool OwnsTexture { get; }

        public static IconCacheEntry CreateGenerated(Sprite sprite, Texture2D texture) => new(sprite, texture, true, true);
        public static IconCacheEntry CreateFallback(Sprite sprite) => new(sprite, null, false, false);
        public static IconCacheEntry CreateExternal(Sprite sprite) => new(sprite, null, false, false);
    }

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

    internal static class ItemIconStableHash
    {
        private const ulong FNV_OFFSET_BASIS = 14695981039346656037UL;
        private const ulong FNV_PRIME = 1099511628211UL;

        public static ulong Begin() => FNV_OFFSET_BASIS;
        public static ulong Add(ulong hash, bool value) => Add(hash, value ? 1 : 0);
        public static ulong Add(ulong hash, int value) => Add(hash, unchecked((uint)value));

        public static ulong Add(ulong hash, uint value)
        {
            for (int shift = 0; shift < 32; shift += 8)
            {
                hash ^= (byte)(value >> shift);
                hash *= FNV_PRIME;
            }

            return hash;
        }

        public static ulong Add(ulong hash, ulong value)
        {
            for (int shift = 0; shift < 64; shift += 8)
            {
                hash ^= (byte)(value >> shift);
                hash *= FNV_PRIME;
            }

            return hash;
        }

        public static ulong Add(ulong hash, float value) => Add(hash, Mathf.RoundToInt(value * 1000f));
        public static ulong Add(ulong hash, Vector2 value) => Add(Add(hash, value.x), value.y);
        public static ulong Add(ulong hash, Vector3 value) => Add(Add(Add(hash, value.x), value.y), value.z);
        public static ulong Add(ulong hash, Color value) => Add(Add(Add(Add(hash, value.r), value.g), value.b), value.a);

        public static ulong Add(ulong hash, string value)
        {
            value ??= string.Empty;

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                hash ^= (byte)character;
                hash *= FNV_PRIME;
                hash ^= (byte)(character >> 8);
                hash *= FNV_PRIME;
            }

            hash ^= 0xff;
            hash *= FNV_PRIME;
            return hash;
        }

        public static ulong BuildKeyHash(string itemId, string variantIds, int width, int height, ItemIconProfileType profileType, ulong visualSignature)
        {
            ulong hash = Begin();
            hash = Add(hash, itemId);
            hash = Add(hash, variantIds);
            hash = Add(hash, width);
            hash = Add(hash, height);
            hash = Add(hash, (int)profileType);
            return Add(hash, visualSignature);
        }

        public static string ToHex(ulong value) => value.ToString("x16");

        public static bool TryParseHex(string value, out ulong result)
        {
            return ulong.TryParse(value, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out result);
        }
    }

    internal static class ItemIconVariantKeyCache
    {
        private static readonly ConditionalWeakTable<object, CachedVariantKey> _cache = new();

        public static string GetCanonicalKey(IReadOnlyList<string> variantIds)
        {
            if (variantIds == null || variantIds.Count == 0)
            {
                return string.Empty;
            }

            object owner = variantIds;

            if (_cache.TryGetValue(owner, out CachedVariantKey cached) && cached.Matches(variantIds))
            {
                return cached.CanonicalKey;
            }

            string[] normalizedIds = BuildNormalizedIds(variantIds);
            var replacement = new CachedVariantKey(normalizedIds);
            _cache.Remove(owner);
            _cache.Add(owner, replacement);
            return replacement.CanonicalKey;
        }

        public static string GetCanonicalKey(IReadOnlyList<ItemDefinition> definitions) => BuildCanonicalKey(BuildNormalizedIds(ToIds(definitions)));

        public static string[] BuildNormalizedIds(IReadOnlyList<string> variantIds)
        {
            if (variantIds == null || variantIds.Count == 0)
            {
                return Array.Empty<string>();
            }

            var ids = new List<string>(variantIds.Count);

            for (int i = 0; i < variantIds.Count; i++)
            {
                string id = variantIds[i];

                if (string.IsNullOrEmpty(id) == false && ids.Contains(id) == false)
                {
                    ids.Add(id);
                }
            }

            ids.Sort(StringComparer.Ordinal);
            return ids.ToArray();
        }

        public static string BuildCanonicalKey(IReadOnlyList<string> normalizedIds)
        {
            return normalizedIds == null || normalizedIds.Count == 0 ? string.Empty : string.Join("|", normalizedIds);
        }

        public static List<string> ToIds(IReadOnlyList<ItemDefinition> definitions)
        {
            var ids = new List<string>();

            if (definitions == null)
            {
                return ids;
            }

            for (int i = 0; i < definitions.Count; i++)
            {
                if (definitions[i] != null && string.IsNullOrEmpty(definitions[i].DefinitionId) == false)
                {
                    ids.Add(definitions[i].DefinitionId);
                }
            }

            return ids;
        }

        private sealed class CachedVariantKey
        {
            private readonly string[] _normalizedIds;

            public CachedVariantKey(string[] normalizedIds)
            {
                _normalizedIds = normalizedIds;
                CanonicalKey = BuildCanonicalKey(normalizedIds);
            }

            public string CanonicalKey { get; }

            public bool Matches(IReadOnlyList<string> variantIds)
            {
                if (variantIds == null)
                {
                    return _normalizedIds.Length == 0;
                }

                int uniqueCount = 0;

                for (int i = 0; i < variantIds.Count; i++)
                {
                    string id = variantIds[i];

                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    bool duplicate = false;

                    for (int previousIndex = 0; previousIndex < i; previousIndex++)
                    {
                        if (string.Equals(variantIds[previousIndex], id, StringComparison.Ordinal))
                        {
                            duplicate = true;
                            break;
                        }
                    }

                    if (duplicate)
                    {
                        continue;
                    }

                    uniqueCount++;

                    if (Array.BinarySearch(_normalizedIds, id, StringComparer.Ordinal) < 0)
                    {
                        return false;
                    }
                }

                return uniqueCount == _normalizedIds.Length;
            }
        }
    }

    internal static class ItemIconHashBuilder
    {
        public static int BuildHash(ItemDefinition definition, IReadOnlyList<string> variantIds, int targetWidth, int targetHeight, bool useSlotIconSettings)
        {
            if (definition == null)
            {
                return 0;
            }

            ItemIconGeneratorSettings settings = ItemIconGeneratorSettings.LoadDefault();
            IconRenderProfile renderProfile = useSlotIconSettings ? IconRenderProfile.CreateSlot(definition, targetWidth, targetHeight, settings) : IconRenderProfile.CreateDefault(definition, targetWidth, targetHeight, settings);
            ulong stableHash = BuildStableHash(definition, settings, renderProfile);
            stableHash = ItemIconStableHash.Add(stableHash, ItemIconVariantKeyCache.GetCanonicalKey(variantIds));
            return unchecked((int)(stableHash ^ (stableHash >> 32)));
        }

        public static ulong BuildStableHash(ItemDefinition definition, ItemIconGeneratorSettings settings, IconRenderProfile renderProfile)
        {
            if (definition == null || settings == null)
            {
                return 0UL;
            }

            ItemIconSettings icon = definition.Icon;
            ItemIconPostProcessSettings postProcess = ItemIconTextureProcessor.CreatePostProcessSettings(definition, renderProfile, settings);
            ulong hash = ItemIconStableHash.Begin();
            hash = ItemIconStableHash.Add(hash, 5);
            hash = ItemIconStableHash.Add(hash, definition.DefinitionId);
            hash = ItemIconStableHash.Add(hash, renderProfile.CellWidth);
            hash = ItemIconStableHash.Add(hash, renderProfile.CellHeight);
            hash = ItemIconStableHash.Add(hash, (int)renderProfile.ProfileType);
            hash = ItemIconStableHash.Add(hash, icon.PixelsPerCell);
            hash = ItemIconStableHash.Add(hash, icon.AntiAliasing);
            hash = ItemIconStableHash.Add(hash, postProcess.UseOutline);
            hash = ItemIconStableHash.Add(hash, (Color)postProcess.OutlineColor);
            hash = ItemIconStableHash.Add(hash, postProcess.OutlineWidth);
            hash = ItemIconStableHash.Add(hash, postProcess.UseShadow);
            hash = ItemIconStableHash.Add(hash, postProcess.ShadowColor);
            hash = ItemIconStableHash.Add(hash, postProcess.ShadowOffset.x);
            hash = ItemIconStableHash.Add(hash, postProcess.ShadowOffset.y);
            hash = ItemIconStableHash.Add(hash, postProcess.ShadowBlur);
            hash = ItemIconStableHash.Add(hash, renderProfile.Padding);
            hash = ItemIconStableHash.Add(hash, renderProfile.ModelEulerAngles);
            hash = ItemIconStableHash.Add(hash, renderProfile.ModelScale);
            hash = ItemIconStableHash.Add(hash, renderProfile.CameraEulerAngles);
            hash = ItemIconStableHash.Add(hash, renderProfile.UseDirectionalLight);
            hash = ItemIconStableHash.Add(hash, renderProfile.LightIntensity);
            hash = ItemIconStableHash.Add(hash, ItemIconVariantKeyCache.GetCanonicalKey(definition.DefaultIconVariant));
            return ItemIconStableHash.Add(hash, settings.BuildStableHash());
        }
    }

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

    internal static class ItemIconGeneratorSettingsHashBuilder
    {
        public static int BuildHash(ItemIconGeneratorSettings settings)
        {
            ulong stableHash = BuildStableHash(settings);
            return unchecked((int)(stableHash ^ (stableHash >> 32)));
        }

        public static ulong BuildStableHash(ItemIconGeneratorSettings settings)
        {
            ulong hash = ItemIconStableHash.Begin();
            hash = ItemIconStableHash.Add(hash, settings.RenderLayer);
            hash = ItemIconStableHash.Add(hash, settings.RenderingLayer);
            hash = ItemIconStableHash.Add(hash, settings.RenderOrigin);
            hash = ItemIconStableHash.Add(hash, settings.ExcludeIconLayerFromSceneLights);
            hash = ItemIconStableHash.Add(hash, (int)settings.RendererLightProbeUsage);
            hash = ItemIconStableHash.Add(hash, (int)settings.RendererReflectionProbeUsage);
            hash = ItemIconStableHash.Add(hash, settings.RendererReceiveShadows);
            hash = ItemIconStableHash.Add(hash, (int)settings.RenderTextureFormat);
            hash = ItemIconStableHash.Add(hash, settings.IconRenderScale);
            hash = ItemIconStableHash.Add(hash, settings.AllowHdr);
            hash = ItemIconStableHash.Add(hash, settings.UseCameraMsaa);
            hash = ItemIconStableHash.Add(hash, settings.CameraDistanceMultiplier);
            hash = ItemIconStableHash.Add(hash, settings.CameraDistanceOffset);
            hash = ItemIconStableHash.Add(hash, settings.NearClipPlane);
            hash = ItemIconStableHash.Add(hash, settings.FarClipBoundsMultiplier);
            hash = ItemIconStableHash.Add(hash, settings.FarClipOffset);
            hash = ItemIconStableHash.Add(hash, settings.EnableDirectSpecularLighting);
            hash = ItemIconStableHash.Add(hash, settings.EnableExposureControl);
            hash = ItemIconStableHash.Add(hash, settings.EnableAtmosphericScattering);
            hash = ItemIconStableHash.Add(hash, settings.EnablePostProcess);
            hash = ItemIconStableHash.Add(hash, settings.EnableColorGrading);
            hash = ItemIconStableHash.Add(hash, settings.EnableTonemapping);
            hash = ItemIconStableHash.Add(hash, settings.EnableVolumetrics);
            hash = ItemIconStableHash.Add(hash, settings.EnableSkyReflection);
            hash = ItemIconStableHash.Add(hash, settings.UseFrontFillLight);
            hash = ItemIconStableHash.Add(hash, settings.FrontFillLightIntensityMultiplier);
            hash = ItemIconStableHash.Add(hash, settings.FrontFillLightCameraDirection);
            hash = ItemIconStableHash.Add(hash, settings.UseSideFillLight);
            hash = ItemIconStableHash.Add(hash, settings.SideFillLightIntensityMultiplier);
            hash = ItemIconStableHash.Add(hash, settings.SideFillLightCameraDirection);
            hash = ItemIconStableHash.Add(hash, settings.UseRimLight);
            hash = ItemIconStableHash.Add(hash, settings.RimLightIntensityMultiplier);
            hash = ItemIconStableHash.Add(hash, settings.RimLightCameraDirection);
            hash = ItemIconStableHash.Add(hash, (int)settings.GeneratedLightShadows);
            hash = ItemIconStableHash.Add(hash, (int)settings.SkyAmbientMode);
            hash = ItemIconStableHash.Add(hash, (int)settings.SkyRenderingSpace);
            hash = ItemIconStableHash.Add(hash, (int)settings.PhysicallyBasedSkyModel);
            hash = ItemIconStableHash.Add(hash, settings.PhysicallyBasedSkyGroundTint);
            hash = ItemIconStableHash.Add(hash, settings.FogEnabled);
            hash = ItemIconStableHash.Add(hash, (int)settings.FogColorMode);
            hash = ItemIconStableHash.Add(hash, settings.MaxFogDistance);
            hash = ItemIconStableHash.Add(hash, settings.MeanFreePath);
            hash = ItemIconStableHash.Add(hash, settings.EnableVolumetricFog);
            hash = ItemIconStableHash.Add(hash, settings.FogAnisotropy);
            hash = ItemIconStableHash.Add(hash, (int)settings.ExposureMode);
            hash = ItemIconStableHash.Add(hash, settings.FixedExposure);
            hash = ItemIconStableHash.Add(hash, settings.ExposureCompensation);
            hash = ItemIconStableHash.Add(hash, settings.ExposureLimitMin);
            hash = ItemIconStableHash.Add(hash, settings.ExposureLimitMax);
            hash = ItemIconStableHash.Add(hash, settings.HistogramPercentages);
            hash = ItemIconStableHash.Add(hash, settings.PostExposure);
            hash = ItemIconStableHash.Add(hash, settings.Contrast);
            hash = ItemIconStableHash.Add(hash, settings.ColorFilter);
            hash = ItemIconStableHash.Add(hash, settings.HueShift);
            hash = ItemIconStableHash.Add(hash, settings.Saturation);
            return ItemIconStableHash.Add(hash, (int)settings.TonemappingMode);
        }
    }
}

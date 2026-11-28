using RaidIntoDarkness.Inventory.Domain;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Infrastructure.IconGeneration
{
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
}

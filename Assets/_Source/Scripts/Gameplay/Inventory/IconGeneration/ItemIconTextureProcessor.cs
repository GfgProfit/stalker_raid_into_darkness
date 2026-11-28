using UnityEngine;

namespace RaidIntoDarkness.Inventory.IconGeneration
{
    internal static class ItemIconTextureProcessor
    {
        private const byte VISIBLE_ALPHA_THRESHOLD = 8;

        public static bool HasVisiblePixels(Color32[] pixels)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a > VISIBLE_ALPHA_THRESHOLD)
                {
                    return true;
                }
            }

            return false;
        }

        public static Color32[] CreateProcessedPixels(Color32[] itemPixels, int width, int height, ItemIconPostProcessSettings settings) => ItemIconPostProcessor.Process(itemPixels, width, height, settings);

        public static ItemIconPostProcessSettings CreatePostProcessSettings(ItemDefinition definition, IconRenderProfile renderProfile, ItemIconGeneratorSettings settings)
        {
            ItemIconSettings icon = definition.Icon;
            Color outlineColor = settings.IconOutlineColor;
            Color shadowColor = settings.IconShadowColor;
            int outlineWidth = icon.GetOutlineTextureWidth(renderProfile.RenderScale);
            Vector2Int shadowOffset = icon.GetShadowTextureOffset(renderProfile.RenderScale);
            int shadowBlur = icon.GetShadowTextureBlur(renderProfile.RenderScale);
            bool useOutline = icon.UseOutline && outlineWidth > 0 && outlineColor.a > 0f;
            bool useShadow = icon.UseShadow && shadowColor.a > 0f && (shadowOffset != Vector2Int.zero || shadowBlur > 0);
            return new ItemIconPostProcessSettings(useShadow, shadowColor, shadowOffset, shadowBlur, useOutline, outlineColor, outlineWidth);
        }

        public static Sprite CreateSprite(ItemDefinition definition, Texture2D texture, IconRenderProfile renderProfile)
        {
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), renderProfile.SpritePixelsPerUnit);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            sprite.name = $"{definition.name} Runtime Icon";
            return sprite;
        }
    }
}

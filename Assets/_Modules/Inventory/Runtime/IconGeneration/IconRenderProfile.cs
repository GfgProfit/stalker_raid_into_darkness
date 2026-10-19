using System;
using UnityEngine;

namespace Inventory.IconGeneration
{
    public readonly struct IconRenderProfile
    {
        public readonly bool UseSlotSettings;
        public readonly int CellWidth;
        public readonly int CellHeight;
        public readonly int RenderScale;
        public readonly int TextureWidth;
        public readonly int TextureHeight;
        public readonly float SpritePixelsPerUnit;
        public readonly Vector3 ModelEulerAngles;
        public readonly Vector3 ModelScale;
        public readonly Vector3 CameraEulerAngles;
        public readonly float Padding;
        public readonly bool UseDirectionalLight;
        public readonly float LightIntensity;

        public ItemIconProfileType ProfileType => UseSlotSettings ? ItemIconProfileType.Slot : ItemIconProfileType.Default;

        private IconRenderProfile(bool useSlotSettings, int cellWidth, int cellHeight, int renderScale, int textureWidth, int textureHeight, float spritePixelsPerUnit, Vector3 modelEulerAngles, Vector3 modelScale, Vector3 cameraEulerAngles, float padding, bool useDirectionalLight, float lightIntensity)
        {
            UseSlotSettings = useSlotSettings;
            CellWidth = Mathf.Max(1, cellWidth);
            CellHeight = Mathf.Max(1, cellHeight);
            RenderScale = Mathf.Clamp(renderScale, 1, 4);
            TextureWidth = Mathf.Max(1, textureWidth);
            TextureHeight = Mathf.Max(1, textureHeight);
            SpritePixelsPerUnit = Mathf.Max(1f, spritePixelsPerUnit);
            ModelEulerAngles = modelEulerAngles;
            ModelScale = modelScale == Vector3.zero ? Vector3.one : modelScale;
            CameraEulerAngles = cameraEulerAngles;
            Padding = Mathf.Max(1f, padding);
            UseDirectionalLight = useDirectionalLight;
            LightIntensity = Mathf.Max(0f, lightIntensity);
        }

        public static IconRenderProfile CreateDefault(ItemDefinition definition) => CreateDefault(definition, definition.Width, definition.Height, ItemIconGeneratorSettings.LoadDefault());
        public static IconRenderProfile CreateDefault(ItemDefinition definition, ItemIconGeneratorSettings settings) => CreateDefault(definition, definition.Width, definition.Height, settings);
        public static IconRenderProfile CreateDefault(ItemDefinition definition, int width, int height) => CreateDefault(definition, width, height, ItemIconGeneratorSettings.LoadDefault());

        public static IconRenderProfile CreateDefault(ItemDefinition definition, int width, int height, ItemIconGeneratorSettings settings)
        {
            int cellWidth = Mathf.Max(1, width);
            int cellHeight = Mathf.Max(1, height);
            int renderScale = ResolveRenderScale(settings);
            ItemIconSettings icon = definition.Icon;
            ItemIconViewSettings view = icon.DefaultView;
            return new IconRenderProfile(false, cellWidth, cellHeight, renderScale, cellWidth * icon.PixelsPerCell * renderScale, cellHeight * icon.PixelsPerCell * renderScale, icon.GetSpritePixelsPerUnit(renderScale), view.ModelEulerAngles, view.ModelScale, view.CameraEulerAngles, view.Padding, view.UseDirectionalLight, view.LightIntensity);
        }

        public static IconRenderProfile CreateSlot(ItemDefinition definition, int slotWidth, int slotHeight) => CreateSlot(definition, slotWidth, slotHeight, ItemIconGeneratorSettings.LoadDefault());

        public static IconRenderProfile CreateSlot(ItemDefinition definition, int slotWidth, int slotHeight, ItemIconGeneratorSettings settings)
        {
            int cellWidth = Mathf.Max(1, slotWidth);
            int cellHeight = Mathf.Max(1, slotHeight);
            int renderScale = ResolveRenderScale(settings);
            ItemIconSettings icon = definition.Icon;
            ItemIconViewSettings view = icon.SlotView;
            return new IconRenderProfile(true, cellWidth, cellHeight, renderScale, cellWidth * icon.PixelsPerCell * renderScale, cellHeight * icon.PixelsPerCell * renderScale, icon.GetSpritePixelsPerUnit(renderScale), view.ModelEulerAngles, view.ModelScale, view.CameraEulerAngles, view.Padding, view.UseDirectionalLight, view.LightIntensity);
        }

        public static IconRenderProfile Create(ItemDefinition definition, int width, int height, ItemIconProfileType profileType, ItemIconGeneratorSettings settings)
        {
            return profileType == ItemIconProfileType.Slot ? CreateSlot(definition, width, height, settings) : CreateDefault(definition, width, height, settings);
        }

        private static int ResolveRenderScale(ItemIconGeneratorSettings settings)
        {
            return (settings == null ? ItemIconGeneratorSettings.LoadDefault() : settings).IconRenderScale;
        }
    }

    [Serializable]
    public struct ItemIconSlotProfile : IEquatable<ItemIconSlotProfile>
    {
        [SerializeField] private bool _restrictCategory;
        [SerializeField] private string _acceptedCategory;
        [SerializeField] private int _width;
        [SerializeField] private int _height;

        public ItemIconSlotProfile(bool restrictCategory, string acceptedCategory, int width, int height)
        {
            _restrictCategory = restrictCategory;
            _acceptedCategory = restrictCategory ? acceptedCategory : string.Empty;
            _width = Mathf.Max(1, width);
            _height = Mathf.Max(1, height);
        }

        public int Width => Mathf.Max(1, _width);
        public int Height => Mathf.Max(1, _height);
        public bool RestrictsCategory => _restrictCategory;
        public string AcceptedCategory => _acceptedCategory ?? string.Empty;

        public bool Accepts(ItemDefinition definition) => definition != null && (_restrictCategory == false || definition.IsCategory(AcceptedCategory));
        public bool Equals(ItemIconSlotProfile other) => _restrictCategory == other._restrictCategory && AcceptedCategory == other.AcceptedCategory && Width == other.Width && Height == other.Height;
        public override bool Equals(object obj) => obj is ItemIconSlotProfile other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_restrictCategory, AcceptedCategory, Width, Height);
    }
}

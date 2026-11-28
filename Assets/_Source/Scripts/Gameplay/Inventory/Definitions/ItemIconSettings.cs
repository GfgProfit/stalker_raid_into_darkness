using System;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Domain
{
    [Serializable]
    public sealed class ItemIconSettings
    {
        [SerializeField] private bool _generateIcon = true;
        [SerializeField] [Min(16)] private int _pixelsPerCell = 64;
        [SerializeField] [Range(1, 8)] private int _antiAliasing = 1;
        [SerializeField] private bool _useOutline = true;
        [SerializeField] [Range(0, 8)] private int _outlineWidth = 2;
        [SerializeField] private bool _useShadow = true;
        [SerializeField] private Vector2 _shadowOffset = new(2f, -2f);
        [SerializeField] [Range(0, 8)] private int _shadowBlur = 2;
        [SerializeField] private ItemIconViewSettings _defaultView = ItemIconViewSettings.Default;
        [SerializeField] private bool _useCustomSlotView;
        [SerializeField] private ItemIconViewSettings _slotView = ItemIconViewSettings.Default;

        public bool GenerateIcon => _generateIcon;
        public int PixelsPerCell => Mathf.Max(16, _pixelsPerCell);
        public int AntiAliasing => GetSupportedAntiAliasing(_antiAliasing);
        public bool UseOutline => _useOutline;
        public int OutlineWidth => Mathf.Max(0, _outlineWidth);
        public bool UseShadow => _useShadow;
        public Vector2 ShadowOffset => _shadowOffset;
        public int ShadowBlur => Mathf.Max(0, _shadowBlur);
        public ItemIconViewSettings DefaultView => _defaultView;
        public bool UseCustomSlotView => _useCustomSlotView;
        public ItemIconViewSettings SlotView => _useCustomSlotView ? _slotView : _defaultView;

        public int GetOutlineTextureWidth(int renderScale) => OutlineWidth * ClampRenderScale(renderScale);
        public Vector2Int GetShadowTextureOffset(int renderScale) => Vector2Int.RoundToInt(_shadowOffset * ClampRenderScale(renderScale));
        public int GetShadowTextureBlur(int renderScale) => ShadowBlur * ClampRenderScale(renderScale);
        public float GetSpritePixelsPerUnit(int renderScale) => PixelsPerCell * ClampRenderScale(renderScale);

        public void Validate()
        {
            _pixelsPerCell = Mathf.Max(16, _pixelsPerCell);
            _antiAliasing = Mathf.Clamp(_antiAliasing, 1, 8);
            _outlineWidth = Mathf.Clamp(_outlineWidth, 0, 8);
            _shadowBlur = Mathf.Clamp(_shadowBlur, 0, 8);
        }

        public static int ClampRenderScale(int renderScale) => Mathf.Clamp(renderScale, 1, 4);

        private static int GetSupportedAntiAliasing(int value)
        {
            int clampedValue = Mathf.Clamp(value, 1, 8);

            if (clampedValue <= 1)
            {
                return 1;
            }

            if (clampedValue <= 2)
            {
                return 2;
            }

            if (clampedValue <= 4)
            {
                return 4;
            }

            return 8;
        }
    }
}

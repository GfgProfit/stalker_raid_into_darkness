using System;
using UnityEngine;

namespace RaidIntoDarkness.GraphicsOptions
{
    public enum VideoCategory
    {
        General,
        Screen,
        Image,
        Textures,
        Lighting,
        Vegetation,
        PostProcessing,
        All
    }

    public enum VideoAntialiasing
    {
        Off,
        Fxaa,
        Smaa,
        Taa
    }

    [Serializable]
    public struct VideoDisplay
    {
        public FullScreenMode mode;
        public int width;
        public int height;
        public uint refreshNumerator;
        public uint refreshDenominator;

        public VideoDisplay(FullScreenMode mode, int width, int height, RefreshRate refresh = default)
        {
            this.mode = mode;
            this.width = width;
            this.height = height;
            refreshNumerator = mode == FullScreenMode.ExclusiveFullScreen ? refresh.numerator : 0;
            refreshDenominator = mode == FullScreenMode.ExclusiveFullScreen ? refresh.denominator : 0;
        }

        public RefreshRate Refresh => new RefreshRate { numerator = refreshNumerator, denominator = refreshDenominator };
        public bool Valid => (mode == FullScreenMode.ExclusiveFullScreen || mode == FullScreenMode.FullScreenWindow || mode == FullScreenMode.Windowed) && width > 0 && height > 0 && (mode != FullScreenMode.ExclusiveFullScreen || refreshNumerator > 0 && refreshDenominator > 0);

        public bool Matches(VideoDisplay other)
        {
            return mode == other.mode && width == other.width && height == other.height && (mode != FullScreenMode.ExclusiveFullScreen || Math.Abs(Refresh.value - other.Refresh.value) < 0.005);
        }
    }

    [Serializable]
    public struct VideoQuality
    {
        public int renderScale;
        public VideoAntialiasing antialiasing;
        public int antialiasingQuality;
        public int textureQuality;
        public int anisotropy;
        public int lodQuality;
        public int shadowQuality;
        public bool contactShadows;
        public int contactShadowQuality;
        public int sunShadowDistance;
        public int localShadowDistance;
        public int ambientOcclusion;
        public float grassSpacing;
        public int grassDistance;
        public bool grassShadows;
        public int fogQuality;
        public bool motionBlur;
        public bool bloom;
        public bool lensFlare;

        public bool Valid => renderScale >= 50 && renderScale <= 100 && (int)antialiasing >= 0 && (int)antialiasing <= 3 && IsTier(antialiasingQuality) && IsTier(textureQuality) && VideoPresets.IsAnisotropyLevel(anisotropy) && IsTier(lodQuality) && IsTier(shadowQuality) && IsTier(contactShadowQuality) && sunShadowDistance >= 75 && sunShadowDistance <= 500 && localShadowDistance >= 5 && localShadowDistance <= 75 && ambientOcclusion >= 0 && ambientOcclusion <= 3 && float.IsFinite(grassSpacing) && grassSpacing >= 0.04f && grassSpacing <= 1.0f && grassDistance >= 0 && grassDistance <= 250 && IsTier(fogQuality);

        private static bool IsTier(int value)
        {
            return value >= 0 && value <= 2;
        }
    }

    [Serializable]
    public struct VideoSettings
    {
        public int version;
        public int preset;
        public VideoDisplay display;
        public bool vSync;
        public int fpsLimit;
        public int brightness;
        public VideoQuality quality;

        public bool Valid => version == VideoPresets.SAVE_VERSION && preset >= 0 && preset < VideoPresets.COUNT && display.Valid && VideoPresets.IsFrameLimit(fpsLimit) && brightness >= 50 && brightness <= 150 && quality.Valid;
    }

    public sealed class VideoPresets
    {
        public const int COUNT = 5;
        public const int DEFAULT_PRESET = 3;
        public const int SAVE_VERSION = 3;

        private static readonly int[] ANISOTROPY_LEVELS = { 0, 2, 4, 8, 16 };
        private static readonly int[] FRAME_LIMITS = { 30, 60, 90, 120, 144, 165, 240, -1 };
        private static readonly float[] LOD_BIASES = { 0.65f, 1.0f, 1.5f };

        private readonly float _grassReference;
        private readonly VideoQuality[] _qualities;

        public static int[] AnisotropyLevels => (int[])ANISOTROPY_LEVELS.Clone();
        public static int[] FrameLimits => (int[])FRAME_LIMITS.Clone();

        public VideoPresets(float grassReference, int localShadowDefault, int grassDistanceDefault, bool grassShadowDefault)
        {
            _grassReference = grassReference;
            localShadowDefault = Mathf.Clamp(localShadowDefault, 5, 75);
            _qualities = new[]
            {
                Quality(60, VideoAntialiasing.Fxaa, 0, 0, 0, 0, 0, 75, Mathf.Min(20, localShadowDefault), 0, 30, 50, false, 0, false, false, false),
                Quality(75, VideoAntialiasing.Smaa, 0, 1, 2, 1, 1, 100, Mathf.Min(35, localShadowDefault), 1, 50, 80, false, 0, false, true, false),
                Quality(90, VideoAntialiasing.Taa, 1, 2, 4, 1, 1, 125, Mathf.Min(55, localShadowDefault), 2, 75, 110, false, 1, false, true, true),
                Quality(100, VideoAntialiasing.Taa, 2, 2, 8, 2, 2, 150, localShadowDefault, 3, 100, grassDistanceDefault, grassShadowDefault, 1, false, true, true),
                Quality(100, VideoAntialiasing.Taa, 2, 2, 16, 2, 2, 220, localShadowDefault, 3, 140, Mathf.Max(200, grassDistanceDefault), true, 2, true, true, true)
            };
            for (int i = 0; i < _qualities.Length; i++)
            {
                SetPresetContactShadows(ref _qualities[i], i);
            }
        }

        public VideoSettings Defaults(VideoDisplay display)
        {
            return new VideoSettings { version = SAVE_VERSION, display = display, preset = DEFAULT_PRESET, brightness = 100, fpsLimit = -1, quality = _qualities[DEFAULT_PRESET] };
        }

        public VideoSettings Select(VideoSettings settings, int preset)
        {
            settings.preset = Mathf.Clamp(preset, 0, COUNT - 1);
            settings.quality = _qualities[settings.preset];
            return settings;
        }

        public int Match(VideoQuality quality)
        {
            return Array.IndexOf(_qualities, quality);
        }

        public bool IsCustom(VideoSettings settings)
        {
            return !_qualities[settings.preset].Equals(settings.quality);
        }

        public float GrassSpacing(int percent)
        {
            return Mathf.Clamp(_grassReference / Mathf.Sqrt(percent * 0.01f), 0.04f, 1.0f);
        }

        public int GrassPercent(float spacing)
        {
            float ratio = _grassReference / spacing;
            return Mathf.RoundToInt(100.0f * ratio * ratio);
        }

        public static float LodBias(int index)
        {
            return LOD_BIASES[index];
        }

        public static bool HasAntialiasingQuality(VideoAntialiasing method)
        {
            return method == VideoAntialiasing.Smaa || method == VideoAntialiasing.Taa;
        }

        public static bool IsAnisotropyLevel(int value)
        {
            return Array.IndexOf(ANISOTROPY_LEVELS, value) >= 0;
        }

        public static bool IsFrameLimit(int value)
        {
            return Array.IndexOf(FRAME_LIMITS, value) >= 0;
        }

        internal static void SetPresetContactShadows(ref VideoQuality quality, int preset)
        {
            quality.contactShadows = preset > 0;
            quality.contactShadowQuality = preset >= 4 ? 2 : preset >= 2 ? 1 : 0;
        }

        public static VideoSettings ResetCategory(VideoSettings settings, VideoSettings defaults, VideoCategory category)
        {
            switch (category)
            {
                case VideoCategory.General:
                    settings.preset = defaults.preset;
                    settings.quality = defaults.quality;
                    break;
                case VideoCategory.Screen:
                    settings.display = defaults.display;
                    settings.vSync = defaults.vSync;
                    settings.fpsLimit = defaults.fpsLimit;
                    break;
                case VideoCategory.Image:
                    settings.brightness = defaults.brightness;
                    settings.quality.renderScale = defaults.quality.renderScale;
                    settings.quality.antialiasing = defaults.quality.antialiasing;
                    settings.quality.antialiasingQuality = defaults.quality.antialiasingQuality;
                    break;
                case VideoCategory.Textures:
                    settings.quality.textureQuality = defaults.quality.textureQuality;
                    settings.quality.anisotropy = defaults.quality.anisotropy;
                    settings.quality.lodQuality = defaults.quality.lodQuality;
                    break;
                case VideoCategory.Lighting:
                    settings.quality.shadowQuality = defaults.quality.shadowQuality;
                    settings.quality.contactShadows = defaults.quality.contactShadows;
                    settings.quality.contactShadowQuality = defaults.quality.contactShadowQuality;
                    settings.quality.sunShadowDistance = defaults.quality.sunShadowDistance;
                    settings.quality.localShadowDistance = defaults.quality.localShadowDistance;
                    settings.quality.ambientOcclusion = defaults.quality.ambientOcclusion;
                    break;
                case VideoCategory.Vegetation:
                    settings.quality.grassSpacing = defaults.quality.grassSpacing;
                    settings.quality.grassDistance = defaults.quality.grassDistance;
                    settings.quality.grassShadows = defaults.quality.grassShadows;
                    settings.quality.fogQuality = defaults.quality.fogQuality;
                    break;
                case VideoCategory.PostProcessing:
                    settings.quality.motionBlur = defaults.quality.motionBlur;
                    settings.quality.bloom = defaults.quality.bloom;
                    settings.quality.lensFlare = defaults.quality.lensFlare;
                    break;
                case VideoCategory.All:
                    return defaults;
            }
            return settings;
        }

        private VideoQuality Quality(int scale, VideoAntialiasing aa, int aaQuality, int textures, int anisotropy, int lod, int shadowQuality, int sun, int local, int ao, int grass, int grassDistance, bool grassShadows, int fog, bool motion, bool bloom, bool flare)
        {
            return new VideoQuality
            {
                renderScale = scale,
                antialiasing = aa,
                antialiasingQuality = aaQuality,
                textureQuality = textures,
                anisotropy = anisotropy,
                lodQuality = lod,
                shadowQuality = shadowQuality,
                sunShadowDistance = sun,
                localShadowDistance = local,
                ambientOcclusion = ao,
                grassSpacing = GrassSpacing(grass),
                grassDistance = grassDistance,
                grassShadows = grassShadows,
                fogQuality = fog,
                motionBlur = motion,
                bloom = bloom,
                lensFlare = flare
            };
        }
    }
}

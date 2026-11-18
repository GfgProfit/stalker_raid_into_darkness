using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.GraphicsOptions
{
    public sealed class VideoSettingsRuntime : MonoBehaviour, IVideoSettingsTarget
    {
        private VideoSettingsStore _store;
        private VideoRendering _rendering;
        private XRayGrassSettings _grass;
        private int _initialVSync;
        private int _initialFrameRate;
        private int _initialMipmapLimit;
        private AnisotropicFiltering _initialAnisotropy;
        private int _lastAnisotropy = -1;
        private bool _hasSavedSettings;

        public static VideoSettingsRuntime Instance { get; private set; }
        public VideoDisplayOptions Display { get; } = new();
        public VideoPresets Presets { get; private set; }
        public VideoSettingsSession Session { get; private set; }
        public VideoDisplay SafeDisplay => Display.SafeDisplay;
        public bool SupportsRendering => VideoRendering.HasHdrp;
        public bool SupportsRenderScale => VideoRendering.SupportsRenderScale;
        public bool SupportsContactShadows => VideoRendering.SupportsContactShadows;
        public bool SupportsAmbientOcclusion => VideoRendering.SupportsAmbientOcclusion;
        public bool SupportsFog => VideoRendering.SupportsFog;
        public bool SupportsMotion => VideoRendering.SupportsMotion;
        public bool SupportsLocalShadows => XRayVideoOverrides.DefaultPunctualShadowDistance.HasValue;
        public bool SupportsGrass => _grass != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void StartRuntime()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || Instance != null)
            {
                return;
            }
            var root = new GameObject("Video Settings");
            DontDestroyOnLoad(root);
            Instance = root.AddComponent<VideoSettingsRuntime>();
            Instance.Initialize();
        }

        public VideoDisplay ReadDisplay()
        {
            return Display.Read();
        }

        public VideoDisplay NormalizeDisplay(VideoDisplay value)
        {
            return Display.Normalize(value);
        }

        public void SetDisplay(VideoDisplay value)
        {
            Display.Apply(value);
        }

        public void SetGraphics(VideoSettings settings)
        {
            VideoQuality quality = settings.quality;
            if (QualitySettings.vSyncCount != (settings.vSync ? 1 : 0))
            {
                QualitySettings.vSyncCount = settings.vSync ? 1 : 0;
            }
            if (Application.targetFrameRate != settings.fpsLimit)
            {
                Application.targetFrameRate = settings.fpsLimit;
            }
            int mipmapLimit = quality.TextureMipmapLimit;
            if (QualitySettings.globalTextureMipmapLimit != mipmapLimit)
            {
                QualitySettings.globalTextureMipmapLimit = mipmapLimit;
            }
            if (_lastAnisotropy != quality.anisotropy)
            {
                int aniso = Mathf.Max(1, quality.anisotropy);
                QualitySettings.anisotropicFiltering = quality.anisotropy == 0 ? AnisotropicFiltering.Disable : AnisotropicFiltering.ForceEnable;
                Texture.SetGlobalAnisotropicFilteringLimits(aniso, aniso);
                _lastAnisotropy = quality.anisotropy;
            }
            XRayVideoOverrides.Apply(quality.localShadowDistance, quality.lensFlare, quality.fogQuality, VideoPresets.LodBias(quality.lodQuality));
            if (_grass != null)
            {
                XRayGrassValues current = _grass.Values;
                if (!_grass.HasOverrides || !Mathf.Approximately(current.Density, quality.grassSpacing) || current.Radius != quality.grassDistance || current.Shadows != quality.grassShadows)
                {
                    _grass.Set(new XRayGrassValues(quality.grassSpacing, quality.grassDistance, current.Height, quality.grassShadows), false);
                }
            }
            _rendering?.Apply(quality);
            PreviewBrightness(settings.brightness);
        }

        public void PreviewBrightness(int brightness)
        {
            _rendering?.SetBrightness(brightness);
        }

        public bool PrepareDisplayTrial(VideoSettings confirmed)
        {
            return _hasSavedSettings || Save(confirmed);
        }

        public bool Save(VideoSettings settings)
        {
            bool saved = _store.TrySave(settings);
            _hasSavedSettings |= saved;
            return saved;
        }

        private void Initialize()
        {
            Display.Refresh();
            _grass = XRayGrassSettingsProvider.Ensure().Settings;
            XRayGrassValues? baseline = _grass?.Defaults.ForLevel(null);
            int localDistance = Mathf.RoundToInt(XRayVideoOverrides.DefaultPunctualShadowDistance ?? 75.0f);
            Presets = new VideoPresets(baseline?.Density ?? 0.45f, localDistance, baseline?.Shadows ?? false);
            VideoSettings defaults = Presets.Defaults(Display.Read());
            string settingsPath = VideoSettingsStore.ResolvePath(Application.isEditor, Application.dataPath, Application.persistentDataPath, Application.productName);
            _store = new VideoSettingsStore(settingsPath);
            bool loaded = _store.TryLoad(out VideoSettings initial, upgradeFile: true);
            _hasSavedSettings = loaded;
            if (!loaded && Application.isEditor && !File.Exists(settingsPath))
            {
                var previousStore = new VideoSettingsStore(Path.Combine(Application.persistentDataPath, VideoSettingsStore.FILE_NAME));
                loaded = previousStore.TryLoad(out initial);
                if (loaded)
                {
                    _hasSavedSettings = _store.TrySave(initial);
                }
            }
            if (!loaded)
            {
                initial = defaults;
                if (_grass != null && _grass.HasOverrides)
                {
                    XRayGrassValues legacy = _grass.Values;
                    initial.quality.grassSpacing = legacy.Density;
                    initial.quality.grassDistance = legacy.Radius;
                    initial.quality.grassShadows = legacy.Shadows;
                }
            }
            _initialVSync = QualitySettings.vSyncCount;
            _initialFrameRate = Application.targetFrameRate;
            _initialMipmapLimit = QualitySettings.globalTextureMipmapLimit;
            _initialAnisotropy = QualitySettings.anisotropicFiltering;
            if (SupportsRendering)
            {
                _rendering = new VideoRendering(gameObject);
            }
            Session = new VideoSettingsSession(this, Presets, defaults, initial);
            Session.Changed += SyncTick;
            Session.RestoreAtStartup(Time.realtimeSinceStartupAsDouble);
        }

        private void Update()
        {
            Session.Tick(Time.realtimeSinceStartupAsDouble);
        }

        private void SyncTick()
        {
            enabled = Session.State != VideoApplyState.Idle;
        }

        private void OnDestroy()
        {
            if (Instance != this || Session == null)
            {
                return;
            }
            Session.Changed -= SyncTick;
            _rendering?.Dispose();
            XRayVideoOverrides.Apply(null, null, null, 1.0f);
            QualitySettings.vSyncCount = _initialVSync;
            Application.targetFrameRate = _initialFrameRate;
            QualitySettings.globalTextureMipmapLimit = _initialMipmapLimit;
            QualitySettings.anisotropicFiltering = _initialAnisotropy;
            Texture.SetGlobalAnisotropicFilteringLimits(1, 16);
            Instance = null;
        }
    }
}

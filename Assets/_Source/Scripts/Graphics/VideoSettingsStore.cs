using System;
using System.IO;
using UnityEngine;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.GraphicsOptions
{
    public sealed class VideoSettingsStore
    {
        public const string FILE_NAME = "VideoSettings.json";

        private const int FIRST_SAVE_VERSION = 1;
        private const int LAST_SAVE_VERSION_WITHOUT_CONTACT_SHADOWS = 2;
        private const int LAST_SAVE_VERSION_WITH_LEGACY_TEXTURE_QUALITY = 3;
        private const int LEGACY_MAX_TEXTURE_QUALITY = 2;

        private readonly string _path;

        public VideoSettingsStore(string path)
        {
            _path = path;
        }

        public static string ResolvePath(bool editor, string dataPath, string persistentDataPath, string productName)
        {
            if (editor)
            {
                return Path.Combine(Path.GetDirectoryName(persistentDataPath), productName, FILE_NAME);
            }
            return Path.Combine(Path.GetDirectoryName(dataPath), "appdata", "settings", FILE_NAME);
        }

        public bool TryLoad(out VideoSettings settings, bool upgradeFile = false, float fallbackGrassHeight = XRayGrassValues.DEFAULT_HEIGHT)
        {
            settings = default;
            if (!File.Exists(_path))
            {
                return false;
            }
            try
            {
                settings = Deserialize(File.ReadAllText(_path), fallbackGrassHeight, out bool upgrade);
                if (settings.Valid)
                {
                    if (upgrade && upgradeFile)
                    {
                        TrySave(settings);
                    }
                    return true;
                }
                Debug.LogWarning("Invalid video settings. Using defaults.");
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogWarning("Could not load video settings: " + exception.Message);
            }
            return false;
        }

        private static VideoSettings Deserialize(string json, float fallbackGrassHeight, out bool upgrade)
        {
            VideoSettings settings = JsonUtility.FromJson<VideoSettings>(json);
            if (settings.grassHeight == 0.0f)
            {
                settings.grassHeight = fallbackGrassHeight;
            }
            upgrade = settings.version >= FIRST_SAVE_VERSION && settings.version <= LAST_SAVE_VERSION_WITH_LEGACY_TEXTURE_QUALITY && settings.quality.textureQuality >= 0 && settings.quality.textureQuality <= LEGACY_MAX_TEXTURE_QUALITY;
            if (upgrade)
            {
                if (settings.version <= LAST_SAVE_VERSION_WITHOUT_CONTACT_SHADOWS)
                {
                    VideoPresets.SetPresetContactShadows(ref settings.quality, settings.preset);
                }
                settings.quality.textureQuality += VideoQuality.MAX_TEXTURE_QUALITY - LEGACY_MAX_TEXTURE_QUALITY;
                settings.version = VideoPresets.SAVE_VERSION;
            }
            return settings;
        }

        public bool TrySave(VideoSettings settings)
        {
            string temporaryPath = _path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(settings, true));
                if (File.Exists(_path))
                {
                    File.Replace(temporaryPath, _path, null);
                }
                else
                {
                    File.Move(temporaryPath, _path);
                }
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning("Could not save video settings: " + exception.Message);
                return false;
            }
        }
    }
}

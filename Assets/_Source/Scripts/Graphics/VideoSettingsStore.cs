using System;
using System.IO;
using UnityEngine;

namespace RaidIntoDarkness.GraphicsOptions
{
    public sealed class VideoSettingsStore
    {
        public const string FILE_NAME = "VideoSettings.json";

        private const int FIRST_SAVE_VERSION = 1;
        private const int LAST_SAVE_VERSION_WITHOUT_CONTACT_SHADOWS = 2;

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

        public bool TryLoad(out VideoSettings settings, bool upgradeFile = false)
        {
            settings = default;
            if (!File.Exists(_path))
            {
                return false;
            }
            try
            {
                settings = JsonUtility.FromJson<VideoSettings>(File.ReadAllText(_path));
                bool upgrade = settings.version >= FIRST_SAVE_VERSION && settings.version <= LAST_SAVE_VERSION_WITHOUT_CONTACT_SHADOWS;
                if (upgrade)
                {
                    VideoPresets.SetPresetContactShadows(ref settings.quality, settings.preset);
                    settings.version = VideoPresets.SAVE_VERSION;
                }
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

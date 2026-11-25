using System;
using System.IO;
using R3;
using UnityEngine;

namespace RaidIntoDarkness.Settings
{
    public interface IGameSettings
    {
        ReadOnlyReactiveProperty<bool> ShowItemDescriptions { get; }
        bool TrySetShowItemDescriptions(bool value);
    }

    public sealed class GameSettingsStore : IGameSettings, IDisposable
    {
        public const string FILE_NAME = "GameSettings.json";
        public const bool DEFAULT_SHOW_ITEM_DESCRIPTIONS = true;

        private readonly string _path;
        private readonly ReactiveProperty<bool> _showItemDescriptions;

        public ReadOnlyReactiveProperty<bool> ShowItemDescriptions => _showItemDescriptions;

        public GameSettingsStore(string path)
        {
            _path = path;
            _showItemDescriptions = new ReactiveProperty<bool>(Load());
        }

        public bool TrySetShowItemDescriptions(bool value)
        {
            if (value == _showItemDescriptions.Value)
            {
                return true;
            }

            try
            {
                Save(value);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning("Could not save game settings: " + exception.Message);
                return false;
            }

            _showItemDescriptions.Value = value;
            return true;
        }

        public void Dispose() => _showItemDescriptions.Dispose();

        private bool Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    var data = new Data();
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(_path), data);
                    return data.showItemDescriptions;
                }

                Save(DEFAULT_SHOW_ITEM_DESCRIPTIONS);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogWarning("Could not initialize game settings: " + exception.Message);
            }

            return DEFAULT_SHOW_ITEM_DESCRIPTIONS;
        }

        private void Save(bool showItemDescriptions)
        {
            SettingsFile.Write(_path, JsonUtility.ToJson(new Data { showItemDescriptions = showItemDescriptions }, true));
        }

        [Serializable]
        private sealed class Data
        {
            public bool showItemDescriptions = DEFAULT_SHOW_ITEM_DESCRIPTIONS;
        }
    }
}

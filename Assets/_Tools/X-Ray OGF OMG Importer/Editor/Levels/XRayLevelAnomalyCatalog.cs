using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace XRay.Importer.Editor.Levels
{
    [Serializable]
    public sealed class XRayLevelAnomalyEntry
    {
        [SerializeField] private string _section;
        [SerializeField] private string _displayName;
        [SerializeField] private GameObject _prefab;

        public string Section => _section;
        public string DisplayName => string.IsNullOrWhiteSpace(_displayName) ? _section : _displayName;
        public GameObject Prefab => _prefab;
    }

    [CreateAssetMenu(fileName = "XRayLevelAnomalyCatalog", menuName = "X-Ray/Levels/Anomaly Catalog")]
    public sealed class XRayLevelAnomalyCatalog : ScriptableObject
    {
        public const string DEFAULT_ASSET_PATH = "Assets/_Tools/X-Ray OGF OMG Importer/Editor/Levels/XRayLevelAnomalyCatalog.asset";

        [SerializeField, Tooltip("Секции аномалий из all.spawn. Для отсутствующих в проекте аномалий оставьте Prefab пустым: импортёр покажет их названия и количество.")] private List<XRayLevelAnomalyEntry> _entries = new List<XRayLevelAnomalyEntry>();

        public IReadOnlyList<XRayLevelAnomalyEntry> Entries => _entries != null ? _entries : Array.Empty<XRayLevelAnomalyEntry>();

        public static XRayLevelAnomalyCatalog LoadDefault()
        {
            return AssetDatabase.LoadAssetAtPath<XRayLevelAnomalyCatalog>(DEFAULT_ASSET_PATH);
        }

        public bool TryGet(string section, out XRayLevelAnomalyEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(section) && _entries != null)
            {
                foreach (XRayLevelAnomalyEntry candidate in _entries)
                {
                    if (candidate != null && string.Equals(candidate.Section, section, StringComparison.OrdinalIgnoreCase))
                    {
                        entry = candidate;
                        return true;
                    }
                }
            }

            entry = null;
            return false;
        }

        public bool Validate(out string error)
        {
            var sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_entries != null)
            {
                foreach (XRayLevelAnomalyEntry entry in _entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Section))
                    {
                        error = "В конфиге аномалий есть запись без секции all.spawn.";
                        return false;
                    }

                    if (!sections.Add(entry.Section))
                    {
                        error = "В конфиге аномалий повторяется секция: " + entry.Section + ".";
                        return false;
                    }

                    if (entry.Prefab != null && (!PrefabUtility.IsPartOfPrefabAsset(entry.Prefab) || entry.Prefab.transform.parent != null))
                    {
                        error = "Для аномалии «" + entry.DisplayName + "» требуется корневой объект prefab asset: " + entry.Section + ".";
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace XRay.Importer.Levels
{
    public sealed class XRayLevelAnomalyGroup : MonoBehaviour
    {
        [SerializeField] private string _levelName;
        [SerializeField] private string _spawnAssetPath;
        [SerializeField] private string _sourceOrigin;
        [SerializeField] private string _sourceSha256;
        [SerializeField] private int _importedCount;
        [SerializeField] private List<string> _missing = new List<string>();
        [SerializeField] private List<string> _unsupported = new List<string>();

        public string LevelName => _levelName;
        public string SpawnAssetPath => _spawnAssetPath;
        public string SourceOrigin => _sourceOrigin;
        public string SourceSha256 => _sourceSha256;
        public int ImportedCount => _importedCount;
        public IReadOnlyList<string> Missing => _missing;
        public IReadOnlyList<string> Unsupported => _unsupported;

        public void Initialize(string levelName, string spawnAssetPath, string sourceOrigin, string sourceSha256, int importedCount, List<string> missing, List<string> unsupported)
        {
            _levelName = levelName;
            _spawnAssetPath = spawnAssetPath;
            _sourceOrigin = sourceOrigin;
            _sourceSha256 = sourceSha256;
            _importedCount = importedCount;
            _missing = new List<string>(missing);
            _unsupported = new List<string>(unsupported);
        }
    }
}

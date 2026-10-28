using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Sources;
using XRay.Importer.Core.IO;
using XRay.Importer.Sources.Levels;
using XRay.Importer.Sources.Textures;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    [Serializable]
    public sealed class XRayLevelSourceFile
    {
        [SerializeField] private string _virtualPath;
        [SerializeField] private string _role;
        [SerializeField] private string _source;
        [SerializeField] private string _origin;
        [SerializeField] private string _kind;
        [SerializeField] private string _priority;
        [SerializeField] private long _size;
        [SerializeField] private long _modifiedTicks;
        [SerializeField] private string _location;
        [SerializeField] private string _contentHash;
        [SerializeField] private string _assetPath;
        [SerializeField] private string _assetGuid;
        [SerializeField] private List<string> _losers = new List<string>();

        public string VirtualPath
        {
            get => _virtualPath;
            set => _virtualPath = value;
        }

        public string Role
        {
            get => _role;
            set => _role = value;
        }

        public string Source
        {
            get => _source;
            set => _source = value;
        }

        public string Origin
        {
            get => _origin;
            set => _origin = value;
        }

        public string Kind
        {
            get => _kind;
            set => _kind = value;
        }

        public string Priority
        {
            get => _priority;
            set => _priority = value;
        }

        public long Size
        {
            get => _size;
            set => _size = value;
        }

        public long ModifiedTicks
        {
            get => _modifiedTicks;
            set => _modifiedTicks = value;
        }

        public string Location
        {
            get => _location;
            set => _location = value;
        }

        public string ContentHash
        {
            get => _contentHash;
            set => _contentHash = value;
        }

        public string AssetPath
        {
            get => _assetPath;
            set => _assetPath = value;
        }

        public string AssetGuid
        {
            get => _assetGuid;
            set => _assetGuid = value;
        }

        public List<string> Losers => _losers;
    }

    [Serializable]
    public sealed class XRayLevelSourceProvenance
    {
        [SerializeField] private string _sourceType;
        [SerializeField] private string _sourceFolder;
        [SerializeField] private string _mo2Instance;
        [SerializeField] private string _mo2Profile;
        [SerializeField] private string _profileId;
        [SerializeField] private string _gameFolder;
        [SerializeField] private string _fsGame;
        [SerializeField] private string _profileFingerprint;
        [SerializeField] private string _indexFingerprint;
        [SerializeField] private string _indexedUtc;
        [SerializeField] private string _importerVersion;
        [SerializeField] private int _thmParserVersion;
        [SerializeField] private string _levelVirtualPath;
        [SerializeField] private string _levelOrigin;
        [SerializeField] private string _terrainProfile;
        [SerializeField] private string _terrainShaderPath;
        [SerializeField] private string _terrainShaderHash;
        [SerializeField] private string _terrainBrdfHash;
        [SerializeField] private bool _levelRegistered;
        [SerializeField] private List<string> _modOrder = new List<string>();
        [SerializeField] private List<XRayLevelSourceFile> _files = new List<XRayLevelSourceFile>();
        [SerializeField] private List<string> _changesSincePrevious = new List<string>();

        public string SourceType
        {
            get => _sourceType;
            set => _sourceType = value;
        }

        public string TerrainProfile
        {
            get => _terrainProfile;
            set => _terrainProfile = value;
        }
        public string TerrainShaderPath
        {
            get => _terrainShaderPath;
            set => _terrainShaderPath = value;
        }
        public string TerrainShaderHash
        {
            get => _terrainShaderHash;
            set => _terrainShaderHash = value;
        }
        public string TerrainBrdfHash
        {
            get => _terrainBrdfHash;
            set => _terrainBrdfHash = value;
        }

        public string SourceFolder
        {
            get => _sourceFolder;
            set => _sourceFolder = value;
        }

        public string Mo2Instance
        {
            get => _mo2Instance;
            set => _mo2Instance = value;
        }

        public string Mo2Profile
        {
            get => _mo2Profile;
            set => _mo2Profile = value;
        }

        public string ProfileId
        {
            get => _profileId;
            set => _profileId = value;
        }

        public string GameFolder
        {
            get => _gameFolder;
            set => _gameFolder = value;
        }

        public string FsGame
        {
            get => _fsGame;
            set => _fsGame = value;
        }

        public string ProfileFingerprint
        {
            get => _profileFingerprint;
            set => _profileFingerprint = value;
        }

        public string IndexFingerprint
        {
            get => _indexFingerprint;
            set => _indexFingerprint = value;
        }

        public string IndexedUtc
        {
            get => _indexedUtc;
            set => _indexedUtc = value;
        }

        public string ImporterVersion
        {
            get => _importerVersion;
            set => _importerVersion = value;
        }

        public int ThmParserVersion
        {
            get => _thmParserVersion;
            set => _thmParserVersion = value;
        }

        public string LevelVirtualPath
        {
            get => _levelVirtualPath;
            set => _levelVirtualPath = value;
        }

        public string LevelOrigin
        {
            get => _levelOrigin;
            set => _levelOrigin = value;
        }

        public bool LevelRegistered
        {
            get => _levelRegistered;
            set => _levelRegistered = value;
        }

        public List<string> ModOrder => _modOrder;
        public List<XRayLevelSourceFile> Files => _files;
        public List<string> ChangesSincePrevious => _changesSincePrevious;

        public XRayLevelSourceFile Find(string virtualPath)
        {
            return _files.Find(file => file.VirtualPath == virtualPath);
        }
    }

    public enum XRayLevelSourceChangeKind
    {
        ContentChanged,
        WinnerChanged,
        Added,
        Removed
    }

    public sealed class XRayLevelSourceChange
    {
        public string VirtualPath { get; set; }
        public XRayLevelSourceChangeKind Kind { get; set; }
        public string Category { get; set; }
        public string Previous { get; set; }
        public string Current { get; set; }
        public string AssetPath { get; set; }

        public override string ToString()
        {
            return $"{Kind} [{Category}] {VirtualPath}: {Previous ?? "-"} -> {Current ?? "-"}";
        }
    }

    public sealed class XRayLevelSourceDiff
    {
        public string Level { get; set; }
        public bool HasPrevious { get; set; }
        public bool ProfileChanged { get; set; }
        public List<XRayLevelSourceChange> Changes { get; } = new List<XRayLevelSourceChange>();
        public List<string> AffectedMaterials { get; } = new List<string>();
        public List<string> MetadataReparse { get; } = new List<string>();
        public bool ReimportRequired => Changes.Count > 0 || MetadataReparse.Count > 0;

        public int Count(string category)
        {
            return Changes.FindAll(change => change.Category == category).Count;
        }
    }

    public static class XRayLevelSourceManifest
    {
        public const string SOURCE_TYPE_FOLDER = "GamedataFolder";
        public const string SOURCE_TYPE_MO2 = "Mo2Profile";
        public const string CATEGORY_LEVEL = "Level file";
        public const string CATEGORY_TEXTURE = "DDS";
        public const string CATEGORY_BUMP = "Bump";
        public const string CATEGORY_THM = "THM";
        public const string CATEGORY_OTHER = "Other";
        public static string HASH_CACHE_PATH => Path.Combine(XRayLevelPaths.Mo2Cache, "ContentHashes.txt");

        private static Dictionary<string, string> _hashes;

        public static void FillHeader(XRayLevelSourceProvenance provenance, XRaySourcePlan plan, XRayAssetIndex index, XRayLevelEntry entry, string profileId)
        {
            provenance.SourceType = SOURCE_TYPE_MO2;
            provenance.Mo2Instance = plan.Installation?.InstanceFolder;
            provenance.Mo2Profile = plan.ProfileName;
            provenance.ProfileId = profileId;
            provenance.GameFolder = plan.GameFolder;
            provenance.FsGame = plan.FsGamePath ?? plan.FsGame?.Origin;
            provenance.ProfileFingerprint = plan.ModOrderFingerprint;
            provenance.IndexFingerprint = index.Fingerprint;
            provenance.IndexedUtc = new DateTime(index.Statistics.BuiltAtUtcTicks, DateTimeKind.Utc).ToString("u");
            provenance.ImporterVersion = XRayLevelImportJob.IMPORTER_VERSION;
            provenance.ThmParserVersion = XRayThmReader.PARSER_VERSION;
            provenance.LevelVirtualPath = entry.VirtualFolder + "/level";
            provenance.LevelOrigin = entry.Origin;
            provenance.LevelRegistered = entry.IsRegistered;
            provenance.ModOrder.Clear();
            provenance.ModOrder.AddRange(plan.ActiveModOrder);
        }

        public static void FillFolderHeader(XRayLevelSourceProvenance provenance, XRayLevelGamedataInfo info)
        {
            provenance.SourceType = SOURCE_TYPE_FOLDER;
            provenance.SourceFolder = info.Root;
            provenance.ImporterVersion = XRayLevelImportJob.IMPORTER_VERSION;
        }

        public static void Collect(XRayLevelImportRecord record, XRayEffectiveGamedata gamedata, XRayLevelEntry entry)
        {
            var files = new Dictionary<string, XRayLevelSourceFile>(StringComparer.Ordinal);
            foreach (XRayEffectiveFile file in gamedata.Enumerate(entry.VirtualFolder ?? "levels/" + entry.Name, true))
            {
                Add(files, gamedata, file.VirtualPath, "Level folder", null);
            }

            foreach (XRayLevelDependency dependency in record.Dependencies)
            {
                string virtualPath = dependency.SearchPath ?? dependency.Reference;
                if (string.IsNullOrEmpty(virtualPath) || !XRayVirtualPath.TryNormalizeSafe(virtualPath, out string normalized))
                {
                    continue;
                }

                XRayEffectiveFile winner = gamedata.Resolve(normalized);
                if (winner == null)
                {
                    continue;
                }

                IReadOnlyList<XRayEffectiveFile> candidates = gamedata.GetCandidates(normalized);
                dependency.SetSource(winner.SourceName, winner.Origin, winner.DisplayPriority, winner.Size, Losers(candidates));
                Add(files, gamedata, normalized, dependency.Role, dependency.AssetPath);
            }

            foreach (string shared in new[] { "shaders.xr", "gamemtl.xr", "configs/system.ltx", "textures.ltx" })
            {
                if (gamedata.Exists(shared))
                {
                    Add(files, gamedata, shared, "Global", null);
                }
            }

            var list = new List<XRayLevelSourceFile>(files.Values);
            list.Sort((a, b) => string.CompareOrdinal(a.VirtualPath, b.VirtualPath));
            record.Source.Files.Clear();
            record.Source.Files.AddRange(list);
            SaveHashes();
        }

        public static XRayLevelSourceDiff Compare(XRayLevelImportRecord previous, XRayEffectiveGamedata gamedata, XRayLevelEntry entry, XRaySourcePlan plan)
        {
            var diff = new XRayLevelSourceDiff { Level = entry?.Name ?? previous?.LevelName };
            if (previous == null || previous.Source == null || (previous.Source.SourceType != SOURCE_TYPE_MO2 && previous.Source.SourceType != SOURCE_TYPE_FOLDER) || previous.Source.Files.Count == 0)
            {
                return diff;
            }

            diff.HasPrevious = true;
            diff.ProfileChanged = plan != null && plan.ModOrderFingerprint != previous.Source.ProfileFingerprint;
            if (previous.Source.ThmParserVersion < XRayThmReader.PARSER_VERSION)
            {
                foreach (XRayLevelSourceFile file in previous.Source.Files)
                {
                    if (Categorize(file.VirtualPath) == CATEGORY_THM && ParsedDifferently(gamedata, file.VirtualPath))
                    {
                        diff.MetadataReparse.Add(file.VirtualPath);
                    }
                }
            }

            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (XRayLevelSourceFile file in previous.Source.Files)
            {
                known.Add(file.VirtualPath);
                XRayEffectiveFile current = gamedata.Resolve(file.VirtualPath);
                if (current == null)
                {
                    diff.Changes.Add(new XRayLevelSourceChange { VirtualPath = file.VirtualPath, Kind = XRayLevelSourceChangeKind.Removed, Category = Categorize(file.VirtualPath), Previous = file.Origin, AssetPath = file.AssetPath });
                    continue;
                }

                if (current.Location == file.Location && current.Size == file.Size && current.ModifiedTicks == file.ModifiedTicks)
                {
                    continue;
                }

                if (current.SourceName != file.Source)
                {
                    diff.Changes.Add(new XRayLevelSourceChange { VirtualPath = file.VirtualPath, Kind = XRayLevelSourceChangeKind.WinnerChanged, Category = Categorize(file.VirtualPath), Previous = file.Origin, Current = current.Origin, AssetPath = file.AssetPath });
                    continue;
                }

                if (current.Size == file.Size && ContentHash(gamedata, current) == file.ContentHash)
                {
                    continue;
                }

                diff.Changes.Add(new XRayLevelSourceChange { VirtualPath = file.VirtualPath, Kind = XRayLevelSourceChangeKind.ContentChanged, Category = Categorize(file.VirtualPath), Previous = $"{file.Origin} ({file.Size} B)", Current = $"{current.Origin} ({current.Size} B)", AssetPath = file.AssetPath });
            }

            foreach (XRayLevelDependency dependency in previous.Dependencies)
            {
                if (!string.IsNullOrEmpty(dependency.SearchPath) && XRayVirtualPath.TryNormalizeSafe(dependency.SearchPath, out string path) && !known.Contains(path))
                {
                    XRayEffectiveFile added = gamedata.Resolve(path);
                    if (added != null)
                    {
                        known.Add(path);
                        diff.Changes.Add(new XRayLevelSourceChange { VirtualPath = path, Kind = XRayLevelSourceChangeKind.Added, Category = Categorize(path), Current = added.Origin, AssetPath = dependency.AssetPath });
                    }
                }
            }

            if (entry != null)
            {
                foreach (XRayEffectiveFile file in gamedata.Enumerate(entry.VirtualFolder ?? "levels/" + entry.Name, true))
                {
                    if (!known.Contains(file.VirtualPath))
                    {
                        diff.Changes.Add(new XRayLevelSourceChange { VirtualPath = file.VirtualPath, Kind = XRayLevelSourceChangeKind.Added, Category = Categorize(file.VirtualPath), Current = file.Origin });
                    }
                }
            }

            CollectMaterials(previous, diff);
            return diff;
        }

        public static bool ParsedDifferently(XRayEffectiveGamedata gamedata, string virtualPath)
        {
            try
            {
                return gamedata.Exists(virtualPath) && XRayThmReader.Read(gamedata.ReadAllBytes(virtualPath), virtualPath).CorrectedChunks.Count > 0;
            }
            catch (Exception exception) when (exception is XRayFormatException || exception is XRaySourceException || exception is IOException)
            {
                return false;
            }
        }

        public static string Categorize(string virtualPath)
        {
            if (virtualPath.StartsWith("levels/", StringComparison.Ordinal) && !virtualPath.EndsWith(".dds", StringComparison.Ordinal))
            {
                return CATEGORY_LEVEL;
            }

            if (virtualPath.EndsWith(".thm", StringComparison.Ordinal))
            {
                return CATEGORY_THM;
            }

            if (virtualPath.EndsWith(".dds", StringComparison.Ordinal))
            {
                string name = Path.GetFileNameWithoutExtension(virtualPath);
                return name.EndsWith("_bump", StringComparison.Ordinal) || name.EndsWith("_bump#", StringComparison.Ordinal) || name.EndsWith("_nm", StringComparison.Ordinal) ? CATEGORY_BUMP : CATEGORY_TEXTURE;
            }

            return CATEGORY_OTHER;
        }

        public static string ContentHash(XRayEffectiveGamedata gamedata, XRayEffectiveFile file)
        {
            if (file.IsArchive)
            {
                XRayArchive archive = gamedata.Index.GetArchive(file.Candidate);
                return archive.TryGet(file.RelativePath, out XRayArchiveEntry entry) ? "crc32:" + entry.Crc.ToString("x8") : null;
            }

            LoadHashes();
            string key = file.Location + "|" + file.Size + "|" + file.ModifiedTicks;
            if (_hashes.TryGetValue(key, out string hash))
            {
                return hash;
            }

            using (FileStream stream = File.OpenRead(file.PhysicalPath))
            using (SHA256 sha = SHA256.Create())
            {
                hash = "sha256:" + BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }

            _hashes[key] = hash;
            return hash;
        }

        private static void Add(Dictionary<string, XRayLevelSourceFile> files, XRayEffectiveGamedata gamedata, string virtualPath, string role, string assetPath)
        {
            if (files.TryGetValue(virtualPath, out XRayLevelSourceFile existing))
            {
                if (existing.AssetPath == null && assetPath != null)
                {
                    existing.AssetPath = assetPath;
                    existing.AssetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                }

                return;
            }

            IReadOnlyList<XRayEffectiveFile> candidates = gamedata.GetCandidates(virtualPath);
            if (candidates.Count == 0)
            {
                return;
            }

            XRayEffectiveFile winner = candidates[0];
            var file = new XRayLevelSourceFile
            {
                VirtualPath = virtualPath,
                Role = role,
                Source = winner.SourceName,
                Origin = winner.Origin,
                Kind = winner.Kind.ToString(),
                Priority = winner.DisplayPriority,
                Size = winner.Size,
                ModifiedTicks = winner.ModifiedTicks,
                Location = winner.Location,
                AssetPath = assetPath,
                AssetGuid = assetPath != null ? AssetDatabase.AssetPathToGUID(assetPath) : null
            };
            try
            {
                file.ContentHash = ContentHash(gamedata, winner);
            }
            catch (Exception exception) when (exception is IOException || exception is XRaySourceException || exception is UnauthorizedAccessException)
            {
                file.ContentHash = "error: " + exception.Message;
            }

            for (int i = 1; i < candidates.Count; i++)
            {
                file.Losers.Add($"{candidates[i].Origin} ({candidates[i].DisplayPriority})");
            }

            files.Add(virtualPath, file);
        }

        private static string Losers(IReadOnlyList<XRayEffectiveFile> candidates)
        {
            if (candidates.Count < 2)
            {
                return null;
            }

            var builder = new StringBuilder();
            for (int i = 1; i < candidates.Count; i++)
            {
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(candidates[i].Origin);
            }

            return builder.ToString();
        }

        private static void CollectMaterials(XRayLevelImportRecord previous, XRayLevelSourceDiff diff)
        {
            var textures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (XRayLevelSourceChange change in diff.Changes)
            {
                if (!string.IsNullOrEmpty(change.AssetPath))
                {
                    textures.Add(change.AssetPath);
                }
            }

            if (textures.Count == 0)
            {
                return;
            }

            foreach (string asset in previous.GeneratedAssets)
            {
                if (!asset.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) || diff.AffectedMaterials.Contains(asset))
                {
                    continue;
                }

                foreach (string dependency in AssetDatabase.GetDependencies(asset, false))
                {
                    if (textures.Contains(dependency))
                    {
                        diff.AffectedMaterials.Add(asset);
                        break;
                    }
                }
            }
        }

        private static void LoadHashes()
        {
            if (_hashes != null)
            {
                return;
            }

            _hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(HASH_CACHE_PATH))
            {
                return;
            }

            foreach (string line in File.ReadAllLines(HASH_CACHE_PATH, Encoding.UTF8))
            {
                int separator = line.LastIndexOf('\t');
                if (separator > 0)
                {
                    _hashes[line.Substring(0, separator)] = line.Substring(separator + 1);
                }
            }
        }

        private static void SaveHashes()
        {
            if (_hashes == null)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(HASH_CACHE_PATH));
            var lines = new List<string>(_hashes.Count);
            foreach (KeyValuePair<string, string> pair in _hashes)
            {
                lines.Add(pair.Key + "\t" + pair.Value);
            }

            File.WriteAllLines(HASH_CACHE_PATH, lines, Encoding.UTF8);
        }
    }
}

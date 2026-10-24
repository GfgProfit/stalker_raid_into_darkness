using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using XRay.Importer.Core.IO;
using XRay.Importer.Core.Level;
using XRay.Importer.Sources;
using XRay.Importer.Sources.Levels;
using XRay.Importer.Sources.Mo2;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayLevelSourceCache
    {
        public const string INDEX_PATH = XRayAssetIndexCache.CACHE_FOLDER + "/LevelIndex.bin";
        public const int PROFILE_SLUG_LENGTH = 40;
        public static readonly string[] MO2_INDEX_SCOPE = { "textures", "textures.ltx", "levels", "shaders.xr", "gamemtl.xr", "configs", "shaders" };

        private static XRayLevelGamedataInfo _info;
        private static string _infoFolder;
        private static XRayAssetIndex _index;
        private static XRayShaderLibrary _shaders;
        private static string _shadersPath;
        private static XRayGameMaterials _materials;
        private static string _materialsPath;
        private static XRayLevelGamedataInfo _mo2Info;
        private static string _mo2InfoFingerprint;
        private static XRayAssetIndex _mo2Index;
        private static XRaySourcePlan _mo2Plan;
        private static XRayLevelProbeJob _probeJob;
        private static readonly Dictionary<string, XRayLevelSourceDiff> _diffs = new Dictionary<string, XRayLevelSourceDiff>(StringComparer.OrdinalIgnoreCase);

        public static XRaySourcePlan LastPlan { get; private set; }
        public static string MO2_INDEX_PREFIX => Path.Combine(XRayLevelPaths.Mo2Cache, "Mo2LevelIndex_");
        public static string EXTRACTION_FOLDER => Path.Combine(XRayLevelPaths.Mo2Cache, "ArchiveCache");
        public static XRayAssetIndex CurrentIndex => IsMo2 ? _mo2Index : _index;
        public static XRaySourcePlan Mo2Plan => _mo2Plan;
        public static bool IsMo2 => XRayAssetSourceUserSettings.instance.LevelSourceType == XRayLevelSourceType.Mo2Profile;
        public static string ProfileId => IsMo2 && _mo2Plan?.Installation != null && _mo2Plan.ProfileName != null ? CreateProfileId(_mo2Plan.Installation.InstanceFolder, _mo2Plan.ProfileName) : null;
        public static string AssetRoot => IsMo2 ? XRayLevelPaths.SpaceRoot(ProfileId) : XRayLevelPaths.ROOT;
        public static XRayLevelIndexJob CurrentBuild { get; private set; }
        public static bool IsProbing => _probeJob != null && !_probeJob.IsDone;
        public static int OverwriteFiles { get; private set; }

        public static event Action Changed;

        public static XRayLevelGamedataInfo Inspect(bool refresh)
        {
            if (IsMo2)
            {
                return InspectMo2(refresh);
            }

            string folder = XRayAssetSourceUserSettings.instance.LevelGamedataFolder;
            if (refresh || _info == null || _infoFolder != folder)
            {
                _info = XRayLevelGamedata.Inspect(folder);
                _infoFolder = folder;
            }

            return _info;
        }

        public static XRayAssetIndex GetIndex(bool rebuild)
        {
            if (IsMo2)
            {
                return GetMo2Index(rebuild, null);
            }

            XRayLevelGamedataInfo info = Inspect(rebuild);
            XRayAssetSourceUserSettings user = XRayAssetSourceUserSettings.instance;
            XRaySourcePlan fallback = user.LevelAllowFallbackSources ? Mo2VirtualFileSystem.Plan(XRayAssetSourceSettings.GetEffective().Sources) : null;
            XRaySourcePlan plan = XRayLevelGamedata.CreatePlan(info, fallback, user.LevelAllowFallbackSources);
            LastPlan = plan;
            if (!plan.IsValid)
            {
                return null;
            }

            if (!rebuild && _index != null && _index.Fingerprint == plan.Fingerprint)
            {
                return _index;
            }

            if (!rebuild)
            {
                XRayAssetIndex cached = XRayAssetIndex.Load(INDEX_PATH);
                if (cached != null && cached.Fingerprint == plan.Fingerprint)
                {
                    _index = cached;
                    return _index;
                }
            }

            _index = XRayAssetIndex.Build(plan);
            _index.Save(INDEX_PATH);
            return _index;
        }

        public static XRaySourceConfiguration CreateMo2Configuration()
        {
            XRayAssetSourceUserSettings user = XRayAssetSourceUserSettings.instance;
            var configuration = new XRaySourceConfiguration
            {
                Mode = XRaySourceMode.Mo2Effective,
                Mo2Folder = user.LevelMo2Folder,
                Mo2Instance = user.LevelMo2Instance,
                Mo2Profile = user.LevelMo2Profile,
                ModsFolder = user.LevelModsFolder,
                ProfilesFolder = user.LevelProfilesFolder,
                OverwriteFolder = user.LevelOverwriteFolder,
                GameFolder = string.IsNullOrWhiteSpace(user.LevelGameFolder) ? null : user.LevelGameFolder.Trim(),
                LocalAppDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                IncludeGameGamedata = true
            };
            configuration.IndexScope.Clear();
            configuration.IndexScope.AddRange(MO2_INDEX_SCOPE);
            return configuration;
        }

        public static XRaySourcePlan PlanMo2()
        {
            _mo2Plan = Mo2VirtualFileSystem.Plan(CreateMo2Configuration());
            LastPlan = _mo2Plan;
            return _mo2Plan;
        }

        public static XRayAssetIndex GetMo2Index(bool rebuild, List<string> notes)
        {
            XRaySourcePlan plan = PlanMo2();
            if (!plan.IsValid)
            {
                return null;
            }

            if (!rebuild && _mo2Index != null && _mo2Index.Fingerprint == plan.Fingerprint)
            {
                return _mo2Index;
            }

            string path = Mo2IndexPath(plan);
            if (!rebuild)
            {
                XRayAssetIndex cached = XRayAssetIndex.Load(path);
                if (cached != null && cached.Fingerprint == plan.Fingerprint)
                {
                    SetMo2Index(cached);
                    return _mo2Index;
                }
            }

            notes?.Add(_mo2Index == null ? "The virtual index was built for this import." : $"The MO2 profile changed since the virtual index was built ({_mo2Index.Fingerprint} -> {plan.Fingerprint}); the index was rebuilt before the import.");
            XRayAssetIndex index = XRayAssetIndex.Build(plan);
            index.Save(path);
            SetMo2Index(index);
            return _mo2Index;
        }

        public static void BeginMo2Build()
        {
            if (CurrentBuild != null && !CurrentBuild.IsDone)
            {
                return;
            }

            XRaySourcePlan plan = PlanMo2();
            if (!plan.IsValid)
            {
                Changed?.Invoke();
                return;
            }

            CurrentBuild = XRayLevelIndexJob.Start(plan, Mo2IndexPath(plan), OnBuildFinished);
        }

        public static void CancelMo2Build()
        {
            CurrentBuild?.Cancel();
        }

        public static string Mo2IndexPath(XRaySourcePlan plan)
        {
            return MO2_INDEX_PREFIX + CreateProfileId(plan.Installation.InstanceFolder, plan.ProfileName) + ".bin";
        }

        public static string CreateProfileId(string instanceFolder, string profile)
        {
            var builder = new StringBuilder();
            foreach (char ch in profile ?? string.Empty)
            {
                bool ascii = ch < 128 && (char.IsLetterOrDigit(ch) || ch == '.' || ch == '-');
                if (ascii)
                {
                    builder.Append(ch);
                }
                else if (builder.Length > 0 && builder[builder.Length - 1] != '_')
                {
                    builder.Append('_');
                }
            }

            string slug = builder.ToString().Trim('_', '.');
            if (slug.Length > PROFILE_SLUG_LENGTH)
            {
                slug = slug.Substring(0, PROFILE_SLUG_LENGTH).TrimEnd('_', '.');
            }

            string hash = XRayFileSystem.Hash((instanceFolder ?? string.Empty).TrimEnd('\\', '/').ToLowerInvariant() + "|" + (profile ?? string.Empty).ToLowerInvariant()).Substring(0, 8);
            return (slug.Length > 0 ? slug + "_" : "profile_") + hash;
        }

        public static XRayLevelSourceDiff GetDiff(XRayLevelEntry entry)
        {
            if (_mo2Info == null || _mo2Info.Effective == null)
            {
                return null;
            }

            string manifestPath = XRayLevelPaths.ManifestPath(AssetRoot, entry.Name);
            string key = entry.Name + "|" + File.GetLastWriteTimeUtc(manifestPath).Ticks;
            if (_diffs.TryGetValue(key, out XRayLevelSourceDiff diff))
            {
                return diff;
            }

            var manifest = UnityEditor.AssetDatabase.LoadAssetAtPath<XRayLevelImportManifest>(manifestPath);
            try
            {
                diff = XRayLevelSourceManifest.Compare(manifest != null ? manifest.Record : null, _mo2Info.Effective, entry, _mo2Plan);
            }
            catch (Exception exception) when (exception is IOException || exception is XRaySourceException)
            {
                diff = new XRayLevelSourceDiff { Level = entry.Name };
                UnityEngine.Debug.LogWarning($"[X-Ray Level] Cannot compare {entry.Name} with its previous import: {exception.Message}");
            }

            _diffs[key] = diff;
            return diff;
        }

        public static void Clear()
        {
            _info = null;
            _index = null;
            _shaders = null;
            _materials = null;
            _mo2Info = null;
            _mo2Index = null;
            _mo2InfoFingerprint = null;
            if (File.Exists(INDEX_PATH))
            {
                File.Delete(INDEX_PATH);
            }
        }

        public static long ClearArchiveCache()
        {
            if (!Directory.Exists(EXTRACTION_FOLDER))
            {
                return 0;
            }

            long size = GetArchiveCacheSize();
            Directory.Delete(EXTRACTION_FOLDER, true);
            return size;
        }

        public static long GetArchiveCacheSize()
        {
            if (!Directory.Exists(EXTRACTION_FOLDER))
            {
                return 0;
            }

            long size = 0;
            foreach (string file in Directory.GetFiles(EXTRACTION_FOLDER, "*", SearchOption.AllDirectories))
            {
                size += new FileInfo(file).Length;
            }

            return size;
        }

        public static XRayShaderLibrary GetShaders(XRayLevelGamedataInfo info, List<string> warnings)
        {
            string key = info.Effective != null ? info.Describe("shaders.xr") : info.ShadersXrPath;
            if (key == null)
            {
                return null;
            }

            if (_shaders == null || _shadersPath != key)
            {
                try
                {
                    byte[] data = info.Effective != null ? info.ReadAllBytes("shaders.xr") : File.ReadAllBytes(info.ShadersXrPath);
                    _shaders = XRayShaderLibrary.Read(data, key);
                    _shadersPath = key;
                }
                catch (Exception exception) when (exception is IOException || exception is XRayFormatException || exception is XRaySourceException)
                {
                    warnings.Add($"Cannot read '{key}': {exception.Message}");
                    return null;
                }
            }

            return _shaders;
        }

        public static XRayGameMaterials GetGameMaterials(XRayLevelGamedataInfo info, List<string> warnings)
        {
            string key = info.Effective != null ? info.Describe("gamemtl.xr") : info.GameMaterialsPath;
            if (key == null)
            {
                return null;
            }

            if (_materials == null || _materialsPath != key)
            {
                try
                {
                    byte[] data = info.Effective != null ? info.ReadAllBytes("gamemtl.xr") : File.ReadAllBytes(info.GameMaterialsPath);
                    _materials = XRayGameMaterials.Read(data, key);
                    _materialsPath = key;
                }
                catch (Exception exception) when (exception is IOException || exception is XRayFormatException || exception is XRaySourceException)
                {
                    warnings.Add($"Cannot read '{key}': {exception.Message}");
                    return null;
                }
            }

            return _materials;
        }

        public static Dictionary<string, float> ReadLtxSection(XRayLevelGamedataInfo info, string virtualPath, string section)
        {
            if (info.Effective == null)
            {
                return ReadLtxSection(info.SystemLtxPath, section);
            }

            return info.Exists(virtualPath) ? ParseLtxSection(info.ReadAllBytes(virtualPath), section) : new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        }

        public static Dictionary<string, float> ReadLtxSection(string path, string section)
        {
            if (path == null || !File.Exists(path))
            {
                return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            }

            return ParseLtxSection(File.ReadAllBytes(path), section);
        }

        private static Dictionary<string, float> ParseLtxSection(byte[] data, string section)
        {
            var values = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            string text = Cp1251.Decode(data, 0, data.Length);
            bool inside = false;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw;
                int comment = line.IndexOf(';');
                if (comment >= 0)
                {
                    line = line.Substring(0, comment);
                }

                line = line.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    int end = line.IndexOf(']');
                    inside = end > 0 && string.Equals(line.Substring(1, end - 1).Trim(), section, StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                int separator = line.IndexOf('=');
                if (!inside || separator <= 0)
                {
                    continue;
                }

                if (float.TryParse(line.Substring(separator + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                {
                    values[line.Substring(0, separator).Trim()] = value;
                }
            }

            return values;
        }

        private static XRayLevelGamedataInfo InspectMo2(bool refresh)
        {
            if (!refresh && _mo2Info != null)
            {
                return _mo2Info;
            }

            XRaySourcePlan plan = PlanMo2();
            if (!plan.IsValid)
            {
                _mo2Info = XRayLevelGamedata.Failed(XRayAssetSourceUserSettings.instance.LevelMo2Folder, plan.Errors.Count > 0 ? plan.Errors : new List<string> { "The MO2 profile has no readable sources." });

                _mo2InfoFingerprint = null;
                return _mo2Info;
            }

            if (_mo2Index == null || _mo2Index.Fingerprint != plan.Fingerprint)
            {
                XRayAssetIndex cached = XRayAssetIndex.Load(Mo2IndexPath(plan));
                if (cached != null && cached.Fingerprint == plan.Fingerprint)
                {
                    SetMo2Index(cached);
                }
            }

            if (_mo2Index == null || _mo2Index.Fingerprint != plan.Fingerprint)
            {
                _mo2Info = XRayLevelGamedata.Failed(XRayAssetSourceUserSettings.instance.LevelMo2Folder, new List<string> { _mo2Index == null ? "The virtual index of this MO2 profile is not built. Press 'Build / Refresh Virtual Index'." : "The MO2 profile changed after the virtual index was built (mod order, enabled mods or files). Press 'Build / Refresh Virtual Index'; imports rebuild it automatically." });
                _mo2InfoFingerprint = null;
                return _mo2Info;
            }

            if (_mo2InfoFingerprint != _mo2Index.Fingerprint)
            {
                _mo2Info = XRayLevelEffectiveCatalog.Inspect(_mo2Index, plan.ProfileName);
                _diffs.Clear();
                OverwriteFiles = 0;
                foreach (XRayIndexedFile file in _mo2Index.Files)
                {
                    if (_mo2Index.GetSource(file.Winner).Kind == XRayAssetSourceKind.Overwrite)
                    {
                        OverwriteFiles++;
                    }
                }

                _mo2Info.Warnings.AddRange(plan.Warnings.FindAll(warning => warning.Contains("fsgame")));
                _mo2InfoFingerprint = _mo2Index.Fingerprint;
                StartProbes(_mo2Info);
            }

            return _mo2Info;
        }

        private static void SetMo2Index(XRayAssetIndex index)
        {
            _diffs.Clear();
            index.ExtractionFolder = Path.GetFullPath(EXTRACTION_FOLDER);
            _mo2Index = index;
            _mo2InfoFingerprint = null;
            _shaders = null;
            _materials = null;
        }

        private static void StartProbes(XRayLevelGamedataInfo info)
        {
            _probeJob?.Cancel();
            _probeJob = XRayLevelProbeJob.Start(info, () => Changed?.Invoke());
        }

        private static void OnBuildFinished(XRayLevelIndexJob job)
        {
            if (job.Index != null)
            {
                SetMo2Index(job.Index);
                InspectMo2(true);
            }

            Changed?.Invoke();
        }
    }
}

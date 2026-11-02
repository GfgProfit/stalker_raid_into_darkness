using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using XRay.Importer.Levels;
using XRay.Importer.Sources;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayRvmGrassSource
    {
        private const string MIGRATION_HASH = "316e7417c9162498b1d0f07c6caad10f04432b7582031f435dd3735ba5a13a92";

        private static readonly string[] COMMANDS = { "r__detail_density", "r__detail_radius", "r__detail_height", "r__enable_grass_shadow" };
        private static readonly string[] OPTIONS = { "video/advanced/detail_density", "video/advanced/detail_radius", "video/advanced/detail_height", "video/advanced/grass_shadow" };

        public static bool TryRead(out string profileJson, out string status, XRaySourcePlan expectedSource = null)
        {
            profileJson = null;
            try
            {
                XRaySourceConfiguration configuration = XRayLevelSourceCache.CreateMo2Configuration();
                configuration.Mo2Profile = null;
                configuration.IndexScope.Clear();
                configuration.IndexScope.AddRange(new[] { "scripts", "configs" });
                XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(configuration);
                if (!plan.IsValid || plan.MissingModFolders.Count > 0) throw new InvalidDataException(string.Join("; ", plan.Errors) + "; missing source folders: " + plan.MissingModFolders.Count);
                if (expectedSource != null && (!string.Equals(plan.GameFolder, expectedSource.GameFolder, StringComparison.OrdinalIgnoreCase) || plan.ProfileName != expectedSource.ProfileName || plan.ModOrderFingerprint != expectedSource.ModOrderFingerprint)) throw new InvalidDataException("The import source differs from the current verified RVM configuration.");
                if (plan.FsGame == null || !File.Exists(plan.FsGamePath) || !plan.FsGame.TryGet(XRayFsGame.APP_DATA_ROOT, out XRayFsPath appdata) || appdata.IsExternal) throw new InvalidDataException("Unresolved app_data_root.");
                string userPath = Path.GetFullPath(Path.Combine(plan.GameFolder, appdata.RelativePath, "user.ltx"));
                var source = new XRayEffectiveGamedata(XRayAssetIndex.Build(plan));
                XRayEffectiveFile mcm = source.Resolve("configs/axr_options.ltx");
                if (mcm == null || mcm.IsArchive) throw new InvalidDataException("Effective MCM settings are unavailable.");
                string frozenJson = Resources.Load<TextAsset>("XRayRvmGrassDefaults").text;
                XRayGrassDefaults frozen = XRayGrassDefaults.Load(frozenJson);
                var evidence = new List<SourceHash>();
                AddFile(evidence, userPath);
                AddFile(evidence, mcm.PhysicalPath);
                AddFile(evidence, plan.FsGamePath);
                AddFile(evidence, plan.Installation.IniPath);
                AddFile(evidence, Path.Combine(plan.Installation.ProfilesDirectory, plan.ProfileName, "modlist.txt"));
                SourceHashes trusted = JsonUtility.FromJson<SourceHashes>("{\"files\":" + frozen.Provenance + "}");
                Dictionary<string, string> savedOptions = Parse(File.ReadAllText(mcm.PhysicalPath));
                int writers = 0;
                foreach (XRayEffectiveFile file in source.Enumerate("scripts", true))
                {
                    if (!file.VirtualPath.EndsWith(".script", StringComparison.OrdinalIgnoreCase)) continue;
                    byte[] bytes = source.ReadAllBytes(file.VirtualPath);
                    string text = Encoding.UTF8.GetString(bytes);
                    bool writer = false;
                    foreach (string command in COMMANDS) writer |= text.Contains(command);
                    if (!writer) continue;
                    string hash = Hash(bytes);
                    bool known = false;
                    foreach (SourceHash entry in trusted.files)
                    {
                        if (entry.sha256 == hash) known = true;
                    }
                    if (hash == MIGRATION_HASH)
                    {
                        if (!savedOptions.TryGetValue("modded_exes/version", out string savedVersion) || !int.TryParse(savedVersion, out int version) || version < 20260421) throw new InvalidDataException("Pending or uncertain detail-radius engine migration.");
                        known = true;
                    }
                    if (!known) throw new InvalidDataException("Unverified effective setting writer: " + file.Location);
                    evidence.Add(new SourceHash { path = file.PhysicalPath, sha256 = file.IsArchive ? XRayLevelSourceRefresh.HashFile(file.PhysicalPath) : hash, entry = file.VirtualPath, entrySha256 = hash });
                    writers++;
                }
                if (writers == 0) throw new InvalidDataException("No verified effective setting writers.");
                string userHash = XRayLevelSourceRefresh.HashFile(userPath);
                string mcmHash = XRayLevelSourceRefresh.HashFile(mcm.PhysicalPath);
                XRayGrassValues values = Reconcile(File.ReadAllText(userPath), File.ReadAllText(mcm.PhysicalPath));
                if (userHash != XRayLevelSourceRefresh.HashFile(userPath) || mcmHash != XRayLevelSourceRefresh.HashFile(mcm.PhysicalPath)) throw new InvalidDataException("Settings changed during discovery.");
                foreach (SourceHash entry in evidence)
                {
                    if (entry.sha256 != XRayLevelSourceRefresh.HashFile(entry.path)) throw new InvalidDataException("Source changed during discovery: " + entry.path);
                }
                string provenance = JsonUtility.ToJson(new SourceHashes { files = evidence.ToArray(), profile = plan.ProfileName, resolution = "MO2 overwrite > enabled mods > game; user.ltx and saved MCM agree; no live console sample" });
                profileJson = JsonUtility.ToJson(frozen.WithValues(values, provenance));
                status = "Verified source configuration: " + plan.ProfileName;
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is XRaySourceException || exception is NullReferenceException)
            {
                status = "SOURCE INPUT UNCERTAIN: " + exception.Message;
                return false;
            }
        }

        public static XRayGrassValues Reconcile(string user, string mcm)
        {
            Dictionary<string, string> console = Parse(user);
            Dictionary<string, string> options = Parse(mcm);
            var values = new string[4];
            for (int i = 0; i < COMMANDS.Length; i++)
            {
                if (!console.TryGetValue(COMMANDS[i], out values[i]) || !options.TryGetValue(OPTIONS[i], out string saved)) throw new InvalidDataException("Missing setting: " + COMMANDS[i]);
                bool equal = i == 3 ? Boolean(values[i]) == Boolean(saved) : Number(values[i]) == Number(saved);
                if (!equal) throw new InvalidDataException("Console/MCM conflict: " + COMMANDS[i]);
            }
            float radius = Number(values[1]);
            if (radius != Mathf.Floor(radius)) throw new InvalidDataException("Non-integral grass radius.");
            var result = new XRayGrassValues(Number(values[0]), (int)radius, Number(values[2]), Boolean(values[3]));
            if (!result.Valid || result.Radius < 50) throw new InvalidDataException("Invalid grass source values.");
            return result;
        }

        private static Dictionary<string, string> Parse(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            string section = string.Empty;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Split(';')[0].Trim();
                if (line.Length == 0) continue;
                if (line[0] == '[')
                {
                    section = line.Trim('[', ']', ' ', '\r');
                    continue;
                }
                int split = line.IndexOf('=');
                if (split < 0) split = line.IndexOfAny(new[] { ' ', '\t' });
                if (split <= 0) continue;
                string key = line.Substring(0, split).Trim();
                if (key == "version" && section == "modded_exes") key = "modded_exes/version";
                if (Array.IndexOf(COMMANDS, key) < 0 && Array.IndexOf(OPTIONS, key) < 0 && key != "modded_exes/version") continue;
                string value = line.Substring(split + 1).Trim();
                if (result.ContainsKey(key)) throw new InvalidDataException("Duplicate setting: " + key);
                result.Add(key, value);
            }
            return result;
        }

        private static float Number(string value)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) || float.IsNaN(result) || float.IsInfinity(result)) throw new InvalidDataException("Invalid number: " + value);
            return result;
        }

        private static bool Boolean(string value)
        {
            if (value == "on" || value == "true" || value == "1") return true;
            if (value == "off" || value == "false" || value == "0") return false;
            throw new InvalidDataException("Invalid boolean: " + value);
        }

        private static void AddFile(List<SourceHash> evidence, string path)
        {
            evidence.Add(new SourceHash { path = path, sha256 = XRayLevelSourceRefresh.HashFile(path) });
        }

        private static string Hash(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        [Serializable]
        private sealed class SourceHashes
        {
            public SourceHash[] files;
            public string profile;
            public string resolution;
        }

        [Serializable]
        private sealed class SourceHash
        {
            public string path;
            public string sha256;
            public string entry;
            public string entrySha256;
        }
    }
}

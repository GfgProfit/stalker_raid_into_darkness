using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using XRay.Importer.Core.IO;
using XRay.Importer.Editor;
using XRay.Importer.Sources.Vfs;

namespace RaidIntoDarkness.Player.Editor
{
    public sealed class StalkerXRaySources
    {
        private readonly Dictionary<string, string> _userValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private StalkerXRaySources(XRaySourcePlan plan, XRayAssetIndex index)
        {
            Plan = plan;
            Index = index;
        }

        public XRaySourcePlan Plan { get; }
        public XRayAssetIndex Index { get; }
        public string UserLtxPath { get; private set; }
        public string GamePath { get; private set; }

        public static StalkerXRaySources Open(IEnumerable<string> scope, out string error)
        {
            XRayAssetSourceOptions options = XRayAssetSourceSettings.GetEffective();
            var configuration = new XRaySourceConfiguration
            {
                Mode = options.Sources.Mode,
                BaseAnomalyFolder = options.Sources.BaseAnomalyFolder,
                Mo2Folder = options.Sources.Mo2Folder,
                Mo2Instance = options.Sources.Mo2Instance,
                Mo2Profile = options.Sources.Mo2Profile,
                CustomFolder = options.Sources.CustomFolder,
                LocalAppDataFolder = options.Sources.LocalAppDataFolder,
                IncludeGameGamedata = options.Sources.IncludeGameGamedata,
                TreatModRootAsGamedata = options.Sources.TreatModRootAsGamedata
            };

            configuration.IndexScope.Clear();
            configuration.IndexScope.AddRange(scope);
            XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(configuration);
            if (!plan.IsValid)
            {
                error = "X-Ray asset sources are invalid: " + string.Join("; ", plan.Errors);
                return null;
            }

            error = null;
            var sources = new StalkerXRaySources(plan, XRayAssetIndex.Build(plan));
            sources.ReadUserLtx();
            return sources;
        }

        public string Describe()
        {
            return $"MO2 profile: {Plan.ProfileName}, active mods: {Plan.ActiveModCount}, indexed files: {Index.Count}";
        }

        public bool Exists(string virtualPath)
        {
            return Index.TryGet(XRayVirtualPath.Normalize(virtualPath), out _);
        }

        public string GetFullPath(string virtualPath)
        {
            return Index.TryGet(XRayVirtualPath.Normalize(virtualPath), out XRayIndexedFile file) ? Index.GetFullPath(file.Winner) : null;
        }

        public string GetSourceName(string virtualPath)
        {
            return Index.TryGet(XRayVirtualPath.Normalize(virtualPath), out XRayIndexedFile file) ? Index.GetSource(file.Winner).Name : null;
        }

        public byte[] ReadBytes(string virtualPath)
        {
            string path = GetFullPath(virtualPath);
            return path != null ? File.ReadAllBytes(path) : null;
        }

        public string ReadScript(string virtualPath)
        {
            byte[] data = ReadBytes(virtualPath);
            return data != null ? Regex.Replace(Cp1251.Decode(data, 0, data.Length), "--[^\\n]*", string.Empty) : null;
        }

        public List<string> FindFiles(string folder, string prefix, string extension)
        {
            string root = XRayVirtualPath.Normalize(folder) + "/";
            var result = new List<string>();
            foreach (XRayIndexedFile file in Index.Files)
            {
                string name = Path.GetFileName(file.VirtualPath);
                if (file.VirtualPath.StartsWith(root, StringComparison.Ordinal) && file.VirtualPath.IndexOf('/', root.Length) < 0 && name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(extension, StringComparison.Ordinal))
                {
                    result.Add(file.VirtualPath);
                }
            }

            result.Sort((a, b) => string.CompareOrdinal(Path.GetFileName(a), Path.GetFileName(b)));
            return result;
        }

        public string GetUserValue(string key)
        {
            return _userValues.TryGetValue(key, out string value) ? value : null;
        }

        public string CopyToProject(string virtualPath, string assetFolder, string relativeName)
        {
            string source = GetFullPath(virtualPath);
            if (source == null)
            {
                return null;
            }

            string assetPath = (assetFolder + "/" + relativeName).Replace('\\', '/');
            EnsureFolder(Path.GetDirectoryName(assetPath));
            string target = Path.GetFullPath(assetPath);
            var info = new FileInfo(source);
            var existing = new FileInfo(target);
            if (!existing.Exists || existing.Length != info.Length || !Same(source, target))
            {
                File.Copy(source, target, true);
            }

            return assetPath;
        }

        public static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        public static bool TryFloat(string value, out float result)
        {
            result = 0.0f;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            int comma = trimmed.IndexOf(',');
            if (comma >= 0)
            {
                trimmed = trimmed.Substring(0, comma);
            }

            if (trimmed.EndsWith("f", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            }

            return float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        public static string Format(float value)
        {
            return value.ToString("0.#####", CultureInfo.InvariantCulture);
        }

        private static bool Same(string a, string b)
        {
            byte[] left = File.ReadAllBytes(a);
            byte[] right = File.ReadAllBytes(b);
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        private void ReadUserLtx()
        {
            string gamePath = Plan.Installation != null ? Plan.Installation.GamePath : null;
            if (string.IsNullOrEmpty(gamePath) && !string.IsNullOrEmpty(Plan.Configuration.BaseAnomalyFolder))
            {
                gamePath = Path.GetFullPath(Path.Combine(Plan.Configuration.BaseAnomalyFolder, "..", ".."));
            }

            GamePath = gamePath;
            string path = string.IsNullOrEmpty(gamePath) ? null : Path.Combine(gamePath, "appdata", "user.ltx");
            if (path == null || !File.Exists(path))
            {
                return;
            }

            UserLtxPath = path;
            foreach (string line in File.ReadAllLines(path, Encoding.Default))
            {
                int space = line.IndexOf(' ');
                if (space > 0)
                {
                    _userValues[line.Substring(0, space).Trim()] = line.Substring(space + 1).Trim();
                }
            }
        }
    }
}

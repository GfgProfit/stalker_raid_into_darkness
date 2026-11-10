using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Sources.Levels
{
    public sealed class XRayLevelEntry
    {
        public string Name { get; internal set; }
        public string Folder { get; internal set; }
        public string LevelPath { get; internal set; }
        public long LevelLength { get; internal set; }
        public List<string> Files { get; } = new List<string>();
        public string VirtualFolder { get; internal set; }
        public string Origin { get; internal set; }
        public XRayAssetSourceKind OriginKind { get; internal set; }
        public int OriginCandidates { get; internal set; }
        public bool IsModded { get; internal set; }
        public bool IsReplaced { get; internal set; }
        public bool IsRegistered { get; internal set; }
        public string RegisteredBy { get; internal set; }
        public Dictionary<string, int> Subfolders { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public XRayLevelProbe Probe { get; set; }
        public bool IsEffective => VirtualFolder != null;
    }

    public sealed class XRayLevelGamedataInfo
    {
        public string RequestedFolder { get; internal set; }
        public string Root { get; internal set; }
        public string LevelsFolder { get; internal set; }
        public string TexturesFolder { get; internal set; }
        public string ShadersXrPath { get; internal set; }
        public string GameMaterialsPath { get; internal set; }
        public string SystemLtxPath { get; internal set; }
        public XRayEffectiveGamedata Effective { get; internal set; }
        public List<XRayLevelEntry> Levels { get; } = new List<XRayLevelEntry>();
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public bool IsValid => Errors.Count == 0 && (Root != null || Effective != null);

        public XRayLevelEntry Find(string name)
        {
            return Levels.Find(level => string.Equals(level.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public bool Exists(string virtualPath)
        {
            return Effective != null ? Effective.Exists(virtualPath) : ResolveLoose(virtualPath) != null;
        }

        public string Materialize(string virtualPath)
        {
            if (Effective != null)
            {
                return Effective.Exists(virtualPath) ? Effective.Materialize(virtualPath) : null;
            }

            return ResolveLoose(virtualPath);
        }

        public byte[] ReadAllBytes(string virtualPath)
        {
            if (Effective != null)
            {
                return Effective.ReadAllBytes(virtualPath);
            }

            string path = ResolveLoose(virtualPath);
            if (path == null)
            {
                throw new XRaySourceException($"'{virtualPath}' was not found in '{Root}'.");
            }

            return File.ReadAllBytes(path);
        }

        public string Describe(string virtualPath)
        {
            if (Effective != null)
            {
                XRayEffectiveFile file = Effective.Resolve(virtualPath);
                return file != null ? file.Location : null;
            }

            return ResolveLoose(virtualPath);
        }

        public XRayEffectiveFile Resolve(string virtualPath)
        {
            return Effective?.Resolve(virtualPath);
        }

        private string ResolveLoose(string virtualPath)
        {
            if (Root == null || !XRayVirtualPath.TryNormalizeSafe(virtualPath, out string normalized) || normalized.Length == 0)
            {
                return null;
            }

            string current = Root;
            string[] segments = normalized.Split('/');
            for (int i = 0; i < segments.Length - 1 && current != null; i++)
            {
                current = XRayFileSystem.FindChildDirectory(current, segments[i]);
            }

            return current != null ? XRayFileSystem.FindChildFile(current, segments[segments.Length - 1]) : null;
        }
    }

    public static class XRayLevelGamedata
    {
        public const string SOURCE_NAME = "Level Gamedata";
        public const string LEVEL_FILE = "level";
        public static readonly string[] INDEX_SCOPE = { "textures", "textures.ltx", "levels", "shaders", "shaders.xr", "configs", "spawns" };

        private static readonly string[] MARKER_FOLDERS = { "levels", "textures", "meshes", "shaders" };

        public static XRayLevelGamedataInfo Failed(string requested, IEnumerable<string> errors)
        {
            var info = new XRayLevelGamedataInfo { RequestedFolder = requested };
            info.Errors.AddRange(errors);
            return info;
        }

        public static string ResolveRoot(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                return null;
            }

            string full = Path.GetFullPath(folder).TrimEnd('\\', '/');
            if (HasMarkers(full))
            {
                return full;
            }

            string gamedata = XRayFileSystem.FindChildDirectory(full, Mo2VirtualFileSystem.GAMEDATA_FOLDER);
            return gamedata != null && HasMarkers(gamedata) ? Path.GetFullPath(gamedata) : null;
        }

        public static XRayLevelGamedataInfo Inspect(string folder)
        {
            var info = new XRayLevelGamedataInfo { RequestedFolder = folder };
            if (string.IsNullOrEmpty(folder))
            {
                info.Errors.Add("Level Gamedata Source is not set.");
                return info;
            }

            if (!Directory.Exists(folder))
            {
                info.Errors.Add($"Level Gamedata Source '{folder}' does not exist.");
                return info;
            }

            info.Root = ResolveRoot(folder);
            if (info.Root == null)
            {
                info.Errors.Add($"'{folder}' is neither a gamedata folder (levels, textures, meshes, shaders) nor a folder that contains 'gamedata'.");
                return info;
            }

            info.LevelsFolder = XRayFileSystem.FindChildDirectory(info.Root, "levels");
            info.TexturesFolder = XRayFileSystem.FindChildDirectory(info.Root, "textures");
            info.ShadersXrPath = XRayFileSystem.FindChildFile(info.Root, "shaders.xr");
            info.GameMaterialsPath = XRayFileSystem.FindChildFile(info.Root, "gamemtl.xr");
            string configs = XRayFileSystem.FindChildDirectory(info.Root, "configs");
            info.SystemLtxPath = configs != null ? XRayFileSystem.FindChildFile(configs, "system.ltx") : null;
            if (info.LevelsFolder == null)
            {
                info.Errors.Add($"'{info.Root}' has no 'levels' folder.");
            }

            if (info.TexturesFolder == null)
            {
                info.Warnings.Add($"'{info.Root}' has no 'textures' folder; level textures can only come from fallback sources.");
            }

            if (info.ShadersXrPath == null)
            {
                info.Warnings.Add("shaders.xr is missing; engine shader classes are guessed from shader names.");
            }

            if (info.GameMaterialsPath == null)
            {
                info.Warnings.Add("gamemtl.xr is missing; passable collision materials cannot be excluded from detail placement.");
            }

            if (info.LevelsFolder != null)
            {
                foreach (string directory in Directory.GetDirectories(info.LevelsFolder))
                {
                    string level = XRayFileSystem.FindChildFile(directory, LEVEL_FILE);
                    if (level == null)
                    {
                        continue;
                    }

                    var entry = new XRayLevelEntry { Name = Path.GetFileName(directory), Folder = directory, LevelPath = level, LevelLength = new FileInfo(level).Length };
                    foreach (string file in Directory.GetFiles(directory))
                    {
                        entry.Files.Add(Path.GetFileName(file));
                    }

                    entry.Files.Sort(StringComparer.OrdinalIgnoreCase);
                    info.Levels.Add(entry);
                }

                info.Levels.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                if (info.Levels.Count == 0)
                {
                    info.Errors.Add($"'{info.LevelsFolder}' contains no level folders with a 'level' file.");
                }
            }

            return info;
        }

        public static XRaySourcePlan CreatePlan(XRayLevelGamedataInfo info, XRaySourcePlan fallback, bool allowFallback)
        {
            var configuration = new XRaySourceConfiguration { Mode = XRaySourceMode.CustomFolder, CustomFolder = info.Root };
            configuration.IndexScope.Clear();
            configuration.IndexScope.AddRange(INDEX_SCOPE);
            var plan = new XRaySourcePlan { Configuration = configuration, ProfileName = SOURCE_NAME };
            var stamp = new StringBuilder("level-source|" + info.Root + "|" + allowFallback);
            if (!info.IsValid)
            {
                plan.Errors.AddRange(info.Errors);
                plan.Fingerprint = XRayFileSystem.Hash(stamp.ToString());
                return plan;
            }

            plan.Warnings.AddRange(info.Warnings);
            plan.Sources.Add(new XRayAssetSource(SOURCE_NAME, XRayAssetSourceKind.LevelGamedata, 0, info.Root, info.Root));
            plan.SkipFileSuffixes.Add(".mohidden");
            if (allowFallback && fallback != null && fallback.IsValid)
            {
                foreach (XRayAssetSource source in fallback.Sources)
                {
                    plan.Sources.Add(source);
                }

                plan.SkipFileSuffixes.AddRange(fallback.SkipFileSuffixes);
                plan.SkipDirectories.AddRange(fallback.SkipDirectories);
                stamp.Append('|').Append(fallback.Fingerprint);
            }

            foreach (XRayAssetSource source in plan.Sources)
            {
                XRayFileSystem.AppendStamp(stamp, source.GamedataRoot);
                foreach (string scope in INDEX_SCOPE)
                {
                    XRayFileSystem.AppendStamp(stamp, Path.Combine(source.GamedataRoot, scope));
                }
            }

            if (info.TexturesFolder != null)
            {
                foreach (string directory in Directory.GetDirectories(info.TexturesFolder))
                {
                    XRayFileSystem.AppendStamp(stamp, directory);
                }
            }

            plan.Fingerprint = XRayFileSystem.Hash(stamp.ToString());
            return plan;
        }

        public static List<XRayLevelEntry> Match(XRayLevelGamedataInfo info, string droppedPath, string sourcePathHint, out string method)
        {
            var result = new List<XRayLevelEntry>();
            method = "none";
            if (!string.IsNullOrEmpty(sourcePathHint))
            {
                string hint = Path.GetFullPath(sourcePathHint);
                foreach (XRayLevelEntry entry in info.Levels)
                {
                    string loose = entry.IsEffective ? LoosePath(info, entry) : entry.LevelPath;
                    if (loose != null && string.Equals(Path.GetFullPath(loose), hint, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(entry);
                        method = "source path";
                        return result;
                    }
                }

                string parent = Path.GetFileName(Path.GetDirectoryName(hint) ?? string.Empty);
                XRayLevelEntry byName = info.Find(parent);
                if (byName != null && byName.LevelLength == new FileInfo(droppedPath).Length && SameContent(PhysicalLevelPath(info, byName), droppedPath))
                {
                    result.Add(byName);
                    method = "source folder name + content hash";
                    return result;
                }
            }

            long length = new FileInfo(droppedPath).Length;
            var candidates = info.Levels.FindAll(entry => entry.LevelLength == length);
            if (candidates.Count == 0)
            {
                method = "size (no candidate)";
                return result;
            }

            string hash = ComputeHash(droppedPath);
            foreach (XRayLevelEntry candidate in candidates)
            {
                if (ComputeHash(PhysicalLevelPath(info, candidate)) == hash)
                {
                    result.Add(candidate);
                }
            }

            method = $"size + SHA-256 ({candidates.Count} size candidates)";
            return result;
        }

        public static string PhysicalLevelPath(XRayLevelGamedataInfo info, XRayLevelEntry entry)
        {
            return entry.IsEffective ? info.Materialize(entry.VirtualFolder + "/" + LEVEL_FILE) : entry.LevelPath;
        }

        private static string LoosePath(XRayLevelGamedataInfo info, XRayLevelEntry entry)
        {
            XRayEffectiveFile file = info.Resolve(entry.VirtualFolder + "/" + LEVEL_FILE);
            return file != null && !file.IsArchive ? file.PhysicalPath : null;
        }

        public static bool SameContent(string a, string b)
        {
            return new FileInfo(a).Length == new FileInfo(b).Length && ComputeHash(a) == ComputeHash(b);
        }

        public static string ComputeHash(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            using (SHA256 sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(stream));
            }
        }

        public static string ComputeHash(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(data));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
            {
                builder.Append(value.ToString("x2"));
            }

            return builder.ToString();
        }

        private static bool HasMarkers(string folder)
        {
            int found = 0;
            foreach (string marker in MARKER_FOLDERS)
            {
                if (XRayFileSystem.FindChildDirectory(folder, marker) != null)
                {
                    found++;
                }
            }

            return found >= 2;
        }
    }
}

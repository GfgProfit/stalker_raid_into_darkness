using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace XRay.Importer.Sources.Vfs
{
    public sealed class XRayArchiveLoad
    {
        public string RelativePath { get; internal set; }
        public string FullPath { get; internal set; }
        public string Origin { get; internal set; }
        public string ContainerFolder { get; internal set; }
        public string FsAlias { get; internal set; }
        public bool AboveLoose { get; internal set; }
    }

    public static class Mo2ArchiveLayout
    {
        public const string GAME_FOLDER_ORIGIN = "Anomaly (game folder)";
        public const string THUMBS_NAME = "Thumbs.db";
        public const string SVN_NAME = ".svn";
        public const string XRIGNORE_NAME = ".xrignore";

        public static List<XRayArchiveLoad> Plan(XRaySourcePlan plan, StringBuilder stamp)
        {
            var containers = new List<(string Folder, string Origin)>();
            if (plan.Installation != null && Directory.Exists(plan.Installation.OverwriteDirectory))
            {
                containers.Add((plan.Installation.OverwriteDirectory, Mo2VirtualFileSystem.OVERWRITE_NAME));
            }

            if (plan.ModList != null && plan.Installation != null)
            {
                foreach (Mo2.Mo2ModEntry entry in plan.ModList.GetActiveModsByPriorityDescending())
                {
                    string folder = Path.Combine(plan.Installation.ModsDirectory, entry.Name);
                    if (Directory.Exists(folder))
                    {
                        containers.Add((folder, entry.Name));
                    }
                }
            }

            if (!string.IsNullOrEmpty(plan.GameFolder) && Directory.Exists(plan.GameFolder))
            {
                containers.Add((plan.GameFolder, GAME_FOLDER_ORIGIN));
            }

            plan.FsGame = LoadFsGame(plan, containers, stamp);
            var loads = new List<XRayArchiveLoad>();
            var simulator = new Simulator(plan, containers, loads);
            int gameDataLine = plan.FsGame.GameDataLine();
            string appData = plan.FsGame.TryGet(XRayFsGame.APP_DATA_ROOT, out XRayFsPath appDataPath) ? appDataPath.RelativePath : null;
            for (int i = 0; i < plan.FsGame.Paths.Count; i++)
            {
                XRayFsPath path = plan.FsGame.Paths[i];
                if (path.IsExternal || IsUnder(path.RelativePath, "gamedata") || (appData != null && IsUnder(path.RelativePath, appData)))
                {
                    continue;
                }

                simulator.Scan(path.RelativePath, path.Recurse, path.Alias, i > gameDataLine);
            }

            var above = new List<XRayAssetSource>();
            var below = new List<XRayAssetSource>();
            for (int i = loads.Count - 1; i >= 0; i--)
            {
                XRayArchiveLoad load = loads[i];
                XRayFileSystem.AppendStamp(stamp, load.FullPath);
                var source = new XRayAssetSource($"{load.RelativePath} [{load.Origin}]", XRayAssetSourceKind.Archive, i, load.FullPath, load.ContainerFolder, load.Origin);
                (load.AboveLoose ? above : below).Add(source);
            }

            plan.Sources.InsertRange(0, above);
            plan.Sources.AddRange(below);
            return loads;
        }

        public static bool IsEngineArchive(string name)
        {
            int dot = name.LastIndexOf('.');
            if (dot < 0)
            {
                return false;
            }

            string extension = name.Substring(dot).ToLowerInvariant();
            return extension.StartsWith(".db", StringComparison.Ordinal) || extension.StartsWith(".xdb", StringComparison.Ordinal);
        }

        private static XRayFsGame LoadFsGame(XRaySourcePlan plan, List<(string Folder, string Origin)> containers, StringBuilder stamp)
        {
            foreach ((string folder, string origin) in containers)
            {
                string file = XRayFileSystem.FindChildFile(folder, XRayFsGame.FILE_NAME);
                if (file == null)
                {
                    continue;
                }

                XRayFileSystem.AppendStamp(stamp, file);
                plan.FsGamePath = file;
                try
                {
                    XRayFsGame fs = XRayFsGame.Parse(File.ReadAllBytes(file), $"{file} ({origin})");
                    plan.Warnings.AddRange(fs.Warnings);
                    return fs;
                }
                catch (IOException exception)
                {
                    plan.Errors.Add($"Cannot read '{file}': {exception.Message}");
                    break;
                }
            }

            plan.Warnings.Add($"{XRayFsGame.FILE_NAME} was not found in the game folder or enabled mods; the Anomaly 1.5.3 archive layout is assumed.");
            plan.FsGamePath = null;
            return XRayFsGame.CreateDefault();
        }

        private static bool IsUnder(string path, string root)
        {
            return path == root || path.StartsWith(root + "/", StringComparison.Ordinal);
        }

        private sealed class Simulator
        {
            private readonly XRaySourcePlan _plan;
            private readonly List<(string Folder, string Origin)> _containers;
            private readonly List<XRayArchiveLoad> _loads;

            public Simulator(XRaySourcePlan plan, List<(string Folder, string Origin)> containers, List<XRayArchiveLoad> loads)
            {
                _plan = plan;
                _containers = containers;
                _loads = loads;
            }

            public void Scan(string relativeDirectory, bool recurse, string alias, bool aboveLoose)
            {
                var files = new Dictionary<string, (string FullPath, string Origin, string Folder)>(StringComparer.OrdinalIgnoreCase);
                var directories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach ((string folder, string origin) in _containers)
                {
                    string directory = Resolve(folder, relativeDirectory);
                    if (directory == null)
                    {
                        continue;
                    }

                    if (File.Exists(Path.Combine(directory, XRIGNORE_NAME)))
                    {
                        return;
                    }

                    foreach (FileInfo file in SafeFiles(directory))
                    {
                        if (Ignored(file.Name, file.Attributes) || HasSkippedSuffix(file.Name))
                        {
                            continue;
                        }

                        if (files.TryGetValue(file.Name, out (string FullPath, string Origin, string Folder) winner))
                        {
                            if (IsEngineArchive(file.Name))
                            {
                                _plan.ShadowedArchives.Add($"{Combine(relativeDirectory, file.Name)} from '{origin}' is hidden by '{winner.Origin}' (MO2 priority).");
                            }

                            continue;
                        }

                        files.Add(file.Name, (file.FullName, origin, folder));
                    }

                    if (!recurse)
                    {
                        continue;
                    }

                    foreach (DirectoryInfo child in SafeDirectories(directory))
                    {
                        if (!Ignored(child.Name, child.Attributes) && !_plan.SkipDirectories.Exists(skip => string.Equals(skip, child.Name, StringComparison.OrdinalIgnoreCase)) && !directories.ContainsKey(child.Name))
                        {
                            directories.Add(child.Name, child.Name);
                        }
                    }
                }

                var names = new List<string>(files.Keys);
                names.AddRange(directories.Keys);
                names.Sort(StringComparer.Ordinal);
                foreach (string name in names)
                {
                    if (files.TryGetValue(name, out (string FullPath, string Origin, string Folder) file))
                    {
                        if (IsEngineArchive(name))
                        {
                            Add(new XRayArchiveLoad { RelativePath = Combine(relativeDirectory, name), FullPath = file.FullPath, Origin = file.Origin, ContainerFolder = file.Folder, FsAlias = alias, AboveLoose = aboveLoose });
                        }

                        continue;
                    }

                    Scan(Combine(relativeDirectory, name), true, alias, aboveLoose);
                }
            }

            private void Add(XRayArchiveLoad load)
            {
                _loads.RemoveAll(existing => string.Equals(existing.RelativePath, load.RelativePath, StringComparison.OrdinalIgnoreCase));
                _loads.Add(load);
            }

            private bool HasSkippedSuffix(string name)
            {
                foreach (string suffix in _plan.SkipFileSuffixes)
                {
                    if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool Ignored(string name, FileAttributes attributes)
            {
                return (attributes & FileAttributes.Hidden) != 0 || name == THUMBS_NAME || name == SVN_NAME;
            }

            private static string Combine(string directory, string name)
            {
                return string.IsNullOrEmpty(directory) ? name : directory + "/" + name;
            }

            private static string Resolve(string folder, string relative)
            {
                string current = folder;
                if (string.IsNullOrEmpty(relative))
                {
                    return current;
                }

                foreach (string segment in relative.Split('/'))
                {
                    current = XRayFileSystem.FindChildDirectory(current, segment);
                    if (current == null)
                    {
                        return null;
                    }
                }

                return current;
            }

            private FileInfo[] SafeFiles(string directory)
            {
                try
                {
                    return new DirectoryInfo(directory).GetFiles();
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    _plan.Warnings.Add($"Cannot read '{directory}': {exception.Message}");
                    return new FileInfo[0];
                }
            }

            private DirectoryInfo[] SafeDirectories(string directory)
            {
                try
                {
                    return new DirectoryInfo(directory).GetDirectories();
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    _plan.Warnings.Add($"Cannot read '{directory}': {exception.Message}");
                    return new DirectoryInfo[0];
                }
            }
        }
    }
}

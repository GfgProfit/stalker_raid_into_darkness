using System;
using System.IO;
using System.Text;
using XRay.Importer.Sources.Mo2;

namespace XRay.Importer.Sources.Vfs
{
    public static class Mo2VirtualFileSystem
    {
        public const string GAMEDATA_FOLDER = "gamedata";
        public const string OVERWRITE_NAME = "Overwrite";
        public const string GAME_GAMEDATA_NAME = "Game gamedata";
        public const string BASE_ANOMALY_NAME = "Base Anomaly";
        public const string CUSTOM_FOLDER_NAME = "Custom folder";
        public const string META_FILE_NAME = "meta.ini";

        private static readonly string[] GAMEDATA_CHILD_FOLDERS = { "textures", "meshes", "configs", "scripts", "sounds", "anims", "shaders", "spawns", "levels" };

        public static XRaySourcePlan Plan(XRaySourceConfiguration configuration)
        {
            var plan = new XRaySourcePlan { Configuration = configuration };
            var stamp = new StringBuilder(configuration.Describe());
            switch (configuration.Mode)
            {
                case XRaySourceMode.Mo2AndBaseAnomaly:
                    PlanMo2(configuration, plan, stamp);
                    break;
                case XRaySourceMode.Mo2Effective:
                    PlanMo2(configuration, plan, stamp);
                    if (plan.Errors.Count == 0)
                    {
                        Mo2ArchiveLayout.Plan(plan, stamp);
                    }

                    break;
                case XRaySourceMode.CustomFolder:
                    AddFolderSource(plan, configuration.CustomFolder, CUSTOM_FOLDER_NAME, XRayAssetSourceKind.Custom, "Custom Folder", stamp);
                    break;
            }

            if (configuration.Mode != XRaySourceMode.Mo2Effective)
            {
                AddFolderSource(plan, configuration.BaseAnomalyFolder, BASE_ANOMALY_NAME, XRayAssetSourceKind.BaseAnomaly, "Base Anomaly Folder", stamp);
            }

            if (plan.SkipFileSuffixes.Count == 0)
            {
                plan.SkipFileSuffixes.Add(".mohidden");
            }

            foreach (XRayAssetSource source in plan.Sources)
            {
                if (source.IsArchive)
                {
                    continue;
                }

                XRayFileSystem.AppendStamp(stamp, source.GamedataRoot);
                foreach (string scope in configuration.IndexScope)
                {
                    string path = Path.Combine(source.GamedataRoot, scope);
                    XRayFileSystem.AppendStamp(stamp, path);
                    if (configuration.Mode == XRaySourceMode.Mo2Effective && Directory.Exists(path))
                    {
                        var files = new System.Collections.Generic.List<FileInfo>(XRayFileSystem.EnumerateFileInfosSafe(path, plan.SkipDirectories, plan.Warnings));
                        files.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
                        foreach (FileInfo file in files)
                        {
                            stamp.Append('|').Append(file.FullName).Append(':').Append(file.Length).Append(':').Append(file.LastWriteTimeUtc.Ticks);
                        }
                    }
                }
            }

            plan.Fingerprint = XRayFileSystem.Hash(stamp.ToString());
            return plan;
        }

        private static void PlanMo2(XRaySourceConfiguration configuration, XRaySourcePlan plan, StringBuilder stamp)
        {
            Mo2Installation installation;
            try
            {
                installation = Mo2InstallationReader.Read(configuration.Mo2Folder, configuration.Mo2Instance, configuration.LocalAppDataFolder);
            }
            catch (XRaySourceException exception)
            {
                plan.Errors.Add(exception.Message);
                return;
            }

            plan.Installation = installation;
            if (!string.IsNullOrWhiteSpace(configuration.ModsFolder))
            {
                installation.ModsDirectory = Path.GetFullPath(configuration.ModsFolder);
            }

            if (!string.IsNullOrWhiteSpace(configuration.ProfilesFolder))
            {
                installation.ProfilesDirectory = Path.GetFullPath(configuration.ProfilesFolder);
                installation.Profiles.Clear();
                installation.Profiles.AddRange(Mo2ProfileReader.ListProfiles(installation.ProfilesDirectory));
            }

            if (!string.IsNullOrWhiteSpace(configuration.OverwriteFolder))
            {
                installation.OverwriteDirectory = Path.GetFullPath(configuration.OverwriteFolder);
            }

            plan.Warnings.AddRange(installation.Warnings);
            plan.SkipFileSuffixes.AddRange(installation.SkipFileSuffixes);
            plan.SkipDirectories.AddRange(installation.SkipDirectories);
            XRayFileSystem.AppendStamp(stamp, installation.IniPath);

            string profile = ResolveProfile(configuration, installation);
            if (profile == null)
            {
                plan.Errors.Add(string.IsNullOrEmpty(configuration.Mo2Profile) ? $"No MO2 profile is selected and the active profile could not be determined from {Mo2InstallationReader.INI_FILE_NAME}." : $"MO2 profile '{configuration.Mo2Profile}' was not found in '{installation.ProfilesDirectory}'.");
                return;
            }

            plan.ProfileName = profile;
            string modListPath = Mo2ProfileReader.GetModListPath(installation.ProfilesDirectory, profile);
            XRayFileSystem.AppendStamp(stamp, modListPath);
            try
            {
                plan.ModList = Mo2ProfileReader.ReadModList(modListPath);
            }
            catch (Exception exception) when (exception is XRaySourceException || exception is IOException)
            {
                plan.Errors.Add(exception.Message);
                return;
            }

            plan.Warnings.AddRange(plan.ModList.Warnings);
            AddMo2Source(plan, configuration, OVERWRITE_NAME, XRayAssetSourceKind.Overwrite, int.MaxValue, installation.OverwriteDirectory);
            foreach (Mo2ModEntry entry in plan.ModList.GetActiveModsByPriorityDescending())
            {
                plan.ActiveModCount++;
                plan.ActiveModOrder.Add(entry.Name);
                string container = Path.Combine(installation.ModsDirectory, entry.Name);
                if (!Directory.Exists(container))
                {
                    plan.MissingModFolders.Add(entry.Name);
                    continue;
                }

                XRayFileSystem.AppendStamp(stamp, container);
                XRayFileSystem.AppendStamp(stamp, Path.Combine(container, META_FILE_NAME));
                AddMo2Source(plan, configuration, entry.Name, XRayAssetSourceKind.Mod, entry.Priority, container);
            }

            plan.ModOrderFingerprint = XRayFileSystem.Hash(installation.InstanceFolder + "|" + profile + "|" + string.Join("\n", plan.ActiveModOrder));
            plan.GameFolder = string.IsNullOrEmpty(configuration.GameFolder) ? installation.GamePath : Path.GetFullPath(configuration.GameFolder).TrimEnd('\\', '/');
            if (configuration.Mode == XRaySourceMode.Mo2Effective && (string.IsNullOrEmpty(plan.GameFolder) || !Directory.Exists(plan.GameFolder)))
            {
                plan.Errors.Add($"Anomaly game folder '{plan.GameFolder}' does not exist. Set it manually (MO2 gamePath is '{installation.GamePath}').");
                return;
            }

            if (plan.MissingModFolders.Count > 0)
            {
                plan.Warnings.Add($"{plan.MissingModFolders.Count} enabled mods have no folder in '{installation.ModsDirectory}': {string.Join(", ", plan.MissingModFolders)}");
            }

            if (plan.ModsWithoutGamedataCount > 0)
            {
                plan.Warnings.Add($"{plan.ModsWithoutGamedataCount} enabled mods keep game folders (textures, meshes, ...) directly in the mod root. MO2 maps a mod root to the game root for {installation.GameName ?? "this game"}, so these files are outside gamedata and are ignored. Enable 'Treat Mod Root As Gamedata' only if your MO2 game plugin maps mods into gamedata.");
            }

            if (configuration.IncludeGameGamedata && !string.IsNullOrEmpty(plan.GameFolder))
            {
                string gameGamedata = XRayFileSystem.FindChildDirectory(plan.GameFolder, GAMEDATA_FOLDER);
                if (gameGamedata != null)
                {
                    plan.Sources.Add(new XRayAssetSource(GAME_GAMEDATA_NAME, XRayAssetSourceKind.GameGamedata, -1, gameGamedata, plan.GameFolder, Mo2ArchiveLayout.GAME_FOLDER_ORIGIN));
                }
            }
        }

        private static string ResolveProfile(XRaySourceConfiguration configuration, Mo2Installation installation)
        {
            string requested = string.IsNullOrEmpty(configuration.Mo2Profile) ? installation.SelectedProfile : configuration.Mo2Profile;
            if (string.IsNullOrEmpty(requested))
            {
                return null;
            }

            return installation.Profiles.Find(name => string.Equals(name, requested, StringComparison.OrdinalIgnoreCase));
        }

        private static void AddMo2Source(XRaySourcePlan plan, XRaySourceConfiguration configuration, string name, XRayAssetSourceKind kind, int priority, string container)
        {
            if (string.IsNullOrEmpty(container) || !Directory.Exists(container))
            {
                return;
            }

            string root = XRayFileSystem.FindChildDirectory(container, GAMEDATA_FOLDER);
            if (root == null && configuration.TreatModRootAsGamedata)
            {
                root = container;
            }

            if (root == null)
            {
                if (HasGamedataChildren(container))
                {
                    plan.ModsWithoutGamedataCount++;
                }

                return;
            }

            plan.Sources.Add(new XRayAssetSource(name, kind, priority, root, container));
        }

        private static bool HasGamedataChildren(string container)
        {
            foreach (string child in GAMEDATA_CHILD_FOLDERS)
            {
                if (XRayFileSystem.FindChildDirectory(container, child) != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddFolderSource(XRaySourcePlan plan, string folder, string name, XRayAssetSourceKind kind, string settingName, StringBuilder stamp)
        {
            if (string.IsNullOrEmpty(folder))
            {
                plan.Warnings.Add($"{settingName} is not set.");
                return;
            }

            if (!Directory.Exists(folder))
            {
                plan.Errors.Add($"{settingName} '{folder}' does not exist.");
                return;
            }

            string root = Path.GetFullPath(folder);
            if (XRayFileSystem.FindChildDirectory(root, "textures") == null && XRayFileSystem.FindChildDirectory(root, GAMEDATA_FOLDER) != null)
            {
                plan.Warnings.Add($"{settingName} '{root}' contains a 'gamedata' folder; the folder itself must be the gamedata root (it must contain 'textures').");
            }

            XRayFileSystem.AppendStamp(stamp, root);
            plan.Sources.Add(new XRayAssetSource(name, kind, kind == XRayAssetSourceKind.BaseAnomaly ? -3 : -2, root, root));
        }
    }
}

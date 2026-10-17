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
                case XRaySourceMode.CustomFolder:
                    AddFolderSource(plan, configuration.CustomFolder, CUSTOM_FOLDER_NAME, XRayAssetSourceKind.Custom, "Custom Folder", stamp);
                    break;
            }

            AddFolderSource(plan, configuration.BaseAnomalyFolder, BASE_ANOMALY_NAME, XRayAssetSourceKind.BaseAnomaly, "Base Anomaly Folder", stamp);
            if (plan.SkipFileSuffixes.Count == 0)
            {
                plan.SkipFileSuffixes.Add(".mohidden");
            }

            foreach (XRayAssetSource source in plan.Sources)
            {
                XRayFileSystem.AppendStamp(stamp, source.GamedataRoot);
                foreach (string scope in configuration.IndexScope)
                {
                    XRayFileSystem.AppendStamp(stamp, Path.Combine(source.GamedataRoot, scope));
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
                string container = Path.Combine(installation.ModsDirectory, entry.Name);
                if (!Directory.Exists(container))
                {
                    plan.MissingModFolders.Add(entry.Name);
                    continue;
                }

                XRayFileSystem.AppendStamp(stamp, container);
                AddMo2Source(plan, configuration, entry.Name, XRayAssetSourceKind.Mod, entry.Priority, container);
            }

            if (plan.MissingModFolders.Count > 0)
            {
                plan.Warnings.Add($"{plan.MissingModFolders.Count} enabled mods have no folder in '{installation.ModsDirectory}': {string.Join(", ", plan.MissingModFolders)}");
            }

            if (plan.ModsWithoutGamedataCount > 0)
            {
                plan.Warnings.Add($"{plan.ModsWithoutGamedataCount} enabled mods keep game folders (textures, meshes, ...) directly in the mod root. MO2 maps a mod root to the game root for {installation.GameName ?? "this game"}, so these files are outside gamedata and are ignored. Enable 'Treat Mod Root As Gamedata' only if your MO2 game plugin maps mods into gamedata.");
            }

            if (configuration.IncludeGameGamedata && !string.IsNullOrEmpty(installation.GamePath))
            {
                string gameGamedata = XRayFileSystem.FindChildDirectory(installation.GamePath, GAMEDATA_FOLDER);
                if (gameGamedata != null)
                {
                    plan.Sources.Add(new XRayAssetSource(GAME_GAMEDATA_NAME, XRayAssetSourceKind.GameGamedata, -1, gameGamedata, installation.GamePath));
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

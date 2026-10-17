using System;
using System.Collections.Generic;
using System.IO;

namespace XRay.Importer.Sources.Mo2
{
    public static class Mo2InstallationReader
    {
        public const string INI_FILE_NAME = "ModOrganizer.ini";
        public const string EXECUTABLE_FILE_NAME = "ModOrganizer.exe";
        public const string PORTABLE_MARKER_FILE_NAME = "portable.txt";
        public const string BASE_DIR_PLACEHOLDER = "%BASE_DIR%";

        private const string DEFAULT_SKIP_SUFFIX = ".mohidden";
        private const string DEFAULT_SKIP_DIRECTORY = ".git";

        public static Mo2Installation Read(string folder, string instanceName, string localAppDataFolder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                throw new XRaySourceException($"MO2 folder '{folder}' does not exist.");
            }

            string fullFolder = Path.GetFullPath(folder);
            string iniPath = Path.Combine(fullFolder, INI_FILE_NAME);
            bool isPortable = true;
            if (!File.Exists(iniPath))
            {
                iniPath = FindInstanceIni(instanceName, localAppDataFolder);
                isPortable = false;
                if (iniPath == null)
                {
                    throw new XRaySourceException($"'{fullFolder}' contains no {INI_FILE_NAME}, and no global MO2 instance '{instanceName}' was found under '{GetInstancesFolder(localAppDataFolder)}'. Select the MO2 instance folder manually.");
                }
            }

            return ReadIni(iniPath, isPortable || File.Exists(Path.Combine(fullFolder, PORTABLE_MARKER_FILE_NAME)));
        }

        public static Mo2Installation ReadIni(string iniPath, bool isPortable)
        {
            Mo2IniFile ini;
            try
            {
                ini = Mo2IniFile.Load(iniPath);
            }
            catch (IOException exception)
            {
                throw new XRaySourceException($"Cannot read '{iniPath}': {exception.Message}", exception);
            }

            string instanceFolder = Path.GetDirectoryName(Path.GetFullPath(iniPath));
            var installation = new Mo2Installation
            {
                InstanceFolder = instanceFolder,
                IniPath = Path.GetFullPath(iniPath),
                IsPortable = isPortable,
                GameName = ini.GetValue("General", "gameName"),
                GamePath = NormalizeDirectory(ini.GetValue("General", "gamePath"), instanceFolder),
                SelectedProfile = ini.GetValue("General", "selected_profile")
            };

            installation.BaseDirectory = ResolveDirectory(ini.GetValue("Settings", "base_directory"), instanceFolder, instanceFolder, instanceFolder);
            installation.ModsDirectory = ResolveDirectory(ini.GetValue("Settings", "mod_directory"), installation.BaseDirectory, instanceFolder, Path.Combine(installation.BaseDirectory, "mods"));
            installation.ProfilesDirectory = ResolveDirectory(ini.GetValue("Settings", "profiles_directory"), installation.BaseDirectory, instanceFolder, Path.Combine(installation.BaseDirectory, "profiles"));
            installation.OverwriteDirectory = ResolveDirectory(ini.GetValue("Settings", "overwrite_directory"), installation.BaseDirectory, instanceFolder, Path.Combine(installation.BaseDirectory, "overwrite"));
            AddList(installation.SkipFileSuffixes, ini.GetValue("Settings", "skip_file_suffixes"), DEFAULT_SKIP_SUFFIX);
            AddList(installation.SkipDirectories, ini.GetValue("Settings", "skip_directories"), DEFAULT_SKIP_DIRECTORY);
            installation.Profiles.AddRange(Mo2ProfileReader.ListProfiles(installation.ProfilesDirectory));

            if (!Directory.Exists(installation.ModsDirectory))
            {
                installation.Warnings.Add($"Mods directory '{installation.ModsDirectory}' does not exist.");
            }

            if (!Directory.Exists(installation.ProfilesDirectory))
            {
                installation.Warnings.Add($"Profiles directory '{installation.ProfilesDirectory}' does not exist.");
            }

            if (!string.IsNullOrEmpty(installation.SelectedProfile) && !installation.Profiles.Contains(installation.SelectedProfile))
            {
                installation.Warnings.Add($"Selected profile '{installation.SelectedProfile}' from {INI_FILE_NAME} was not found in '{installation.ProfilesDirectory}'.");
            }

            return installation;
        }

        public static List<string> FindInstances(string localAppDataFolder)
        {
            var instances = new List<string>();
            string root = GetInstancesFolder(localAppDataFolder);
            if (root == null || !Directory.Exists(root))
            {
                return instances;
            }

            foreach (string directory in Directory.GetDirectories(root))
            {
                if (File.Exists(Path.Combine(directory, INI_FILE_NAME)))
                {
                    instances.Add(Path.GetFileName(directory));
                }
            }

            instances.Sort(StringComparer.OrdinalIgnoreCase);
            return instances;
        }

        private static string FindInstanceIni(string instanceName, string localAppDataFolder)
        {
            List<string> instances = FindInstances(localAppDataFolder);
            if (instances.Count == 0)
            {
                return null;
            }

            string selected = null;
            if (!string.IsNullOrEmpty(instanceName))
            {
                selected = instances.Find(name => string.Equals(name, instanceName, StringComparison.OrdinalIgnoreCase));
            }
            else if (instances.Count == 1)
            {
                selected = instances[0];
            }

            return selected == null ? null : Path.Combine(GetInstancesFolder(localAppDataFolder), selected, INI_FILE_NAME);
        }

        private static string GetInstancesFolder(string localAppDataFolder)
        {
            return string.IsNullOrEmpty(localAppDataFolder) ? null : Path.Combine(localAppDataFolder, "ModOrganizer");
        }

        private static string ResolveDirectory(string value, string baseDirectory, string instanceFolder, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Path.GetFullPath(fallback);
            }

            string expanded = value.Replace(BASE_DIR_PLACEHOLDER, baseDirectory);
            return NormalizeDirectory(expanded, Path.IsPathRooted(expanded) ? instanceFolder : baseDirectory);
        }

        private static string NormalizeDirectory(string value, string relativeTo)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string path = value.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(relativeTo, path)).TrimEnd(Path.DirectorySeparatorChar);
        }

        private static void AddList(List<string> target, string value, string fallback)
        {
            if (value == null)
            {
                target.Add(fallback);
                return;
            }

            foreach (string item in value.Split(';'))
            {
                string trimmed = item.Trim();
                if (trimmed.Length > 0)
                {
                    target.Add(trimmed);
                }
            }
        }
    }
}

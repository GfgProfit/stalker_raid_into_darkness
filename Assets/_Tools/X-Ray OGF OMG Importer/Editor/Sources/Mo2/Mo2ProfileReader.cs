using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace XRay.Importer.Sources.Mo2
{
    public static class Mo2ProfileReader
    {
        public const string MOD_LIST_FILE_NAME = "modlist.txt";
        public const string SEPARATOR_SUFFIX = "_separator";

        public static List<string> ListProfiles(string profilesDirectory)
        {
            var profiles = new List<string>();
            if (string.IsNullOrEmpty(profilesDirectory) || !Directory.Exists(profilesDirectory))
            {
                return profiles;
            }

            foreach (string directory in Directory.GetDirectories(profilesDirectory))
            {
                if (File.Exists(Path.Combine(directory, MOD_LIST_FILE_NAME)))
                {
                    profiles.Add(Path.GetFileName(directory));
                }
            }

            profiles.Sort(StringComparer.OrdinalIgnoreCase);
            return profiles;
        }

        public static string GetModListPath(string profilesDirectory, string profileName)
        {
            return Path.Combine(profilesDirectory, profileName, MOD_LIST_FILE_NAME);
        }

        public static Mo2ModList ReadModList(string modListPath)
        {
            if (!File.Exists(modListPath))
            {
                throw new XRaySourceException($"MO2 mod list '{modListPath}' does not exist.");
            }

            return ParseModList(File.ReadAllLines(modListPath, Encoding.UTF8));
        }

        public static Mo2ModList ParseModList(IList<string> lines)
        {
            var list = new Mo2ModList();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                Mo2ModState state;
                switch (line[0])
                {
                    case '+':
                        state = Mo2ModState.Enabled;
                        break;
                    case '-':
                        state = Mo2ModState.Disabled;
                        break;
                    case '*':
                        state = Mo2ModState.Unmanaged;
                        break;
                    default:
                        list.Warnings.Add($"modlist.txt line {i + 1}: '{line}' has no state prefix and was ignored.");
                        continue;
                }

                string name = line.Substring(1).Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                if (!seen.Add(name))
                {
                    list.Warnings.Add($"modlist.txt line {i + 1}: mod '{name}' is listed more than once; the first entry is used.");
                    continue;
                }

                bool isSeparator = name.EndsWith(SEPARATOR_SUFFIX, StringComparison.OrdinalIgnoreCase);
                list.Entries.Add(new Mo2ModEntry { Name = name, State = state, IsSeparator = isSeparator, LineNumber = i + 1 });
            }

            for (int i = 0; i < list.Entries.Count; i++)
            {
                list.Entries[i].Priority = list.Entries.Count - 1 - i;
            }

            return list;
        }
    }
}

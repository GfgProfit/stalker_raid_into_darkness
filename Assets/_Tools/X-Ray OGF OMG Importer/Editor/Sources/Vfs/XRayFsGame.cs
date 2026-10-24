using System;
using System.Collections.Generic;
using XRay.Importer.Core.IO;

namespace XRay.Importer.Sources.Vfs
{
    public sealed class XRayFsPath
    {
        public string Alias { get; internal set; }
        public bool Recurse { get; internal set; }
        public string RelativePath { get; internal set; }
        public bool IsExternal { get; internal set; }
        public int Line { get; internal set; }
    }

    public sealed class XRayFsGame
    {
        public const string FILE_NAME = "fsgame.ltx";
        public const string FS_ROOT = "$fs_root$";
        public const string GAME_DATA = "$game_data$";
        public const string APP_DATA_ROOT = "$app_data_root$";

        private static readonly string DEFAULT_TEXT = string.Join("\n", "$app_data_root$ = true | false | $fs_root$ | appdata\\", "$arch_dir$ = false | false | $fs_root$ | db\\", "$arch_dir_configs$ = false | false | $arch_dir$ | configs\\", "$arch_dir_maps$ = false | false | $arch_dir$ | levels\\", "$arch_dir_meshes$ = false | false | $arch_dir$ | meshes\\", "$arch_dir_sounds$ = false | false | $arch_dir$ | sounds\\", "$arch_dir_textures$ = false | false | $arch_dir$ | textures\\", "$arch_dir_resource$ = false | false | $arch_dir$ | mods\\", "$arch_dir_patches$ = false | true | $arch_dir$ | patches\\", "$arch_dir_addons$ = false | true | $arch_dir$ | addons\\", "$game_data$ = true | true | $fs_root$ | gamedata\\");

        private readonly Dictionary<string, XRayFsPath> _aliases = new Dictionary<string, XRayFsPath>(StringComparer.OrdinalIgnoreCase);

        public List<XRayFsPath> Paths { get; } = new List<XRayFsPath>();
        public List<string> Warnings { get; } = new List<string>();
        public string Origin { get; internal set; }

        public static XRayFsGame CreateDefault()
        {
            XRayFsGame fs = Parse(DEFAULT_TEXT);
            fs.Origin = "built-in Anomaly 1.5.3 defaults";
            return fs;
        }

        public static XRayFsGame Parse(byte[] data, string origin)
        {
            XRayFsGame fs = Parse(Cp1251.Decode(data, 0, data.Length));
            fs.Origin = origin;
            return fs;
        }

        public static XRayFsGame Parse(string text)
        {
            var fs = new XRayFsGame();
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == ';')
                {
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                string alias = line.Substring(0, separator).Trim().ToLowerInvariant();
                string[] items = line.Substring(separator + 1).Split('|');
                if (items.Length < 3)
                {
                    fs.Warnings.Add($"{FILE_NAME} line {i + 1}: '{line}' has fewer than 3 items and was ignored.");
                    continue;
                }

                string root = items[2].Trim().ToLowerInvariant();
                string add = items.Length >= 4 ? items[3].Trim() : string.Empty;
                var path = new XRayFsPath { Alias = alias, Recurse = IsTrue(items[0]), Line = i + 1 };
                if (root == FS_ROOT)
                {
                    path.RelativePath = XRayVirtualPath.Normalize(add);
                }
                else if (fs._aliases.TryGetValue(root, out XRayFsPath parent))
                {
                    path.IsExternal = parent.IsExternal;
                    path.RelativePath = XRayVirtualPath.Normalize(parent.RelativePath + "/" + add);
                }
                else
                {
                    path.IsExternal = true;
                    path.RelativePath = XRayVirtualPath.Normalize(root + "/" + add);
                }

                fs.Paths.Add(path);
                fs._aliases[alias] = path;
            }

            return fs;
        }

        public bool TryGet(string alias, out XRayFsPath path)
        {
            return _aliases.TryGetValue(alias, out path);
        }

        public int GameDataLine()
        {
            for (int i = 0; i < Paths.Count; i++)
            {
                if (Paths[i].Alias == GAME_DATA)
                {
                    return i;
                }
            }

            return Paths.Count;
        }

        public string ResolveEntryPoint(string entryPoint)
        {
            string value = (entryPoint ?? string.Empty).Trim();
            if (string.Equals(value, "gamedata", StringComparison.OrdinalIgnoreCase))
            {
                return "gamedata";
            }

            if (value.Length == 0 || value[0] != '$')
            {
                return null;
            }

            int end = value.IndexOf('$', 1);
            if (end < 0)
            {
                return null;
            }

            string alias = value.Substring(0, end + 1).ToLowerInvariant();
            string rest = value.Substring(end + 1);
            if (alias == FS_ROOT)
            {
                return XRayVirtualPath.Normalize(rest);
            }

            if (_aliases.TryGetValue(alias, out XRayFsPath path) && !path.IsExternal)
            {
                return XRayVirtualPath.Normalize(path.RelativePath + "/" + rest);
            }

            return null;
        }

        private static bool IsTrue(string value)
        {
            string trimmed = value.Trim();
            return string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase) || trimmed == "1" || string.Equals(trimmed, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(trimmed, "on", StringComparison.OrdinalIgnoreCase);
        }
    }
}

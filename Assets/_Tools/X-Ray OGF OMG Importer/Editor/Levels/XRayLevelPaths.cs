using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayLevelPaths
    {
        public const string ROOT = "Assets/_Source/Levels";
        public const string TEXTURES = ROOT + "/Textures";
        public const string MATERIALS = ROOT + "/Materials";
        public const string MANIFEST_NAME = "LevelImportManifest.asset";
        public const string REPORT_NAME = "ImportReport.json";
        public const string LEVEL_FILE = "level";
        public const string ANOMALY_ROOT = ROOT + "/Anomaly";
        public const string SOURCE_MANIFESTS = "Manifests";

        public static string Root { get; private set; } = ROOT;
        public static string Textures => Root + "/Textures";
        public static string Materials => Root + "/Materials";
        public static string ArchiveRoot => "E:/Projects/stalker_raid_into_darkness_Archive";
        public static string Mo2Cache => Path.Combine(ArchiveRoot, "XRayMO2LevelImporter_Cache");

        public static string AttemptReportPath(string level, string profileId)
        {
            string folder = Path.Combine(ArchiveRoot, "XRayLevelImporter_Reports", SanitizeName(profileId ?? "Gamedata"), SanitizeName(level), System.DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff"));
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, REPORT_NAME);
        }

        public static string SpaceRoot(string profileId)
        {
            return string.IsNullOrEmpty(profileId) ? ROOT : ANOMALY_ROOT + "/" + profileId;
        }

        public static void UseRoot(string root)
        {
            Root = string.IsNullOrEmpty(root) ? ROOT : root;
        }

        public static string LevelFolder(string level)
        {
            return LevelFolder(Root, level);
        }

        public static string LevelFolder(string root, string level)
        {
            return root + "/" + level;
        }

        public static string ScenePath(string root, string level)
        {
            return LevelFolder(root, level) + "/Scenes/" + level + ".unity";
        }

        public static string ManifestPath(string root, string level)
        {
            return LevelFolder(root, level) + "/" + MANIFEST_NAME;
        }

        public static string Meshes(string level)
        {
            return LevelFolder(level) + "/Meshes";
        }

        public static string Prefabs(string level)
        {
            return LevelFolder(level) + "/Prefabs";
        }

        public static string Scenes(string level)
        {
            return LevelFolder(level) + "/Scenes";
        }

        public static string Details(string level)
        {
            return LevelFolder(level) + "/Details";
        }

        public static string ScenePath(string level)
        {
            return Scenes(level) + "/" + level + ".unity";
        }

        public static string PrefabPath(string level)
        {
            return Prefabs(level) + "/" + level + ".prefab";
        }

        public static string ManifestPath(string level)
        {
            return LevelFolder(level) + "/" + MANIFEST_NAME;
        }

        public static string ReportPath(string level)
        {
            return LevelFolder(level) + "/" + REPORT_NAME;
        }

        public static string TextureFolder(string virtualPath)
        {
            string normalized = (virtualPath ?? string.Empty).Replace('\\', '/');
            if (normalized.StartsWith("textures/"))
            {
                normalized = normalized.Substring("textures/".Length);
            }

            string directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
            return string.IsNullOrEmpty(directory) ? Textures : Textures + "/" + SanitizePath(directory);
        }

        public static string MaterialFolder(string virtualPath)
        {
            string normalized = (virtualPath ?? string.Empty).Replace('\\', '/');
            if (normalized.StartsWith("textures/"))
            {
                normalized = normalized.Substring("textures/".Length);
            }

            string directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
            return string.IsNullOrEmpty(directory) ? Materials : Materials + "/" + SanitizePath(directory);
        }

        public static string SanitizeName(string name)
        {
            var builder = new StringBuilder(name.Length);
            foreach (char ch in name)
            {
                builder.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.' ? ch : '_');
            }

            return builder.ToString();
        }

        public static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetFolder));
        }

        private static string SanitizePath(string path)
        {
            string[] parts = path.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = SanitizeName(parts[i]);
            }

            return string.Join("/", parts);
        }
    }
}

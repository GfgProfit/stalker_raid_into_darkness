using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Core.Level;
using XRay.Importer.Sources.Levels;

namespace XRay.Importer.Editor.Levels
{
    public sealed class XRayLevelPendingChoice
    {
        public string FilePath { get; set; }
        public string DroppedAssetPath { get; set; }
        public string Method { get; set; }
        public List<XRayLevelEntry> Candidates { get; } = new List<XRayLevelEntry>();
        public bool NoMatch { get; set; }
    }

    public static class XRayLevelDropHandler
    {
        public static XRayLevelPendingChoice Pending { get; private set; }
        public static string LastMessage { get; private set; }

        public static bool IsLevelFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || !string.IsNullOrEmpty(Path.GetExtension(path)))
            {
                return false;
            }

            var head = new byte[16];
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (stream.Read(head, 0, head.Length) != head.Length)
                {
                    return false;
                }
            }

            return XRayLevelReader.IsLevelSignature(head);
        }

        public static bool Handle(string filePath, string droppedAssetPath, bool interactive)
        {
            if (!IsLevelFile(filePath))
            {
                LastMessage = $"'{filePath}' is not an X-Ray game level (expected an extensionless 'level' file with chunk 0x1 header).";
                Debug.LogWarning("[X-Ray Level] " + LastMessage);
                return false;
            }

            XRayLevelGamedataInfo info = XRayLevelSourceCache.Inspect(true);
            if (!info.IsValid)
            {
                LastMessage = "Level Gamedata Source is invalid: " + string.Join("; ", info.Errors);
                Debug.LogError("[X-Ray Level] " + LastMessage);
                if (interactive)
                {
                    XRayLevelImporterWindow.Open();
                }

                return false;
            }

            string hint = droppedAssetPath == null ? filePath : null;
            List<XRayLevelEntry> matches = XRayLevelGamedata.Match(info, filePath, hint, out string method);
            if (matches.Count == 1)
            {
                Pending = null;
                return Start(matches[0].Name, droppedAssetPath, $"{method}: {matches[0].LevelPath}");
            }

            var choice = new XRayLevelPendingChoice { FilePath = filePath, DroppedAssetPath = droppedAssetPath, Method = method, NoMatch = matches.Count == 0 };
            choice.Candidates.AddRange(matches.Count > 0 ? matches : info.Levels);
            Pending = choice;
            LastMessage = matches.Count == 0 ? $"'{filePath}' does not match any level of '{info.LevelsFolder}' by {method}. Select the level manually; its level.geom must belong to this file." : $"'{filePath}' matches {matches.Count} levels with identical content; select one.";
            Debug.LogWarning("[X-Ray Level] " + LastMessage);
            XRayLevelImporterWindow.Open();
            return false;
        }

        public static bool Resolve(XRayLevelEntry entry)
        {
            XRayLevelPendingChoice choice = Pending;
            Pending = null;
            if (choice == null)
            {
                return false;
            }

            string identification = choice.NoMatch ? $"manual selection (no content match by {choice.Method})" : $"manual selection among identical candidates ({choice.Method})";
            if (choice.NoMatch && !XRayLevelGamedata.SameContent(choice.FilePath, XRayLevelGamedata.PhysicalLevelPath(XRayLevelSourceCache.Inspect(false), entry)))
            {
                identification += "; WARNING: dropped file differs from the gamedata level, the gamedata version is imported";
            }

            return Start(entry.Name, choice.DroppedAssetPath, identification);
        }

        public static void Dismiss()
        {
            Pending = null;
        }

        public static bool Start(string level, string droppedAssetPath, string identification)
        {
            var request = new XRayLevelImportRequest { LevelName = level, DroppedAssetPath = droppedAssetPath, Identification = identification, Settings = XRayAssetSourceUserSettings.instance.LevelImport.Clone() };
            bool queued = XRayLevelImportJob.Enqueue(request);
            LastMessage = queued ? $"Import of '{level}' started ({identification})." : $"Import of '{level}' is already running.";
            return queued;
        }
    }

    public sealed class XRayLevelDropDetector : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths, bool didDomainReload)
        {
            if (!XRayAssetSourceUserSettings.instance.LevelImport.AutoImportDroppedLevels)
            {
                return;
            }

            foreach (string path in importedAssets)
            {
                if (!string.Equals(Path.GetFileName(path), XRayLevelPaths.LEVEL_FILE, StringComparison.Ordinal) || IsPlacedLevel(path))
                {
                    continue;
                }

                string full = Path.GetFullPath(path);
                if (!XRayLevelDropHandler.IsLevelFile(full))
                {
                    continue;
                }

                string assetPath = path;
                EditorApplication.delayCall += () => XRayLevelDropHandler.Handle(full, assetPath, true);
            }
        }

        private static bool IsPlacedLevel(string path)
        {
            string folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string parent = folder != null ? Path.GetDirectoryName(folder)?.Replace('\\', '/') : null;
            string space = parent != null ? Path.GetDirectoryName(parent)?.Replace('\\', '/') : null;
            return string.Equals(parent, XRayLevelPaths.ROOT, StringComparison.OrdinalIgnoreCase) || string.Equals(space, XRayLevelPaths.ANOMALY_ROOT, StringComparison.OrdinalIgnoreCase);
        }
    }
}

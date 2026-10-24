using System;
using System.Collections.Generic;
using UnityEditor;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayLevelSharedAssets
    {
        public static void Count(XRayLevelImportRecord record)
        {
            var own = new HashSet<string>(record.GeneratedAssets, StringComparer.OrdinalIgnoreCase);
            var sharedTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sharedMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var levels = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(XRayLevelImportManifest), new[] { XRayLevelPaths.Root }))
            {
                var manifest = AssetDatabase.LoadAssetAtPath<XRayLevelImportManifest>(AssetDatabase.GUIDToAssetPath(guid));
                if (manifest == null || manifest.Record == null || string.Equals(manifest.Record.LevelName, record.LevelName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool shares = false;
                foreach (string path in manifest.Record.GeneratedAssets)
                {
                    if (!own.Contains(path))
                    {
                        continue;
                    }

                    if (path.StartsWith(XRayLevelPaths.Textures + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        sharedTextures.Add(path);
                        shares = true;
                    }
                    else if (path.StartsWith(XRayLevelPaths.Materials + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        sharedMaterials.Add(path);
                        shares = true;
                    }
                }

                if (shares)
                {
                    levels.Add(manifest.Record.LevelName);
                }
            }

            record.SetMetric("SharedTexturesWithOtherLevels", sharedTextures.Count);
            record.SetMetric("SharedMaterialsWithOtherLevels", sharedMaterials.Count);
            if (levels.Count > 0)
            {
                record.Warnings.RemoveAll(item => item.StartsWith("Shares assets with"));
                record.Warnings.Add($"Shares assets with {string.Join(", ", levels)}: {sharedTextures.Count} textures, {sharedMaterials.Count} materials.");
            }
        }
    }
}

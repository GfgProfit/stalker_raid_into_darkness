using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Levels;
using XRay.Importer.Sources;
using XRay.Importer.Sources.Dds;
using XRay.Importer.Sources.Levels;
using XRay.Importer.Sources.Mo2;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayLevelMo2Validation
    {
        public static string BeginIndex()
        {
            if (Application.isPlaying || EditorApplication.isCompiling || XRayLevelImportJob.Current != null)
            {
                throw new InvalidOperationException("The Editor must be idle before MO2 validation.");
            }

            XRayAssetSourceUserSettings user = XRayAssetSourceUserSettings.instance;
            user.LevelSourceType = XRayLevelSourceType.Mo2Profile;
            user.SaveSettings();
            XRayLevelSourceCache.BeginMo2Build();
            return Status();
        }

        public static string Status()
        {
            XRayLevelIndexJob index = XRayLevelSourceCache.CurrentBuild;
            XRayLevelImportJob import = XRayLevelImportJob.Current;
            if (import != null)
            {
                return $"Import {import.LevelName}: {import.Stage} ({import.Progress:P0})";
            }

            if (index != null && !index.IsDone)
            {
                return $"Index: {index.Stage} ({index.Progress:P0})";
            }

            if (index != null && index.Error != null)
            {
                return "Index failed: " + index.Error;
            }

            XRayAssetIndex current = XRayLevelSourceCache.CurrentIndex;
            return $"Index files: {current?.Count ?? 0}; probes: {XRayLevelSourceCache.IsProbing}; last import: {XRayLevelImportJob.LastState}; errors: {string.Join("; ", XRayLevelImportJob.LastRecord?.Errors ?? new List<string>())}";
        }

        public static string ExportCatalog(string outputDirectory)
        {
            string output = PrepareOutput(outputDirectory);
            XRayLevelGamedataInfo info = XRayLevelSourceCache.Inspect(false);
            XRaySourcePlan plan = XRayLevelSourceCache.Mo2Plan;
            XRayAssetIndex index = XRayLevelSourceCache.CurrentIndex;
            if (index == null || info.Effective == null)
            {
                throw new InvalidOperationException("Build the MO2 virtual index first.");
            }

            var profile = new StringBuilder();
            profile.AppendLine("Instance: " + plan.Installation.InstanceFolder);
            profile.AppendLine("Mods: " + plan.Installation.ModsDirectory);
            profile.AppendLine("Profiles: " + plan.Installation.ProfilesDirectory);
            profile.AppendLine("Overwrite: " + plan.Installation.OverwriteDirectory);
            profile.AppendLine("Game: " + plan.GameFolder);
            profile.AppendLine("Profile: " + plan.ProfileName);
            profile.AppendLine("Fingerprint: " + plan.Fingerprint);
            profile.AppendLine("Enabled: " + plan.ActiveModCount);
            profile.AppendLine($"Files: {index.Count}; conflicts: {index.Statistics.ConflictCount}; archives: {index.Statistics.LoadedArchiveCount}; archive entries: {index.Statistics.ArchiveEntryCount}");
            foreach (Mo2ModEntry mod in plan.ModList.Entries)
            {
                profile.AppendLine($"{mod.State}\t{mod.Priority}\t{mod.Name}");
            }

            profile.AppendLine("Warnings:");
            foreach (string warning in index.Warnings)
            {
                profile.AppendLine(warning);
            }

            File.WriteAllText(Path.Combine(output, "profile.txt"), profile.ToString());
            File.WriteAllLines(Path.Combine(output, "unreadable_archives.txt"), index.UnreadableArchives);
            var levels = new List<string> { "Level,Source,Version,Status,Visuals,Trees,Details,Collision,Wallmarks,LODs,Registered,Modded,Errors,Missing" };
            foreach (XRayLevelEntry entry in info.Levels)
            {
                XRayLevelProbe probe = entry.Probe ?? XRayLevelProbe.Run(info.Effective, entry);
                levels.Add(string.Join(",", Csv(entry.Name), Csv(entry.Origin), probe.Version, probe.Status, probe.Visuals, probe.Trees, probe.HasDetails, probe.HasCollision, probe.HasWallmarks, probe.HasLods, entry.IsRegistered, entry.IsModded, Csv(string.Join("; ", probe.Errors)), Csv(string.Join("; ", probe.MissingRequired))));
            }

            File.WriteAllLines(Path.Combine(output, "levels.csv"), levels);
            return profile.ToString().Split(new[] { "Warnings:" }, StringSplitOptions.None)[0] + $"Levels: {info.Levels.Count}; unreadable archives: {index.UnreadableArchives.Count}";
        }

        public static string StartImport(string level)
        {
            return StartImport(level, false);
        }

        public static string StartImport(string level, bool updateMaterials)
        {
            var settings = JsonUtility.FromJson<XRayLevelImportSettings>(JsonUtility.ToJson(XRayAssetSourceUserSettings.instance.LevelImport));
            settings.OpenSceneAfterImport = false;
            if (updateMaterials)
            {
                settings.MaterialUpdateMode = XRayLevelMaterialUpdateMode.Update;
            }

            bool queued = XRayLevelImportJob.Enqueue(new XRayLevelImportRequest { LevelName = level, Settings = settings, Identification = "MO2 end-to-end validation" });
            return queued ? Status() : "Not queued";
        }

        public static string ExportImport(string level, string outputDirectory)
        {
            string output = PrepareOutput(outputDirectory);
            string root = XRayLevelSourceCache.AssetRoot;
            var manifest = AssetDatabase.LoadAssetAtPath<XRayLevelImportManifest>(XRayLevelPaths.ManifestPath(root, level));
            if (manifest == null || !manifest.Record.Completed)
            {
                throw new InvalidOperationException("No completed manifest for " + level + ". " + Status());
            }

            XRayLevelImportRecord record = manifest.Record;
            File.WriteAllText(Path.Combine(output, "import.json"), record.ToJson());
            var assets = new List<string> { "Asset,GUID" };
            var paths = new HashSet<string>(record.GeneratedAssets);
            foreach (string asset in paths)
            {
                assets.Add(Csv(asset) + "," + AssetDatabase.AssetPathToGUID(asset));
            }

            File.WriteAllLines(Path.Combine(output, "guids.csv"), assets);
            var textures = new List<string> { "VirtualPath,WinningSource,Priority,Size,Resolution,Format,Mips,UnityAsset,GUID,UnityResolution,UsedBy,Status" };
            XRayEffectiveGamedata gamedata = XRayLevelSourceCache.Inspect(false).Effective;
            int highResolution = 0;
            int mismatches = 0;
            var usedBy = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string asset in paths)
            {
                if (!asset.EndsWith(".mat", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (string dependency in AssetDatabase.GetDependencies(asset, false))
                {
                    if (!usedBy.TryGetValue(dependency, out List<string> users))
                    {
                        users = new List<string>();
                        usedBy.Add(dependency, users);
                    }

                    users.Add(asset);
                }
            }

            foreach (XRayLevelSourceFile file in record.Source.Files)
            {
                if (string.IsNullOrEmpty(file.AssetPath) || !file.VirtualPath.EndsWith(".dds", StringComparison.Ordinal))
                {
                    continue;
                }

                DdsHeader header = DdsHeader.Parse(gamedata.ReadAllBytes(file.VirtualPath));
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(file.AssetPath);
                bool matches = texture != null && texture.width == header.Width && texture.height == header.Height;
                mismatches += matches ? 0 : 1;
                highResolution += matches && Math.Max(header.Width, header.Height) >= 4096 ? 1 : 0;
                usedBy.TryGetValue(file.AssetPath, out List<string> materials);
                materials = materials ?? new List<string>();
                textures.Add(string.Join(",", Csv(file.VirtualPath), Csv(file.Origin), Csv(file.Priority), file.Size, Csv(header.Width + "x" + header.Height), header.Format, header.MipCount, Csv(file.AssetPath), file.AssetGuid, Csv(texture != null ? texture.width + "x" + texture.height : "missing"), Csv(string.Join("; ", materials)), matches ? "OK" : "Mismatch"));
            }

            File.WriteAllLines(Path.Combine(output, "textures.csv"), textures);
            return $"{level}: completed; {paths.Count} assets; {textures.Count - 1} DDS; {highResolution} 4K+; {mismatches} size mismatches; scene {record.ScenePath}";
        }

        public static string SelectFolderSource()
        {
            XRayAssetSourceUserSettings user = XRayAssetSourceUserSettings.instance;
            user.LevelSourceType = XRayLevelSourceType.GamedataFolder;
            user.SaveSettings();
            return user.LevelGamedataFolder;
        }

        public static string AuditTreeMaterials(string level, string outputDirectory)
        {
            string output = PrepareOutput(outputDirectory);
            string prefabPath = XRayLevelPaths.LevelFolder(XRayLevelSourceCache.AssetRoot, level) + "/Prefabs/" + level + ".prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var materials = new HashSet<Material>();
            foreach (XRayLevelInstancedRenderer renderer in prefab.GetComponentsInChildren<XRayLevelInstancedRenderer>(true))
            {
                foreach (XRayInstanceBatch batch in renderer.BatchSet.Batches)
                {
                    foreach (Material material in batch.Materials)
                    {
                        materials.Add(material);
                    }
                }
            }

            int missing = 0;
            int transparent = 0;
            var rows = new List<string> { "Material,Texture,Size,AlphaMin,AlphaMax,AlphaCutoff,AlphaTest" };
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(32, 32, TextureFormat.RGBAFloat, false, true);
            try
            {
                foreach (Material material in materials)
                {
                    Texture texture = material != null && material.HasProperty("_BaseColorMap") ? material.GetTexture("_BaseColorMap") : null;
                    if (texture == null)
                    {
                        missing++;
                        rows.Add(Csv(AssetDatabase.GetAssetPath(material)) + ",missing");
                        continue;
                    }

                    Graphics.Blit(texture, target);
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
                    readback.Apply();
                    float minimum = 1;
                    float maximum = 0;
                    foreach (Color color in readback.GetPixels())
                    {
                        minimum = Mathf.Min(minimum, color.a);
                        maximum = Mathf.Max(maximum, color.a);
                    }

                    transparent += maximum < 0.001f ? 1 : 0;
                    float cutoff = material.HasProperty("_AlphaCutoff") ? material.GetFloat("_AlphaCutoff") : 0;
                    rows.Add(string.Join(",", Csv(AssetDatabase.GetAssetPath(material)), Csv(AssetDatabase.GetAssetPath(texture)), Csv(texture.width + "x" + texture.height), Csv(minimum.ToString("R", System.Globalization.CultureInfo.InvariantCulture)), Csv(maximum.ToString("R", System.Globalization.CultureInfo.InvariantCulture)), Csv(cutoff.ToString("R", System.Globalization.CultureInfo.InvariantCulture)), material.IsKeywordEnabled("_ALPHATEST_ON")));
                }
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(readback);
            }

            File.WriteAllLines(Path.Combine(output, "tree_materials.csv"), rows);
            return $"{materials.Count} instanced materials; {missing} missing albedo; {transparent} fully transparent source textures";
        }

        private static string PrepareOutput(string directory)
        {
            string path = Path.GetFullPath(directory);
            string archive = Path.GetFullPath(XRayLevelPaths.ArchiveRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            if (!path.StartsWith(archive, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Validation output must be inside " + archive);
            }

            Directory.CreateDirectory(path);
            return path;
        }

        private static string Csv(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
        }
    }
}

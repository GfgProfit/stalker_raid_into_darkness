using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Core.IO;
using XRay.Importer.Core.Ogf;
using XRay.Importer.Sources.Shaders;
using XRay.Importer.Sources.Textures;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor
{
    public enum XRayGenerationOutcome
    {
        NothingToDo,
        Disabled,
        Completed,
        WaitingForTextures,
        Failed
    }

    public static class XRayAssetGenerationCoordinator
    {
        public const string TEXTURES_FOLDER = "Textures";
        public const string MATERIALS_FOLDER = "Materials";
        public const int MAX_ATTEMPTS = 4;

        private static readonly XRayShaderRules _shaderRules = XRayShaderRules.CreateDefault();

        public static XRayShaderRules ShaderRules => _shaderRules;

        public static bool NeedsGeneration(string modelPath, XRayAssetSourceOptions options)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as XRayOgfImporter;
            if (importer == null || !importer.AutoResolveResources || !options.Import.GenerateMaterials)
            {
                return false;
            }

            OgfModel model = TryReadModel(modelPath, null);
            if (model == null)
            {
                return false;
            }

            Dictionary<AssetImporter.SourceAssetIdentifier, UnityEngine.Object> map = importer.GetExternalObjectMap();
            foreach (XRayMaterialSlot slot in XRayMaterialSlot.Collect(model))
            {
                if (!map.TryGetValue(XRayMaterialSlot.MakeIdentifier(slot.Key), out UnityEngine.Object mapped) || !(mapped is Material))
                {
                    return true;
                }
            }

            return false;
        }

        public static XRayGenerationOutcome Refresh(string modelPath, out XRayResourceReport report)
        {
            XRayAssetPostprocessor.ResetAttempts(modelPath);
            return Process(modelPath, XRayAssetSourceSettings.GetEffective(), true, out report);
        }

        public static List<string> FindModels()
        {
            var models = new List<string>();
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (path.StartsWith("Assets/", StringComparison.Ordinal) && path.EndsWith(".ogf", StringComparison.OrdinalIgnoreCase))
                {
                    models.Add(path);
                }
            }

            models.Sort(StringComparer.Ordinal);
            return models;
        }

        public static XRayGenerationOutcome Process(string modelPath, XRayAssetSourceOptions options, bool explicitRefresh, out XRayResourceReport report)
        {
            report = null;
            var importer = AssetImporter.GetAtPath(modelPath) as XRayOgfImporter;
            if (importer == null || !importer.AutoResolveResources)
            {
                return XRayGenerationOutcome.Disabled;
            }

            if (!options.Import.AutoImportTextures && !options.Import.GenerateMaterials)
            {
                return XRayGenerationOutcome.Disabled;
            }

            if (!explicitRefresh && !NeedsGeneration(modelPath, options))
            {
                XRayPendingGenerationStore.Remove(modelPath);
                return XRayGenerationOutcome.NothingToDo;
            }

            report = new XRayResourceReport { ModelPath = modelPath, SourceMode = options.Sources.Mode.ToString(), CreatedUtcTicks = DateTime.UtcNow.Ticks };
            string guid = AssetDatabase.AssetPathToGUID(modelPath);
            OgfModel model = TryReadModel(modelPath, report);
            if (model == null)
            {
                XRayResourceReportStore.Save(guid, report);
                return XRayGenerationOutcome.Failed;
            }

            XRayAssetIndex index = XRayAssetIndexCache.GetOrBuild(options);
            XRaySourcePlan plan = XRayAssetIndexCache.LastPlan;
            if (plan != null)
            {
                report.Errors.AddRange(plan.Errors);
                report.Warnings.AddRange(plan.Warnings);
                report.Profile = plan.ProfileName;
            }

            XRayTextureResolver resolver = index != null ? new XRayTextureResolver(index) : null;
            report.IndexFingerprint = index?.Fingerprint;
            string modelFolder = Path.GetDirectoryName(modelPath).Replace('\\', '/');
            string texturesFolder = options.Import.CreateTexturesFolder ? modelFolder + "/" + TEXTURES_FOLDER : modelFolder;
            string materialsFolder = options.Import.CreateMaterialsFolder ? modelFolder + "/" + MATERIALS_FOLDER : modelFolder;
            var claimed = new HashSet<string>();
            var textureImporter = new XRayTextureImporter(options.Import, explicitRefresh, claimed);
            List<XRayMaterialSlot> slots = XRayMaterialSlot.Collect(model);
            var albedoPaths = new string[slots.Count];
            var normalPaths = new string[slots.Count];
            for (int i = 0; i < slots.Count; i++)
            {
                XRayReportMaterial entry = CreateEntry(i, slots[i]);
                report.Materials.Add(entry);
                if (resolver == null || !options.Import.AutoImportTextures)
                {
                    entry.Notes.Add(resolver == null ? "Asset sources are not configured or invalid; textures were not resolved." : "Automatic texture import is disabled.");
                    continue;
                }

                XRayTextureResolution resolution = resolver.Resolve(slots[i].Texture);
                entry.DescriptionOrigin = resolution.DescriptionOrigin;
                entry.Notes.AddRange(resolution.Notes);
                XRayTextureAssetResult albedo = textureImporter.ImportAlbedo(resolution.Diffuse, texturesFolder);
                entry.Textures.Add(CreateTextureEntry("Albedo", resolution.Diffuse, albedo));
                albedoPaths[i] = albedo.AssetPath;
                if (options.Import.LogMissingTextures && !resolution.Diffuse.IsResolved)
                {
                    Debug.LogWarning($"[X-Ray] {modelPath}: texture '{resolution.Diffuse.DisplayPath}' was not found in the asset sources.");
                }

                if (options.Import.ResolveBumpMaps)
                {
                    XRayTextureAssetResult normal = textureImporter.ImportNormal(resolution.Bump, resolution.BumpCorrection, texturesFolder);
                    entry.Textures.Add(CreateTextureEntry("Normal", resolution.Bump, normal));
                    normalPaths[i] = normal.AssetPath;
                }
            }

            textureImporter.ImportPending();
            var awaited = new List<string>();
            foreach (string path in CollectPaths(albedoPaths, normalPaths))
            {
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null)
                {
                    PersistMarker(path);
                }
                else if (XRayTextureImportQueue.TryPeek(path, out XRayTextureImportRequest request) && !request.WasImported)
                {
                    awaited.Add(path);
                }
                else
                {
                    XRayTextureImportQueue.Complete(path);
                    report.Warnings.Add($"Unity could not import '{path}' as a texture; the material is created without it.");
                }
            }

            if (awaited.Count > 0)
            {
                return Defer(modelPath, guid, explicitRefresh, awaited, report);
            }

            if (options.Import.GenerateMaterials)
            {
                GenerateMaterials(importer, slots, albedoPaths, normalPaths, materialsFolder, options, claimed, report);
            }

            report.IsComplete = true;
            XRayPendingGenerationStore.Remove(modelPath);
            XRayResourceReportStore.Save(guid, report);
            return XRayGenerationOutcome.Completed;
        }

        private static void GenerateMaterials(XRayOgfImporter importer, List<XRayMaterialSlot> slots, string[] albedoPaths, string[] normalPaths, string materialsFolder, XRayAssetSourceOptions options, HashSet<string> claimed, XRayResourceReport report)
        {
            var generator = new XRayMaterialGenerator(options.Import.PreserveExistingMaterials, claimed, report.Warnings);
            Dictionary<AssetImporter.SourceAssetIdentifier, UnityEngine.Object> map = importer.GetExternalObjectMap();
            bool remapChanged = false;
            for (int i = 0; i < slots.Count; i++)
            {
                XRaySurfaceDescription surface = _shaderRules.Resolve(slots[i].Shader);
                XRayReportMaterial entry = report.Materials[i];
                entry.SurfaceRule = surface.RuleName;
                entry.SurfaceKind = surface.Kind.ToString();
                if (!string.IsNullOrEmpty(surface.Note))
                {
                    entry.Notes.Add(surface.Note);
                }

                Texture2D albedo = albedoPaths[i] != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPaths[i]) : null;
                Texture2D normal = normalPaths[i] != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(normalPaths[i]) : null;
                string nameSource = albedoPaths[i] ?? "textures/" + (string.IsNullOrEmpty(slots[i].Texture) ? "xray_default" : slots[i].Texture);
                XRayMaterialResult result = generator.GetOrCreate(slots[i].Key, nameSource, slots[i].Shader, surface, albedo, normal, materialsFolder);
                entry.MaterialPath = result.AssetPath;
                entry.MaterialStatus = result.Status;
                AssetImporter.SourceAssetIdentifier identifier = XRayMaterialSlot.MakeIdentifier(slots[i].Key);
                if (!map.TryGetValue(identifier, out UnityEngine.Object mapped) || mapped != result.Material)
                {
                    importer.AddRemap(identifier, result.Material);
                    remapChanged = true;
                }
            }

            if (remapChanged)
            {
                importer.SaveAndReimport();
            }
        }

        private static XRayGenerationOutcome Defer(string modelPath, string guid, bool explicitRefresh, List<string> awaited, XRayResourceReport report)
        {
            XRayPendingGeneration pending = XRayPendingGenerationStore.Find(modelPath) ?? new XRayPendingGeneration(modelPath, explicitRefresh);
            pending.ExplicitRefresh |= explicitRefresh;
            pending.Attempts++;
            pending.AwaitedAssets.Clear();
            pending.AwaitedAssets.AddRange(awaited);
            if (pending.Attempts > MAX_ATTEMPTS)
            {
                report.Errors.Add($"Textures were not imported after {MAX_ATTEMPTS} attempts: {string.Join(", ", awaited)}. Generation was stopped; use 'Resolve Resources' to retry.");
                XRayPendingGenerationStore.Remove(modelPath);
                XRayResourceReportStore.Save(guid, report);
                return XRayGenerationOutcome.Failed;
            }

            XRayPendingGenerationStore.Set(pending);
            foreach (XRayReportMaterial material in report.Materials)
            {
                material.MaterialStatus = XRayResourceStatus.Pending;
            }

            XRayResourceReportStore.Save(guid, report);
            return XRayGenerationOutcome.WaitingForTextures;
        }

        private static void PersistMarker(string assetPath)
        {
            if (!XRayTextureImportQueue.TryPeek(assetPath, out XRayTextureImportRequest request))
            {
                return;
            }

            XRayAssetMarker.Write(assetPath, request.Marker);
            XRayTextureImportQueue.Complete(assetPath);
        }

        private static List<string> CollectPaths(string[] albedoPaths, string[] normalPaths)
        {
            var paths = new List<string>();
            foreach (string path in albedoPaths)
            {
                if (path != null && !paths.Contains(path))
                {
                    paths.Add(path);
                }
            }

            foreach (string path in normalPaths)
            {
                if (path != null && !paths.Contains(path))
                {
                    paths.Add(path);
                }
            }

            return paths;
        }

        private static OgfModel TryReadModel(string modelPath, XRayResourceReport report)
        {
            try
            {
                return OgfReader.ReadFile(File.ReadAllBytes(modelPath), Path.GetFileName(modelPath));
            }
            catch (Exception exception) when (exception is XRayFormatException || exception is IOException)
            {
                report?.Errors.Add($"Cannot read '{modelPath}': {exception.Message}");
                return null;
            }
        }

        private static XRayReportMaterial CreateEntry(int index, XRayMaterialSlot slot)
        {
            var entry = new XRayReportMaterial { Index = index, Key = slot.Key, Texture = slot.Texture, Shader = slot.Shader, MaterialStatus = XRayResourceStatus.Skipped };
            foreach (int mesh in slot.MeshIndices)
            {
                entry.Meshes.Add($"mesh_{mesh:00}");
            }

            return entry;
        }

        private static XRayReportTexture CreateTextureEntry(string role, XRayResolvedFile file, XRayTextureAssetResult result)
        {
            var entry = new XRayReportTexture { Role = role, Requested = file?.DisplayPath, Status = result.Status, AssetPath = result.AssetPath, Format = result.Format, Note = result.Note };
            if (file != null && file.IsResolved)
            {
                entry.Winner = CreateSource(file.Winner);
                foreach (XRayResolvedSource loser in file.Losers)
                {
                    entry.Losers.Add(CreateSource(loser));
                }
            }

            return entry;
        }

        private static XRayReportSource CreateSource(XRayResolvedSource source)
        {
            return new XRayReportSource(source.SourceName, source.Kind.ToString(), source.Priority, source.FullPath);
        }
    }
}

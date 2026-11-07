using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Editor
{
    public static class MonolithMaterialMigration
    {
        private const string ARCHIVE_ROOT = "E:/Projects/stalker_raid_into_darkness_Archive";
        private const string LEVELS_ROOT = "Assets/_Source/Levels/";

        public static string ApplyBatch(string archive, int start, int count)
        {
            archive = ValidateArchive(archive);
            string[] paths = ReadPaths(archive, "MaterialPaths.txt", false);
            if (start < 0 || start >= paths.Length || count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(start), "The requested material batch is outside MaterialPaths.txt.");
            }

            int end = start + Math.Min(count, paths.Length - start);
            int converted = 0;
            int skipped = 0;
            int failed = 0;
            int nativeNormals = 0;
            var csv = new StringBuilder();
            WriteCsvRow(csv, "Path", "Result", "ShaderBefore", "ShaderAfter", "NativeNormal", "TextureCountBefore", "TextureCountAfter", "RenderQueueBefore", "RenderQueueAfter", "SurfaceBefore", "SurfaceAfter", "Error");
            for (int i = start; i < end; i++)
            {
                string path = paths[i];
                Material material = null;
                Material backup = null;
                MaterialState before = null;
                try
                {
                    material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null || AssetDatabase.IsSubAsset(material) || !AssetDatabase.IsMainAsset(material))
                    {
                        throw new InvalidOperationException("The planned path is not a standalone material asset.");
                    }

                    before = new MaterialState(material, path);
                    if (before.HasNativeNormal)
                    {
                        nativeNormals++;
                    }

                    if (before.ShaderName == MonolithMaterialConfigurator.SHADER_NAME)
                    {
                        skipped++;
                        WriteMaterialRow(csv, path, "already_monolith", before, before, string.Empty);
                        continue;
                    }

                    if (before.ShaderName != XRayMaterialGenerator.HDRP_LIT_SHADER)
                    {
                        throw new InvalidOperationException("The planned material no longer uses HDRP/Lit: " + before.ShaderName);
                    }

                    backup = new Material(material) { name = material.name, hideFlags = material.hideFlags };
                    Undo.RecordObject(material, "Migrate material to Monolith/Lit");
                    MonolithMaterialConfigurator.ConfigurePreservingSurface(material, null, material.GetTexture("_BaseColorMap") as Texture2D, null, null, true);
                    MaterialState after = new MaterialState(material, path);
                    AssertPreserved(before, after);
                    EditorUtility.SetDirty(material);
                    AssetDatabase.SaveAssetIfDirty(material);
                    after = new MaterialState(material, path);
                    AssertPreserved(before, after);
                    converted++;
                    WriteMaterialRow(csv, path, "converted", before, after, string.Empty);
                }
                catch (Exception exception)
                {
                    string error = exception.Message;
                    if (backup != null && material != null)
                    {
                        try
                        {
                            EditorUtility.CopySerialized(backup, material);
                            EditorUtility.SetDirty(material);
                            AssetDatabase.SaveAssetIfDirty(material);
                        }
                        catch (Exception rollbackException)
                        {
                            error += " Rollback failed: " + rollbackException.Message;
                        }
                    }

                    failed++;
                    WriteMaterialRow(csv, path, "failed", before, null, error);
                }
                finally
                {
                    if (backup != null)
                    {
                        UnityEngine.Object.DestroyImmediate(backup);
                    }
                }
            }

            string report = UniquePath(archive, "MaterialMigration_" + start.ToString(CultureInfo.InvariantCulture));
            File.WriteAllText(report, csv.ToString(), new UTF8Encoding(false));
            return "{\"passed\":" + (failed == 0 ? "true" : "false") + ",\"planned\":" + paths.Length + ",\"start\":" + start + ",\"processed\":" + (end - start) + ",\"converted\":" + converted + ",\"skipped\":" + skipped + ",\"failed\":" + failed + ",\"nativeNormals\":" + nativeNormals + ",\"csv\":" + JsonString(report) + "}";
        }

        public static string Validate(string archive)
        {
            archive = ValidateArchive(archive);
            string[] paths = ReadPaths(archive, "MaterialPaths.txt", false);
            int valid = 0;
            int failed = 0;
            int nativeNormals = 0;
            var csv = new StringBuilder();
            WriteCsvRow(csv, "Path", "Shader", "NativeNormal", "NativeNormalKeyword", "RenderQueue", "Surface", "Error");
            foreach (string path in paths)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                string error = string.Empty;
                if (material == null || material.shader == null || AssetDatabase.IsSubAsset(material))
                {
                    error = "The planned standalone material is missing.";
                }
                else if (material.shader.name != MonolithMaterialConfigurator.SHADER_NAME)
                {
                    error = "The material does not use HDRP/MonolithLit.";
                }
                else if (!material.shader.isSupported || material.GetFloat("_MaterialID") != 4.0f || material.GetFloat("_EnergyConservingSpecularColor") != 0.0f)
                {
                    error = "The Monolith shader or specular material contract is invalid.";
                }

                bool normal = material != null && material.HasProperty("_NormalMap") && material.GetTexture("_NormalMap") != null;
                if (normal)
                {
                    nativeNormals++;
                }

                if (error.Length == 0)
                {
                    valid++;
                }
                else
                {
                    failed++;
                }

                WriteCsvRow(csv, path, material != null && material.shader != null ? material.shader.name : string.Empty, normal.ToString(), (material != null && material.IsKeywordEnabled("_NORMALMAP")).ToString(), material != null ? material.renderQueue.ToString(CultureInfo.InvariantCulture) : string.Empty, material != null && material.HasProperty("_SurfaceType") ? material.GetFloat("_SurfaceType").ToString(CultureInfo.InvariantCulture) : string.Empty, error);
            }

            string materialReport = UniquePath(archive, "MaterialValidation");
            File.WriteAllText(materialReport, csv.ToString(), new UTF8Encoding(false));
            var renderersCsv = new StringBuilder();
            WriteCsvRow(renderersCsv, "Scene", "Renderer", "Slot", "Material", "AssetPath", "Shader", "ProtectedLevel");
            int slots = 0;
            int nativeOutsideLevels = 0;
            foreach (Renderer renderer in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (renderer == null || EditorUtility.IsPersistent(renderer) || !renderer.gameObject.scene.IsValid() || !renderer.gameObject.scene.isLoaded || EditorSceneManager.IsPreviewSceneObject(renderer.gameObject))
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null)
                    {
                        continue;
                    }

                    string path = AssetDatabase.GetAssetPath(material);
                    bool protectedLevel = IsProtected(path);
                    string shader = material.shader != null ? material.shader.name : string.Empty;
                    if (shader == XRayMaterialGenerator.HDRP_LIT_SHADER && !protectedLevel)
                    {
                        nativeOutsideLevels++;
                    }

                    slots++;
                    WriteCsvRow(renderersCsv, renderer.gameObject.scene.path, HierarchyPath(renderer.transform), i.ToString(CultureInfo.InvariantCulture), material.name, path, shader, protectedLevel.ToString());
                }
            }

            string rendererReport = UniquePath(archive, "LoadedRendererValidation");
            File.WriteAllText(rendererReport, renderersCsv.ToString(), new UTF8Encoding(false));
            return "{\"passed\":" + (failed == 0 ? "true" : "false") + ",\"planned\":" + paths.Length + ",\"valid\":" + valid + ",\"failed\":" + failed + ",\"nativeNormals\":" + nativeNormals + ",\"loadedRendererSlots\":" + slots + ",\"loadedNativeOutsideLevels\":" + nativeOutsideLevels + ",\"materialsCsv\":" + JsonString(materialReport) + ",\"loadedRenderersCsv\":" + JsonString(rendererReport) + "}";
        }

        public static string AuditModels(string archive, int start, int count)
        {
            archive = ValidateArchive(archive);
            string[] paths = ReadPaths(archive, "ModelPaths.txt", true);
            if (start < 0 || start >= paths.Length || count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(start), "The requested model batch is outside ModelPaths.txt.");
            }

            int end = start + Math.Min(count, paths.Length - start);
            int failed = 0;
            int renderers = 0;
            int slots = 0;
            int nativeSlots = 0;
            var nativeMaterials = new HashSet<Material>();
            var csv = new StringBuilder();
            WriteCsvRow(csv, "ModelPath", "Renderer", "Slot", "MaterialPath", "Material", "Guid", "LocalFileId", "Result", "Error");
            for (int i = start; i < end; i++)
            {
                string path = paths[i];
                try
                {
                    GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (model == null)
                    {
                        throw new InvalidOperationException("The planned model or prefab has no GameObject asset.");
                    }

                    foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                    {
                        renderers++;
                        Material[] materials = renderer.sharedMaterials;
                        for (int slot = 0; slot < materials.Length; slot++)
                        {
                            Material material = materials[slot];
                            slots++;
                            if (material == null || material.shader == null || material.shader.name != XRayMaterialGenerator.HDRP_LIT_SHADER)
                            {
                                continue;
                            }

                            string materialPath = AssetDatabase.GetAssetPath(material);
                            if (IsProtected(materialPath))
                            {
                                continue;
                            }

                            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string guid, out long localId);
                            nativeSlots++;
                            nativeMaterials.Add(material);
                            WriteCsvRow(csv, path, HierarchyPath(renderer.transform), slot.ToString(CultureInfo.InvariantCulture), materialPath, material.name, guid, localId.ToString(CultureInfo.InvariantCulture), "native_hdrp_lit", string.Empty);
                        }
                    }
                }
                catch (Exception exception)
                {
                    failed++;
                    WriteCsvRow(csv, path, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, "failed", exception.Message);
                }
            }

            string report = UniquePath(archive, "ModelMaterialAudit_" + start.ToString(CultureInfo.InvariantCulture));
            File.WriteAllText(report, csv.ToString(), new UTF8Encoding(false));
            return "{\"passed\":" + (failed == 0 && nativeSlots == 0 ? "true" : "false") + ",\"planned\":" + paths.Length + ",\"start\":" + start + ",\"processed\":" + (end - start) + ",\"failed\":" + failed + ",\"renderers\":" + renderers + ",\"slots\":" + slots + ",\"nativeSlots\":" + nativeSlots + ",\"nativeMaterials\":" + nativeMaterials.Count + ",\"csv\":" + JsonString(report) + "}";
        }

        public static string MigrateSceneDefaultMaterial(string archive, string scenePath, string rendererPath, string outputMaterialPath)
        {
            archive = ValidateArchive(archive);
            scenePath = ValidateProjectAssetPath(scenePath, ".unity");
            outputMaterialPath = ValidateProjectAssetPath(outputMaterialPath, ".mat");
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            if (!scene.IsValid() || !scene.isLoaded || scene.path != scenePath || scene.isDirty)
            {
                throw new InvalidOperationException("The exact target scene must already be loaded and have no unsaved changes: " + scenePath);
            }

            Renderer target = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (HierarchyPath(renderer.transform) != rendererPath)
                    {
                        continue;
                    }

                    if (target != null)
                    {
                        throw new InvalidOperationException("The target renderer hierarchy path is ambiguous: " + rendererPath);
                    }

                    target = renderer;
                }
            }

            if (target == null)
            {
                throw new InvalidOperationException("The target renderer was not found: " + rendererPath);
            }

            Material[] materials = target.sharedMaterials;
            var changedSlots = new List<int>();
            Material source = null;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null || material.shader == null || material.shader.name != XRayMaterialGenerator.HDRP_LIT_SHADER)
                {
                    continue;
                }

                string sourcePath = AssetDatabase.GetAssetPath(material);
                bool package = sourcePath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);
                bool builtIn = sourcePath == "Resources/unity_builtin_extra" || sourcePath == "Library/unity default resources" || (string.IsNullOrEmpty(sourcePath) && UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null && material == UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.defaultMaterial);
                if (!package && !builtIn)
                {
                    continue;
                }

                if (source != null && source != material)
                {
                    throw new InvalidOperationException("The target renderer uses different package default materials; one output material cannot preserve both surfaces.");
                }

                source = material;
                changedSlots.Add(i);
            }

            if (changedSlots.Count == 0)
            {
                return "{\"passed\":true,\"changedSlots\":0,\"newMat\":\"\",\"sceneDirty\":false}";
            }

            string folder = Path.GetDirectoryName(outputMaterialPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder))
            {
                throw new InvalidOperationException("The output material folder must already exist: " + folder);
            }

            BackupSceneInput(archive, scenePath);
            BackupSceneInput(archive, scenePath + ".meta");
            Material clone = new Material(source) { name = source.name, hideFlags = source.hideFlags };
            bool created = false;
            try
            {
                MaterialState before = new MaterialState(clone, null);
                MonolithMaterialConfigurator.ConfigurePreservingSurface(clone, null, clone.GetTexture("_BaseColorMap") as Texture2D, null, null, true);
                clone.renderQueue = before.RawRenderQueue;
                AssertPreserved(before, new MaterialState(clone, null));
                clone.name = Path.GetFileNameWithoutExtension(outputMaterialPath);
                Material output = AssetDatabase.LoadAssetAtPath<Material>(outputMaterialPath);
                if (output != null)
                {
                    if (output.shader == null || output.shader.name != MonolithMaterialConfigurator.SHADER_NAME)
                    {
                        throw new InvalidOperationException("The existing output material is not Monolith/Lit: " + outputMaterialPath);
                    }

                    AssertPreserved(new MaterialState(clone, null), new MaterialState(output, null));
                }
                else
                {
                    if (File.Exists(outputMaterialPath) || File.Exists(outputMaterialPath + ".meta"))
                    {
                        throw new InvalidOperationException("The output material path already exists without a usable material: " + outputMaterialPath);
                    }

                    AssetDatabase.CreateAsset(clone, outputMaterialPath);
                    AssetDatabase.SaveAssetIfDirty(clone);
                    output = clone;
                    created = true;
                }

                Material[] replacement = (Material[])materials.Clone();
                foreach (int slot in changedSlots)
                {
                    replacement[slot] = output;
                }

                Undo.RecordObject(target, "Migrate scene default material to Monolith/Lit");
                target.sharedMaterials = replacement;
                if (PrefabUtility.IsPartOfPrefabInstance(target))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                }

                EditorSceneManager.MarkSceneDirty(scene);
                return "{\"passed\":true,\"changedSlots\":" + changedSlots.Count + ",\"slots\":[" + string.Join(",", changedSlots) + "],\"newMat\":" + JsonString(outputMaterialPath) + ",\"created\":" + (created ? "true" : "false") + ",\"sceneDirty\":" + (scene.isDirty ? "true" : "false") + "}";
            }
            finally
            {
                if (!EditorUtility.IsPersistent(clone))
                {
                    UnityEngine.Object.DestroyImmediate(clone);
                }
            }
        }

        private static string ValidateProjectAssetPath(string path, string extension)
        {
            path = (path ?? string.Empty).Trim().Replace('\\', '/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
            string fullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), path)).Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) || path.Contains("/../") || path.Contains("/./") || !fullPath.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase) || IsProtected(path))
            {
                throw new InvalidOperationException("Invalid or protected project asset path: " + path);
            }

            return path;
        }

        private static void BackupSceneInput(string archive, string assetPath)
        {
            string backup = Path.Combine(archive, "SceneBefore", assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if (File.Exists(backup))
            {
                if (HashFile(assetPath) != HashFile(backup))
                {
                    throw new InvalidOperationException("An existing scene backup differs and cannot be overwritten: " + backup);
                }

                return;
            }

            File.Copy(assetPath, backup, false);
        }

        private static void AssertPreserved(MaterialState before, MaterialState after)
        {
            var errors = new List<string>();
            if (after.ShaderName != MonolithMaterialConfigurator.SHADER_NAME)
            {
                errors.Add("shader");
            }

            if (before.Guid != after.Guid || before.MetaHash != after.MetaHash)
            {
                errors.Add("material GUID or metadata");
            }

            if (before.Name != after.Name || before.RenderQueue != after.RenderQueue || before.RawRenderQueue != after.RawRenderQueue || before.Instancing != after.Instancing || before.DoubleSidedGi != after.DoubleSidedGi || before.GiFlags != after.GiFlags)
            {
                errors.Add("name, render queue, instancing or GI flags");
            }

            foreach (KeyValuePair<string, float> property in before.Floats)
            {
                if (!IsIntendedProperty(property.Key) && (!after.Floats.TryGetValue(property.Key, out float value) || !property.Value.Equals(value)))
                {
                    errors.Add(property.Key);
                }
            }

            foreach (KeyValuePair<string, int> property in before.Ints)
            {
                if (!IsIntendedProperty(property.Key) && (!after.Ints.TryGetValue(property.Key, out int value) || property.Value != value))
                {
                    errors.Add(property.Key);
                }
            }

            foreach (KeyValuePair<string, Color> property in before.Colors)
            {
                bool exists = after.Colors.TryGetValue(property.Key, out Color value);
                if (!exists || !property.Value.Equals(value))
                {
                    errors.Add(property.Key + " before=" + ColorComponents(property.Value) + " after=" + (exists ? ColorComponents(value) : "missing"));
                }
            }

            foreach (KeyValuePair<string, TextureBinding> property in before.Textures)
            {
                if (!after.Textures.TryGetValue(property.Key, out TextureBinding value) || !property.Value.Matches(value))
                {
                    errors.Add(property.Key);
                }
            }

            foreach (string keyword in before.Keywords)
            {
                if (!keyword.StartsWith("_MATERIAL_FEATURE_", StringComparison.Ordinal) && Array.IndexOf(after.Keywords, keyword) < 0)
                {
                    errors.Add("keyword " + keyword);
                }
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException("Material preservation failed: " + string.Join(", ", errors));
            }
        }

        private static bool IsIntendedProperty(string property)
        {
            return property == "_MaterialID" || property == "_EnergyConservingSpecularColor" || property.StartsWith("_Stencil", StringComparison.Ordinal);
        }

        private static string ColorComponents(Color color)
        {
            return "RGBA(" + color.r.ToString("R", CultureInfo.InvariantCulture) + "," + color.g.ToString("R", CultureInfo.InvariantCulture) + "," + color.b.ToString("R", CultureInfo.InvariantCulture) + "," + color.a.ToString("R", CultureInfo.InvariantCulture) + ")";
        }

        private static string[] ReadPaths(string archive, string fileName, bool models)
        {
            string file = Path.Combine(archive, fileName);
            var paths = new List<string>();
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string project = Path.GetDirectoryName(Application.dataPath);
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
            foreach (string line in File.ReadAllLines(file))
            {
                string path = line.Trim().Replace('\\', '/');
                if (path.Length == 0)
                {
                    continue;
                }

                string fullPath = Path.GetFullPath(Path.Combine(project, path)).Replace('\\', '/');
                string extension = Path.GetExtension(path);
                bool supported = models ? extension.Equals(".ogf", StringComparison.OrdinalIgnoreCase) || extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".obj", StringComparison.OrdinalIgnoreCase) || extension.Equals(".blend", StringComparison.OrdinalIgnoreCase) || extension.Equals(".prefab", StringComparison.OrdinalIgnoreCase) : extension.Equals(".mat", StringComparison.OrdinalIgnoreCase);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !supported || !fullPath.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase) || path.Contains("/../") || path.Contains("/./") || IsProtected(path) || !unique.Add(path))
                {
                    throw new InvalidOperationException("Invalid or protected planned asset path: " + path);
                }

                paths.Add(path);
            }

            if (paths.Count == 0)
            {
                throw new InvalidOperationException(fileName + " has no planned assets.");
            }

            return paths.ToArray();
        }

        private static bool IsProtected(string path)
        {
            return !string.IsNullOrEmpty(path) && (path.StartsWith(LEVELS_ROOT, StringComparison.OrdinalIgnoreCase) || path.IndexOf("l07_military", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string ValidateArchive(string archive)
        {
            if (string.IsNullOrWhiteSpace(archive) || !Path.IsPathRooted(archive))
            {
                throw new ArgumentException("An absolute task directory below the project Archive is required.", nameof(archive));
            }

            string root = Path.GetFullPath(ARCHIVE_ROOT).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullPath = Path.GetFullPath(archive).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(fullPath))
            {
                throw new ArgumentException("The existing output directory must be below " + root + ".", nameof(archive));
            }

            DirectoryInfo current = new DirectoryInfo(fullPath);
            while (current != null)
            {
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException("Archive output cannot traverse a reparse point: " + current.FullName);
                }

                if (string.Equals(current.FullName.TrimEnd(Path.DirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase))
                {
                    return fullPath;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException("The archive output ancestry could not be verified.");
        }

        private static string UniquePath(string archive, string prefix)
        {
            return Path.Combine(archive, prefix + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".csv");
        }

        private static string HashFile(string path)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                return BitConverter.ToString(algorithm.ComputeHash(File.ReadAllBytes(path))).Replace("-", string.Empty);
            }
        }

        private static void WriteMaterialRow(StringBuilder csv, string path, string result, MaterialState before, MaterialState after, string error)
        {
            WriteCsvRow(csv, path, result, before != null ? before.ShaderName : string.Empty, after != null ? after.ShaderName : string.Empty, before != null ? before.HasNativeNormal.ToString() : string.Empty, before != null ? before.TextureCount.ToString(CultureInfo.InvariantCulture) : string.Empty, after != null ? after.TextureCount.ToString(CultureInfo.InvariantCulture) : string.Empty, before != null ? before.RenderQueue.ToString(CultureInfo.InvariantCulture) : string.Empty, after != null ? after.RenderQueue.ToString(CultureInfo.InvariantCulture) : string.Empty, before != null ? before.Surface.ToString(CultureInfo.InvariantCulture) : string.Empty, after != null ? after.Surface.ToString(CultureInfo.InvariantCulture) : string.Empty, error);
        }

        private static void WriteCsvRow(StringBuilder csv, params string[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    csv.Append(',');
                }

                csv.Append('"').Append((values[i] ?? string.Empty).Replace("\"", "\"\"")).Append('"');
            }

            csv.AppendLine();
        }

        private static string JsonString(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
        }

        private static string HierarchyPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }

            return path;
        }

        private sealed class MaterialState
        {
            public string Name { get; }
            public string ShaderName { get; }
            public string Guid { get; }
            public string MetaHash { get; }
            public int RenderQueue { get; }
            public int RawRenderQueue { get; }
            public bool Instancing { get; }
            public bool DoubleSidedGi { get; }
            public MaterialGlobalIlluminationFlags GiFlags { get; }
            public bool HasNativeNormal { get; }
            public int TextureCount { get; }
            public float Surface { get; }
            public string[] Keywords { get; }
            public Dictionary<string, float> Floats { get; } = new Dictionary<string, float>();
            public Dictionary<string, int> Ints { get; } = new Dictionary<string, int>();
            public Dictionary<string, Color> Colors { get; } = new Dictionary<string, Color>();
            public Dictionary<string, TextureBinding> Textures { get; } = new Dictionary<string, TextureBinding>();

            public MaterialState(Material material, string path)
            {
                Name = material.name;
                ShaderName = material.shader != null ? material.shader.name : string.Empty;
                Guid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
                MetaHash = string.IsNullOrEmpty(path) ? string.Empty : HashFile(path + ".meta");
                RenderQueue = material.renderQueue;
                Instancing = material.enableInstancing;
                DoubleSidedGi = material.doubleSidedGI;
                GiFlags = material.globalIlluminationFlags;
                HasNativeNormal = material.HasProperty("_NormalMap") && material.GetTexture("_NormalMap") != null;
                Surface = material.HasProperty("_SurfaceType") ? material.GetFloat("_SurfaceType") : 0.0f;
                Keywords = material.shaderKeywords;
                var serialized = new SerializedObject(material);
                serialized.Update();
                RawRenderQueue = serialized.FindProperty("m_CustomRenderQueue").intValue;
                SerializedProperty floats = serialized.FindProperty("m_SavedProperties.m_Floats");
                for (int i = 0; i < floats.arraySize; i++)
                {
                    SerializedProperty entry = floats.GetArrayElementAtIndex(i);
                    Floats[entry.FindPropertyRelative("first").stringValue] = entry.FindPropertyRelative("second").floatValue;
                }

                SerializedProperty ints = serialized.FindProperty("m_SavedProperties.m_Ints");
                if (ints != null)
                {
                    for (int i = 0; i < ints.arraySize; i++)
                    {
                        SerializedProperty entry = ints.GetArrayElementAtIndex(i);
                        Ints[entry.FindPropertyRelative("first").stringValue] = entry.FindPropertyRelative("second").intValue;
                    }
                }

                SerializedProperty colors = serialized.FindProperty("m_SavedProperties.m_Colors");
                for (int i = 0; i < colors.arraySize; i++)
                {
                    SerializedProperty entry = colors.GetArrayElementAtIndex(i);
                    Colors[entry.FindPropertyRelative("first").stringValue] = entry.FindPropertyRelative("second").colorValue;
                }

                SerializedProperty textures = serialized.FindProperty("m_SavedProperties.m_TexEnvs");
                int textureCount = 0;
                for (int i = 0; i < textures.arraySize; i++)
                {
                    SerializedProperty entry = textures.GetArrayElementAtIndex(i);
                    var binding = new TextureBinding(entry.FindPropertyRelative("second"));
                    Textures[entry.FindPropertyRelative("first").stringValue] = binding;
                    if (binding.Texture != null)
                    {
                        textureCount++;
                    }
                }

                TextureCount = textureCount;
            }
        }

        private sealed class TextureBinding
        {
            public UnityEngine.Object Texture { get; }
            public Vector2 Scale { get; }
            public Vector2 Offset { get; }

            public TextureBinding(SerializedProperty property)
            {
                Texture = property.FindPropertyRelative("m_Texture").objectReferenceValue;
                Scale = property.FindPropertyRelative("m_Scale").vector2Value;
                Offset = property.FindPropertyRelative("m_Offset").vector2Value;
            }

            public bool Matches(TextureBinding other)
            {
                return Texture == other.Texture && Scale.Equals(other.Scale) && Offset.Equals(other.Offset);
            }
        }
    }
}

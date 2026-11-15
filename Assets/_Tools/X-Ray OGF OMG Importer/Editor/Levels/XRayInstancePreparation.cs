using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    [InitializeOnLoad]
    public sealed class XRayInstancePreparation : AssetPostprocessor, IPreprocessBuildWithReport
    {
        private static readonly HashSet<string> PENDING = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> SAVING = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, HashSet<string>> DEPENDENTS = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string[]> DEPENDENCIES = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string[]> INCLUDE_DEPENDENCIES = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private static readonly Regex INCLUDE = new Regex(@"(?m)^\s*#\s*include(?:_with_pragmas)?\s*[""<]([^"">]+)["">]", RegexOptions.CultureInvariant);

        private static bool _queued;
        private static bool _running;
        private static bool _scanAll = true;

        public int callbackOrder => -9000;

        static XRayInstancePreparation()
        {
            Queue();
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:XRayInstanceBatchSet", new[] { "Assets" }))
            {
                Ensure(AssetDatabase.LoadAssetAtPath<XRayInstanceBatchSet>(AssetDatabase.GUIDToAssetPath(guid)));
            }
        }

        public static bool Ensure(XRayInstanceBatchSet set)
        {
            if (set == null)
            {
                throw new ArgumentNullException(nameof(set));
            }
            string signature = InputSignature(set);
            bool changed = set.MatrixPreparationVersion != XRayInstanceBatchSet.MATRIX_PREPARATION_VERSION || set.MatrixInputSignature != signature;
            if (changed) set.PrepareMatrices(signature);
            changed |= XRayInstanceGrouping.Ensure(set, signature);
            if (changed)
            {
                EditorUtility.SetDirty(set);
                Save(set);
            }
            RegisterDependencies(set);
            return changed;
        }

        public static string InputSignature(XRayInstanceBatchSet set)
        {
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true))
            {
                writer.Write(XRayInstanceBatchSet.MATRIX_PREPARATION_VERSION);
                writer.Write(set.LevelName ?? string.Empty);
                writer.Write(set.CellSize);
                writer.Write(set.Batches.Count);
                foreach (XRayInstanceBatch batch in set.Batches)
                {
                    if (batch == null)
                    {
                        throw new InvalidDataException("Null instance batch in " + AssetDatabase.GetAssetPath(set));
                    }
                    writer.Write(batch.Name ?? string.Empty);
                    writer.Write(batch.Count);
                    for (int index = 0; index < batch.Count; index++)
                    {
                        Matrix4x4 matrix = batch.GetMatrix(index);
                        for (int element = 0; element < 16; element++)
                        {
                            writer.Write(matrix[element]);
                        }
                    }
                }
            }
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(buffer.GetBuffer(), 0, checked((int)buffer.Length))).Replace("-", string.Empty).ToLowerInvariant();
        }

        public static int UpdateDependencies(string prefabPath, string scenePath)
        {
            var sets = new HashSet<XRayInstanceBatchSet>();
            foreach (string path in new[] { prefabPath, scenePath })
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    continue;
                }
                foreach (string dependency in AssetDatabase.GetDependencies(path, true))
                {
                    if (AssetDatabase.GetMainAssetTypeAtPath(dependency) != typeof(XRayInstanceBatchSet))
                    {
                        continue;
                    }
                    var set = AssetDatabase.LoadAssetAtPath<XRayInstanceBatchSet>(dependency);
                    if (set != null)
                    {
                        sets.Add(set);
                    }
                }
            }
            int updated = 0;
            foreach (var set in sets)
            {
                updated += Ensure(set) ? 1 : 0;
            }
            return updated;
        }

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (string path in importedAssets) RecordChange(path);
            foreach (string path in deletedAssets) RecordChange(path);
            foreach (string path in movedAssets) RecordChange(path);
            foreach (string path in movedFromAssetPaths) RecordChange(path);
            if (PENDING.Count > 0 || _scanAll)
            {
                Queue();
            }
        }

        private static void RecordChange(string path)
        {
            if (SAVING.Contains(path)) return;
            string extension = Path.GetExtension(path);
            if (extension == ".shader" || extension == ".hlsl" || extension == ".cginc") INCLUDE_DEPENDENCIES.Clear();
            if (DEPENDENTS.TryGetValue(path, out HashSet<string> sets))
            {
                foreach (string set in sets) PENDING.Add(set);
            }
            if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) PENDING.Add(path);
        }

        private static void RegisterDependencies(XRayInstanceBatchSet set)
        {
            string path = AssetDatabase.GetAssetPath(set);
            if (string.IsNullOrEmpty(path)) return;
            RemoveDependencies(path);
            var dependencies = new HashSet<string>(AssetDatabase.GetDependencies(path, true), StringComparer.Ordinal);
            foreach (string dependency in new List<string>(dependencies))
            {
                string extension = Path.GetExtension(dependency);
                if (extension != ".shader" && extension != ".hlsl" && extension != ".cginc") continue;
                if (!INCLUDE_DEPENDENCIES.TryGetValue(dependency, out string[] includes))
                {
                    var found = new HashSet<string>(StringComparer.Ordinal);
                    AddIncludes(dependency, found, new HashSet<string>(StringComparer.Ordinal));
                    includes = new string[found.Count];
                    found.CopyTo(includes);
                    INCLUDE_DEPENDENCIES.Add(dependency, includes);
                }
                dependencies.UnionWith(includes);
            }
            var stored = new string[dependencies.Count];
            dependencies.CopyTo(stored);
            DEPENDENCIES[path] = stored;
            foreach (string dependency in stored)
            {
                if (!DEPENDENTS.TryGetValue(dependency, out HashSet<string> sets))
                {
                    sets = new HashSet<string>(StringComparer.Ordinal);
                    DEPENDENTS.Add(dependency, sets);
                }
                sets.Add(path);
            }
        }

        private static void AddIncludes(string path, HashSet<string> dependencies, HashSet<string> visited)
        {
            string extension = Path.GetExtension(path);
            if (extension != ".shader" && extension != ".hlsl" && extension != ".cginc") return;
            if (!visited.Add(path)) return;
            string source = path;
            if (!File.Exists(source) && path.StartsWith("Packages/", StringComparison.Ordinal))
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
                if (package != null) source = package.resolvedPath + path.Substring(package.assetPath.Length);
            }
            if (!File.Exists(source)) return;
            foreach (Match match in INCLUDE.Matches(File.ReadAllText(source)))
            {
                string include = match.Groups[1].Value.Replace('\\', '/');
                string candidate = include.StartsWith("Assets/", StringComparison.Ordinal) || include.StartsWith("Packages/", StringComparison.Ordinal) ? include : Path.GetDirectoryName(path) + "/" + include;
                candidate = Path.GetRelativePath(Directory.GetCurrentDirectory(), Path.GetFullPath(candidate)).Replace('\\', '/');
                if (!candidate.StartsWith("Assets/", StringComparison.Ordinal) && !candidate.StartsWith("Packages/", StringComparison.Ordinal)) continue;
                dependencies.Add(candidate);
                AddIncludes(candidate, dependencies, visited);
            }
        }

        private static void RemoveDependencies(string path)
        {
            if (!DEPENDENCIES.TryGetValue(path, out string[] previous)) return;
            foreach (string dependency in previous)
            {
                HashSet<string> sets = DEPENDENTS[dependency];
                sets.Remove(path);
                if (sets.Count == 0) DEPENDENTS.Remove(dependency);
            }
            DEPENDENCIES.Remove(path);
        }

        private static void Save(XRayInstanceBatchSet set)
        {
            string path = AssetDatabase.GetAssetPath(set);
            if (string.IsNullOrEmpty(path)) return;
            SAVING.Add(path);
            try
            {
                AssetDatabase.SaveAssetIfDirty(set);
            }
            finally
            {
                SAVING.Remove(path);
            }
        }

        private static void Queue()
        {
            if (_queued)
            {
                return;
            }
            _queued = true;
            EditorApplication.update += Flush;
        }

        private static void Flush()
        {
            if (_running || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer || XRayLevelImportJob.Current != null)
            {
                return;
            }
            EditorApplication.update -= Flush;
            _queued = false;
            _running = true;
            if (_scanAll)
            {
                _scanAll = false;
                foreach (string guid in AssetDatabase.FindAssets("t:XRayInstanceBatchSet", new[] { "Assets" }))
                {
                    PENDING.Add(AssetDatabase.GUIDToAssetPath(guid));
                }
            }
            var paths = new List<string>(PENDING);
            paths.Sort(StringComparer.Ordinal);
            PENDING.Clear();
            try
            {
                foreach (string path in paths)
                {
                    if (AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(XRayInstanceBatchSet))
                    {
                        RemoveDependencies(path);
                        continue;
                    }
                    var set = AssetDatabase.LoadAssetAtPath<XRayInstanceBatchSet>(path);
                    if (set == null)
                    {
                        continue;
                    }
                    try
                    {
                        Ensure(set);
                    }
                    catch (Exception exception)
                    {
                        set.InvalidateSubmissionGroups();
                        EditorUtility.SetDirty(set);
                        Save(set);
                        Debug.LogError("[X-Ray Level] Matrix preparation failed for " + path + ": " + exception.Message);
                    }
                }
            }
            finally
            {
                _running = false;
            }
        }
    }
}

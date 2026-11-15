using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
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
            changed |= XRayInstanceGrouping.Ensure(set);
            if (!changed) return false;
            EditorUtility.SetDirty(set);
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(set)))
            {
                AssetDatabase.SaveAssetIfDirty(set);
            }
            return true;
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
            var setPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (string guid in AssetDatabase.FindAssets("t:XRayInstanceBatchSet", new[] { "Assets" }))
            {
                setPaths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            foreach (string path in new[] { prefabPath, scenePath })
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    continue;
                }
                foreach (string dependency in AssetDatabase.GetDependencies(path, true))
                {
                    if (!setPaths.Contains(dependency))
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
            if (_running)
            {
                return;
            }
            foreach (string path in importedAssets)
            {
                if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase)) _scanAll = true;
                if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                {
                    PENDING.Add(path);
                }
            }
            foreach (string path in movedAssets)
            {
                if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase)) _scanAll = true;
                if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                {
                    PENDING.Add(path);
                }
            }
            if (PENDING.Count > 0 || _scanAll)
            {
                Queue();
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
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer || XRayLevelImportJob.Current != null)
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
            PENDING.Clear();
            try
            {
                foreach (string path in paths)
                {
                    if (AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(XRayInstanceBatchSet))
                    {
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

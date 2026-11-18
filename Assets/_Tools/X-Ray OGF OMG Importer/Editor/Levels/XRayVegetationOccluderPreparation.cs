using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    [InitializeOnLoad]
    public sealed class XRayVegetationOccluderPreparation : AssetPostprocessor, IPreprocessBuildWithReport
    {
        private const string STATIC_MESH_DIRECTORY = "/Meshes/Static";
        private const int MAX_TRIANGLES_PER_MESH = 40;
        private const float MIN_DOUBLE_AREA_SQUARED = 1f;
        private static readonly HashSet<string> PENDING = new HashSet<string>(StringComparer.Ordinal);
        private static bool _scanExisting = true;
        private static bool _queued;

        public int callbackOrder => -8500;

        static XRayVegetationOccluderPreparation()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            Queue();
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            PrepareExistingLevels();
        }

        public static int PrepareExistingLevels()
        {
            if (!AssetDatabase.IsValidFolder(XRayLevelPaths.ROOT)) return 0;
            var folders = new HashSet<string>(StringComparer.Ordinal);
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { XRayLevelPaths.ROOT }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                int separator = path.LastIndexOf("/Scenes/", StringComparison.Ordinal);
                if (separator > 0) folders.Add(path.Substring(0, separator));
            }

            int changed = 0;
            foreach (string folder in folders)
            {
                if (PrepareLevel(folder)) changed++;
            }

            return changed;
        }

        public static bool PrepareLevel(string levelFolder)
        {
            string meshFolder = levelFolder + STATIC_MESH_DIRECTORY;
            if (!AssetDatabase.IsValidFolder(meshFolder)) return false;
            string[] guids = AssetDatabase.FindAssets("t:Mesh", new[] { meshFolder });
            Array.Sort(guids, StringComparer.Ordinal);
            var paths = new List<string>(guids.Length);
            var fingerprint = new StringBuilder();
            fingerprint.Append(XRayVegetationOccluderData.VERSION).Append('\n');
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                paths.Add(path);
                fingerprint.Append(guid).Append(' ').Append(AssetDatabase.GetAssetDependencyHash(path)).Append('\n');
            }

            string signature = Hash128.Compute(fingerprint.ToString()).ToString();
            string dataPath = DataPath(levelFolder);
            XRayVegetationOccluderData data = AssetDatabase.LoadAssetAtPath<XRayVegetationOccluderData>(dataPath);
            if (data != null && data.Version == XRayVegetationOccluderData.VERSION && data.Signature == signature) return false;

            var entries = new List<XRayVegetationOccluderData.Entry>(paths.Count);
            var triangles = new XRayVegetationOccluderData.Triangle[MAX_TRIANGLES_PER_MESH];
            var priorities = new float[MAX_TRIANGLES_PER_MESH];
            foreach (string path in paths)
            {
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null || mesh.vertexCount < 3) continue;
                XRayVegetationOccluderData.Triangle[] selected = SelectTriangles(mesh, triangles, priorities);
                if (selected.Length > 0) entries.Add(new XRayVegetationOccluderData.Entry(mesh, selected));
            }

            if (data == null)
            {
                if (entries.Count == 0) return false;
                data = ScriptableObject.CreateInstance<XRayVegetationOccluderData>();
                data.Initialize(signature, entries.ToArray());
                XRayLevelPaths.EnsureFolder(Path.GetDirectoryName(dataPath)?.Replace('\\', '/'));
                AssetDatabase.CreateAsset(data, dataPath);
            }
            else
            {
                data.Initialize(signature, entries.ToArray());
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(data);
            }

            return true;
        }

        public static string DataPath(string levelFolder)
        {
            return levelFolder + "/Resources/" + XRayVegetationOccluderData.ResourceKey(levelFolder) + ".asset";
        }

        internal static void RecordChanges(string[] paths)
        {
            foreach (string path in paths)
            {
                if (!path.StartsWith(XRayLevelPaths.ROOT + "/", StringComparison.Ordinal) || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) continue;
                int separator = path.LastIndexOf(STATIC_MESH_DIRECTORY + "/", StringComparison.Ordinal);
                if (separator > 0) PENDING.Add(path.Substring(0, separator));
            }

            if (PENDING.Count > 0) Queue();
        }

        private static XRayVegetationOccluderData.Triangle[] SelectTriangles(Mesh mesh, XRayVegetationOccluderData.Triangle[] triangles, float[] priorities)
        {
            using Mesh.MeshDataArray snapshot = MeshUtility.AcquireReadOnlyMeshData(mesh);
            Mesh.MeshData data = snapshot[0];
            using var vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            data.GetVertices(vertices);
            bool shortIndices = data.indexFormat == IndexFormat.UInt16;
            NativeArray<ushort> shortBuffer = shortIndices ? data.GetIndexData<ushort>() : default;
            NativeArray<uint> longBuffer = shortIndices ? default : data.GetIndexData<uint>();
            int count = 0;
            for (int s = 0; s < data.subMeshCount; s++)
            {
                SubMeshDescriptor subMesh = data.GetSubMesh(s);
                if (subMesh.topology != MeshTopology.Triangles) continue;
                int end = subMesh.indexStart + subMesh.indexCount;
                for (int i = subMesh.indexStart; i + 2 < end; i += 3)
                {
                    int aIndex = subMesh.baseVertex + (shortIndices ? shortBuffer[i] : (int)longBuffer[i]);
                    int bIndex = subMesh.baseVertex + (shortIndices ? shortBuffer[i + 1] : (int)longBuffer[i + 1]);
                    int cIndex = subMesh.baseVertex + (shortIndices ? shortBuffer[i + 2] : (int)longBuffer[i + 2]);
                    if ((uint)aIndex >= (uint)vertices.Length || (uint)bIndex >= (uint)vertices.Length || (uint)cIndex >= (uint)vertices.Length) continue;
                    Vector3 a = vertices[aIndex];
                    Vector3 b = vertices[bIndex];
                    Vector3 c = vertices[cIndex];
                    float priority = Vector3.Cross(b - a, c - a).sqrMagnitude;
                    if (!(priority >= MIN_DOUBLE_AREA_SQUARED) || float.IsInfinity(priority)) continue;
                    int index = Mathf.Min(count, MAX_TRIANGLES_PER_MESH - 1);
                    if (count == MAX_TRIANGLES_PER_MESH && priority <= priorities[index]) continue;
                    while (index > 0 && priority > priorities[index - 1])
                    {
                        priorities[index] = priorities[index - 1];
                        triangles[index] = triangles[index - 1];
                        index--;
                    }

                    priorities[index] = priority;
                    triangles[index] = new XRayVegetationOccluderData.Triangle(s, a, b, c);
                    count = Mathf.Min(count + 1, MAX_TRIANGLES_PER_MESH);
                }
            }

            if (count == 0) return Array.Empty<XRayVegetationOccluderData.Triangle>();
            var selected = new XRayVegetationOccluderData.Triangle[count];
            Array.Copy(triangles, selected, count);
            return selected;
        }

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            RecordChanges(importedAssets);
            RecordChanges(deletedAssets);
            RecordChanges(movedAssets);
            RecordChanges(movedFromAssetPaths);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode && (_scanExisting || PENDING.Count > 0)) Queue();
        }

        private static void Queue()
        {
            if (_queued || EditorApplication.isPlayingOrWillChangePlaymode) return;
            _queued = true;
            EditorApplication.delayCall += Flush;
        }

        private static void Flush()
        {
            _queued = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                Queue();
                return;
            }

            var folders = new List<string>(PENDING);
            PENDING.Clear();
            if (_scanExisting)
            {
                _scanExisting = false;
                PrepareExistingLevels();
            }

            foreach (string folder in folders) PrepareLevel(folder);
        }
    }

    internal sealed class XRayVegetationOccluderSaveObserver : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            XRayVegetationOccluderPreparation.RecordChanges(paths);
            return paths;
        }
    }
}

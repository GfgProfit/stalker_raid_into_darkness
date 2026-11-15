using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayInstanceGrouping
    {
        public const int CELL_SPAN = 2;

        public static bool Ensure(XRayInstanceBatchSet set)
        {
            if (Application.isPlaying) return false;
            string signature = InputSignature(set);
            if (set.GroupingVersion == XRayInstanceBatchSet.GROUPING_VERSION && set.GroupingSignature == signature) return false;
            set.InvalidateSubmissionGroups();
            List<XRayInstanceBatchGroup> groups = Build(set);
            set.PrepareSubmissionGroups(groups, signature);
            EditorUtility.SetDirty(set);
            return true;
        }

        public static List<XRayInstanceBatchGroup> Build(XRayInstanceBatchSet set)
        {
            var buckets = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var ordered = new List<List<int>>();
            float size = set.CellSize * CELL_SPAN;
            for (int index = 0; index < set.Batches.Count; index++)
            {
                XRayInstanceBatch batch = set.Batches[index];
                if (!Eligible(batch, size, out int x, out int z)) continue;
                int orientation = batch.GetMatrixOrientation();
                if (orientation == 0) continue;
                string key = batch.Mesh.GetEntityId() + ":" + batch.CastShadows + ":" + orientation + ":" + x + ":" + z;
                foreach (Material material in batch.Materials) key += ":" + (material != null ? material.GetEntityId().ToString() : "null");
                if (!buckets.TryGetValue(key, out List<int> bucket))
                {
                    bucket = new List<int>();
                    buckets.Add(key, bucket);
                    ordered.Add(bucket);
                }
                bucket.Add(index);
            }
            var result = new List<XRayInstanceBatchGroup>();
            foreach (List<int> bucket in ordered)
            {
                Bounds bounds = set.Batches[bucket[0]].Bounds;
                foreach (int index in bucket) bounds.Encapsulate(set.Batches[index].Bounds);
                if (bounds.size.x > size || bounds.size.y > size || bounds.size.z > size) continue;
                var indices = new List<int>();
                int count = 0;
                foreach (int index in bucket)
                {
                    int next = set.Batches[index].Count;
                    if (count + next > XRayInstanceBatchSet.MAX_GROUP_INSTANCES)
                    {
                        Add(result, indices, set);
                        indices.Clear();
                        count = 0;
                    }
                    indices.Add(index);
                    count += next;
                }
                Add(result, indices, set);
            }
            return result;
        }

        public static string InputSignature(XRayInstanceBatchSet set)
        {
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true))
            {
                writer.Write(XRayInstanceBatchSet.GROUPING_VERSION);
                writer.Write(CELL_SPAN);
                writer.Write(XRayInstanceBatchSet.MAX_GROUP_INSTANCES);
                writer.Write(XRayInstancePreparation.InputSignature(set));
                var references = new Dictionary<UnityEngine.Object, string>();
                WriteReference(writer, set.Lods, references);
                foreach (var batch in set.Batches)
                {
                    WriteReference(writer, batch.Mesh, references);
                    writer.Write(batch.Mesh != null ? batch.Mesh.subMeshCount : 0);
                    writer.Write(batch.CastShadows);
                    WriteVector(writer, batch.Bounds.center);
                    WriteVector(writer, batch.Bounds.extents);
                    writer.Write(batch.Materials != null ? batch.Materials.Length : -1);
                    if (batch.Materials != null)
                    {
                        foreach (var material in batch.Materials)
                        {
                            WriteReference(writer, material, references);
                            writer.Write(material != null ? material.renderQueue : -1);
                            writer.Write(material != null && material.enableInstancing);
                            writer.Write(material != null ? material.GetTag("RenderType", false, string.Empty) : string.Empty);
                            WriteReference(writer, material != null ? material.shader : null, references);
                            writer.Write(material != null && material.HasProperty("_SurfaceType") ? material.GetFloat("_SurfaceType") : 0f);
                        }
                    }
                    writer.Write(batch.Data != null ? batch.Data.Length : -1);
                    if (batch.Data != null)
                    {
                        foreach (Vector4 data in batch.Data)
                        {
                            writer.Write(data.x);
                            writer.Write(data.y);
                            writer.Write(data.z);
                            writer.Write(data.w);
                        }
                    }
                    writer.Write(batch.LodIndices != null ? batch.LodIndices.Length : -1);
                    if (batch.LodIndices != null)
                    {
                        foreach (int index in batch.LodIndices) writer.Write(index);
                    }
                }
            }
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(buffer.GetBuffer(), 0, checked((int)buffer.Length))).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static bool Eligible(XRayInstanceBatch batch, float size, out int x, out int z)
        {
            x = 0;
            z = 0;
            if (batch == null || batch.Mesh == null || batch.Count == 0 || batch.Count > XRayInstanceBatchSet.MAX_GROUP_INSTANCES || !(size > 0f) || float.IsInfinity(size) || batch.Materials == null || batch.Materials.Length == 0 || batch.Data == null || batch.Data.Length != batch.Count) return false;
            if (batch.LodIndices != null && batch.LodIndices.Length != 0 && batch.LodIndices.Length != batch.Count) return false;
            foreach (Material material in batch.Materials)
            {
                if (!XRayInstanceBatchSet.SupportsSubmissionMaterial(material)) return false;
            }
            for (int index = 0; index < batch.Count; index++)
            {
                Matrix4x4 matrix = batch.GetMatrix(index);
                if (!float.IsFinite(matrix.m03) || !float.IsFinite(matrix.m23)) return false;
                int nextX = Mathf.FloorToInt(matrix.m03 / size);
                int nextZ = Mathf.FloorToInt(matrix.m23 / size);
                if (index == 0)
                {
                    x = nextX;
                    z = nextZ;
                }
                else if (nextX != x || nextZ != z) return false;
            }
            Vector3 extent = batch.Bounds.size;
            Vector3 center = batch.Bounds.center;
            return float.IsFinite(extent.x) && float.IsFinite(extent.y) && float.IsFinite(extent.z) && float.IsFinite(center.x) && float.IsFinite(center.y) && float.IsFinite(center.z) && extent.x >= 0f && extent.y >= 0f && extent.z >= 0f;
        }

        private static void Add(List<XRayInstanceBatchGroup> result, List<int> indices, XRayInstanceBatchSet set)
        {
            if (indices.Count < 2) return;
            Bounds bounds = set.Batches[indices[0]].Bounds;
            foreach (int index in indices) bounds.Encapsulate(set.Batches[index].Bounds);
            result.Add(new XRayInstanceBatchGroup(indices.ToArray(), bounds));
        }

        private static void WriteVector(BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
        }

        private static void WriteReference(BinaryWriter writer, UnityEngine.Object value, Dictionary<UnityEngine.Object, string> references)
        {
            if (value == null)
            {
                writer.Write(string.Empty);
                return;
            }
            if (!references.TryGetValue(value, out string identity))
            {
                string path = AssetDatabase.GetAssetPath(value);
                identity = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long localId) ? guid + ":" + localId + ":" + AssetDatabase.GetAssetDependencyHash(path) : "transient:" + value.GetEntityId();
                references.Add(value, identity);
            }
            writer.Write(identity);
        }
    }
}

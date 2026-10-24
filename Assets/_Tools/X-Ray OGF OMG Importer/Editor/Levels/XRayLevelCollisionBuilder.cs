using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Core.Level;

namespace XRay.Importer.Editor.Levels
{
    public sealed class XRayLevelCollisionChunk
    {
        public string Name { get; set; }
        public Mesh Mesh { get; set; }
        public int Triangles { get; set; }
    }

    public static class XRayLevelCollisionBuilder
    {
        public static List<XRayLevelCollisionChunk> Build(string level, XRayCollisionModel model, bool[] passable, float cellSize, XRayLevelImportRecord record)
        {
            string folder = XRayLevelPaths.Meshes(level) + "/Collision";
            XRayLevelPaths.EnsureFolder(folder);
            float originX = model.BoundsMin[0];
            float originZ = model.BoundsMin[2];
            int cellsX = Mathf.Max(1, Mathf.CeilToInt((model.BoundsMax[0] - originX) / cellSize));
            int cellsZ = Mathf.Max(1, Mathf.CeilToInt((model.BoundsMax[2] - originZ) / cellSize));
            var buckets = new List<int>[cellsX * cellsZ];
            int skipped = 0;
            for (int face = 0; face < model.TriangleCount; face++)
            {
                ushort material = model.Materials[face];
                if (passable != null && material < passable.Length && passable[material])
                {
                    skipped++;
                    continue;
                }

                float cx = 0.0f;
                float cz = 0.0f;
                for (int k = 0; k < 3; k++)
                {
                    int v = model.Triangles[face * 3 + k] * 3;
                    cx += model.Vertices[v];
                    cz += model.Vertices[v + 2];
                }

                int x = Mathf.Clamp(Mathf.FloorToInt((cx / 3.0f - originX) / cellSize), 0, cellsX - 1);
                int z = Mathf.Clamp(Mathf.FloorToInt((cz / 3.0f - originZ) / cellSize), 0, cellsZ - 1);
                int cell = z * cellsX + x;
                (buckets[cell] ?? (buckets[cell] = new List<int>())).Add(face);
            }

            var result = new List<XRayLevelCollisionChunk>();
            int total = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                for (int cell = 0; cell < buckets.Length; cell++)
                {
                    List<int> faces = buckets[cell];
                    if (faces == null)
                    {
                        continue;
                    }

                    string name = $"Collision_{cell % cellsX:00}_{cell / cellsX:00}";
                    string path = folder + "/" + name + ".asset";
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    bool created = mesh == null;
                    if (created)
                    {
                        mesh = new Mesh();
                    }

                    mesh.Clear();
                    mesh.name = name;
                    var remap = new Dictionary<int, int>();
                    var vertices = new List<Vector3>();
                    var indices = new int[faces.Count * 3];
                    for (int i = 0; i < faces.Count; i++)
                    {
                        for (int k = 0; k < 3; k++)
                        {
                            int source = model.Triangles[faces[i] * 3 + k];
                            if (!remap.TryGetValue(source, out int target))
                            {
                                target = vertices.Count;
                                remap.Add(source, target);
                                vertices.Add(new Vector3(model.Vertices[source * 3], model.Vertices[source * 3 + 1], model.Vertices[source * 3 + 2]));
                            }

                            indices[i * 3 + k] = target;
                        }
                    }

                    mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                    mesh.SetVertices(vertices);
                    mesh.SetTriangles(indices, 0, true);
                    if (created)
                    {
                        AssetDatabase.CreateAsset(mesh, path);
                    }
                    else
                    {
                        EditorUtility.SetDirty(mesh);
                    }

                    record.GeneratedAssets.Add(path);
                    result.Add(new XRayLevelCollisionChunk { Name = name, Mesh = mesh, Triangles = faces.Count });
                    total += faces.Count;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            record.SetMetric("CollisionTriangles", total);
            record.SetMetric("CollisionPassableTrianglesExcluded", skipped);
            record.SetMetric("CollisionChunks", result.Count);
            return result;
        }
    }
}

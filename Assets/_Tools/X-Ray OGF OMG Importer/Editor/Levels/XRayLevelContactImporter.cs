using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Core.Level;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayLevelContactImporter
    {
        public static void Prepare(string level, string collisionPath, byte[] materialData, XRayLevelSceneContent content, XRayLevelImportRecord record)
        {
            string folder = XRayLevelPaths.LevelFolder(level) + "/Contacts";
            XRayLevelPaths.EnsureFolder(folder);
            TextAsset collision = content.Details != null ? content.Details.SourceCollision : null;
            if (collision == null)
            {
                string path = folder + "/Collision.bytes";
                collision = Import(path, File.ReadAllBytes(collisionPath));
                record.GeneratedAssets.Add(path);
            }

            string materialsPath = folder + "/GameMaterials.bytes";
            content.ContactCollision = collision;
            content.ContactMaterials = Import(materialsPath, materialData);
            record.GeneratedAssets.Add(materialsPath);
        }

        public static bool Install(string prefabPath, string materialsPath)
        {
            if (string.IsNullOrEmpty(prefabPath) || !prefabPath.StartsWith("Assets/", StringComparison.Ordinal) || !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("An existing imported level prefab asset path is required.", nameof(prefabPath));
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                throw new ArgumentException($"Imported level prefab '{prefabPath}' was not found.", nameof(prefabPath));
            }

            XRayLevelContactSource installed = prefab.GetComponent<XRayLevelContactSource>();
            if (installed != null && installed.CollisionSource != null && installed.MaterialSource != null)
            {
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                XRayLevelContactSource existing = root.GetComponent<XRayLevelContactSource>();
                if (existing != null && existing.CollisionSource != null && existing.MaterialSource != null)
                {
                    return false;
                }

                XRayLevelMetadata metadata = root.GetComponentInChildren<XRayLevelMetadata>(true);
                XRayDetailVegetation vegetation = root.GetComponentInChildren<XRayDetailVegetation>(true);
                TextAsset collision = vegetation != null && vegetation.Database != null ? vegetation.Database.SourceCollision : null;
                if (metadata == null || collision == null)
                {
                    throw new InvalidOperationException($"'{prefabPath}' has no imported level metadata or retained CFORM source; a scoped collision-source import is required.");
                }

                byte[] data = File.ReadAllBytes(materialsPath);
                XRayGameMaterials.Read(data, materialsPath);
                string levelFolder = Path.GetDirectoryName(Path.GetDirectoryName(prefabPath)).Replace('\\', '/');
                string folder = levelFolder + "/Contacts";
                XRayLevelPaths.EnsureFolder(folder);
                TextAsset materials = Import(folder + "/GameMaterials.bytes", data);
                XRayLevelContactSource source = existing != null ? existing : root.AddComponent<XRayLevelContactSource>();
                source.Initialize(collision, materials);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
                if (!success)
                {
                    throw new InvalidOperationException($"Contact source could not be saved to '{prefabPath}'.");
                }

                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static int RepairCollision(string prefabPath, string materialsPath, float cellSize = 64.0f)
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (root == null)
            {
                throw new ArgumentException($"Imported level prefab '{prefabPath}' was not found.", nameof(prefabPath));
            }

            string previousRoot = XRayLevelPaths.Root;
            try
            {
                XRayLevelMetadata metadata = root.GetComponentInChildren<XRayLevelMetadata>(true);
                XRayDetailVegetation vegetation = root.GetComponentInChildren<XRayDetailVegetation>(true);
                XRayDetailDatabase database = vegetation != null ? vegetation.Database : null;
                Transform collisionRoot = root.transform.Find("Collision");
                if (metadata == null || database == null || database.SourceCollision == null || collisionRoot == null || cellSize <= 0.0f)
                {
                    throw new InvalidOperationException($"'{prefabPath}' has no retained CFORM, collision hierarchy or valid cell size.");
                }

                XRayCollisionModel model = XRayCollisionModel.Read(database.SourceCollision.bytes, prefabPath);
                XRayGameMaterials materials = XRayGameMaterials.Read(File.ReadAllBytes(materialsPath), materialsPath);
                bool[] passable = materials.CreatePassableMask();
                bool[] actorPassable = materials.CreateActorPassableMask();
                var existing = new HashSet<string>(StringComparer.Ordinal);
                string levelFolder = Path.GetDirectoryName(Path.GetDirectoryName(prefabPath)).Replace('\\', '/');
                string meshFolder = levelFolder + "/Meshes/Collision/";
                foreach (MeshCollider collider in collisionRoot.GetComponentsInChildren<MeshCollider>(true))
                {
                    Mesh mesh = collider.sharedMesh;
                    if (mesh == null || AssetDatabase.GetAssetPath(mesh) != meshFolder + mesh.name + ".asset")
                    {
                        throw new InvalidOperationException("Collision repair requires existing level-owned mesh assets with stable names and GUIDs.");
                    }

                    existing.Add(mesh.name);
                }

                HashSet<string> required = CollisionNames(model, actorPassable, cellSize);
                if (!existing.SetEquals(required))
                {
                    throw new InvalidOperationException("Collision chunk layout changes; asset-only repair cannot preserve all existing collider references.");
                }

                XRayLevelPaths.UseRoot(Path.GetDirectoryName(levelFolder).Replace('\\', '/'));
                var record = new XRayLevelImportRecord();
                List<XRayLevelCollisionChunk> chunks = XRayLevelCollisionBuilder.Build(metadata.LevelName, model, actorPassable, cellSize, record);
                int triangles = 0;
                foreach (XRayLevelCollisionChunk chunk in chunks)
                {
                    AssetDatabase.SaveAssetIfDirty(chunk.Mesh);
                    triangles += chunk.Triangles;
                }

                database.SetRuntimeSource(database.SourceDetails, database.SourceCollision, passable, database.SourceProfile);
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssetIfDirty(database);
                return triangles;
            }
            finally
            {
                XRayLevelPaths.UseRoot(previousRoot);
            }
        }

        private static HashSet<string> CollisionNames(XRayCollisionModel model, bool[] passable, float cellSize)
        {
            int cellsX = Mathf.Max(1, Mathf.CeilToInt((model.BoundsMax[0] - model.BoundsMin[0]) / cellSize));
            int cellsZ = Mathf.Max(1, Mathf.CeilToInt((model.BoundsMax[2] - model.BoundsMin[2]) / cellSize));
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int face = 0; face < model.TriangleCount; face++)
            {
                int material = model.Materials[face];
                if (material < passable.Length && passable[material])
                {
                    continue;
                }

                float centerX = 0.0f;
                float centerZ = 0.0f;
                for (int k = 0; k < 3; k++)
                {
                    int vertex = model.Triangles[face * 3 + k] * 3;
                    centerX += model.Vertices[vertex];
                    centerZ += model.Vertices[vertex + 2];
                }

                int x = Mathf.Clamp(Mathf.FloorToInt((centerX / 3.0f - model.BoundsMin[0]) / cellSize), 0, cellsX - 1);
                int z = Mathf.Clamp(Mathf.FloorToInt((centerZ / 3.0f - model.BoundsMin[2]) / cellSize), 0, cellsZ - 1);
                names.Add($"Collision_{x:00}_{z:00}");
            }

            return names;
        }

        private static TextAsset Import(string path, byte[] data)
        {
            bool equal = File.Exists(path);
            if (equal)
            {
                byte[] previous = File.ReadAllBytes(path);
                equal = previous.Length == data.Length;
                for (int i = 0; equal && i < data.Length; i++)
                {
                    equal = previous[i] == data[i];
                }
            }

            if (!equal)
            {
                File.WriteAllBytes(path, data);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        }
    }
}

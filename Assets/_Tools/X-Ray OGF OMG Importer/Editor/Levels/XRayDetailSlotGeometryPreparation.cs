using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Core.IO;
using XRay.Importer.Core.Level;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayDetailSlotGeometryPreparation
    {
        public const string GEOMETRY_NAME = "DetailSlotGeometry.bytes";

        public static bool Prepare(XRayDetailDatabase database)
        {
            return Prepare(database, null, null, out _);
        }

        internal static bool PrepareForImport(XRayDetailDatabase database, XRayLevelImportRecord record, XRayDetails details = null, XRayCollisionModel collision = null)
        {
            try
            {
                if (!Prepare(database, details, collision, out bool changed)) return false;
                string geometryPath = AssetDatabase.GetAssetPath(database.SourceSlotGeometry);
                if (record.GeneratedAssets.Contains(geometryPath)) return changed;
                record.GeneratedAssets.Add(geometryPath);
                return true;
            }
            catch (Exception error) when (error is XRayFormatException || error is IOException || error is UnauthorizedAccessException || error is InvalidOperationException)
            {
                record.Warnings.Add("Grass slot geometry preparation: " + error.Message);
                return false;
            }
        }

        private static bool Prepare(XRayDetailDatabase database, XRayDetails details, XRayCollisionModel collision, out bool changed)
        {
            changed = false;
            if (database == null || !database.HasRuntimeSource) return false;
            string directory = Path.GetDirectoryName(AssetDatabase.GetAssetPath(database))?.Replace('\\', '/');
            string detailsPath = AssetDatabase.GetAssetPath(database.SourceDetails);
            string collisionPath = AssetDatabase.GetAssetPath(database.SourceCollision);
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(detailsPath) || string.IsNullOrEmpty(collisionPath)) return false;
            string geometryPath = directory + "/" + GEOMETRY_NAME;
            string signature = Signature(detailsPath, collisionPath, geometryPath);
            if (database.SourceSlotGeometry != null && AssetDatabase.GetAssetPath(database.SourceSlotGeometry) == geometryPath && database.SourceSlotGeometrySignature == signature && !string.IsNullOrEmpty(database.SourceSlotGeometryDetailsHash) && !string.IsNullOrEmpty(database.SourceSlotGeometryCollisionHash)) return true;

            try
            {
                details ??= XRayDetails.Read(database.SourceDetails.bytes, detailsPath);
                if (collision == null)
                {
                    collision = XRayCollisionModel.Read(collisionPath);
                    collision.BuildGrid(XRayDetails.SLOT_SIZE);
                }

                byte[] bytes = XRayDetailSlotGeometry.Bake(details, collision);
                string detailsHash = SourceHash(detailsPath);
                string collisionHash = SourceHash(collisionPath);
                File.WriteAllBytes(geometryPath, bytes);
                AssetDatabase.ImportAsset(geometryPath, ImportAssetOptions.ForceSynchronousImport);
                TextAsset geometry = AssetDatabase.LoadAssetAtPath<TextAsset>(geometryPath);
                if (geometry == null) throw new InvalidOperationException("Could not import grass slot geometry at " + geometryPath);
                database.SetRuntimeSlotGeometry(geometry, Signature(detailsPath, collisionPath, geometryPath), detailsHash, collisionHash);
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssetIfDirty(database);
                changed = true;
                return true;
            }
            catch
            {
                if (database.SourceSlotGeometry != null)
                {
                    database.SetRuntimeSlotGeometry(null, null, null, null);
                    EditorUtility.SetDirty(database);
                    AssetDatabase.SaveAssetIfDirty(database);
                }

                throw;
            }
        }

        private static string Signature(string detailsPath, string collisionPath, string geometryPath)
        {
            return $"{XRayDetailSlotGeometry.VERSION}:{AssetDatabase.GetAssetDependencyHash(detailsPath)}:{AssetDatabase.GetAssetDependencyHash(collisionPath)}:{AssetDatabase.GetAssetDependencyHash(geometryPath)}";
        }

        private static string SourceHash(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return XRayDetailDatabase.ComputeSlotGeometrySourceHash(stream);
        }
    }
}

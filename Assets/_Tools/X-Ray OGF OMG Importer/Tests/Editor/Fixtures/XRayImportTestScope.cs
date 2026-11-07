using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Editor;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Tests
{
    public sealed class XRayImportTestScope : IDisposable
    {
        public const string ROOT_FOLDER = "Assets/__XRayTests_tmp";

        private const int MAX_SETTLE_ITERATIONS = 12;

        public XRayImportTestScope(XRaySourceConfiguration sources)
        {
            Directory = new TestDirectory();
            Folder = ROOT_FOLDER + "/" + Guid.NewGuid().ToString("N").Substring(0, 8);
            System.IO.Directory.CreateDirectory(Folder);
            Options = new XRayAssetSourceOptions { Sources = sources ?? new XRaySourceConfiguration(), Import = new XRayImportOptions { MaterialGenerationMode = XRayMaterialGenerationMode.HdrpLitLegacy }, PersistIndexCache = false };
            XRayAssetSourceSettings.Override = Options;
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        public TestDirectory Directory { get; }
        public string Folder { get; }
        public XRayAssetSourceOptions Options { get; }
        public string TexturesFolder => Folder + "/" + XRayAssetGenerationCoordinator.TEXTURES_FOLDER;
        public string MaterialsFolder => Folder + "/" + XRayAssetGenerationCoordinator.MATERIALS_FOLDER;

        public string ImportModel(string fileName, byte[] data)
        {
            return ImportFile(fileName, data);
        }

        public string ImportFile(string fileName, byte[] data)
        {
            string path = Folder + "/" + fileName;
            File.WriteAllBytes(path, data);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            Settle();
            return path;
        }

        public void Reimport(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Settle();
        }

        public void Settle()
        {
            for (int i = 0; i < MAX_SETTLE_ITERATIONS; i++)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                if (!HasPendingWork())
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    return;
                }
            }

            Assert.Fail($"Asset generation did not settle after {MAX_SETTLE_ITERATIONS} refreshes.");
        }

        public string[] FindAssets(string filter, string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return new string[0];
            }

            string[] guids = AssetDatabase.FindAssets(filter, new[] { folder });
            var paths = new List<string>();
            foreach (string guid in guids)
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            paths.Sort(StringComparer.Ordinal);
            return paths.ToArray();
        }

        public XRayResourceReport LoadReport(string modelPath)
        {
            return XRayResourceReportStore.Load(AssetDatabase.AssetPathToGUID(modelPath));
        }

        public static Material GetRendererMaterial(string modelPath, int meshIndex)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            Assert.IsNotNull(root, modelPath);
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            Assert.Greater(renderers.Length, meshIndex);
            return renderers[meshIndex].sharedMaterial;
        }

        public void Dispose()
        {
            XRayAssetSourceSettings.Override = null;
            foreach (XRayPendingGeneration pending in XRayPendingGenerationStore.Load())
            {
                if (pending.ModelPath.StartsWith(Folder, StringComparison.Ordinal))
                {
                    XRayPendingGenerationStore.Remove(pending.ModelPath);
                }
            }

            foreach (string asset in FindAssets(string.Empty, Folder))
            {
                string report = XRayResourceReportStore.GetPath(AssetDatabase.AssetPathToGUID(asset));
                if (File.Exists(report))
                {
                    File.Delete(report);
                }
            }

            AssetDatabase.DeleteAsset(Folder);
            if (AssetDatabase.IsValidFolder(ROOT_FOLDER) && AssetDatabase.FindAssets(string.Empty, new[] { ROOT_FOLDER }).Length == 0)
            {
                AssetDatabase.DeleteAsset(ROOT_FOLDER);
            }

            Directory.Dispose();
        }

        private bool HasPendingWork()
        {
            foreach (XRayPendingGeneration pending in XRayPendingGenerationStore.Load())
            {
                if (pending.ModelPath.StartsWith(Folder, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            foreach (string model in FindAssets(string.Empty, Folder))
            {
                if (model.EndsWith(".ogf", StringComparison.OrdinalIgnoreCase) && XRayAssetGenerationCoordinator.NeedsGeneration(model, Options))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

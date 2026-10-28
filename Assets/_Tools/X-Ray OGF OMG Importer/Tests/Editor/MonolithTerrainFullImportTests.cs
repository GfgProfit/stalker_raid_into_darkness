using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using XRay.Importer.Editor.Levels;

namespace XRay.Importer.Tests
{
    public sealed class MonolithTerrainFullImportTests
    {
        [Test]
        public void IsolatedImportRejectsUnsafeOutputRoots()
        {
            foreach (string root in new[] { "Assets", "Assets/_Source/Levels", "Assets/_Source/Levels/Acceptance", "Assets/Foo/../_Source/Levels", "Assets//_Source/Levels", "Assets/./_Source/Levels", "Assets/Foo/.", "Assets/Foo/Bar.", "Assets/Foo/Bar ", "E:/Outside", "Assets\\Outside\\Test" })
            {
                Assert.Throws<ArgumentException>(() => XRayLevelImportJob.Enqueue(new XRayLevelImportRequest { LevelName = "terrain_acceptance", OutputRoot = root }));
            }
            Assert.IsNull(XRayLevelImportJob.Current);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator AnomalySavedImportRendersAllQualityModes()
        {
            using (var fixture = new XRayTerrainLevelFixture(false))
            {
                yield return Import(fixture, XRayTerrainImportMode.EnableVerifiedAnomaly, XRayTerrainQuality.High);
                var before = Snapshot(fixture.Root);
                yield return XRayTerrainImportCapture.Run(fixture, false);
                CollectionAssert.AreEquivalent(before, Snapshot(fixture.Root));
            }
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator TrueStalkerSavedImportRendersWithoutAnomaly()
        {
            using (var fixture = new XRayTerrainLevelFixture(true))
            {
                yield return Import(fixture, XRayTerrainImportMode.EnableVerifiedAnomaly, XRayTerrainQuality.High);
                var before = Snapshot(fixture.Root);
                yield return XRayTerrainImportCapture.Run(fixture, true);
                CollectionAssert.AreEquivalent(before, Snapshot(fixture.Root));
            }
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator AnomalyFullJobPreservesExistingRendering()
        {
            yield return Accept(false, XRayTerrainImportMode.Preserve, XRayTerrainQuality.High);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator AnomalyFullJobBindsWithoutActivation()
        {
            yield return Accept(false, XRayTerrainImportMode.Bind, XRayTerrainQuality.High);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator AnomalyFullJobEnablesHigh()
        {
            yield return Accept(false, XRayTerrainImportMode.EnableVerifiedAnomaly, XRayTerrainQuality.High);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator AnomalyFullJobEnablesMedium()
        {
            yield return Accept(false, XRayTerrainImportMode.EnableVerifiedAnomaly, XRayTerrainQuality.Medium);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator AnomalyFullJobEnablesLow()
        {
            yield return Accept(false, XRayTerrainImportMode.EnableVerifiedAnomaly, XRayTerrainQuality.Low);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator TrueStalkerFullJobRejectsAnomalyActivation()
        {
            yield return Accept(true, XRayTerrainImportMode.EnableVerifiedAnomaly, XRayTerrainQuality.Medium);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator TrueStalkerFullJobBindsWithoutActivation()
        {
            yield return Accept(true, XRayTerrainImportMode.Bind, XRayTerrainQuality.High);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator MissingHeightDisablesPomAndAoInFullJob()
        {
            using (var fixture = new XRayTerrainLevelFixture(false))
            {
                fixture.RemoveHeight();
                yield return Import(fixture, XRayTerrainImportMode.EnableVerifiedAnomaly, XRayTerrainQuality.High);
                Material material = Material(fixture);
                Assert.AreEqual(0, material.GetFloat("_MonolithHasTerrainHeight"));
                Assert.AreEqual(0, material.GetFloat("_MonolithTerrainParallax"));
                Assert.AreEqual(0, material.GetFloat("_MonolithTerrainOcclusion"));
                Assert.IsNull(material.GetTexture("_MonolithTerrainHeightR"));
            }
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator UnknownSourceDisablesAnomalyInFullJob()
        {
            using (var fixture = new XRayTerrainLevelFixture(false))
            {
                fixture.ReplaceWinner(true);
                yield return Import(fixture, XRayTerrainImportMode.EnableVerifiedAnomaly, XRayTerrainQuality.High);
                Material material = Material(fixture);
                Assert.AreEqual(0, material.GetFloat("_MonolithTerrainSource"));
                Assert.AreEqual(0, material.GetFloat("_MonolithSourceProfile"));
                Assert.AreEqual(0, material.GetFloat("_MonolithSourceTerrain"));
            }
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator PartialTerrainMetadataCannotEnableSourceInFullJob()
        {
            using (var fixture = new XRayTerrainLevelFixture(false))
            {
                fixture.RemoveLayerMetadata();
                yield return Import(fixture, XRayTerrainImportMode.EnableVerifiedAnomaly, XRayTerrainQuality.High);
                Material material = Material(fixture);
                Assert.AreEqual(0, material.GetFloat("_MonolithHasTerrainSourceColor"));
                Assert.AreEqual(0, material.GetFloat("_MonolithSourceTerrain"));
            }
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator ChangedWinnerKeepsMaterialAndTextureGuidInFullJob()
        {
            using (var fixture = new XRayTerrainLevelFixture(false))
            {
                yield return Import(fixture, XRayTerrainImportMode.EnableVerifiedAnomaly, XRayTerrainQuality.High);
                Material material = Material(fixture);
                string path = AssetDatabase.GetAssetPath(material);
                string guid = AssetDatabase.AssetPathToGUID(path);
                string bump = AssetDatabase.GetAssetPath(material.GetTexture("_MonolithTerrainBumpR"));
                string bumpGuid = AssetDatabase.AssetPathToGUID(bump);
                string before = XRayLevelSourceRefresh.HashFile(bump);
                fixture.ReplaceWinner(false);
                yield return Import(fixture, XRayTerrainImportMode.EnableVerifiedAnomaly, XRayTerrainQuality.High);
                material = Material(fixture);
                Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material)));
                Assert.AreEqual(bumpGuid, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material.GetTexture("_MonolithTerrainBumpR"))));
                Assert.AreNotEqual(before, XRayLevelSourceRefresh.HashFile(bump));
            }
        }

        private static IEnumerator Accept(bool trueStalker, XRayTerrainImportMode mode, XRayTerrainQuality quality)
        {
            string root;
            using (var fixture = new XRayTerrainLevelFixture(trueStalker))
            {
                root = fixture.Root;
                yield return Import(fixture, mode, quality);
                Material material = Material(fixture);
                bool enabled = !trueStalker && mode == XRayTerrainImportMode.EnableVerifiedAnomaly;
                Assert.AreEqual(trueStalker ? 2 : 1, material.GetFloat("_MonolithTerrainSource"));
                Assert.AreEqual(enabled ? 1 : 0, material.GetFloat("_MonolithSourceProfile"));
                Assert.AreEqual(enabled ? 1 : 0, material.GetFloat("_MonolithSourceTerrain"));
                if (trueStalker)
                {
                    Assert.AreEqual(0, material.GetFloat("_MonolithHasTerrainHeight"));
                    Assert.AreEqual(0, material.GetFloat("_MonolithTerrainParallax"));
                    Assert.AreEqual(0, material.GetFloat("_MonolithTerrainOcclusion"));
                }
                Assert.AreEqual((float)quality, material.GetFloat("_MonolithTerrainQuality"));
                Assert.IsNotNull(material.GetTexture("_MonolithTerrainMask"));
                foreach (char channel in "RGBA")
                {
                    Assert.IsNotNull(material.GetTexture("_MonolithTerrainDetail" + channel));
                    Assert.IsNotNull(material.GetTexture("_MonolithTerrainBump" + channel));
                    Assert.AreEqual(!trueStalker, material.GetTexture("_MonolithTerrainHeight" + channel) != null);
                }
                if (enabled && quality == XRayTerrainQuality.Medium) Assert.AreEqual(1, material.GetFloat("_MonolithTerrainMidAvailable"));
                if (enabled && quality == XRayTerrainQuality.Low) Assert.AreEqual(1, material.GetFloat("_MonolithTerrainLowAvailable"));
                Assert.IsNotEmpty(material.GetTag("XRayTerrainShaderSHA256", false));
                Assert.IsNotEmpty(material.GetTag("XRayTerrainBrdfSHA256", false));
                AssetDatabase.SaveAssets();
                var before = Snapshot(root);
                yield return Import(fixture, mode, quality);
                AssetDatabase.SaveAssets();
                var after = Snapshot(root);
                WriteComparison(fixture, before, after, "Idempotence");
                CollectionAssert.AreEquivalent(before, after, "A no-op full import must not change saved asset bytes.");
                material = Material(fixture);
                string path = AssetDatabase.GetAssetPath(material);
                string guid = AssetDatabase.AssetPathToGUID(path);
                Texture custom = material.GetTexture("_MonolithTerrainBumpB");
                material.SetTexture("_MonolithTerrainBumpG", custom);
                material.SetFloat("_MonolithTerrainParallax", 0);
                material.SetFloat("_MonolithTerrainOcclusion", 0);
                material.SetFloat("_MonolithTerrainQuality", 2);
                material.SetFloat("_MonolithTerrainDetailScale", 17);
                material.SetVector("_MonolithTerrainOffset", new Vector4(0.11f, 0.12f, 0.13f, 0.14f));
                AssetDatabase.SaveAssetIfDirty(material);
                yield return Import(fixture, mode, quality);
                material = Material(fixture);
                Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material)));
                Assert.AreEqual(AssetDatabase.GetAssetPath(custom), AssetDatabase.GetAssetPath(material.GetTexture("_MonolithTerrainBumpG")));
                Assert.AreEqual(0, material.GetFloat("_MonolithTerrainParallax"));
                Assert.AreEqual(0, material.GetFloat("_MonolithTerrainOcclusion"));
                Assert.AreEqual(2, material.GetFloat("_MonolithTerrainQuality"));
                Assert.AreEqual(17, material.GetFloat("_MonolithTerrainDetailScale"));
                Assert.AreEqual(new Vector4(0.11f, 0.12f, 0.13f, 0.14f), material.GetVector("_MonolithTerrainOffset"));
                WriteComparison(fixture, before, Snapshot(root), "Overrides");
            }
            Assert.IsFalse(Directory.Exists(root) || File.Exists(root + ".meta"));
        }

        public static IEnumerator Import(XRayTerrainLevelFixture fixture, XRayTerrainImportMode mode, XRayTerrainQuality quality)
        {
            Assert.IsNull(XRayLevelImportJob.Current);
            string previousRoot = XRayLevelPaths.Root;
            Assert.IsTrue(XRayLevelImportJob.Enqueue(fixture.Request(mode, quality)));
            double start = EditorApplication.timeSinceStartup;
            while (XRayLevelImportJob.Current != null)
            {
                if (EditorApplication.timeSinceStartup - start > 180)
                {
                    XRayLevelImportJob.CancelCurrent();
                    Assert.Fail("Full import exceeded 180 seconds.");
                }
                yield return null;
            }
            Assert.AreEqual(XRayLevelJobState.Completed, XRayLevelImportJob.LastState, string.Join("; ", XRayLevelImportJob.LastRecord.Errors));
            Assert.IsTrue(XRayLevelImportJob.LastRecord.Completed);
            Assert.AreEqual(previousRoot, XRayLevelPaths.Root);
            AssetDatabase.SaveAssets();
            var manifest = AssetDatabase.LoadAssetAtPath<XRayLevelImportManifest>(XRayLevelPaths.ManifestPath(fixture.Root, fixture.LevelName));
            Assert.IsNotNull(manifest);
            Assert.IsTrue(manifest.Record.Completed);
            Assert.IsNotEmpty(manifest.Record.Source.Files);
            Assert.IsTrue(manifest.Record.Dependencies.Exists(dependency => dependency.Role == "Terrain source shader"));
            Assert.IsTrue(File.Exists(manifest.Record.ScenePath));
            Assert.IsTrue(File.Exists(manifest.Record.PrefabPath));
            GameObject prefab = PrefabUtility.LoadPrefabContents(manifest.Record.PrefabPath);
            try
            {
                MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
                Assert.Greater(renderers.Length, 0);
                foreach (MeshRenderer renderer in renderers)
                {
                    Assert.IsNotNull(renderer.GetComponent<MeshFilter>().sharedMesh);
                    foreach (Material material in renderer.sharedMaterials) Assert.IsNotNull(material);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }
            Scene scene = EditorSceneManager.OpenScene(manifest.Record.ScenePath, OpenSceneMode.Additive);
            try
            {
                Assert.IsTrue(scene.IsValid());
                Assert.IsTrue(Array.Exists(scene.GetRootGameObjects(), gameObject => gameObject.GetComponentInChildren<MeshRenderer>(true) != null));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
            fixture.SaveEvidence();
        }

        public static Material Material(XRayTerrainLevelFixture fixture)
        {
            var manifest = AssetDatabase.LoadAssetAtPath<XRayLevelImportManifest>(XRayLevelPaths.ManifestPath(fixture.Root, fixture.LevelName));
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(manifest.Record.PrefabPath);
            Material material = prefab.GetComponentInChildren<MeshRenderer>(true).sharedMaterial;
            Assert.IsNotNull(material);
            Assert.AreEqual("HDRP/MonolithLit", material.shader.name);
            return material;
        }

        private static Dictionary<string, string> Snapshot(string root)
        {
            var result = new Dictionary<string, string>();
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) result[file] = XRayLevelSourceRefresh.HashFile(file);
            return result;
        }

        private static void WriteComparison(XRayTerrainLevelFixture fixture, Dictionary<string, string> before, Dictionary<string, string> after, string stage)
        {
            string output = Environment.GetEnvironmentVariable("MONOLITH_PHASE05C_OUTPUT");
            if (string.IsNullOrEmpty(output)) return;
            Assert.IsTrue(Path.GetFullPath(output).StartsWith("E:\\Projects\\stalker_raid_into_darkness_Archive\\", StringComparison.OrdinalIgnoreCase));
            string folder = Path.Combine(output, fixture.Root.Substring(fixture.Root.LastIndexOf('/') + 1));
            Directory.CreateDirectory(folder);
            var rows = new List<string> { "Stage,Asset,BeforeSHA256,AfterSHA256,GUID,Equal" };
            foreach (KeyValuePair<string, string> pair in after)
            {
                before.TryGetValue(pair.Key, out string old);
                rows.Add(stage + ",\"" + pair.Key + "\"," + old + "," + pair.Value + "," + AssetDatabase.AssetPathToGUID(pair.Key) + "," + (old == pair.Value));
            }
            File.WriteAllLines(Path.Combine(folder, stage + ".csv"), rows);
            File.WriteAllText(Path.Combine(folder, "Input.txt"), fixture.SourceIdentity + "\nShader=" + fixture.SourceShader + "\nVisual=" + fixture.SourceVisual.ToString(CultureInfo.InvariantCulture));
            File.WriteAllText(Path.Combine(folder, "Manifest.json"), XRayLevelImportJob.LastRecord.ToJson());
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
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

        [Test]
        public void StaticOcclusionUsesOnlyOpaqueVisibleTrianglesAsOccluders()
        {
            Shader shader = Shader.Find("HDRP/MonolithLit");
            Assert.IsNotNull(shader);
            var mesh = new Mesh();
            var opaque = new Material(shader);
            var clipped = new Material(shader);
            try
            {
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.subMeshCount = 2;
                mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
                mesh.SetTriangles(new[] { 0, 1, 2 }, 1);
                clipped.SetFloat("_AlphaCutoffEnable", 1.0f);
                Assert.AreEqual(StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic, XRayLevelOcclusionBaking.GeometryFlags(mesh, new[] { opaque, opaque }, ShadowCastingMode.On));
                Assert.AreEqual(StaticEditorFlags.OccludeeStatic, XRayLevelOcclusionBaking.GeometryFlags(mesh, new[] { opaque, clipped }, ShadowCastingMode.On));
                Assert.AreEqual((StaticEditorFlags)0, XRayLevelOcclusionBaking.GeometryFlags(mesh, new[] { opaque, opaque }, ShadowCastingMode.ShadowsOnly));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clipped);
                UnityEngine.Object.DestroyImmediate(opaque);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
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
        public IEnumerator FreshImportBakesSkyOcclusionAndReusesProbeData()
        {
            Assume.That(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null);
            using (var fixture = new XRayTerrainLevelFixture(false, true))
            {
                string hostPath = SceneManager.GetActiveScene().path;
                var rejectedMeshes = new List<string>();
                void CollectMeshRejection(string message, string stack, LogType type)
                {
                    if (message.IndexOf("invalid mesh was removed from light baking input", StringComparison.OrdinalIgnoreCase) >= 0) rejectedMeshes.Add(message);
                }
                Application.logMessageReceived += CollectMeshRejection;
                try
                {
                    yield return Import(fixture, XRayTerrainImportMode.EnableVerifiedAnomaly, XRayTerrainQuality.High);
                }
                finally
                {
                    Application.logMessageReceived -= CollectMeshRejection;
                }
                Assert.IsEmpty(rejectedMeshes);
                Assert.AreEqual(hostPath, SceneManager.GetActiveScene().path);
                string scenePath = XRayLevelImportJob.LastRecord.ScenePath;
                string sceneGuid = AssetDatabase.AssetPathToGUID(scenePath);
                string sceneHash = XRayLevelSourceRefresh.HashFile(scenePath);
                string apvFolder = Path.GetDirectoryName(scenePath).Replace('\\', '/') + "/APV";
                string bakingSetPath = apvFolder + "/" + fixture.LevelName + "_BakingSet.asset";
                string bakingSetGuid = AssetDatabase.AssetPathToGUID(bakingSetPath);
                Assert.IsNotEmpty(bakingSetGuid);
                AssertBakedScene(scenePath, sceneGuid, bakingSetPath, fixture.LevelName);
                Assert.IsTrue(Array.Exists(Directory.GetFiles(apvFolder, "*.bytes", SearchOption.AllDirectories), path => new FileInfo(path).Length > 0));
                string occlusionPath = XRayLevelOcclusionBaking.FindDataPath(scenePath);
                Assert.IsNotNull(occlusionPath, string.Join("\n", XRayLevelImportJob.LastRecord.Warnings));
                Assert.Greater(XRayLevelImportJob.LastRecord.GetMetric("StaticOcclusionDataBytes"), 0.0);
                string occlusionHash = XRayLevelSourceRefresh.HashFile(occlusionPath);
                string occlusionGuid = AssetDatabase.AssetPathToGUID(occlusionPath);
                Dictionary<string, string> before = Snapshot(apvFolder);
                yield return Import(fixture, XRayTerrainImportMode.EnableVerifiedAnomaly, XRayTerrainQuality.High);
                Assert.AreEqual(hostPath, SceneManager.GetActiveScene().path);
                Assert.AreEqual(scenePath, XRayLevelImportJob.LastRecord.ScenePath);
                Assert.AreEqual(sceneGuid, AssetDatabase.AssetPathToGUID(scenePath));
                Assert.AreEqual(sceneHash, XRayLevelSourceRefresh.HashFile(scenePath));
                Assert.AreEqual(bakingSetGuid, AssetDatabase.AssetPathToGUID(bakingSetPath));
                Assert.AreEqual(occlusionPath, XRayLevelOcclusionBaking.FindDataPath(scenePath));
                Assert.AreEqual(occlusionGuid, AssetDatabase.AssetPathToGUID(occlusionPath));
                Assert.AreEqual(occlusionHash, XRayLevelSourceRefresh.HashFile(occlusionPath));
                Assert.Zero(XRayLevelImportJob.LastRecord.GetMetric(XRayLevelOcclusionBaking.UPDATED_METRIC));
                CollectionAssert.AreEquivalent(before, Snapshot(apvFolder));
                AssertBakedScene(scenePath, sceneGuid, bakingSetPath, fixture.LevelName);
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

        private static void AssertBakedScene(string scenePath, string sceneGuid, string bakingSetPath, string levelName)
        {
            var bakingSet = AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(bakingSetPath);
            Assert.IsNotNull(bakingSet);
            Assert.IsTrue(bakingSet.skyOcclusion);
            Assert.IsTrue(bakingSet.HasBakedData());
            CollectionAssert.AreEqual(new[] { sceneGuid }, bakingSet.sceneGUIDs);
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                GameObject[] roots = scene.GetRootGameObjects();
                GameObject probeRoot = Array.Find(roots, root => root.GetComponentInChildren<ProbeVolume>(true) != null);
                Assert.IsNotNull(probeRoot);
                var volume = probeRoot.GetComponentInChildren<ProbeVolume>(true);
                Assert.AreEqual(ProbeVolume.Mode.Global, volume.mode);
                Assert.IsFalse(volume.fillEmptySpaces);
                GameObject dataRoot = Array.Find(roots, root => root.GetComponentInChildren<ProbeVolumePerSceneData>(true) != null);
                Assert.IsNotNull(dataRoot);
                Assert.AreSame(bakingSet, dataRoot.GetComponentInChildren<ProbeVolumePerSceneData>(true).bakingSet);
                GameObject level = Array.Find(roots, root => root.name == levelName);
                Assert.IsNotNull(level);
                Transform geometry = level.transform.Find("Geometry");
                Assert.IsNotNull(geometry);
                int contributors = 0;
                foreach (MeshRenderer renderer in geometry.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    Assert.IsNotNull(filter);
                    MeshFilter sourceFilter = PrefabUtility.GetCorrespondingObjectFromSource(filter);
                    MeshRenderer sourceRenderer = PrefabUtility.GetCorrespondingObjectFromSource(renderer);
                    Assert.IsNotNull(sourceFilter);
                    Assert.IsNotNull(sourceRenderer);
                    Assert.IsNotNull(filter.sharedMesh);
                    Assert.AreSame(sourceFilter.sharedMesh, filter.sharedMesh);
                    CollectionAssert.AreEqual(sourceRenderer.sharedMaterials, renderer.sharedMaterials);
                    if ((GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & StaticEditorFlags.ContributeGI) == 0) continue;
                    Assert.AreEqual(ReceiveGI.LightProbes, renderer.receiveGI);
                    contributors++;
                }
                Assert.Greater(contributors, 0);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using XRay.Importer.Core.Level;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;
using XRay.Importer.Sources.Levels;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Tests
{
    [Category("NonVisual")]
    public sealed class XRayGrassRuntimeTests
    {
        [Test]
        public void MaximumPopulationRepresentativeCellsMatchPinnedSourceCounts()
        {
            var database = AssetDatabase.LoadAssetAtPath<XRayDetailDatabase>("Assets/_Source/Levels/Anomaly/1v2_DX11_v_12gb_2.5_168_662acc8d/l01_escape/Details/DetailDatabase.asset");
            Assert.IsNotNull(database);
            XRayDetails details = XRayDetails.Read(database.SourceDetails.bytes, "Representative source cells");
            XRayCollisionModel collision = XRayCollisionModel.Read(database.SourceCollision.bytes, "Representative source collision");
            collision.BuildGrid(XRayDetails.SLOT_SIZE);
            var settings = new XRayDetailPlacementSettings { Density = 0.04f, HeightScale = 1.0f, MonolithRelease = true, TerrainSlope = 1.0f };
            var placement = new XRayDetailPlacement(details, collision, database.Passable, settings);
            Assert.AreEqual(50, placement.DitherSize, "Pinned native float32 division and iCeil.");
            var cells = new[] { new Vector2Int(406, 391), new Vector2Int(405, 409), new Vector2Int(406, 407), new Vector2Int(417, 317) };
            int[] expected = { 262, 873, 322, 416 };
            var actual = new int[cells.Length];
            var rows = new List<string> { "CellX,CellZ,Model,X,Y,Z,Yaw,Scale,NormalX,NormalY,NormalZ,Hemi,Sun" };
            for (int index = 0; index < cells.Length; index++)
            {
                Vector2Int cell = cells[index];
                var instances = new List<XRayDetailInstance>();
                placement.PlaceSlot(cell.x, cell.y, instances);
                actual[index] = instances.Count;
                var repeated = new List<XRayDetailInstance>();
                placement.PlaceSlot(cell.x, cell.y, repeated);
                CollectionAssert.AreEqual(instances, repeated, "Stable source IDs and poses after repeated generation: " + cell);
                foreach (XRayDetailInstance instance in instances) rows.Add(string.Join(",", cell.x, cell.y, instance.Model, instance.X.ToString("R", CultureInfo.InvariantCulture), instance.Y.ToString("R", CultureInfo.InvariantCulture), instance.Z.ToString("R", CultureInfo.InvariantCulture), instance.Yaw.ToString("R", CultureInfo.InvariantCulture), instance.Scale.ToString("R", CultureInfo.InvariantCulture), instance.NormalX.ToString("R", CultureInfo.InvariantCulture), instance.NormalY.ToString("R", CultureInfo.InvariantCulture), instance.NormalZ.ToString("R", CultureInfo.InvariantCulture), instance.Hemi.ToString("R", CultureInfo.InvariantCulture), instance.Sun.ToString("R", CultureInfo.InvariantCulture)));
            }
            string output = Environment.GetEnvironmentVariable("MONOLITH_PHASE06B3_OUTPUT");
            if (!string.IsNullOrEmpty(output))
            {
                Assert.IsTrue(Path.GetFullPath(output).StartsWith("E:\\Projects\\stalker_raid_into_darkness_Archive\\", StringComparison.OrdinalIgnoreCase));
                Directory.CreateDirectory(output);
                File.WriteAllLines(Path.Combine(output, "Representative_Accepted.csv"), rows);
                File.WriteAllText(Path.Combine(output, "Representative_Grid.txt"), "DitherSize=" + placement.DitherSize + " Density=" + settings.Density.ToString("R", CultureInfo.InvariantCulture));
            }
            CollectionAssert.AreEqual(expected, actual, "Pinned source accepted counts.");
        }

        [Test]
        public void RvmSnapshotMatchesCurrentUserSettingsAndRelease()
        {
            XRayGrassDefaults profile = Defaults();
            XRayGrassValues values = profile.ForLevel("l01_escape");
            Assert.AreEqual(0.45f, values.Density);
            Assert.AreEqual(150, values.Radius);
            Assert.AreEqual(1.0f, values.Height);
            Assert.IsFalse(values.Shadows);
            StringAssert.Contains("cec1246b55d37ac26d1bb4a1829ea3e567bc161270396e6aa1a8f230f8e6671f", profile.Provenance);
            StringAssert.Contains("313a751d4c6e1e40ead2529256b54520a72c0ed9", profile.EngineRevision);
            StringAssert.Contains("sha256", profile.Provenance);
            Assert.AreEqual(0.35f, profile.GrassShadows.y);
        }

        [Test]
        public void CurrentRvmDiscoveryRecordsDriftWithoutMutatingSceneOrFrozenSnapshot()
        {
            string frozen = Resources.Load<TextAsset>("XRayRvmGrassDefaults").text;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            bool dirty = scene.isDirty;
            Assert.IsTrue(XRayRvmGrassSource.TryRead(out string json, out string status), status);
            XRayGrassDefaults current = XRayGrassDefaults.Load(json);
            Assert.IsTrue(current.ForLevel(null).Valid);
            StringAssert.Contains("sha256", current.Provenance);
            Assert.AreEqual(frozen, Resources.Load<TextAsset>("XRayRvmGrassDefaults").text);
            Assert.AreEqual(dirty, scene.isDirty);
            string output = Environment.GetEnvironmentVariable("MONOLITH_PHASE06B3_OUTPUT");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "RVM_Current_Source_Profile.json"), json);
            File.WriteAllText(Path.Combine(output, "RVM_Source_Drift.json"), "{\"frozen\":" + JsonUtility.ToJson(Defaults().ForLevel(null)) + ",\"current\":" + JsonUtility.ToJson(current.ForLevel(null)) + ",\"valuesDiffer\":" + (JsonUtility.ToJson(Defaults().ForLevel(null)) != JsonUtility.ToJson(current.ForLevel(null)) ? "true" : "false") + "}");
        }

        [Test]
        public void RvmRefreshRejectsConflictingRuntimeInputs()
        {
            const string USER = "r__detail_density 0.68\nr__detail_radius 150\nr__detail_height 0.8\nr__enable_grass_shadow off";
            const string MCM = "video/advanced/detail_density = 0.68\nvideo/advanced/detail_radius = 150\nvideo/advanced/detail_height = 0.8\nvideo/advanced/grass_shadow = false";
            XRayGrassValues values = XRayRvmGrassSource.Reconcile(USER, MCM);
            Assert.AreEqual(0.68f, values.Density);
            Assert.AreEqual(0.8f, values.Height);
            Assert.Throws<InvalidDataException>(() => XRayRvmGrassSource.Reconcile(USER, MCM.Replace("0.68", "0.45")));
            Assert.Throws<InvalidDataException>(() => XRayRvmGrassSource.Reconcile(USER, ""));
        }

        [Test]
        public void ExplicitRvmRefreshPreservesPerParameterOverridesAndUndoRedo()
        {
            var root = new GameObject("RVM refresh acceptance");
            root.SetActive(false);
            var database = ScriptableObject.CreateInstance<XRayDetailDatabase>();
            var source = new TextAsset("Source metadata only");
            try
            {
                database.Initialize("fixture", 32.0f, 0.6f, 1.0f, 2.0f, new List<XRayDetailModelEntry>(), new List<XRayDetailChunk>(), null, null, default, default, 0, 0);
                database.SetRuntimeSource(source, source, Array.Empty<bool>(), 1);
                var vegetation = root.AddComponent<XRayDetailVegetation>();
                vegetation.Database = database;
                var controller = root.AddComponent<XRayDetailVegetationController>();
                controller.Initialize(vegetation);
                controller.ResetToRvm();
                string imported = controller.ImportRvmSnapshot;
                controller.SetValues(new XRayGrassValues(0.3f, 150, 1.0f, true));
                string before = EditorJsonUtility.ToJson(controller);
                string refreshed = JsonUtility.ToJson(Defaults().WithValues(new XRayGrassValues(0.68f, 190, 0.8f, false), "fixture SHA256 verified inputs"));
                Undo.RecordObject(controller, "Refresh RVM acceptance");
                controller.RefreshFromRvm(refreshed);
                Undo.FlushUndoRecordObjects();
                string after = EditorJsonUtility.ToJson(controller);
                Assert.AreEqual(0.3f, controller.Values.Density);
                Assert.AreEqual(190, controller.Values.Radius);
                Assert.AreEqual(0.8f, controller.Values.Height);
                Assert.IsTrue(controller.Values.Shadows);
                Assert.AreEqual(imported, controller.ImportRvmSnapshot);
                Undo.PerformUndo();
                controller.Apply();
                Assert.AreEqual(before, EditorJsonUtility.ToJson(controller));
                Undo.PerformRedo();
                controller.Apply();
                Assert.AreEqual(after, EditorJsonUtility.ToJson(controller));
                controller.ResetToRvm();
                Assert.AreEqual(0, controller.OverrideMask);
                Assert.AreEqual(0.68f, controller.Values.Density);
                Assert.AreEqual(0.8f, controller.Values.Height);
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(controller, "Refresh RVM baseline undo acceptance");
                controller.RefreshFromRvm(JsonUtility.ToJson(Defaults().WithValues(new XRayGrassValues(0.72f, 230, 1.2f, true), "second fixture SHA256 verified inputs")));
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                controller.Apply();
                Assert.AreEqual(refreshed, controller.RvmSnapshot);
                controller.ResetToRvm();
                Assert.AreEqual(0.68f, controller.Values.Density);
                Assert.AreEqual(190, controller.Values.Radius);
                Assert.AreEqual(0.8f, controller.Values.Height);
                Assert.IsFalse(controller.Values.Shadows);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(database);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void SceneViewPopulationKeepsOneOwnerUntilExplicitSwitchOrDestruction()
        {
            var root = new GameObject("Scene View ownership acceptance");
            var first = new GameObject("Scene View A").AddComponent<Camera>();
            var second = new GameObject("Scene View B").AddComponent<Camera>();
            var game = new GameObject("Runtime streaming camera").AddComponent<Camera>();
            try
            {
                first.cameraType = CameraType.SceneView;
                second.cameraType = CameraType.SceneView;
                first.enabled = false;
                second.enabled = false;
                var vegetation = root.AddComponent<XRayDetailVegetation>();
                Assert.IsTrue(vegetation.CanStreamFrom(first));
                Assert.IsFalse(vegetation.CanStreamFrom(second));
                Assert.IsTrue(vegetation.CanStreamFrom(first));
                vegetation.StreamingCamera = game;
                Assert.IsTrue(vegetation.CanStreamFrom(first));
                Assert.IsFalse(vegetation.CanStreamFrom(second));
                vegetation.StreamingCamera = second;
                Assert.IsFalse(vegetation.CanStreamFrom(first));
                Assert.IsTrue(vegetation.CanStreamFrom(second));
                vegetation.StreamingCamera = null;
                UnityEngine.Object.DestroyImmediate(first.gameObject);
                Assert.IsTrue(vegetation.CanStreamFrom(second));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                if (first != null) UnityEngine.Object.DestroyImmediate(first.gameObject);
                UnityEngine.Object.DestroyImmediate(second.gameObject);
                UnityEngine.Object.DestroyImmediate(game.gameObject);
            }
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator ControllerSurvivesSceneReloadDomainReloadAndRepeatedPlayMode()
        {
            Assert.IsFalse(EditorSettings.enterPlayModeOptionsEnabled && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0, "Domain reload coverage requires domain reload to remain enabled.");
            string output = Path.Combine(Environment.GetEnvironmentVariable("MONOLITH_PHASE06B3_OUTPUT"), "ControllerLifecycle");
            Directory.CreateDirectory(output);
            string folder = "Assets/__XRayGrassControllerLifecycle_tmp_" + Guid.NewGuid().ToString("N");
            string path = folder + "/Controller.unity";
            string settingsPath = Path.Combine(output, "RuntimeSettings.json");
            var scene = default(UnityEngine.SceneManagement.Scene);
            GameObject providerRoot = null;
            try
            {
                Assert.IsTrue(XRayRvmGrassSource.TryRead(out string json, out string status), status);
                var defaults = XRayGrassDefaults.Load(json);
                var original = new XRayGrassValues(0.32f, 110, 1.3f, true);
                var saved = new XRayGrassValues(0.74f, 170, 1.6f, false);
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
                var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (Application.isBatchMode && active.isDirty && active.path.StartsWith("Assets/Tests/VerifyFixtures/", StringComparison.Ordinal)) active = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
                Assert.IsFalse(active.isDirty, "Lifecycle acceptance requires a clean NUnit host scene; path=" + active.path);
                scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
                var root = new GameObject("Grass lifecycle acceptance");
                root.SetActive(false);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                var vegetation = root.AddComponent<XRayDetailVegetation>();
                vegetation.Database = AssetDatabase.LoadAssetAtPath<XRayDetailDatabase>("Assets/_Source/Levels/Anomaly/1v2_DX11_v_12gb_2.5_168_662acc8d/l01_escape/Details/DetailDatabase.asset");
                Assert.IsNotNull(vegetation.Database);
                var controller = root.AddComponent<XRayDetailVegetationController>();
                controller.Initialize(vegetation);
                controller.InitializeRvmSnapshot(json);
                controller.ResetToRvm();
                controller.SetValues(original);
                string snapshot = controller.RvmSnapshot;
                string importSnapshot = controller.ImportRvmSnapshot;
                int mask = controller.OverrideMask;
                Assert.IsTrue(UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, path));
                File.Copy(path, Path.Combine(output, "SavedController.unity"));
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Additive);
                controller = scene.GetRootGameObjects()[0].GetComponent<XRayDetailVegetationController>();
                Assert.AreEqual(JsonUtility.ToJson(original), JsonUtility.ToJson(controller.Values));
                Assert.AreEqual(snapshot, controller.RvmSnapshot);
                Assert.AreEqual(importSnapshot, controller.ImportRvmSnapshot);
                Assert.AreEqual(mask, controller.OverrideMask);
                Assert.IsFalse(scene.isDirty);
                SessionState.SetString("XRayGrassLifecycle.Folder", folder);
                SessionState.SetString("XRayGrassLifecycle.Path", path);
                SessionState.SetString("XRayGrassLifecycle.Output", output);
                SessionState.SetString("XRayGrassLifecycle.Profile", json);
                SessionState.SetString("XRayGrassLifecycle.Snapshot", snapshot);
                SessionState.SetString("XRayGrassLifecycle.Import", importSnapshot);
                SessionState.SetInt("XRayGrassLifecycle.Mask", mask);
                for (int cycle = 0; cycle < 2; cycle++)
                {
                    SessionState.SetInt("XRayGrassLifecycle.Cycle", cycle);
                    yield return new EnterPlayMode();
                    folder = SessionState.GetString("XRayGrassLifecycle.Folder", string.Empty);
                    path = SessionState.GetString("XRayGrassLifecycle.Path", string.Empty);
                    output = SessionState.GetString("XRayGrassLifecycle.Output", string.Empty);
                    json = SessionState.GetString("XRayGrassLifecycle.Profile", string.Empty);
                    cycle = SessionState.GetInt("XRayGrassLifecycle.Cycle", 0);
                    original = new XRayGrassValues(0.32f, 110, 1.3f, true);
                    saved = new XRayGrassValues(0.74f, 170, 1.6f, false);
                    settingsPath = Path.Combine(output, "RuntimeSettings.json");
                    scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
                    if (!scene.IsValid()) scene = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(path, new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Additive));
                    root = scene.GetRootGameObjects()[0];
                    controller = root.GetComponent<XRayDetailVegetationController>();
                    Assert.IsNull(XRayGrassSettingsProvider.Active);
                    providerRoot = new GameObject("Isolated lifecycle settings");
                    providerRoot.SetActive(false);
                    var provider = providerRoot.AddComponent<XRayGrassSettingsProvider>();
                    provider.Initialize(new XRayGrassSettings(XRayGrassDefaults.Load(json), settingsPath));
                    providerRoot.SetActive(true);
                    root.SetActive(true);
                    Assert.AreEqual(JsonUtility.ToJson(cycle == 0 ? original : saved), JsonUtility.ToJson(controller.Values));
                    Assert.AreSame(provider, XRayGrassSettingsProvider.Ensure());
                    Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<XRayGrassSettingsProvider>(FindObjectsSortMode.None).Length);
                    controller.SetValues(saved);
                    Assert.AreEqual(JsonUtility.ToJson(saved), JsonUtility.ToJson(new XRayGrassSettings(XRayGrassDefaults.Load(json), settingsPath).Values));
                    root.SetActive(false);
                    UnityEngine.Object.DestroyImmediate(providerRoot);
                    providerRoot = null;
                    Assert.IsNull(XRayGrassSettingsProvider.Active);
                    yield return new ExitPlayMode();
                    folder = SessionState.GetString("XRayGrassLifecycle.Folder", string.Empty);
                    path = SessionState.GetString("XRayGrassLifecycle.Path", string.Empty);
                    output = SessionState.GetString("XRayGrassLifecycle.Output", string.Empty);
                    json = SessionState.GetString("XRayGrassLifecycle.Profile", string.Empty);
                    snapshot = SessionState.GetString("XRayGrassLifecycle.Snapshot", string.Empty);
                    importSnapshot = SessionState.GetString("XRayGrassLifecycle.Import", string.Empty);
                    mask = SessionState.GetInt("XRayGrassLifecycle.Mask", 0);
                    cycle = SessionState.GetInt("XRayGrassLifecycle.Cycle", 0);
                    original = new XRayGrassValues(0.32f, 110, 1.3f, true);
                    saved = new XRayGrassValues(0.74f, 170, 1.6f, false);
                    scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
                    if (!scene.IsValid()) scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Single);
                    controller = scene.GetRootGameObjects()[0].GetComponent<XRayDetailVegetationController>();
                    Assert.AreEqual(JsonUtility.ToJson(original), JsonUtility.ToJson(controller.Values));
                    Assert.AreEqual(snapshot, controller.RvmSnapshot);
                    Assert.AreEqual(importSnapshot, controller.ImportRvmSnapshot);
                    Assert.AreEqual(mask, controller.OverrideMask);
                    Assert.IsFalse(scene.isDirty);
                }
                File.WriteAllText(Path.Combine(output, "Result.json"), "{\"sceneSaveReload\":true,\"domainReloadEnabled\":true,\"playCycles\":2,\"serializedValuesPreserved\":true,\"runtimePersistence\":true,\"duplicateProviders\":false,\"sceneDirtyAfterReload\":false}");
            }
            finally
            {
                if (string.IsNullOrEmpty(folder)) folder = SessionState.GetString("XRayGrassLifecycle.Folder", string.Empty);
                if (providerRoot != null) UnityEngine.Object.DestroyImmediate(providerRoot);
                if (scene.IsValid() && scene.isLoaded)
                {
                    if (Application.isPlaying) UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
                    else UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                }
                if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
                foreach (string key in new[] { "Folder", "Path", "Output", "Profile", "Snapshot", "Import" }) SessionState.EraseString("XRayGrassLifecycle." + key);
                SessionState.EraseInt("XRayGrassLifecycle.Mask");
                SessionState.EraseInt("XRayGrassLifecycle.Cycle");
            }
        }

        [TestCase(50, 47.5f)]
        [TestCase(150, 147.5f)]
        [TestCase(250, 247.5f)]
        public void RadiusUsesQuantizedEngineFade(int radius, float expected)
        {
            Assert.AreEqual(expected, new XRayGrassValues(1.0f, radius, 1.0f, false).FadeDistance);
            Assert.AreEqual(1.0f, XRayDetailVegetation.SourceFade(0.5f, expected));
            Assert.AreEqual(0.0f, XRayDetailVegetation.SourceFade(expected * expected, expected));
        }

        [Test]
        public void PersistenceOverridesLevelDefaultsAndResetRestoresRvm()
        {
            using (var directory = new TestDirectory())
            {
                XRayGrassDefaults defaults = Defaults();
                string path = directory.Combine("grass.json");
                var settings = new XRayGrassSettings(defaults, path);
                settings.SetLevel("l01_escape");
                string initial = JsonUtility.ToJson(settings.Values);
                int changes = 0;
                settings.Changed += () => changes++;
                var custom = new XRayGrassValues(0.2f, 210, 1.6f, true);
                settings.Set(custom);
                settings.SetLevel("escape");
                Assert.AreEqual(JsonUtility.ToJson(custom), JsonUtility.ToJson(settings.Values));
                var reloaded = new XRayGrassSettings(defaults, path);
                Assert.IsTrue(reloaded.HasOverrides);
                Assert.AreEqual(JsonUtility.ToJson(custom), JsonUtility.ToJson(reloaded.Values));
                reloaded.SetLevel("l01_escape");
                reloaded.ResetToRvm();
                Assert.AreEqual(initial, JsonUtility.ToJson(reloaded.Values));
                Assert.IsFalse(File.Exists(path));
                Assert.AreEqual(2, changes);
                Assert.Throws<ArgumentOutOfRangeException>(() => settings.Set(new XRayGrassValues(float.NaN, 150, 1.0f, false)));
                Assert.Throws<InvalidDataException>(() => XRayGrassDefaults.Load("{}"));
            }
        }

        [Test]
        public void PerLevelDefaultsFollowSavedOverridePriority()
        {
            using (var directory = new TestDirectory())
            {
                string json = Resources.Load<TextAsset>("XRayRvmGrassDefaults").text;
                json = json.Replace("\"_levels\": []", "\"_levels\": [{\"_level\":\"fixture\",\"_values\":{\"_density\":0.8,\"_radius\":50,\"_height\":0.5,\"_shadows\":true}}]");
                var settings = new XRayGrassSettings(XRayGrassDefaults.Load(json), directory.Combine("settings.json"));
                settings.SetLevel("fixture");
                Assert.AreEqual(0.8f, settings.Values.Density);
                settings.Set(new XRayGrassValues(0.3f, 100, 1.2f, false));
                settings.SetLevel("l01_escape");
                Assert.AreEqual(0.3f, settings.Values.Density);
                settings.ResetToRvm();
                Assert.AreEqual(Defaults().ForLevel("l01_escape").Density, settings.Values.Density);
                settings.SetLevel("fixture");
                Assert.AreEqual(0.8f, settings.Values.Density);
            }
        }

        [Test]
        public void DetailShaderPassesCompileWithoutRendering()
        {
            var material = new Material(Shader.Find(XRay.Importer.Editor.MonolithMaterialConfigurator.SHADER_NAME));
            try
            {
                Assert.IsNotNull(material.shader);
                material.EnableKeyword("_MONOLITH_LEVEL_DETAIL");
                material.EnableKeyword("_ALPHATEST_ON");
                material.enableInstancing = true;
                foreach (string pass in new[] { "ForwardOnly", "DepthForwardOnly", "ShadowCaster", "MotionVectors" })
                {
                    int index = material.FindPass(pass);
                    Assert.GreaterOrEqual(index, 0, pass);
                    ShaderUtil.CompilePass(material, index, true);
                }

                string errors = string.Join("\n", ShaderUtil.GetShaderMessages(material.shader).Where(message => message.severity.ToString() == "Error").Select(message => message.message));
                Assert.IsEmpty(errors);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void TerrainAlignmentUsesSourceBasisAndAnchoredUniformScale()
        {
            var instance = new XRayDetailInstance(0, 12.0f, -3.0f, 7.0f, 0.0f, 1.0f, 1.0f, 1.0f, 0);
            Matrix4x4 matrix = XRayDetailPopulation.MatrixFor(instance, 2.0f, true);
            Assert.Less((matrix.MultiplyVector(Vector3.up) - Vector3.up * 2.0f).magnitude, 0.00001f);
            Assert.Less((matrix.MultiplyVector(Vector3.right) - Vector3.back * 2.0f).magnitude, 0.00001f);
            Assert.Less((matrix.MultiplyVector(Vector3.forward) - Vector3.right * 2.0f).magnitude, 0.00001f);
            Assert.AreEqual(new Vector3(12.0f, -3.0f, 7.0f), matrix.MultiplyPoint3x4(Vector3.zero));
            Assert.AreEqual(8.0f, matrix.determinant, 0.00001f);
        }

        [Test]
        public void EditModeStreamingUsesSceneViewOnly()
        {
            var root = new GameObject("Streaming camera ownership");
            root.SetActive(false);
            try
            {
                var first = new GameObject("First camera");
                first.transform.SetParent(root.transform);
                var second = new GameObject("Second camera");
                second.transform.SetParent(root.transform);
                Camera owner = first.AddComponent<Camera>();
                Camera other = second.AddComponent<Camera>();
                XRayDetailVegetation vegetation = root.AddComponent<XRayDetailVegetation>();
                vegetation.StreamingCamera = owner;
                Assert.IsFalse(vegetation.CanStreamFrom(owner));
                Assert.IsFalse(vegetation.CanStreamFrom(other));
                Assert.IsFalse(vegetation.CanStreamFrom(null));
                other.cameraType = CameraType.SceneView;
                vegetation.StreamingCamera = other;
                Assert.IsTrue(vegetation.CanStreamFrom(other));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator RealEscapeSourceImportsAndRuntimeSettingsResampleWithoutAssetChanges()
        {
            XRaySourceConfiguration configuration = XRayLevelSourceCache.CreateMo2Configuration();
            configuration.IndexScope.Clear();
            configuration.IndexScope.AddRange(new[] { "levels/l01_escape", "textures", "shaders", "shaders.xr", "textures.ltx", "gamemtl.xr" });
            var source = new XRayEffectiveGamedata(XRayAssetIndex.Build(Mo2VirtualFileSystem.Plan(configuration)));
            using (var fixture = new XRayFloraLevelFixture(false))
            using (var directory = new TestDirectory())
            {
                fixture.AttachDetails(source);
                XRayLevelImportRequest request = fixture.Request(XRayFloraImportMode.EnableVerifiedAnomaly);
                request.Settings.ImportDetails = true;
                request.Settings.RuntimeDetails = true;
                Assert.IsTrue(XRayLevelImportJob.Enqueue(request));
                double started = EditorApplication.timeSinceStartup;
                while (XRayLevelImportJob.Current != null)
                {
                    if (EditorApplication.timeSinceStartup - started > 300.0)
                    {
                        XRayLevelImportJob.CancelCurrent();
                        Assert.Fail("Runtime detail acceptance exceeded 300 seconds.");
                    }
                    yield return null;
                }

                Assert.AreEqual(XRayLevelJobState.Completed, XRayLevelImportJob.LastState, string.Join("; ", XRayLevelImportJob.LastRecord.Errors));
                string path = fixture.Root + "/" + fixture.LevelName + "/Details/DetailDatabase.asset";
                var database = AssetDatabase.LoadAssetAtPath<XRayDetailDatabase>(path);
                Assert.IsTrue(database.HasRuntimeSource);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fixture.Root + "/" + fixture.LevelName + "/Prefabs/" + fixture.LevelName + ".prefab");
                Assert.IsNotNull(prefab.GetComponentInChildren<XRayDetailVegetationController>());
                Assert.AreEqual(0, database.InstanceCount);
                CollectionAssert.AreEqual(source.ReadAllBytes("levels/l01_escape/level.details"), database.SourceDetails.bytes);
                foreach (XRayDetailModelEntry model in database.Models)
                {
                    Assert.IsNotNull(model.Mesh);
                    Assert.IsNotNull(model.Material.GetTexture("_BaseColorMap"));
                    Assert.IsNotNull(model.Material.GetTexture("_MonolithBump"));
                    Assert.AreEqual(1.0f, model.Material.GetFloat("_MonolithGrassSource"));
                    Assert.AreEqual(200.0f / 255.0f, model.Material.GetFloat("_AlphaCutoff"));
                    Assert.IsTrue(model.Material.enableInstancing);
                    Assert.AreEqual(1.0f, model.Material.GetFloat("_MonolithSourceProfile"));
                }

                string before = Snapshot(fixture.Root);
                var settings = new XRayGrassSettings(Defaults(), directory.Combine("settings.json"));
                var go = new GameObject("Nonvisual detail state acceptance");
                try
                {
                    var vegetation = go.AddComponent<XRayDetailVegetation>();
                    vegetation.Database = database;
                    vegetation.PrepareSource(settings);
                    Vector3 eye = FindGround(database, Defaults());
                    for (int i = 0; i < 80; i++) vegetation.StreamSource(eye);
                    Assert.Greater(vegetation.ResidentInstances, 0);
                    string original = Signature(vegetation.Population, eye);
                    settings.Set(new XRayGrassValues(1.0f, 50, 2.0f, true));
                    Assert.IsTrue(vegetation.CastShadows);
                    Assert.AreEqual(47.5f, vegetation.DrawDistance);
                    Assert.AreEqual(2.0f, vegetation.GrassValues.Height);
                    for (int i = 0; i < 80; i++) vegetation.StreamSource(eye);
                    string sparse = Signature(vegetation.Population, eye);
                    Assert.AreNotEqual(original, sparse);
                    settings.ResetToRvm();
                    for (int i = 0; i < 80; i++) vegetation.StreamSource(eye);
                    Assert.AreEqual(original, Signature(vegetation.Population, eye));
                    string reset = Signature(vegetation.Population, eye);
                    settings.Set(new XRayGrassValues(settings.Values.Density, settings.Values.Radius, 0.5f, true));
                    Assert.AreEqual(reset, Signature(vegetation.Population, eye));
                    vegetation.StreamSource(eye + Vector3.one * 5000.0f);
                    Assert.AreEqual(0, vegetation.ResidentCells);
                    settings.ResetToRvm();
                    for (int i = 0; i < 80; i++) vegetation.StreamSource(eye);
                    Assert.AreEqual(original, Signature(vegetation.Population, eye));
                    Assert.AreEqual(before, Snapshot(fixture.Root));
                    string output = Environment.GetEnvironmentVariable("MONOLITH_PHASE06B3_OUTPUT");
                    if (!string.IsNullOrEmpty(output))
                    {
                        Directory.CreateDirectory(output);
                        File.WriteAllText(Path.Combine(output, "Runtime_State_Acceptance.json"), JsonUtility.ToJson(settings.Values, true));
                        File.WriteAllText(Path.Combine(output, "Detail_Instance_Signatures.txt"), original + "\n" + sparse);
                        var rows = new StringBuilder("CellX,CellZ,Model,X,Y,Z,Yaw,Scale,NormalX,NormalY,NormalZ,Hemi,Sun,Wave,Alpha\n");
                        foreach (XRayDetailCell cell in vegetation.Population.Cells)
                        {
                            if (!vegetation.Population.IsCurrent(cell)) continue;
                            foreach (XRayDetailInstance instance in cell.Instances)
                            {
                                rows.AppendLine(string.Join(",", cell.X, cell.Z, instance.Model, Num(instance.X), Num(instance.Y), Num(instance.Z), Num(instance.Yaw), Num(instance.Scale), Num(instance.NormalX), Num(instance.NormalY), Num(instance.NormalZ), Num(instance.Hemi), Num(instance.Sun), instance.Wave, Num(cell.Alpha)));
                            }
                        }

                        File.WriteAllText(Path.Combine(output, "Detail_Runtime_Instances.csv"), rows.ToString());
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
        }

        [UnityTest]
        public IEnumerator RuntimeControllerPersistsAllFourValuesAndReset()
        {
            yield return new EnterPlayMode();
            using (var directory = new TestDirectory())
            {
                string path = directory.Combine("controller-settings.json");
                var settings = new XRayGrassSettings(Defaults(), path);
                var providerRoot = new GameObject("Grass settings acceptance");
                providerRoot.SetActive(false);
                var root = new GameObject("Grass controller acceptance");
                root.SetActive(false);
                var database = ScriptableObject.CreateInstance<XRayDetailDatabase>();
                var details = new TextAsset("Source metadata only");
                var collision = new TextAsset("Source metadata only");
                try
                {
                    var provider = providerRoot.AddComponent<XRayGrassSettingsProvider>();
                    provider.Initialize(settings);
                    providerRoot.SetActive(true);
                    Assert.AreSame(provider, XRayGrassSettingsProvider.Ensure());
                    Assert.IsNull(providerRoot.GetComponentInChildren<Canvas>());
                    database.Initialize("fixture", 32.0f, 0.6f, 1.0f, 2.0f, new List<XRayDetailModelEntry>(), new List<XRayDetailChunk>(), null, null, default, default, 0, 0);
                    database.SetRuntimeSource(details, collision, Array.Empty<bool>(), 1);
                    XRayDetailVegetation vegetation = root.AddComponent<XRayDetailVegetation>();
                    vegetation.Database = database;
                    var controller = root.AddComponent<XRayDetailVegetationController>();
                    controller.Initialize(vegetation);
                    controller.SetValues(new XRayGrassValues(0.8f, 210, 1.7f, true));
                    Assert.AreEqual(0.8f, vegetation.GrassValues.Density);
                    Assert.AreEqual(210, vegetation.GrassValues.Radius);
                    Assert.AreEqual(1.7f, vegetation.GrassValues.Height);
                    Assert.IsTrue(vegetation.CastShadows);
                    var reload = new XRayGrassSettings(Defaults(), path);
                    Assert.AreEqual(JsonUtility.ToJson(controller.Values), JsonUtility.ToJson(reload.Values));
                    controller.ResetToRvm();
                    Assert.AreEqual(JsonUtility.ToJson(Defaults().ForLevel("fixture")), JsonUtility.ToJson(controller.Values));
                    Assert.IsFalse(File.Exists(path));
                    controller.SetValues(new XRayGrassValues(0.3f, 150, 1.0f, false));
                    string refreshed = JsonUtility.ToJson(Defaults().WithValues(new XRayGrassValues(0.68f, 190, 0.8f, true), "fixture verified inputs"));
                    controller.RefreshFromRvm(refreshed);
                    Assert.AreEqual(1, controller.OverrideMask);
                    Assert.AreEqual(0.3f, controller.Values.Density);
                    Assert.AreEqual(190, controller.Values.Radius);
                    Assert.AreEqual(0.8f, controller.Values.Height);
                    Assert.IsTrue(controller.Values.Shadows);
                    string next = JsonUtility.ToJson(Defaults().WithValues(new XRayGrassValues(0.72f, 210, 1.2f, false), "next fixture verified inputs"));
                    controller.RefreshFromRvm(next);
                    Assert.AreEqual(1, controller.OverrideMask);
                    Assert.AreEqual(0.3f, controller.Values.Density);
                    Assert.AreEqual(210, controller.Values.Radius);
                    Assert.AreEqual(1.2f, controller.Values.Height);
                    Assert.IsFalse(controller.Values.Shadows);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                    UnityEngine.Object.DestroyImmediate(providerRoot);
                    UnityEngine.Object.DestroyImmediate(database);
                    UnityEngine.Object.DestroyImmediate(details);
                    UnityEngine.Object.DestroyImmediate(collision);
                }
            }

            yield return new ExitPlayMode();
        }

        [Test]
        public void EditModeSettingsRemainTransient()
        {
            var settings = new XRayGrassSettings(Defaults(), null);
            settings.Set(new XRayGrassValues(0.8f, 210, 1.7f, true), false);
            Assert.AreEqual(0.8f, settings.Values.Density);
            Assert.AreEqual(210, settings.Values.Radius);
            Assert.AreEqual(1.7f, settings.Values.Height);
            Assert.IsTrue(settings.Values.Shadows);
            settings.ResetToRvm();
            Assert.IsFalse(settings.HasOverrides);
            Assert.AreEqual(JsonUtility.ToJson(Defaults().ForLevel(null)), JsonUtility.ToJson(settings.Values));
        }

        [Test]
        public void ControllerExposesFourSerializedGrassControls()
        {
            System.Reflection.FieldInfo[] fields = typeof(XRayDetailVegetationController).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Where(field => field.IsDefined(typeof(SerializeField), false) && !field.IsDefined(typeof(HideInInspector), false)).ToArray();
            CollectionAssert.AreEquivalent(new[] { "_density", "_radius", "_height", "_shadows" }, fields.Select(field => field.Name).ToArray());
            Assert.IsNull(typeof(XRayDetailVegetationController).Assembly.GetType("XRay.Importer.Levels.XRayGrassSettingsView"));
        }

        private static string Num(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static XRayGrassDefaults Defaults()
        {
            return XRayGrassDefaults.Load(Resources.Load<TextAsset>("XRayRvmGrassDefaults").text);
        }

        private static Vector3 FindGround(XRayDetailDatabase database, XRayGrassDefaults defaults)
        {
            var population = new XRayDetailPopulation(database, defaults);
            XRayDetails details = XRayDetails.Read(database.SourceDetails.bytes, "source");
            population.Apply(defaults.ForLevel(database.LevelName));
            for (int z = details.SizeZ / 2; z < details.SizeZ; z += 16)
            {
                for (int x = details.SizeX / 2; x < details.SizeX; x += 16)
                {
                    XRayDetailSlot slot = details.GetSlot(x, z);
                    if (slot.IsEmpty) continue;
                    var eye = new Vector3((x - details.OffsetX + 0.5f) * 2.0f, slot.YBase + 1.0f, (z - details.OffsetZ + 0.5f) * 2.0f);
                    population.Stream(eye);
                    if (population.ResidentInstances > 0) return eye;
                }
            }

            throw new InvalidDataException("Real escape source has no populated ground in the acceptance region.");
        }

        private static string Signature(XRayDetailPopulation population, Vector3 eye)
        {
            XRayDetailCell anchor = population.Cells.Where(cell => population.IsCurrent(cell) && cell.Instances.Count > 0).OrderBy(cell => (cell.Bounds.center - eye).sqrMagnitude).First();
            return string.Join("|", anchor.Instances.Select(instance => string.Join(",", instance.Model, instance.X.ToString("R", CultureInfo.InvariantCulture), instance.Y.ToString("R", CultureInfo.InvariantCulture), instance.Z.ToString("R", CultureInfo.InvariantCulture), instance.Scale.ToString("R", CultureInfo.InvariantCulture), instance.Yaw.ToString("R", CultureInfo.InvariantCulture))).OrderBy(value => value));
        }

        private static string Snapshot(string root)
        {
            return string.Join("|", Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(file => file).Select(file => file + ":" + XRayLevelSourceRefresh.HashFile(file)));
        }
    }
}

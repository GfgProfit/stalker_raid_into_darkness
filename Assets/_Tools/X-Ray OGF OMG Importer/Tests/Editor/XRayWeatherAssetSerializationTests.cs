using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using XRay.Importer.Core.Level;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class XRayWeatherAssetSerializationTests
    {
        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(false, true)]
        public void BuilderAndSceneSerializationPreserveAuthoredConsumersWhileAnotherSceneOwnsWeather(bool occupiedDuringBuild, bool disabled)
        {
            Assert.IsNull(XRayWeatherController.Active, "Run this isolated fixture without a live weather preview.");
            Scene previous = SceneManager.GetActiveScene();
            bool previousDirty = previous.isDirty;
            string previousRoot = XRayLevelPaths.Root;
            string folder = "Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/Fixtures/__WeatherSerialization_" + Guid.NewGuid().ToString("N");
            Scene sceneA = default;
            Scene buildScene = default;
            Scene sceneB = default;
            string hostPath = null;
            string evidence = EvidenceFolder(occupiedDuringBuild, disabled);
            try
            {
                XRayLevelPaths.EnsureFolder(folder);
                if (string.IsNullOrEmpty(previous.path))
                {
                    if (!Application.isBatchMode || previous.isDirty || previous.rootCount != 0 || SceneManager.sceneCount != 1) Assert.Ignore("The isolated serialization fixture requires a saved host or a clean empty batch host; user scenes were preserved.");
                    hostPath = folder + "/Host.unity";
                    Assert.IsTrue(EditorSceneManager.SaveScene(previous, hostPath));
                }
                XRayLevelPaths.UseRoot(folder);
                XRayLevelPaths.EnsureFolder(XRayLevelPaths.Scenes("B"));
                XRayWeatherProfile profile = Profile(folder);
                AssetDatabase.CreateAsset(profile, folder + "/Weather.asset");
                sceneA = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                Assert.IsTrue(EditorSceneManager.SaveScene(sceneA, folder + "/A.unity"));
                XRayWeatherController ownerA = occupiedDuringBuild ? CreateOwner(sceneA, profile) : null;
                GlobalState stateA = ownerA != null ? new GlobalState(ownerA) : null;
                buildScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                if (occupiedDuringBuild) ExpectConflict();
                GameObject built = XRayLevelSceneBuilder.BuildRoot(buildScene, new XRayLevelSceneContent { LevelName = "B", Level = new XRayLevelData(), WeatherProfile = profile, SourceFolder = "synthetic" }, new XRayLevelImportRecord());
                if (stateA != null) stateA.AssertUnchanged(ownerA);
                if (disabled)
                {
                    built.GetComponentInChildren<MonolithWeatherInputs>(true).enabled = false;
                    built.GetComponentInChildren<MonolithSourceLighting>(true).enabled = false;
                    XRayLevelSceneBuilder.ConfigureWeather(built, profile);
                }
                string prefabPath = folder + "/B.prefab";
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(built, prefabPath);
                Assert.IsNotNull(prefab);
                SaveEvidence(prefabPath, evidence);
                if (evidence != null)
                {
                    foreach (string dependency in AssetDatabase.GetDependencies(prefabPath, true))
                    {
                        if (dependency != prefabPath && dependency.StartsWith(folder + "/", StringComparison.Ordinal)) SaveEvidence(dependency, evidence);
                    }
                }
                AssertBindings(built, profile, !disabled, "BuildRoot");
                AssertBindings(prefab, profile, !disabled, "SaveAsPrefabAsset");
                if (stateA != null) stateA.AssertUnchanged(ownerA);
                Assert.IsTrue(EditorSceneManager.CloseScene(buildScene, true));
                buildScene = default;
                if (!occupiedDuringBuild)
                {
                    ownerA = CreateOwner(sceneA, profile);
                    stateA = new GlobalState(ownerA);
                }
                stateA.AssertUnchanged(ownerA);
                sceneB = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                if (!disabled) ExpectConflict();
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, sceneB);
                Assert.IsFalse(instance.GetComponentInChildren<XRayWeatherController>(true).BeginPreview());
                stateA.AssertUnchanged(ownerA);
                string scenePath = folder + "/B.unity";
                Assert.IsTrue(EditorSceneManager.SaveScene(sceneB, scenePath));
                SaveEvidence(scenePath, evidence);
                AssertBindings(instance, profile, !disabled, "InstantiatePrefab/SaveScene");
                stateA.AssertUnchanged(ownerA);
                Assert.IsTrue(EditorSceneManager.CloseScene(sceneB, true));
                sceneB = default;
                if (!disabled) ExpectConflict();
                sceneB = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                AssertBindings(sceneB.GetRootGameObjects()[0], profile, !disabled, "Reload with A");
                stateA.AssertUnchanged(ownerA);
                Assert.IsTrue(EditorSceneManager.CloseScene(sceneB, true));
                sceneB = default;
                Assert.IsTrue(EditorSceneManager.CloseScene(sceneA, true));
                sceneA = default;
                XRayWeatherPreview.Stop();
                Assert.IsNull(XRayWeatherController.Active);
                sceneB = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                GameObject isolated = sceneB.GetRootGameObjects()[0];
                AssertBindings(isolated, profile, !disabled, "Isolated reload");
                XRayWeatherController controllerB = isolated.GetComponentInChildren<XRayWeatherController>(true);
                XRayFloraWindProvider windB = isolated.GetComponentInChildren<XRayFloraWindProvider>(true);
                Assert.AreEqual(!disabled, controllerB.BeginPreview(), controllerB.Diagnostic);
                if (!disabled)
                {
                    Assert.AreSame(controllerB, XRayWeatherController.Active);
                    Assert.IsTrue(windB.WeatherDriven);
                    Assert.AreSame(isolated.GetComponentInChildren<MonolithWeatherInputs>(true), GlobalOwner(typeof(MonolithWeatherInputs)));
                    Assert.AreSame(isolated.GetComponentInChildren<MonolithSourceLighting>(true), GlobalOwner(typeof(MonolithSourceLighting)));
                    Assert.AreEqual(1.0f, Shader.GetGlobalFloat("_MonolithWeatherActive"));
                    Assert.IsTrue(isolated.GetComponentInChildren<MonolithWeatherInputs>(true).EnvironmentActive);
                    Assert.AreEqual(1.0f, Shader.GetGlobalFloat("_MonolithWeatherEnvironmentActive"));
                    for (int i = 0; i < 4; i++) AssertTextureBinding(profile.GetKeyframe(0).EnvironmentResources.Texture(i), Shader.GetGlobalTexture("_MonolithWeatherEnvironment" + i), "Isolated reload environment " + i);
                    Assert.AreEqual(1, Shader.GetGlobalInt("_MonolithBoundLightCount"));
                    controllerB.EndPreview();
                }
                Assert.IsNull(XRayWeatherController.Active);
                Assert.IsFalse(windB.WeatherDriven);
                Assert.AreEqual(0.0f, Shader.GetGlobalFloat("_MonolithWeatherEnvironmentActive"));
                Assert.IsTrue(EditorSceneManager.CloseScene(sceneB, true));
                sceneB = default;
                Assert.IsNull(GlobalOwner(typeof(MonolithWeatherInputs)));
                Assert.IsNull(GlobalOwner(typeof(MonolithSourceLighting)));
                Assert.IsNull(XRayFloraWindProvider.Active);
                Assert.AreEqual(0.0f, Shader.GetGlobalFloat("_MonolithWeatherActive"));
                Assert.AreEqual(0, Shader.GetGlobalInt("_MonolithBoundLightCount"));
            }
            finally
            {
                XRayWeatherPreview.Stop();
                if (sceneB.IsValid() && sceneB.isLoaded) EditorSceneManager.CloseScene(sceneB, true);
                if (buildScene.IsValid() && buildScene.isLoaded) EditorSceneManager.CloseScene(buildScene, true);
                if (sceneA.IsValid() && sceneA.isLoaded) EditorSceneManager.CloseScene(sceneA, true);
                XRayLevelPaths.UseRoot(previousRoot);
                if (previous.IsValid() && previous.isLoaded)
                {
                    SceneManager.SetActiveScene(previous);
                    Assert.AreEqual(previousDirty, previous.isDirty);
                    if (hostPath != null)
                    {
                        Assert.AreEqual(hostPath, previous.path);
                        Assert.AreEqual(0, previous.rootCount);
                        Assert.AreEqual(1, SceneManager.sceneCount);
                        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    }
                }
                if (AssetDatabase.IsValidFolder(folder)) Assert.IsTrue(AssetDatabase.DeleteAsset(folder));
            }
        }

        private static XRayWeatherController CreateOwner(Scene scene, XRayWeatherProfile profile)
        {
            var root = new GameObject("A weather owner");
            SceneManager.MoveGameObjectToScene(root, scene);
            XRayWeatherController controller = XRayLevelSceneBuilder.ConfigureWeather(root, profile);
            Assert.IsTrue(XRayWeatherPreview.Start(controller), controller.Diagnostic);
            controller.SetTimeScale(0.0f);
            controller.Seek(21600.0);
            controller.TickPreview(0.1f);
            Assert.IsTrue(controller.GetComponent<MonolithWeatherInputs>().EnvironmentActive);
            Assert.AreEqual(1.0f, Shader.GetGlobalFloat("_MonolithWeatherEnvironmentActive"));
            return controller;
        }

        private static string EvidenceFolder(bool occupiedDuringBuild, bool disabled)
        {
            string configured = Environment.GetEnvironmentVariable("XRAY_WEATHER_SERIALIZATION_RESULTS");
            if (string.IsNullOrEmpty(configured)) return null;
            string archive = Path.GetFullPath(XRayLevelPaths.ArchiveRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string output = Path.GetFullPath(configured);
            if (!output.StartsWith(archive, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Weather serialization evidence must be stored in the project archive.");
            string path = Path.Combine(output, "BuildOccupied_" + occupiedDuringBuild + "_Disabled_" + disabled + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void SaveEvidence(string path, string evidence)
        {
            if (evidence == null) return;
            File.Copy(path, Path.Combine(evidence, Path.GetFileName(path)));
            if (File.Exists(path + ".meta")) File.Copy(path + ".meta", Path.Combine(evidence, Path.GetFileName(path) + ".meta"));
        }

        private static void AssertBindings(GameObject root, XRayWeatherProfile profile, bool enabled, string phase)
        {
            XRayWeatherController controller = root.GetComponentInChildren<XRayWeatherController>(true);
            Assert.IsNotNull(controller);
            Assert.AreEqual(1, root.GetComponentsInChildren<XRayWeatherController>(true).Length);
            MonolithWeatherInputs weather = root.GetComponentInChildren<MonolithWeatherInputs>(true);
            MonolithSourceLighting lighting = root.GetComponentInChildren<MonolithSourceLighting>(true);
            Assert.IsNotNull(weather);
            Assert.IsNotNull(lighting);
            Assert.AreEqual(enabled, weather.enabled, phase + ": MonolithWeatherInputs serialized enabled");
            Assert.AreEqual(enabled, lighting.enabled, phase + ": MonolithSourceLighting serialized enabled");
            Assert.IsTrue(controller.enabled);
            var serialized = new SerializedObject(controller);
            Assert.AreSame(profile, serialized.FindProperty("_profile").objectReferenceValue);
            Assert.IsTrue(profile.HasEnvironmentScale);
            for (int i = 0; i < 4; i++)
            {
                Assert.Greater(profile.GetKeyframe(0).EnvironmentResources.Weights[i], 0.0f);
                Assert.IsInstanceOf<Cubemap>(profile.GetKeyframe(0).EnvironmentResources.Texture(i));
                Assert.IsTrue(AssetDatabase.Contains(profile.GetKeyframe(0).EnvironmentResources.Texture(i)));
            }
            Assert.AreSame(weather, serialized.FindProperty("_weatherInputs").objectReferenceValue);
            Assert.AreSame(lighting, serialized.FindProperty("_sourceLighting").objectReferenceValue);
            Assert.AreSame(root.GetComponentInChildren<XRayFloraWindProvider>(true), serialized.FindProperty("_windProvider").objectReferenceValue);
            Assert.AreSame(root.GetComponentInChildren<Light>(true), serialized.FindProperty("_sun").objectReferenceValue);
        }

        private static Object GlobalOwner(Type type)
        {
            return (Object)type.GetField("_owner", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        }

        private static void AssertTextureBinding(Texture expected, Texture actual, string phase)
        {
            Assert.IsInstanceOf<Cubemap>(actual, phase);
            Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(expected, out string expectedGuid, out long expectedId), phase + ": expected persistent identity");
            Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(actual, out string actualGuid, out long actualId), phase + ": actual persistent identity");
            Assert.AreEqual(expectedGuid, actualGuid, phase + ": GUID");
            Assert.AreEqual(expectedId, actualId, phase + ": local fileID");
        }

        private static void ExpectConflict()
        {
            LogAssert.Expect(LogType.Error, "Only one MonolithWeatherInputs provider can be active.");
            LogAssert.Expect(LogType.Error, "Only one MonolithSourceLighting controller can be active.");
        }

        private static XRayWeatherProfile Profile(string folder)
        {
            var profile = ScriptableObject.CreateInstance<XRayWeatherProfile>();
            var resources = new XRayWeatherResources[4];
            for (int i = 0; i < resources.Length; i++)
            {
                var cube = new Cubemap(1, TextureFormat.RGBA32, false);
                for (int face = 0; face < 6; face++) cube.SetPixel((CubemapFace)face, 0, 0, new Color(0.1f * (i + 1), 0.2f, 0.3f));
                cube.Apply(false, false);
                AssetDatabase.CreateAsset(cube, folder + "/Environment" + i + ".asset");
                resources[i] = new XRayWeatherResources("fixture_environment_" + i, cube);
            }
            XRayWeatherResources environment = XRayWeatherResources.Blend(XRayWeatherResources.Blend(resources[0], resources[1], 0.5f), XRayWeatherResources.Blend(resources[2], resources[3], 0.5f), 0.5f);
            var positions = new Vector2[24];
            for (int i = 0; i < positions.Length; i++) positions[i] = new Vector2(i * 10.0f, -30.0f);
            var first = new XRayWeatherSnapshot("00:00:00", 0.0, Color.black, Color.gray, Color.gray, 0.0f, 300.0f, 0.004f, 0.0f, Color.gray, Color.gray, 0.5f, 500.0f, 1000.0f, Color.gray, Color.gray, 0.0f, default, default);
            var second = new XRayWeatherSnapshot("12:00:00", 43200.0, Color.white, Color.gray, Color.gray, 0.0f, 700.0f, 0.004f, 0.8f, Color.gray, Color.gray, 0.5f, 500.0f, 1000.0f, Color.gray, Color.gray, 0.0f, default, default);
            profile.Initialize("serialization_fixture", "synthetic", "serialization regression", new[] { first.WithResources(default, default, environment), second.WithResources(default, default, environment) }, positions, new Vector4(11.0f, 0.15f, 0.5f, 0.15f), environmentScale: Vector2.one);
            return profile;
        }

        private sealed class GlobalState
        {
            private static readonly string[] VECTOR_NAMES = { "_MonolithWeatherState", "_MonolithWeatherSun", "_MonolithWeatherLightSetup", "_MonolithWeatherFloraFixes", "_MonolithWeatherNightAmbient", "_MonolithWeatherEnvironmentWeights", "_MonolithWeatherEnvironmentColor", "_MonolithWeatherAmbient", "_MonolithLightCalibration", "_MonolithWindParams", "_MonolithWindTreesSetup", "_MonolithWindAnim", "_MonolithWindAnimPrev", "_MonolithTreeWind", "_MonolithTreeWave", "_MonolithTreeWindPrev", "_MonolithTreeWavePrev" };
            private static readonly string[] FLOAT_NAMES = { "_MonolithWeatherActive", "_MonolithWeatherEnvironmentActive", "_MonolithSourceLightingMode", "_MonolithBoundLightCount", "_MonolithBoundRadianceScale", "_MonolithWindActive" };
            private static readonly string[] ARRAY_NAMES = { "_MonolithBoundPositionType64", "_MonolithBoundDirectionRange64", "_MonolithBoundColor64" };

            private readonly Vector4[] _vectors = new Vector4[VECTOR_NAMES.Length];
            private readonly float[] _floats = new float[FLOAT_NAMES.Length];
            private readonly Vector4[][] _arrays = new Vector4[ARRAY_NAMES.Length][];
            private readonly Texture[] _textures = new Texture[4];
            private readonly string[] _authored = new string[5];
            private readonly Object _weather;
            private readonly Object _lighting;
            private readonly XRayFloraWindProvider _wind;

            public GlobalState(XRayWeatherController controller)
            {
                for (int i = 0; i < VECTOR_NAMES.Length; i++) _vectors[i] = Shader.GetGlobalVector(VECTOR_NAMES[i]);
                for (int i = 0; i < FLOAT_NAMES.Length; i++) _floats[i] = Shader.GetGlobalFloat(FLOAT_NAMES[i]);
                for (int i = 0; i < ARRAY_NAMES.Length; i++) _arrays[i] = Shader.GetGlobalVectorArray(ARRAY_NAMES[i]);
                for (int i = 0; i < _textures.Length; i++) _textures[i] = Shader.GetGlobalTexture("_MonolithWeatherEnvironment" + i);
                _weather = GlobalOwner(typeof(MonolithWeatherInputs));
                _lighting = GlobalOwner(typeof(MonolithSourceLighting));
                _wind = XRayFloraWindProvider.Active;
                Object[] objects = Objects(controller);
                for (int i = 0; i < objects.Length; i++) _authored[i] = EditorJsonUtility.ToJson(objects[i]);
            }

            public void AssertUnchanged(XRayWeatherController controller)
            {
                Assert.AreSame(controller, XRayWeatherController.Active);
                Assert.AreSame(controller, XRayWeatherPreview.Controller);
                Assert.AreSame(_weather, GlobalOwner(typeof(MonolithWeatherInputs)));
                Assert.AreSame(_lighting, GlobalOwner(typeof(MonolithSourceLighting)));
                Assert.AreSame(_wind, XRayFloraWindProvider.Active);
                for (int i = 0; i < VECTOR_NAMES.Length; i++) Assert.AreEqual(_vectors[i], Shader.GetGlobalVector(VECTOR_NAMES[i]), VECTOR_NAMES[i]);
                for (int i = 0; i < FLOAT_NAMES.Length; i++) Assert.AreEqual(_floats[i], Shader.GetGlobalFloat(FLOAT_NAMES[i]), FLOAT_NAMES[i]);
                for (int i = 0; i < ARRAY_NAMES.Length; i++) CollectionAssert.AreEqual(_arrays[i], Shader.GetGlobalVectorArray(ARRAY_NAMES[i]), ARRAY_NAMES[i]);
                for (int i = 0; i < _textures.Length; i++) Assert.AreSame(_textures[i], Shader.GetGlobalTexture("_MonolithWeatherEnvironment" + i), "_MonolithWeatherEnvironment" + i);
                Object[] objects = Objects(controller);
                for (int i = 0; i < objects.Length; i++) Assert.AreEqual(_authored[i], EditorJsonUtility.ToJson(objects[i]), objects[i].GetType().Name);
            }

            private static Object[] Objects(XRayWeatherController controller)
            {
                GameObject root = controller.gameObject;
                return new Object[] { controller, root.GetComponent<MonolithWeatherInputs>(), root.GetComponent<MonolithSourceLighting>(), root.GetComponent<XRayFloraWindProvider>(), root.GetComponentInChildren<Light>(true) };
            }
        }
    }
}

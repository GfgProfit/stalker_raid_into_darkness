using System;
using System.Reflection;
using NUnit.Framework;
using Rvm.Benchmark.Editor;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Levels;

namespace Rvm.Benchmark.Tests
{
    public sealed class BenchmarkRouteProfileTests
    {
        private const string EYE_PATH = "Assets/_BENCHMARK/Scenarios/Military_PlayerEye.asset";
        private const string OVERVIEW_PATH = "Assets/_BENCHMARK/Scenarios/Military_Overview.asset";

        [Test]
        public void AuthoringUsesWorldPosesAndKeyOrderWithoutCameraScale()
        {
            var root = new GameObject("Authoring parent");
            var cameraObject = new GameObject("Camera");
            var profile = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<BenchmarkScenario>(EYE_PATH));
            try
            {
                root.transform.SetPositionAndRotation(new Vector3(19, -7, 42), Quaternion.Euler(12, 43, 8));
                root.transform.localScale = new Vector3(2, 3, 4);
                for (int i = 4; i > 0; i--)
                {
                    var pose = new GameObject("Pos " + i).transform;
                    pose.SetParent(root.transform, false);
                    pose.localPosition = new Vector3(i, i * 2, -i);
                    pose.localRotation = Quaternion.Euler(i * 3, i * 12, i * 2);
                }
                var ordered = BenchmarkRouteAuthoring.OrderedChildren(profile, root.transform);
                var positions = new Vector3[4];
                var rotations = new Quaternion[4];
                for (int i = 0; i < 4; i++)
                {
                    Assert.That(ordered[i].name, Is.EqualTo("Pos " + (i + 1)));
                    positions[i] = ordered[i].position;
                    rotations[i] = ordered[i].rotation;
                }
                profile.ConfigureRoute(profile.ScenarioId, profile.DisplayName, profile.ScenePath, profile.SceneGuid, "", positions, rotations, profile.CameraSettings, profile.CameraProvenance);
                var camera = cameraObject.AddComponent<Camera>();
                camera.transform.localScale = new Vector3(1.1f, 1.2f, 1.3f);
                Vector3 scale = camera.transform.localScale;
                profile.CameraSettings.Apply(camera);
                camera.transform.SetPositionAndRotation(profile.Position(0), profile.Rotation(0));
                Assert.That(camera.transform.position, Is.EqualTo(ordered[0].position));
                Assert.That(Quaternion.Angle(camera.transform.rotation, ordered[0].rotation), Is.LessThan(0.001));
                Assert.That(camera.transform.localScale, Is.EqualTo(scale));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void SceneBindingRejectsOtherScenesMissingAndDuplicateProfiles()
        {
            var eye = AssetDatabase.LoadAssetAtPath<BenchmarkScenario>(EYE_PATH);
            var overview = AssetDatabase.LoadAssetAtPath<BenchmarkScenario>(OVERVIEW_PATH);
            Assert.That(BenchmarkScenario.SelectRoute(new[] { overview, eye }, eye.ScenarioId, eye.SceneGuid), Is.SameAs(eye));
            Assert.Throws<InvalidOperationException>(() => BenchmarkScenario.SelectRoute(new[] { eye }, eye.ScenarioId, "different-scene-guid"));
            Assert.Throws<InvalidOperationException>(() => BenchmarkScenario.SelectRoute(new[] { eye }, "escape-player-eye", eye.SceneGuid));
            Assert.Throws<InvalidOperationException>(() => BenchmarkScenario.SelectRoute(new[] { eye, eye }, eye.ScenarioId, eye.SceneGuid));
        }

        [Test]
        public void RepeatedAuthoringUpdateIsIdempotentAndPreservesOverview()
        {
            var overview = AssetDatabase.LoadAssetAtPath<BenchmarkScenario>(OVERVIEW_PATH);
            string beforeOverview = EditorJsonUtility.ToJson(overview);
            var profile = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<BenchmarkScenario>(EYE_PATH));
            try
            {
                string original = EditorJsonUtility.ToJson(profile);
                var poses = BenchmarkRouteAuthoring.AuthoringPoses(profile);
                BenchmarkRouteAuthoring.UpdateRoute(profile, poses);
                BenchmarkRouteAuthoring.UpdateRoute(profile, poses);
                Assert.That(EditorJsonUtility.ToJson(profile), Is.EqualTo(original));
                Assert.That(EditorJsonUtility.ToJson(overview), Is.EqualTo(beforeOverview));
                for (int i = 0; i < 4; i++) Assert.That(profile.Position(i), Is.EqualTo(poses[i].position));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void MissingDuplicateNonfiniteAndForeignPosesAreRejected()
        {
            var root = new GameObject("foreign poses");
            var profile = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<BenchmarkScenario>(EYE_PATH));
            try
            {
                Assert.Throws<ArgumentException>(() => BenchmarkRouteAuthoring.OrderedChildren(profile, root.transform));
                for (int i = 1; i <= 4; i++) new GameObject("Pos " + i).transform.SetParent(root.transform);
                var poses = BenchmarkRouteAuthoring.OrderedChildren(profile, root.transform);
                Assert.Throws<ArgumentException>(() => BenchmarkRouteAuthoring.UpdateRoute(profile, poses));
                new GameObject("Pos 1").transform.SetParent(root.transform);
                Assert.Throws<ArgumentException>(() => BenchmarkRouteAuthoring.OrderedChildren(profile, root.transform));
                var positions = new[] { Vector3.zero, Vector3.one, Vector3.right, new Vector3(float.NaN, 0, 0) };
                var rotations = new[] { Quaternion.identity, Quaternion.identity, Quaternion.identity, Quaternion.identity };
                Assert.Throws<ArgumentException>(() => profile.ConfigureRoute("test", "test", profile.ScenePath, profile.SceneGuid, "", positions, rotations, profile.CameraSettings, "test"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CompletionAndAbortRestoreCameraAndGroupingConfiguration(bool abort)
        {
            var root = new GameObject("route lifecycle");
            var config = ScriptableObject.CreateInstance<GameConfig>();
            try
            {
                var camera = root.AddComponent<Camera>();
                camera.transform.SetPositionAndRotation(new Vector3(1, 2, 3), Quaternion.Euler(7, 23, 5));
                camera.transform.localScale = new Vector3(1, 2, 3);
                camera.fieldOfView = 71;
                camera.nearClipPlane = 0.17f;
                string original = JsonUtility.ToJson(BenchmarkCameraSettings.Capture(camera));
                var state = new BenchmarkCameraState(camera);
                var renderer = root.AddComponent<XRayLevelInstancedRenderer>();
                renderer.ApplyConfiguration(config);
                string originalConfig = JsonUtility.ToJson(config);
                using (var grouping = new BenchmarkGroupingState(new[] { renderer }, "G1"))
                {
                    typeof(LocationBatchingBenchmark).GetField("_modeState", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, grouping);
                    Assert.That(renderer.Configuration.InstanceSubmissionGroupingEnabled, Is.True);
                    Assert.That(config.InstanceSubmissionGroupingEnabled, Is.False);
                    var controller = root.AddComponent<Rvm.CleanReleaseRoute07.CleanReleaseRouteController>();
                    controller.ConfigureProfiles(camera, new BenchmarkScenario[0], "test", null);
                    typeof(Rvm.CleanReleaseRoute07.CleanReleaseRouteController).GetField("_cameraState", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, state);
                    camera.transform.SetPositionAndRotation(new Vector3(23, 45, 67), Quaternion.identity);
                    camera.fieldOfView = 95;
                    if (abort) controller.Abort();
                    else typeof(Rvm.CleanReleaseRoute07.CleanReleaseRouteController).GetMethod("RestoreState", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                    Assert.That(camera.transform.position, Is.EqualTo(new Vector3(1, 2, 3)));
                    Assert.That(camera.transform.localScale, Is.EqualTo(new Vector3(1, 2, 3)));
                    Assert.That(JsonUtility.ToJson(BenchmarkCameraSettings.Capture(camera)), Is.EqualTo(original));
                    Assert.That(renderer.Configuration, Is.SameAs(config));
                    state.Dispose();
                }
                Assert.That(renderer.Configuration, Is.SameAs(config));
                Assert.That(JsonUtility.ToJson(config), Is.EqualTo(originalConfig));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void RepeatedTeardownDoesNotRecheckResumedWorld()
        {
            var root = new GameObject("idempotent teardown");
            string outputRoot = Environment.GetEnvironmentVariable("BENCHMARK_ROUTE_TEST_OUTPUT") ?? System.IO.Path.Combine(BenchmarkOutput.DEFAULT_ROOT, "RouteProfileTests");
            string output = System.IO.Path.Combine(BenchmarkOutput.ValidateDirectory(outputRoot), Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(output);
            try
            {
                var camera = root.AddComponent<Camera>();
                var controller = root.AddComponent<Rvm.CleanReleaseRoute07.CleanReleaseRouteController>();
                controller.ConfigureProfiles(camera, new BenchmarkScenario[0], "test", null);
                var type = controller.GetType();
                type.GetField("_cameraState", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, new BenchmarkCameraState(camera));
                type.GetField("_originalCameraSettings", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, JsonUtility.ToJson(BenchmarkCameraSettings.Capture(camera)));
                type.GetField("_originalCameraRotation", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, Quaternion.identity);
                type.GetField("_output", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, output);
                controller.Abort();
                string first = System.IO.File.ReadAllText(System.IO.Path.Combine(output, "restoration.json"));
                camera.transform.position = new Vector3(7, 8, 9);
                controller.Abort();
                Assert.That(camera.transform.position, Is.EqualTo(new Vector3(7, 8, 9)));
                Assert.That(System.IO.File.ReadAllText(System.IO.Path.Combine(output, "restoration.json")), Is.EqualTo(first));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OrdinaryStartupDoesNotApplyRouteOrGrouping()
        {
            int target = Application.targetFrameRate;
            int vSync = QualitySettings.vSyncCount;
            var type = typeof(Rvm.CleanReleaseRoute07.CleanReleaseRouteController);
            type.GetMethod("ApplyDefaults", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            type.GetMethod("StartRequestedRoute", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            typeof(LocationBatchingBenchmark).GetMethod("Install", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            Assert.That(Application.targetFrameRate, Is.EqualTo(target));
            Assert.That(QualitySettings.vSyncCount, Is.EqualTo(vSync));
            Assert.Throws<ArgumentException>(() => BenchmarkGroupingState.Enabled("G0"));
        }
    }
}

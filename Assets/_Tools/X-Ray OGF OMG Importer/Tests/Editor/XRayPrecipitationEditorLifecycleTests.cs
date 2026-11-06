using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class XRayPrecipitationEditorLifecycleTests
    {
        [Test]
        public void EditorReleaseDisposesAllOwnedResourcesWhileRuntimeSuspendPreservesMoistureStorage()
        {
            GameConfig game = GameConfig.Load();
            Assert.IsNotNull(game);
            Assert.IsNotNull(game.Precipitation);
            Assert.IsNotNull(game.Precipitation.WetSurfaceTextures);
            int ownerId = Shader.PropertyToID("_MonolithWetnessOwner");
            Assert.AreEqual(0, Shader.GetGlobalInteger(ownerId));
            string canonicalBefore = JsonUtility.ToJson(game.Precipitation);
            PrecipitationConfig local = Object.Instantiate(game.Precipitation);
            var data = new SerializedObject(local);
            data.FindProperty("_wetnessEnabled").boolValue = true;
            data.FindProperty("_puddlesEnabled").boolValue = true;
            data.FindProperty("_quality").enumValueIndex = (int)XRayPrecipitationQuality.Low;
            data.ApplyModifiedPropertiesWithoutUndo();
            Assert.AreSame(game.Precipitation.WetSurfaceTextures, local.WetSurfaceTextures);
            string localBefore = JsonUtility.ToJson(local);
            Scene scene = EditorSceneManager.NewPreviewScene();
            XRayPrecipitationSystem system = null;
            try
            {
                var root = new GameObject("Precipitation Editor lifecycle regression");
                SceneManager.MoveGameObjectToScene(root, scene);
                XRayWeatherProfile profile = game.DefaultWeather;
                Assert.IsNotNull(profile);
                Assert.IsTrue(profile.TryEvaluate(43200.0, out XRayWeatherSnapshot weather, out string diagnostic), diagnostic);
                XRayWeatherController controller = XRayLevelSceneBuilder.ConfigureWeather(root, profile);
                Assert.IsNull(controller.Precipitation);
                system = new XRayPrecipitationSystem(controller.gameObject);
                FieldInfo systemField = typeof(XRayWeatherController).GetField("_precipitation", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(systemField);
                systemField.SetValue(controller, system);
                system.Publish(local, weather, 0.0f, 1.0f, true, false);
                Assert.IsTrue(system.Enabled, system.Diagnostic);
                Assert.IsNotNull(system.Geometry.Heights);
                Assert.IsNotNull(system.Wetness);
                Assert.IsNotNull(system.Pass);
                Assert.IsNotNull(system.Reflections);
                Assert.IsNotNull(system.Reflections.Probe);
                Assert.Greater(system.EstimatedBytes, 0L);
                RenderTexture previousHeights = system.Geometry.Heights;
                XRayWetnessField previousField = system.Wetness;
                GraphicsBuffer previousBuffer = previousField.Buffer;
                long previousFieldBytes = previousField.EstimatedBytes;
                Transform previousVolume = controller.transform.Find("X-Ray Precipitation");
                GameObject previousReflection = system.Reflections.Probe.gameObject;
                Assert.IsNotNull(previousVolume);
                Assert.IsTrue(previousBuffer.IsValid());
                Assert.AreNotEqual(0, Shader.GetGlobalInteger(ownerId));
                system.Suspend();
                Assert.IsNull(system.Reflections);
                Assert.IsTrue(previousReflection == null);
                Assert.AreEqual(0, Shader.GetGlobalInteger(ownerId));
                Assert.AreSame(previousField, system.Wetness);
                Assert.AreSame(previousBuffer, previousField.Buffer);
                Assert.IsTrue(previousBuffer.IsValid());
                Assert.AreSame(previousHeights, system.Geometry.Heights);
                Assert.Greater(system.EstimatedBytes, 0L);
                system.Publish(local, weather, 0.0f, 1.0f, true, false);
                Assert.AreSame(previousField, system.Wetness);
                Assert.IsTrue(previousBuffer.IsValid());
                Assert.IsTrue(system.Wetness.Buffer.IsValid());
                Assert.AreEqual(previousFieldBytes, system.Wetness.EstimatedBytes);
                Assert.AreNotEqual(0, Shader.GetGlobalInteger(ownerId));
                XRayPrecipitationEditorLifecycle.Release(controller);
                Assert.IsFalse(system.Enabled);
                Assert.IsNull(system.Pass);
                Assert.IsNull(system.Wetness);
                Assert.IsNull(system.Reflections);
                Assert.IsNull(previousField.Buffer);
                Assert.IsFalse(previousBuffer.IsValid());
                Assert.IsNull(system.Geometry.Heights);
                Assert.IsTrue(previousHeights == null);
                Assert.IsTrue(previousVolume == null);
                Assert.AreEqual(0L, system.EstimatedBytes);
                Assert.AreEqual(0, Shader.GetGlobalInteger(ownerId));
                Assert.DoesNotThrow(() => XRayPrecipitationEditorLifecycle.Release(controller));
                Assert.AreEqual(0L, system.EstimatedBytes);
                Assert.AreEqual(0, Shader.GetGlobalInteger(ownerId));
                Assert.AreEqual(localBefore, JsonUtility.ToJson(local));
                Assert.AreEqual(canonicalBefore, JsonUtility.ToJson(game.Precipitation));
            }
            finally
            {
                if (system != null) system.Dispose();
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(local);
            }
        }
    }
}

using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class XRayWetnessReflectionTests
    {
        [Test]
        public void AutomaticProbeIsBoundedIdempotentAndReleasedByOptOutWithoutChangingSharedConfiguration()
        {
            PrecipitationConfig canonical = GameConfig.Load().Precipitation;
            string canonicalBefore = JsonUtility.ToJson(canonical);
            PrecipitationConfig settings = Object.Instantiate(canonical);
            var data = new SerializedObject(settings);
            data.FindProperty("_wetnessEnabled").boolValue = true;
            data.FindProperty("_nonCameraReflectionsEnabled").boolValue = true;
            data.FindProperty("_quality").enumValueIndex = (int)XRayPrecipitationQuality.High;
            data.ApplyModifiedPropertiesWithoutUndo();
            string settingsBefore = JsonUtility.ToJson(settings);
            Scene scene = EditorSceneManager.NewPreviewScene();
            XRayWetnessReflection reflections = null;
            try
            {
                var owner = new GameObject("Wetness reflection lifecycle regression");
                SceneManager.MoveGameObjectToScene(owner, scene);
                reflections = new XRayWetnessReflection(owner);
                reflections.Tick(settings, 0.0f);
                HDAdditionalReflectionData first = reflections.Probe;
                Assert.IsNotNull(first);
                Assert.AreEqual(scene, first.gameObject.scene);
                Assert.AreSame(owner.transform, first.transform.parent);
                Assert.AreEqual(ProbeSettings.Mode.Realtime, first.mode);
                Assert.AreEqual(ProbeSettings.RealtimeMode.OnDemand, first.realtimeMode);
                Assert.IsTrue(first.timeSlicing);
                Assert.IsTrue(first.settingsRaw.cubeResolution.useOverride);
                Assert.AreEqual(128, (int)first.settingsRaw.cubeResolution.@override);
                Assert.AreEqual(0, reflections.CaptureRequests);
                reflections.Tick(settings, 5.0f);
                Assert.AreSame(first, reflections.Probe);
                Assert.AreEqual(1, owner.transform.childCount);
                Assert.AreEqual(0, reflections.CaptureRequests);
                Assert.AreEqual(settingsBefore, JsonUtility.ToJson(settings));
                data.Update();
                data.FindProperty("_nonCameraReflectionsEnabled").boolValue = false;
                data.ApplyModifiedPropertiesWithoutUndo();
                reflections.Tick(settings, 0.0f);
                Assert.IsNull(reflections.Probe);
                Assert.IsTrue(first == null);
                Assert.AreEqual(0, owner.transform.childCount);
                Assert.AreEqual(0L, reflections.EstimatedBytes);
                data.Update();
                data.FindProperty("_nonCameraReflectionsEnabled").boolValue = true;
                data.FindProperty("_quality").enumValueIndex = (int)XRayPrecipitationQuality.Low;
                data.ApplyModifiedPropertiesWithoutUndo();
                reflections.Tick(settings, 0.0f);
                Assert.IsNotNull(reflections.Probe);
                Assert.AreEqual(64, (int)reflections.Probe.settingsRaw.cubeResolution.@override);
                Assert.AreEqual(1, owner.transform.childCount);
                reflections.Dispose();
                Assert.IsNull(reflections.Probe);
                Assert.AreEqual(0, owner.transform.childCount);
                Assert.AreEqual(canonicalBefore, JsonUtility.ToJson(canonical));
            }
            finally
            {
                reflections?.Dispose();
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(settings);
            }
        }

        [TestCase(-1.0f, 5.0f)]
        [TestCase(1.0f, 0.0f)]
        [TestCase(1.0f, 4.9f)]
        [TestCase(float.NaN, 5.0f)]
        [TestCase(1.0f, float.PositiveInfinity)]
        public void InvalidReflectionIntensityAndUnboundedCaptureFrequencyAreRejected(float intensity, float refreshSeconds)
        {
            PrecipitationConfig settings = Object.Instantiate(GameConfig.Load().Precipitation);
            try
            {
                var data = new SerializedObject(settings);
                data.FindProperty("_reflectionIntensity").floatValue = intensity;
                data.FindProperty("_reflectionRefreshSeconds").floatValue = refreshSeconds;
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.IsFalse(settings.TryValidate(out string diagnostic));
                StringAssert.Contains("reflection", diagnostic);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }
    }
}

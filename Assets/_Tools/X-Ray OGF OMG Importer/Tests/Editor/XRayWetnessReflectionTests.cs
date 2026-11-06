using NUnit.Framework;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class XRayWetnessReflectionTests
    {
        [Test]
        public void NativeTexturesRemainValidUntilTheirProbeOwnerIsDestroyed()
        {
            var owner = new GameObject("Deferred native reflection texture lifetime regression");
            var color = new RenderTexture(8, 8, 0);
            var depth = new RenderTexture(8, 8, 16, RenderTextureFormat.Depth);
            color.Create();
            depth.Create();
            RTHandle colorHandle = RTHandles.Alloc(color, transferOwnership: true);
            RTHandle depthHandle = RTHandles.Alloc(depth, transferOwnership: true);
            try
            {
                owner.AddComponent<XRayWetnessReflectionRelease>().Initialize(colorHandle, depthHandle);
                owner.SetActive(false);
                Assert.IsTrue(color.IsCreated());
                Assert.IsTrue(depth.IsCreated());
                Assert.AreSame(color, colorHandle.rt);
                Assert.AreSame(depth, depthHandle.rt);
                Object.DestroyImmediate(owner);
                Assert.IsTrue(color == null || !color.IsCreated());
                Assert.IsTrue(depth == null || !depth.IsCreated());
            }
            finally
            {
                if (owner != null) Object.DestroyImmediate(owner);
                colorHandle.Release();
                depthHandle.Release();
                if (color != null) Object.DestroyImmediate(color);
                if (depth != null) Object.DestroyImmediate(depth);
            }
        }

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
                Assert.IsTrue(first.settingsRaw.proxySettings.useInfluenceVolumeAsProxyVolume);
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

        [Test]
        public void LocalProbePoolReusesOverlappingGridCellsAndNeverExceedsFourOwnedProbes()
        {
            PrecipitationConfig settings = Object.Instantiate(GameConfig.Load().Precipitation);
            Scene scene = EditorSceneManager.NewPreviewScene();
            XRayWetnessReflection reflections = null;
            try
            {
                var owner = new GameObject("Local wet reflection pool regression");
                SceneManager.MoveGameObjectToScene(owner, scene);
                reflections = new XRayWetnessReflection(owner);
                reflections.Tick(settings, 0.0f);
                MethodInfo prepare = typeof(XRayWetnessReflection).GetMethod("EnsureLocalSlots", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(prepare);
                prepare.Invoke(reflections, new object[] { Vector2Int.zero });
                Assert.AreEqual(4, reflections.ProbeCount);
                Assert.AreEqual(4, owner.transform.childCount);
                var probes = new HashSet<HDAdditionalReflectionData>();
                for (int i = 0; i < reflections.ProbeCount; i++) probes.Add(reflections.GetProbe(i));
                for (int cell = -10; cell <= 10; cell++)
                {
                    prepare.Invoke(reflections, new object[] { new Vector2Int(cell, cell) });
                    Assert.AreEqual(4, reflections.ProbeCount);
                    Assert.AreEqual(4, owner.transform.childCount);
                    for (int i = 0; i < reflections.ProbeCount; i++) Assert.IsTrue(probes.Contains(reflections.GetProbe(i)));
                }
                reflections.Weight = 0.0f;
                reflections.Tick(settings, 20.0f);
                for (int i = 0; i < reflections.ProbeCount; i++) Assert.AreEqual(0.0f, reflections.GetProbe(i).weight);
                Assert.AreEqual(0, reflections.CaptureRequests);
                reflections.Dispose();
                Assert.AreEqual(0, owner.transform.childCount);
                Assert.AreEqual(0, reflections.ProbeCount);
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

        [Test]
        public void ProbeBlendUsesUnscaledClockWhenTheWeatherSimulationPublishesZeroDelta()
        {
            PrecipitationConfig settings = Object.Instantiate(GameConfig.Load().Precipitation);
            string before = JsonUtility.ToJson(settings);
            Scene scene = EditorSceneManager.NewPreviewScene();
            XRayWetnessReflection reflections = null;
            try
            {
                var owner = new GameObject("Paused wet reflection clock regression");
                SceneManager.MoveGameObjectToScene(owner, scene);
                reflections = new XRayWetnessReflection(owner);
                reflections.Tick(settings, 0.0f);
                var slots = (System.Array)typeof(XRayWetnessReflection).GetField("_slots", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(reflections);
                object slot = slots.GetValue(0);
                slot.GetType().GetProperty("Captured").SetValue(slot, true);
                typeof(XRayWetnessReflection).GetField("_lastClock", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(reflections, Time.realtimeSinceStartupAsDouble - 0.5);
                reflections.Tick(settings, 0.0f);
                Assert.GreaterOrEqual(reflections.Probe.weight, 0.45f);
                Assert.AreEqual(0, reflections.CaptureRequests);
                Assert.AreEqual(before, JsonUtility.ToJson(settings));
            }
            finally
            {
                reflections?.Dispose();
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void PendingCaptureRemainsVisibleToHdrpAfterItsContributionIsReenabled()
        {
            PrecipitationConfig settings = Object.Instantiate(GameConfig.Load().Precipitation);
            Scene scene = EditorSceneManager.NewPreviewScene();
            XRayWetnessReflection reflections = null;
            try
            {
                var owner = new GameObject("Pending reflection visibility regression");
                SceneManager.MoveGameObjectToScene(owner, scene);
                reflections = new XRayWetnessReflection(owner);
                reflections.Tick(settings, 0.0f);
                var slots = (System.Array)typeof(XRayWetnessReflection).GetField("_slots", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(reflections);
                typeof(XRayWetnessReflection).GetField("_rendering", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(reflections, slots.GetValue(0));
                reflections.Weight = 0.0f;
                Assert.AreEqual(0.0f, reflections.Probe.weight);
                reflections.Weight = 1.0f;
                Assert.Greater(reflections.Probe.weight, 0.0f);
                Assert.Less(reflections.Probe.weight, 0.001f);
                reflections.Tick(settings, 0.0f);
                Assert.Greater(reflections.Probe.weight, 0.0f);
            }
            finally
            {
                reflections?.Dispose();
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(settings);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PendingCaptureIsDiscardedAfterTimeoutOrCameraTeleport(bool teleport)
        {
            PrecipitationConfig settings = Object.Instantiate(GameConfig.Load().Precipitation);
            Scene scene = EditorSceneManager.NewPreviewScene();
            XRayWetnessReflection reflections = null;
            try
            {
                var owner = new GameObject("Interrupted reflection capture regression");
                var cameraObject = new GameObject("Interrupted capture camera");
                SceneManager.MoveGameObjectToScene(owner, scene);
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.transform.position = teleport ? new Vector3(256.0f, 2.0f, 256.0f) : new Vector3(2.0f, 2.0f, 2.0f);
                reflections = new XRayWetnessReflection(owner);
                reflections.Tick(settings, 0.0f);
                typeof(XRayWetnessReflection).GetMethod("EnsureLocalSlots", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(reflections, new object[] { Vector2Int.zero });
                var slots = (System.Array)typeof(XRayWetnessReflection).GetField("_slots", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(reflections);
                object slot = slots.GetValue(0);
                slot.GetType().GetProperty("CaptureStarted").SetValue(slot, Time.realtimeSinceStartupAsDouble - (teleport ? 0.0 : 11.0));
                typeof(XRayWetnessReflection).GetField("_rendering", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(reflections, slot);
                HDAdditionalReflectionData interrupted = reflections.Probe;
                typeof(XRayWetnessReflection).GetMethod("BeginCameraRendering", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(reflections, new object[] { default(UnityEngine.Rendering.ScriptableRenderContext), camera });
                Assert.AreEqual(1, reflections.CaptureRestarts);
                Assert.AreEqual(4, reflections.ProbeCount);
                Assert.AreEqual(4, owner.transform.childCount);
                Assert.IsTrue(interrupted == null);
                Assert.IsNotNull(reflections.Probe);
                Assert.AreEqual(0, reflections.CaptureRequests);
                Assert.IsNull(typeof(XRayWetnessReflection).GetField("_rendering", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(reflections));
            }
            finally
            {
                reflections?.Dispose();
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(settings);
            }
        }


        [TestCase(-23.45655f, -24.34594f, -23.00655f, true, -23.45655f)]
        [TestCase(4.0f, 0.0f, 1.0f, true, 0.0f)]
        [TestCase(4.0f, 2.0f, 1.0f, false, 0.0f)]
        public void PlanarGroundSelectionUsesTheHighestSurfaceBelowTheActualViewer(float top, float bottom, float viewer, bool expectedValid, float expectedGround)
        {
            bool valid = XRayWetnessReflection.TrySelectPlanarGround(new Vector2(top, bottom), viewer, out float ground);
            Assert.AreEqual(expectedValid, valid);
            Assert.That(ground, Is.EqualTo(expectedGround).Within(0.00001f));
        }

        [TestCase(0)]
        [TestCase(64)]
        [TestCase(1024)]
        public void UnsupportedPlanarReflectionResolutionsAreRejected(int resolution)
        {
            PrecipitationConfig settings = Object.Instantiate(GameConfig.Load().Precipitation);
            try
            {
                var data = new SerializedObject(settings);
                data.FindProperty("_planarReflectionResolution").intValue = resolution;
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.IsFalse(settings.TryValidate(out string diagnostic));
                StringAssert.Contains("Planar", diagnostic);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void NativePlanarSupplementKeepsTheMirrorHorizontalAndAllOptOutsExplicit()
        {
            PrecipitationConfig settings = Object.Instantiate(GameConfig.Load().Precipitation);
            string canonicalBefore = JsonUtility.ToJson(GameConfig.Load().Precipitation);
            Scene scene = EditorSceneManager.NewPreviewScene();
            XRayWetnessReflection reflections = null;
            try
            {
                var owner = new GameObject("Local planar reflection configuration regression");
                SceneManager.MoveGameObjectToScene(owner, scene);
                var configurationData = new SerializedObject(settings);
                configurationData.FindProperty("_wetnessEnabled").boolValue = true;
                configurationData.FindProperty("_puddlesEnabled").boolValue = true;
                configurationData.FindProperty("_nonCameraReflectionsEnabled").boolValue = true;
                configurationData.FindProperty("_planarReflectionsEnabled").boolValue = true;
                configurationData.FindProperty("_planarReflectionResolution").intValue = 256;
                configurationData.ApplyModifiedPropertiesWithoutUndo();
                reflections = new XRayWetnessReflection(owner);
                reflections.Tick(settings, 0.0f);
                Assert.IsNull(reflections.PlanarProbe);
                typeof(XRayWetnessReflection).GetMethod("CreatePlanar", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(reflections, null);
                PlanarReflectionProbe planar = reflections.PlanarProbe;
                Assert.IsNotNull(planar);
                Assert.AreEqual(ProbeSettings.Mode.Realtime, planar.mode);
                Assert.AreEqual(ProbeSettings.RealtimeMode.EveryFrame, planar.realtimeMode);
                Assert.AreEqual(256, (int)planar.settingsRaw.resolutionScalable.@override);
                Assert.Greater(Vector3.Dot(planar.settingsRaw.proxySettings.mirrorRotationProxySpace * Vector3.forward, Vector3.up), 0.999f);
                Assert.AreEqual(Quaternion.identity, planar.transform.rotation);
                Assert.AreEqual(new Vector3(16.0f, 1.0f, 16.0f), planar.influenceVolume.boxSize);
                Assert.Greater(planar.importance, reflections.Probe.importance);
                reflections.Weight = 0.0f;
                Assert.AreEqual(0.0f, planar.weight);
                reflections.Weight = 1.0f;
                reflections.PlanarWeight = 0.0f;
                Assert.AreEqual(0.0f, planar.weight);
                reflections.PlanarWeight = 1.0f;
                Assert.AreEqual(1.0f, planar.weight);
                var data = new SerializedObject(settings);
                data.FindProperty("_planarReflectionsEnabled").boolValue = false;
                data.ApplyModifiedPropertiesWithoutUndo();
                reflections.Tick(settings, 0.0f);
                Assert.IsNull(reflections.PlanarProbe);
                Assert.IsTrue(planar == null);
                Assert.IsNotNull(reflections.Probe);
                Assert.AreEqual(1, owner.transform.childCount);
                Assert.AreEqual(canonicalBefore, JsonUtility.ToJson(GameConfig.Load().Precipitation));
            }
            finally
            {
                reflections?.Dispose();
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(settings);
            }
        }

    }
}

using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayInstanceNativeContextTests
    {
        [Test]
        public void EmptyPreviewContextSupportsCameraAndRejectsMissingCamera()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                Camera camera = CreateCamera(scene);
                var context = new XRayInstanceNativeContext();
                Assert.That(context.Supports(null), Is.False);
                Assert.That(context.Supports(camera), Is.True);
                Assert.That(context.Supports(camera), Is.True);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void PlanarProbeViewsDoNotReplaceTheSingleGameContextOwner()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                Camera camera = CreateCamera(scene);
                var context = new XRayInstanceNativeContext();
                Assert.That(context.Supports(camera), Is.True);
                var probeObject = new GameObject("Native context planar probe");
                SceneManager.MoveGameObjectToScene(probeObject, scene);
                var probe = probeObject.AddComponent<PlanarReflectionProbe>();
                Assert.That(context.Supports(camera), Is.True);
                probe.enabled = false;
                Assert.That(context.Supports(camera), Is.True);
                probe.enabled = true;
                Assert.That(context.Supports(camera), Is.True);
                Object.DestroyImmediate(probeObject);
                Assert.That(context.Supports(camera), Is.True);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void BuiltinRealtimeProbeViewsKeepTheGameCameraContract()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                Camera camera = CreateCamera(scene);
                var context = new XRayInstanceNativeContext();
                var probeObject = new GameObject("Native context builtin probe");
                SceneManager.MoveGameObjectToScene(probeObject, scene);
                var probe = probeObject.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Realtime;
                Assert.That(probe.GetComponent<HDAdditionalReflectionData>(), Is.Null);
                Assert.That(context.Supports(camera), Is.True);
                probe.enabled = false;
                Assert.That(context.Supports(camera), Is.True);
                probe.enabled = true;
                probe.mode = ReflectionProbeMode.Baked;
                Assert.That(context.Supports(camera), Is.True);
                probe.mode = ReflectionProbeMode.Realtime;
                Assert.That(context.Supports(camera), Is.True);
                Object.DestroyImmediate(probeObject);
                Assert.That(context.Supports(camera), Is.True);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [TestCase(CameraType.Reflection)]
        [TestCase(CameraType.Preview)]
        [TestCase(CameraType.SceneView)]
        public void AuxiliaryViewsCannotBecomeTheGameContextOwner(CameraType type)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                Camera camera = CreateCamera(scene);
                typeof(Camera).GetProperty(nameof(Camera.cameraType)).SetValue(camera, type);
                var context = new XRayInstanceNativeContext();
                Assert.That(context.Supports(camera), Is.False);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static Camera CreateCamera(Scene scene)
        {
            var root = new GameObject("Native context camera");
            SceneManager.MoveGameObjectToScene(root, scene);
            var camera = root.AddComponent<Camera>();
            camera.enabled = false;
            camera.scene = scene;
            return camera;
        }
    }
}

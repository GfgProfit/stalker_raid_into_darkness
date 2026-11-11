using System.Reflection;
using NUnit.Framework;
using RaidIntoDarkness.Weapons.Optics;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace RaidIntoDarkness.Weapons.Tests
{
    public sealed class ScopeOpticsRendererTests
    {
        private GameObject _root;
        private ScopeOpticsRenderer _renderer;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("ScopeOpticsRendererTests");
            _root.SetActive(false);
            _root.hideFlags = HideFlags.HideAndDontSave;
            _renderer = _root.AddComponent<ScopeOpticsRenderer>();
        }

        [TearDown]
        public void TearDown()
        {
            _renderer.ReleaseResources();
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void MissingConfigurationAndCameraProvideADiagnosticWithoutCreatingResources()
        {
            Assert.That(_renderer.TryValidate(out string diagnostic), Is.False);
            Assert.That(diagnostic, Is.Not.Null.And.Not.Empty);
            Assert.That(_renderer.Target, Is.Null);
            Assert.That(_renderer.IsRendering, Is.False);
            Assert.That(_root.GetComponentsInChildren<Camera>(true), Is.Empty);
        }

        [Test]
        public void EditModeLifecycleDoesNotCreateCamerasOrRenderTargets()
        {
            Assert.That(Application.isPlaying, Is.False);
            Invoke(_renderer, "OnEnable");
            Invoke(_renderer, "LateUpdate");
            Assert.That(_renderer.Target, Is.Null);
            Assert.That(_renderer.IsRendering, Is.False);
            Assert.That(_root.GetComponentsInChildren<Camera>(true), Is.Empty);
        }

        [Test]
        public void RepeatedReleaseAndDisableAreSafeWithoutInitializedDependencies()
        {
            Assert.DoesNotThrow(_renderer.ReleaseResources);
            Assert.DoesNotThrow(_renderer.ReleaseResources);
            Assert.DoesNotThrow(() => Invoke(_renderer, "OnDisable"));
            Assert.That(_renderer.Target, Is.Null);
            Assert.That(_renderer.IsRendering, Is.False);
        }

        [Test]
        public void ReleaseDestroysOwnedUncreatedTargetAndIsIdempotent()
        {
            var target = new RenderTexture(16, 16, 0);
            target.hideFlags = HideFlags.HideAndDontSave;
            SetField(_renderer, "_target", target);
            try
            {
                Assert.That(target.IsCreated(), Is.False);
                _renderer.ReleaseResources();
                Assert.That(_renderer.Target, Is.Null);
                Assert.That(target == null, Is.True);
                Assert.DoesNotThrow(_renderer.ReleaseResources);
            }
            finally
            {
                if (target != null)
                {
                    Object.DestroyImmediate(target);
                }
            }
        }

        [Test]
        public void ReleaseRestoresAssignedCameraAndPreservesItsExistingTarget()
        {
            Camera camera = _root.AddComponent<Camera>();
            HDAdditionalCameraData data = _root.AddComponent<HDAdditionalCameraData>();
            var originalTarget = new RenderTexture(16, 16, 0);
            originalTarget.hideFlags = HideFlags.HideAndDontSave;
            camera.targetTexture = originalTarget;
            camera.fieldOfView = 57f;
            camera.depth = 8f;
            camera.transform.localPosition = new Vector3(2f, 3f, 4f);
            camera.transform.localRotation = Quaternion.Euler(3f, 5f, 7f);
            data.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            SetField(_renderer, "_scopeCamera", camera);
            SetField(_renderer, "_scopeCameraData", data);
            try
            {
                Invoke(_renderer, "TakeCameraControl");
                camera.targetTexture = null;
                camera.fieldOfView = 5f;
                camera.depth = -2f;
                camera.transform.localPosition = Vector3.zero;
                camera.transform.localRotation = Quaternion.identity;
                data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                _renderer.ReleaseResources();
                Assert.That(camera, Is.Not.Null);
                Assert.That(camera.targetTexture, Is.SameAs(originalTarget));
                Assert.That(originalTarget == null, Is.False);
                Assert.That(camera.fieldOfView, Is.EqualTo(57f));
                Assert.That(camera.depth, Is.EqualTo(8f));
                Assert.That(camera.transform.localPosition, Is.EqualTo(new Vector3(2f, 3f, 4f)));
                Assert.That(Quaternion.Angle(camera.transform.localRotation, Quaternion.Euler(3f, 5f, 7f)), Is.LessThan(0.001f));
                Assert.That(data.antialiasing, Is.EqualTo(HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing));
            }
            finally
            {
                camera.targetTexture = null;
                Object.DestroyImmediate(originalTarget);
            }
        }

        private static void SetField(object instance, string name, object value)
        {
            FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(instance, value);
        }

        private static void Invoke(object instance, string name)
        {
            MethodInfo method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name);
            method.Invoke(instance, null);
        }
    }
}

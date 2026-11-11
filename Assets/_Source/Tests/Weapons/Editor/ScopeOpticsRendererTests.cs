using System.Reflection;
using NUnit.Framework;
using RaidIntoDarkness.Weapons.Optics;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
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

        [TestCase(false)]
        [TestCase(true)]
        public void StoppingCaptureRestoresTracerExposureGlobals(bool releaseResources)
        {
            int enabled = Shader.PropertyToID("_ScopeTracerExposureEnabled");
            int texture = Shader.PropertyToID("_ScopeTracerViewerExposure");
            float previousEnabled = Shader.GetGlobalFloat(enabled);
            Texture previousTexture = Shader.GetGlobalTexture(texture);
            try
            {
                Shader.SetGlobalFloat(enabled, 1f);
                Shader.SetGlobalTexture(texture, Texture2D.blackTexture);
                SetField(_renderer, "_previousTracerExposureEnabled", 0.25f);
                SetField(_renderer, "_previousTracerExposureTexture", Texture2D.whiteTexture);
                SetField(_renderer, "_tracerExposureBound", true);
                if (releaseResources)
                {
                    _renderer.ReleaseResources();
                }
                else
                {
                    Invoke(_renderer, "SuspendCapture");
                }

                Assert.That(Shader.GetGlobalFloat(enabled), Is.EqualTo(0.25f));
                Assert.That(Shader.GetGlobalTexture(texture), Is.SameAs(Texture2D.whiteTexture));
                Assert.That(_renderer.Target, Is.Null);
                _renderer.ReleaseResources();
                Assert.That(Shader.GetGlobalFloat(enabled), Is.EqualTo(0.25f));
            }
            finally
            {
                Shader.SetGlobalFloat(enabled, previousEnabled);
                Shader.SetGlobalTexture(texture, previousTexture != null ? previousTexture : Texture2D.whiteTexture);
            }
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
        public void LocationEnvironmentSwitchesWithoutChangingSharedMaterialAndReleaseRestoresTheBlock()
        {
            Shader shader = Shader.Find("RaidIntoDarkness/Optics/ScopeProLens");
            Assert.That(shader, Is.Not.Null);
            Scene originalScene = SceneManager.GetActiveScene();
            Scene firstScene = SceneManager.CreateScene("ScopeEnvironmentFirst_" + _root.GetEntityId());
            Scene secondScene = SceneManager.CreateScene("ScopeEnvironmentSecond_" + _root.GetEntityId());
            var first = new Cubemap(1, TextureFormat.RGBAHalf, false);
            var second = new Cubemap(1, TextureFormat.RGBAHalf, false);
            var material = new Material(shader);
            material.SetTexture("_LensEnvironment", first);
            material.SetFloat("_LensUseEnvironment", 1f);
            material.SetFloat("_LensReflectionStrength", 0.37f);
            material.SetFloat("_LensEnvironmentIntensity", 2.5f);
            var lens = _root.AddComponent<MeshRenderer>();
            lens.sharedMaterial = material;
            lens.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            var block = new MaterialPropertyBlock();
            block.SetFloat("_LensUseEnvironment", 0.25f);
            block.SetTexture("_LensEnvironment", first);
            lens.SetPropertyBlock(block, 0);
            SetField(_renderer, "_lensRenderer", lens);
            try
            {
                var firstObject = new GameObject("FirstEnvironment");
                SceneManager.MoveGameObjectToScene(firstObject, firstScene);
                ScopeLensEnvironment firstEnvironment = firstObject.AddComponent<ScopeLensEnvironment>();
                SetField(firstEnvironment, "_cubemap", first);
                SetField(firstEnvironment, "_useBakedEnvironment", true);
                var secondObject = new GameObject("SecondEnvironment");
                SceneManager.MoveGameObjectToScene(secondObject, secondScene);
                ScopeLensEnvironment secondEnvironment = secondObject.AddComponent<ScopeLensEnvironment>();
                SetField(secondEnvironment, "_cubemap", second);
                SetField(secondEnvironment, "_useBakedEnvironment", true);
                SceneManager.SetActiveScene(firstScene);
                _renderer.UpdateLensEnvironment();
                lens.GetPropertyBlock(block, 0);
                Assert.That(block.GetTexture("_LensEnvironment"), Is.SameAs(first));
                Assert.That(block.GetFloat("_LensUseEnvironment"), Is.EqualTo(1f));
                Assert.That(lens.motionVectorGenerationMode, Is.EqualTo(MotionVectorGenerationMode.ForceNoMotion));
                SceneManager.SetActiveScene(secondScene);
                _renderer.UpdateLensEnvironment();
                lens.GetPropertyBlock(block, 0);
                Assert.That(block.GetTexture("_LensEnvironment"), Is.SameAs(second));
                Assert.That(material.GetTexture("_LensEnvironment"), Is.SameAs(first));
                Assert.That(material.GetFloat("_LensReflectionStrength"), Is.EqualTo(0.37f));
                Assert.That(material.GetFloat("_LensEnvironmentIntensity"), Is.EqualTo(2.5f));
                SetField(secondEnvironment, "_useBakedEnvironment", false);
                _renderer.UpdateLensEnvironment();
                lens.GetPropertyBlock(block, 0);
                Assert.That(block.GetFloat("_LensUseEnvironment"), Is.Zero);
                SetField(secondEnvironment, "_useBakedEnvironment", true);
                SetField(secondEnvironment, "_cubemap", null);
                _renderer.UpdateLensEnvironment();
                lens.GetPropertyBlock(block, 0);
                Assert.That(block.GetFloat("_LensUseEnvironment"), Is.Zero);
                SceneManager.SetActiveScene(firstScene);
                firstEnvironment.enabled = false;
                _renderer.UpdateLensEnvironment();
                lens.GetPropertyBlock(block, 0);
                Assert.That(block.GetFloat("_LensUseEnvironment"), Is.Zero);
                _renderer.ReleaseResources();
                lens.GetPropertyBlock(block, 0);
                Assert.That(block.GetFloat("_LensUseEnvironment"), Is.EqualTo(0.25f));
                Assert.That(lens.motionVectorGenerationMode, Is.EqualTo(MotionVectorGenerationMode.Camera));
                Assert.That(block.GetTexture("_LensEnvironment"), Is.SameAs(first));
                Assert.That(_renderer.Target, Is.Null);
                Assert.That(_root.GetComponentsInChildren<Camera>(true), Is.Empty);
            }
            finally
            {
                SceneManager.SetActiveScene(originalScene);
                EditorSceneManager.CloseScene(firstScene, true);
                EditorSceneManager.CloseScene(secondScene, true);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void SdfAvailabilityTracksTextureAssignmentsAndReleaseRestoresTheOriginalBlock()
        {
            Shader shader = Shader.Find("RaidIntoDarkness/Optics/ScopeProLens");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            var sdf = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var configuration = ScriptableObject.CreateInstance<ScopeOpticsConfig>();
            var lens = _root.AddComponent<MeshRenderer>();
            lens.sharedMaterial = material;
            int available = Shader.PropertyToID("_ReticleSdfAvailable");
            var block = new MaterialPropertyBlock();
            block.SetFloat(available, 0.25f);
            lens.SetPropertyBlock(block, 0);
            SetField(_renderer, "_configuration", configuration);
            SetField(_renderer, "_lensRenderer", lens);
            SetField(_renderer, "_lensMaterial", material);
            SetField(_renderer, "_effectiveLensHalfSizeMeters", new Vector2(0.014f, 0.014f));
            SetField(_renderer, "_eyeLocal", new Vector3(0f, 0f, -0.09f));
            _renderer.SetMagnification(1f, true);
            try
            {
                Assert.That(material.GetFloat("_ReticleMode"), Is.EqualTo(0f));
                Invoke(_renderer, "BindSurface", true);
                lens.GetPropertyBlock(block, 0);
                Assert.That(block.GetFloat(available), Is.EqualTo(0f));
                material.SetTexture("_ReticleSdf", sdf);
                Invoke(_renderer, "BindSurface", true);
                lens.GetPropertyBlock(block, 0);
                Assert.That(block.GetFloat(available), Is.EqualTo(1f));
                material.SetTexture("_ReticleSdf", null);
                Invoke(_renderer, "BindSurface", true);
                lens.GetPropertyBlock(block, 0);
                Assert.That(block.GetFloat(available), Is.EqualTo(0f));
                _renderer.ReleaseResources();
                lens.GetPropertyBlock(block, 0);
                Assert.That(block.GetFloat(available), Is.EqualTo(0.25f));
            }
            finally
            {
                Object.DestroyImmediate(configuration);
                Object.DestroyImmediate(sdf);
                Object.DestroyImmediate(material);
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

        private static void Invoke(object instance, string name, params object[] arguments)
        {
            MethodInfo method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name);
            method.Invoke(instance, arguments);
        }
    }
}

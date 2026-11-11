using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RaidIntoDarkness.Weapons.Optics;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RaidIntoDarkness.Weapons.Tests
{
    public sealed class ScopeMarchBindingTests
    {
        private Scene _preview;
        private GameObject _root;
        private GameObject _hands;
        private GameObject _model;
        private GameObject _scope;
        private FirstPersonWeaponController _controller;
        private ScopeOpticsConfig _configuration;
        private ScopeOpticsRenderer _renderer;
        private ScopeOpticsWeaponBinding _binding;
        private Camera _viewer;
        private Camera _otherCamera;
        private MeshRenderer _visibleRenderer;
        private MeshRenderer _hiddenRenderer;

        [SetUp]
        public void SetUp()
        {
            _preview = EditorSceneManager.NewPreviewScene();
            _root = new GameObject("ScopeMarchBindingTests");
            _root.hideFlags = HideFlags.HideAndDontSave;
            _root.SetActive(false);
            SceneManager.MoveGameObjectToScene(_root, _preview);
            _controller = _root.AddComponent<FirstPersonWeaponController>();
            _hands = CreateChild(_root, "Hands");
            _model = CreateChild(_hands, "Weapon");
            _scope = CreateChild(_model, "March");
            _viewer = CreateChild(_root, "Viewer").AddComponent<Camera>();
            _viewer.enabled = false;
            _otherCamera = CreateChild(_root, "OtherCamera").AddComponent<Camera>();
            _otherCamera.enabled = false;
            _visibleRenderer = CreateChild(_hands, "VisibleHands").AddComponent<MeshRenderer>();
            _hiddenRenderer = CreateChild(_hands, "AlreadyHiddenHands").AddComponent<MeshRenderer>();
            _hiddenRenderer.forceRenderingOff = true;
            _configuration = ScriptableObject.CreateInstance<ScopeOpticsConfig>();
            _configuration.hideFlags = HideFlags.HideAndDontSave;
            SetField(_configuration, "_minMagnification", 1f);
            SetField(_configuration, "_maxMagnification", 8f);
            SetField(_configuration, "_calibrationMagnification", 8f);
            _renderer = _scope.AddComponent<ScopeOpticsRenderer>();
            _renderer.enabled = false;
            SetField(_renderer, "_configuration", _configuration);
            _binding = _scope.AddComponent<ScopeOpticsWeaponBinding>();
            SetField(_binding, "_renderer", _renderer);
            SetField(_controller, "_model", _model);
            SetField(_controller, "_boundHands", _hands);
            GetBindings().Add(_binding);
            _root.SetActive(true);
        }

        [Test]
        public void AimHudFieldOfViewRequiresAnOwnedScopeAndExplicitOverride()
        {
            _binding.Bind(_controller, _viewer);
            Assert.That(_binding.AimHudFieldOfView, Is.Zero);
            SetField(_configuration, "_overrideAimHudFieldOfView", true);
            SetField(_configuration, "_aimHudFieldOfView", 0.6f);
            Assert.That(_binding.AimHudFieldOfView, Is.EqualTo(0.6f));
            _binding.Bind(null, null);
            Assert.That(_binding.AimHudFieldOfView, Is.Zero);
        }

        [Test]
        public void DevTestReleasesItsAimOverrideWhenDisabled()
        {
            DevTest test = _root.AddComponent<DevTest>();
            Invoke(test, "OnEnable");
            SetField(test, "_inZoom", true);
            Invoke(test, "Update");
            Assert.That(_controller.DevelopmentAimOverride, Is.True);
            SetField(test, "_inZoom", false);
            Invoke(test, "Update");
            Assert.That(_controller.DevelopmentAimOverride, Is.False);
            SetField(test, "_inZoom", true);
            Invoke(test, "Update");
            Invoke(test, "OnDisable");
            Assert.That(_controller.DevelopmentAimOverride, Is.False);
        }

        [Test]
        public void ScrollOutsideAimingDoesNotChangeMagnification()
        {
            float initial = _binding.Magnification;
            _binding.ApplyMagnificationScroll(1.5f);
            Assert.That(_binding.Magnification, Is.EqualTo(initial));
        }

        [Test]
        public void AimingScrollUsesFractionalTargetsAndStopsOnRelease()
        {
            SetField(_configuration, "_maxMagnification", 8f);
            _binding.Bind(_controller, _viewer);
            _binding.SetMagnification(1f);
            SetField(_controller, "<State>k__BackingField", FirstPersonWeaponState.Ready);
            SetField(_controller, "_aiming", true);
            SetField(_controller, "_aim", 1f);
            _binding.ApplyMagnificationScroll(1.5f);
            Assert.That(_binding.Magnification, Is.EqualTo(Mathf.Pow(1.35f, 1.5f)).Within(0.00001f));
            Assert.That(_renderer.State.TargetMagnification, Is.EqualTo(Mathf.Pow(1.35f, 1.5f)).Within(0.00001f));
            SetField(_controller, "_aiming", false);
            _binding.ApplyMagnificationScroll(2f);
            Assert.That(_binding.Magnification, Is.EqualTo(Mathf.Pow(1.35f, 1.5f)).Within(0.00001f));
        }

        [Test]
        public void ScrollHasEqualRelativeSpeedAndReversesImmediatelyAtLimits()
        {
            _binding.Bind(_controller, _viewer);
            SetField(_controller, "<State>k__BackingField", FirstPersonWeaponState.Ready);
            SetField(_controller, "_aiming", true);
            SetField(_controller, "_aim", 1f);
            _binding.SetMagnification(1f);
            _binding.ApplyMagnificationScroll(1f);
            float lowRatio = _binding.Magnification;
            _binding.SetMagnification(5f);
            _binding.ApplyMagnificationScroll(1f);
            Assert.That(_binding.Magnification / 5f, Is.EqualTo(lowRatio).Within(0.00001f));
            _binding.ApplyMagnificationScroll(-1f);
            Assert.That(_binding.Magnification, Is.EqualTo(5f).Within(0.00001f));
            _binding.ApplyMagnificationScroll(1000f);
            Assert.That(_binding.Magnification, Is.EqualTo(8f).Within(0.00001f));
            _binding.ApplyMagnificationScroll(-1f);
            Assert.That(_binding.Magnification, Is.EqualTo(8f / 1.35f).Within(0.00001f));
            _binding.ApplyMagnificationScroll(-1000f);
            Assert.That(_binding.Magnification, Is.EqualTo(1f).Within(0.00001f));
            _binding.ApplyMagnificationScroll(1f);
            Assert.That(_binding.Magnification, Is.EqualTo(1.35f).Within(0.00001f));
        }
        [Test]
        public void AuthoredScrollSensitivityIsNotSilentlyCapped()
        {
            _binding.Bind(_controller, _viewer);
            SetField(_controller, "<State>k__BackingField", FirstPersonWeaponState.Ready);
            SetField(_controller, "_aiming", true);
            SetField(_controller, "_aim", 1f);
            SetField(_binding, "_magnificationPerScrollNotch", 1000f);
            _binding.SetMagnification(1f);
            _binding.ApplyMagnificationScroll(0.1f);
            Assert.That(_binding.Magnification, Is.EqualTo(Mathf.Pow(1001f, 0.1f)).Within(0.00001f));
            _binding.ApplyMagnificationScroll(1f);
            Assert.That(_binding.Magnification, Is.EqualTo(8f).Within(0.00001f));
            _binding.ApplyMagnificationScroll(-1f);
            Assert.That(_binding.Magnification, Is.EqualTo(1f).Within(0.00001f));
        }
        [TearDown]
        public void TearDown()
        {
            if (_binding != null)
            {
                _binding.Bind(null, null);
            }

            if (_controller != null)
            {
                SetField(_controller, "_model", null);
                SetField(_controller, "_boundHands", null);
                GetBindings().Clear();
            }

            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }

            if (_configuration != null)
            {
                Object.DestroyImmediate(_configuration);
            }

            if (_preview.IsValid())
            {
                EditorSceneManager.ClosePreviewScene(_preview);
            }
        }

        [Test]
        public void UnboundWorldCopyDoesNotEnableRenderer()
        {
            _binding.SetMagnification(4f);
            Invoke(_binding, "LateUpdate");
            Assert.That(_binding.UsesOpticalMagnification, Is.False);
            Assert.That(_renderer.enabled, Is.False);
            Assert.That(_renderer.State, Is.Null);
            Assert.That(_renderer.Target, Is.Null);
            Assert.That(_renderer.IsRendering, Is.False);
            Assert.That(_viewer.enabled, Is.False);
        }

        [Test]
        public void OnlyTheActiveOwnedHudScopeUsesOpticalMagnification()
        {
            _binding.Bind(_controller, _viewer);
            Assert.That(_renderer.enabled, Is.True);
            Assert.That(_binding.UsesOpticalMagnification, Is.True);
            _scope.SetActive(false);
            Assert.That(_binding.UsesOpticalMagnification, Is.False);
            Assert.That(_renderer.State.Active, Is.False);
        }

        [Test]
        public void ChangingTheOwnersModelInvalidatesTheOldScopeBinding()
        {
            _binding.Bind(_controller, _viewer);
            Assert.That(_binding.UsesOpticalMagnification, Is.True);
            SetField(_controller, "_model", CreateChild(_hands, "ReplacementWeapon"));
            Assert.That(_binding.UsesOpticalMagnification, Is.False);
        }

        [Test]
        public void UnbindingClearsViewerAndStopsTheAuthoredRenderer()
        {
            _binding.Bind(_controller, _viewer);
            _renderer.SetActive(true);
            _binding.Bind(null, null);
            Assert.That(_binding.UsesOpticalMagnification, Is.False);
            Assert.That(_renderer.enabled, Is.False);
            Assert.That(_renderer.State.Active, Is.False);
            Assert.That(GetField(_renderer, "_viewerCamera"), Is.Null);
            Assert.That(GetField(_renderer, "_excludedRoot"), Is.Null);
            Assert.That(_renderer.Target, Is.Null);
        }

        [Test]
        public void CaptureHidesTheOwnersHandsAndRestoresEachPreviousVisibilityFlag()
        {
            PrepareCapture();
            Invoke(_renderer, "BeginCameraRendering", default(ScriptableRenderContext), _viewer);
            Assert.That(_visibleRenderer.forceRenderingOff, Is.True);
            Assert.That(_hiddenRenderer.forceRenderingOff, Is.True);
            Invoke(_renderer, "EndCameraRendering", default(ScriptableRenderContext), _viewer);
            Assert.That(_visibleRenderer.forceRenderingOff, Is.False);
            Assert.That(_hiddenRenderer.forceRenderingOff, Is.True);
        }

        [Test]
        public void RepeatedCaptureBeginAndResourceReleasePreserveOriginalVisibility()
        {
            PrepareCapture();
            Invoke(_renderer, "BeginCameraRendering", default(ScriptableRenderContext), _viewer);
            Invoke(_renderer, "BeginCameraRendering", default(ScriptableRenderContext), _viewer);
            Assert.That(_visibleRenderer.forceRenderingOff, Is.True);
            _renderer.ReleaseResources();
            _renderer.ReleaseResources();
            Assert.That(_visibleRenderer.forceRenderingOff, Is.False);
            Assert.That(_hiddenRenderer.forceRenderingOff, Is.True);
        }

        [Test]
        public void AnotherCameraRestoresVisibilityIfCaptureEndWasInterrupted()
        {
            PrepareCapture();
            Invoke(_renderer, "BeginCameraRendering", default(ScriptableRenderContext), _viewer);
            Assert.That(_visibleRenderer.forceRenderingOff, Is.True);
            Invoke(_renderer, "BeginCameraRendering", default(ScriptableRenderContext), _otherCamera);
            Assert.That(_visibleRenderer.forceRenderingOff, Is.False);
            Assert.That(_hiddenRenderer.forceRenderingOff, Is.True);
        }

        [Test]
        public void DestroyedCachedBindingDoesNotThrow()
        {
            _binding.Bind(_controller, _viewer);
            Assert.DoesNotThrow(() => Object.DestroyImmediate(_scope));
            Assert.That(GetBindings()[0] == null, Is.True);
        }

        private void PrepareCapture()
        {
            _binding.Bind(_controller, _viewer);
            _renderer.SetActive(true);
            SetField(_renderer, "_controlledCamera", _viewer);
            _viewer.enabled = true;
        }

        private List<MonoBehaviour> GetBindings()
        {
            return (List<MonoBehaviour>)GetField(_controller, "_opticsBindings");
        }

        private static GameObject CreateChild(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.hideFlags = HideFlags.HideAndDontSave;
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private static object GetField(object instance, string name)
        {
            FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return field.GetValue(instance);
        }

        private static void SetField(object instance, string name, object value)
        {
            FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(instance, value);
        }

        private static object Invoke(object instance, string name, params object[] arguments)
        {
            MethodInfo method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name);
            return method.Invoke(instance, arguments);
        }
    }
}

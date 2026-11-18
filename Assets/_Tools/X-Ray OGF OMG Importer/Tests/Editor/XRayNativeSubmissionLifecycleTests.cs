using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayNativeSubmissionLifecycleTests
    {
        private static readonly MethodInfo BEGIN = typeof(XRayLevelInstancedRenderer).GetMethod("OnBeginContextRendering", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo END = typeof(XRayLevelInstancedRenderer).GetMethod("OnEndContextRendering", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo DRAW = typeof(XRayLevelInstancedRenderer).GetMethod("OnBeginCameraRendering", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo SHADOW_SELECTION = typeof(XRayInstanceNativeRenderer).GetMethod("PrepareShadowSources", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private readonly List<XRayInstanceNativeRenderer> _backends = new List<XRayInstanceNativeRenderer>();
        private Scene _scene;
        private GameObject _root;
        private Camera _camera;
        private XRayLevelInstancedRenderer _renderer;
        private Mesh _mesh;
        private Material _firstMaterial;
        private Material _secondMaterial;

        [SetUp]
        public void SetUp()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Native submission lifecycle requires a graphics device.");
            _scene = EditorSceneManager.NewPreviewScene();
            _root = new GameObject("Native frontend lifecycle fixture");
            SceneManager.MoveGameObjectToScene(_root, _scene);
            var cameraObject = new GameObject("Native frontend game camera");
            cameraObject.transform.SetParent(_root.transform);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.orthographic = true;
            _camera.orthographicSize = 100f;
            _camera.farClipPlane = 1000f;
            _camera.transform.position = new Vector3(0f, 2f, -8f);
            Assert.That(_camera.cameraType, Is.EqualTo(CameraType.Game));
            _renderer = _root.AddComponent<XRayLevelInstancedRenderer>();
            _renderer.ApplyConfiguration(Own(ScriptableObject.CreateInstance<GameConfig>()));
            _renderer.NativeSubmissionOverride = true;
            _mesh = Own(new Mesh { name = "Native frontend quad" });
            _mesh.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
            _mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            _mesh.tangents = new[] { new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f) };
            _mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            _mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _mesh.RecalculateBounds();
            _firstMaterial = CreateMaterial("Native frontend first");
            _secondMaterial = CreateMaterial("Native frontend second");
            _renderer.BatchSet = Set(_firstMaterial);
        }

        [TearDown]
        public void TearDown()
        {
            if (_renderer != null) EndContext();
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
            foreach (XRayInstanceNativeRenderer backend in _backends) backend.Dispose();
            _backends.Clear();
            for (int index = _owned.Count - 1; index >= 0; index--) UnityEngine.Object.DestroyImmediate(_owned[index]);
            _owned.Clear();
            if (_scene.IsValid() && _scene.isLoaded) EditorSceneManager.ClosePreviewScene(_scene);
            _root = null;
            _renderer = null;
        }

        [Test]
        public void MultipleCamerasSelectLegacyBeforeTheFirstCameraCull()
        {
            XRayInstanceNativeRenderer backend = ActivateNative();
            var otherObject = new GameObject("Native frontend second camera");
            otherObject.transform.SetParent(_root.transform);
            Camera other = otherObject.AddComponent<Camera>();
            other.enabled = false;
            BEGIN.Invoke(_renderer, new object[] { default(ScriptableRenderContext), new List<Camera> { _camera, other } });
            Assert.That(Field<bool>("_nativeContextActive"), Is.True);
            Assert.That(Field<bool>("_nativeContextAllowed"), Is.False);
            Assert.That(Field<Camera>("_nativeContextCamera"), Is.Null);
            Assert.That(backend.Active, Is.False);
            Draw(_camera);
            Assert.That(_renderer.NativeSubmissionActive, Is.False);
            Assert.That(_renderer.LastSubmittedCalls, Is.EqualTo(1));
            Assert.That(_renderer.LastSubmittedInstances, Is.EqualTo(1));
            Assert.That(backend.MemoryBytes, Is.GreaterThan(0));
        }

        [Test]
        public void ReflectionAndPreviewCallbacksKeepTheLiveNativeGroup()
        {
            XRayInstanceNativeRenderer backend = ActivateNative();
            long bytes = backend.MemoryBytes;
            GraphicsBuffer buffer = Buffer(backend);
            long legacyReflections = _renderer.ReflectionSubmittedBatches;
            var auxiliaryObject = new GameObject("Native frontend auxiliary camera");
            auxiliaryObject.transform.SetParent(_root.transform);
            Camera auxiliary = auxiliaryObject.AddComponent<Camera>();
            auxiliary.enabled = false;
            auxiliary.orthographic = true;
            auxiliary.orthographicSize = 100f;
            auxiliary.transform.position = _camera.transform.position;
            foreach (CameraType type in new[] { CameraType.Reflection, CameraType.Preview })
            {
                typeof(Camera).GetProperty(nameof(Camera.cameraType)).SetValue(auxiliary, type);
                Assert.That(auxiliary.cameraType, Is.EqualTo(type));
                Draw(auxiliary);
                Assert.That(Field<XRayInstanceNativeRenderer>("_nativeRenderer"), Is.SameAs(backend));
                Assert.That(backend.Active, Is.True);
                Assert.That(backend.MemoryBytes, Is.GreaterThanOrEqualTo(bytes));
                Assert.That(Buffer(backend), Is.SameAs(buffer));
                Assert.That(_renderer.ReflectionSubmittedBatches, Is.EqualTo(legacyReflections));
                Assert.That(backend.RegisteredReflectionViews, Is.EqualTo(1));
            }
            EndContext();
            Assert.That(backend.Active, Is.False);
        }

        [Test]
        public void ReflectionBeforeGamePreparationUsesLegacyAndLaterRegistersWithoutRebuilding()
        {
            var reflectionObject = new GameObject("Native frontend reflection order camera");
            reflectionObject.transform.SetParent(_root.transform);
            Camera reflection = reflectionObject.AddComponent<Camera>();
            reflection.enabled = false;
            reflection.orthographic = true;
            reflection.orthographicSize = 100f;
            reflection.farClipPlane = 1000f;
            reflection.transform.position = _camera.transform.position;
            typeof(Camera).GetProperty(nameof(Camera.cameraType)).SetValue(reflection, CameraType.Reflection);
            BeginEligibleContext();
            Draw(reflection);
            Assert.That(_renderer.NativeSubmissionActive, Is.False);
            Assert.That(_renderer.ReflectionSubmittedBatches, Is.EqualTo(1));
            Draw(_camera);
            Assert.That(_renderer.NativeSubmissionActive, Is.True);
            XRayInstanceNativeRenderer backend = Field<XRayInstanceNativeRenderer>("_nativeRenderer");
            _backends.Add(backend);
            GraphicsBuffer buffer = Buffer(backend);
            Draw(reflection);
            Assert.That(backend.RegisteredReflectionViews, Is.EqualTo(1));
            Assert.That(_renderer.ReflectionSubmittedBatches, Is.EqualTo(1));
            Assert.That(Buffer(backend), Is.SameAs(buffer));
            EndContext();
            BeginEligibleContext();
            Assert.That(backend.RegisteredReflectionViews, Is.Zero);
            Draw(reflection);
            Assert.That(_renderer.ReflectionSubmittedBatches, Is.EqualTo(2));
            Draw(_camera);
            Assert.That(Buffer(backend), Is.SameAs(buffer));
            Draw(reflection);
            Assert.That(backend.RegisteredReflectionViews, Is.EqualTo(1));
            Assert.That(_renderer.ReflectionSubmittedBatches, Is.EqualTo(2));
        }

        [Test]
        public void ResolverChangedAfterGamePreparationKeepsOriginalReflectionResolution()
        {
            XRayInstanceNativeRenderer backend = ActivateNative();
            GraphicsBuffer buffer = Buffer(backend);
            var reflectionObject = new GameObject("Native frontend resolver reflection camera");
            reflectionObject.transform.SetParent(_root.transform);
            Camera reflection = reflectionObject.AddComponent<Camera>();
            reflection.enabled = false;
            reflection.orthographic = true;
            reflection.orthographicSize = 100f;
            reflection.farClipPlane = 1000f;
            reflection.transform.position = _camera.transform.position;
            typeof(Camera).GetProperty(nameof(Camera.cameraType)).SetValue(reflection, CameraType.Reflection);
            int calls = 0;
            _renderer.MaterialResolver = material =>
            {
                calls++;
                return material;
            };
            Draw(reflection);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(_renderer.ReflectionSubmittedBatches, Is.EqualTo(1));
            Assert.That(backend.RegisteredReflectionViews, Is.Zero);
            Assert.That(backend.Active, Is.True);
            Assert.That(Buffer(backend), Is.SameAs(buffer));
        }

        [TestCase("resolver")]
        [TestCase("castShadows")]
        [TestCase("layer")]
        public void ChangedReflectionContractUnregistersPreviouslyAcceptedCamera(string change)
        {
            MethodInfo assign = typeof(XRayLevelInstancedRenderer).GetMethod("SetOrderIndependentMaterialResolver", BindingFlags.Instance | BindingFlags.NonPublic);
            int calls = 0;
            Func<Material, Material> firstResolver = material =>
            {
                calls++;
                return _firstMaterial;
            };
            assign.Invoke(_renderer, new object[] { firstResolver });
            XRayInstanceNativeRenderer backend = ActivateNative();
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(_renderer.LastSubmittedCalls, Is.Zero);
            Assert.That(_renderer.LastSubmittedInstances, Is.Zero);
            Assert.That(_renderer.LastShadowOnlyBatches, Is.Zero);
            GraphicsBuffer buffer = Buffer(backend);
            var reflectionObject = new GameObject("Native frontend accepted contract camera");
            reflectionObject.transform.SetParent(_root.transform);
            Camera reflection = reflectionObject.AddComponent<Camera>();
            reflection.enabled = false;
            reflection.orthographic = true;
            reflection.orthographicSize = 100f;
            reflection.farClipPlane = 1000f;
            reflection.transform.position = _camera.transform.position;
            typeof(Camera).GetProperty(nameof(Camera.cameraType)).SetValue(reflection, CameraType.Reflection);
            Draw(reflection);
            Assert.That(backend.RegisteredReflectionViews, Is.EqualTo(1));
            Assert.That(_renderer.ReflectionSubmittedBatches, Is.Zero);
            int before = calls;
            if (change == "resolver")
            {
                Func<Material, Material> secondResolver = material =>
                {
                    calls++;
                    return _secondMaterial;
                };
                assign.Invoke(_renderer, new object[] { secondResolver });
            }
            else if (change == "castShadows") SetField("_castShadows", false);
            else _root.layer = 1;
            Draw(reflection);
            Assert.That(calls, Is.EqualTo(before + 1));
            Assert.That(backend.RegisteredReflectionViews, Is.Zero);
            Assert.That(_renderer.ReflectionSubmittedBatches, Is.EqualTo(1));
            Assert.That(backend.Active, Is.True);
            Assert.That(Buffer(backend), Is.SameAs(buffer));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NonCastingSourcesKeepNativeColorWithoutLegacyShadowCalls(bool batchOptOut)
        {
            if (batchOptOut) typeof(XRayInstanceBatch).GetField("_castShadows", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_renderer.BatchSet.Batches[0], false);
            else SetField("_castShadows", false);
            ActivateNative();
            Assert.That(_renderer.LastSubmittedCalls, Is.Zero);
            Assert.That(_renderer.LastSubmittedInstances, Is.Zero);
            Assert.That(_renderer.LastShadowOnlyBatches, Is.Zero);
            Assert.That(_renderer.LastVisibleBatches, Is.Zero);
            Assert.That(_renderer.NativeShadowCommands, Is.Zero);
        }

        [Test]
        public void NativeShadowPreparationReusesOneResolvedMaterialAndPreparedHistory()
        {
            _renderer.BatchSet = Set(_firstMaterial, _firstMaterial);
            var observedHistories = new List<object>();
            var observedBuffers = new List<Array>();
            var observedPrevious = new List<Matrix4x4>();
            var observedFrames = new List<int>();
            int calls = 0;
            Func<Material, Material> resolver = material =>
            {
                calls++;
                var histories = (System.Collections.IList)typeof(XRayLevelInstancedRenderer).GetField("_histories", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_renderer);
                foreach (object history in histories)
                {
                    var instances = (Array)history.GetType().GetProperty("Instances").GetValue(history);
                    object instance = instances.GetValue(0);
                    observedHistories.Add(history);
                    observedBuffers.Add(instances);
                    observedPrevious.Add((Matrix4x4)instance.GetType().GetField("prevObjectToWorld").GetValue(instance));
                    observedFrames.Add((int)history.GetType().GetProperty("Frame").GetValue(history));
                }
                return material;
            };
            typeof(XRayLevelInstancedRenderer).GetMethod("SetOrderIndependentMaterialResolver", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_renderer, new object[] { resolver });
            ActivateNative();
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(_renderer.LastSubmittedCalls, Is.Zero);
            Assert.That(_renderer.LastSubmittedInstances, Is.Zero);
            Assert.That(_renderer.LastShadowOnlyBatches, Is.Zero);
            Assert.That(_renderer.NativeShadowCommands, Is.Zero);
            Assert.That(observedHistories, Has.Count.EqualTo(2));
            var current = (System.Collections.IList)typeof(XRayLevelInstancedRenderer).GetField("_histories", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_renderer);
            for (int index = 0; index < current.Count; index++)
            {
                object history = current[index];
                var instances = (Array)history.GetType().GetProperty("Instances").GetValue(history);
                object instance = instances.GetValue(0);
                Assert.That(history, Is.SameAs(observedHistories[index]));
                Assert.That(instances, Is.SameAs(observedBuffers[index]));
                Assert.That((Matrix4x4)instance.GetType().GetField("prevObjectToWorld").GetValue(instance), Is.EqualTo(observedPrevious[index]));
                Assert.That((int)history.GetType().GetProperty("Frame").GetValue(history), Is.EqualTo(observedFrames[index]));
            }
        }

        [Test]
        public void ShadowSplitsKeepIndependentPointFaces()
        {
            Vector3[] directions = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            var batches = new List<XRayInstanceBatch>();
            var planes = new Plane[directions.Length];
            var splits = new CullingSplit[directions.Length];
            for (int index = 0; index < directions.Length; index++)
            {
                Vector3 center = directions[index] * 4f;
                batches.Add(new XRayInstanceBatch("Native point face " + index, _mesh, new[] { _firstMaterial }, new Bounds(center, Vector3.one), new[] { Matrix4x4.Translate(center) }, new[] { Vector4.one }, true));
                planes[index] = new Plane(directions[index], -1f);
                splits[index] = new CullingSplit { cullingPlaneOffset = index, cullingPlaneCount = 1 };
            }
            _renderer.BatchSet = PreparedSet(batches);
            XRayInstanceNativeRenderer backend = ActivateNative();
            ushort[] masks = SelectShadowSources(backend, planes, splits);
            for (int index = 0; index < directions.Length; index++) Assert.That(masks[index], Is.EqualTo((ushort)(1 << index)));
            Assert.That(BackendField<int>(backend, "_selectedCount"), Is.EqualTo(directions.Length));
            Assert.That(_renderer.NativeShadowCommands, Is.Zero);
            Assert.That(_renderer.LastSubmittedCalls, Is.Zero);
        }

        [Test]
        public void ShadowSplitBoundariesUnionWithoutDroppingTouchingCasters()
        {
            var batches = new List<XRayInstanceBatch>();
            Vector3[] centers = { new Vector3(-3f, 0f, 4f), new Vector3(3f, 0f, 4f), new Vector3(0f, 0f, 4f), new Vector3(0f, 0f, 4f), new Vector3(-0.5f, 0f, 4f), new Vector3(0.5f, 0f, 4f) };
            for (int index = 0; index < centers.Length; index++) batches.Add(new XRayInstanceBatch("Native split boundary " + index, _mesh, new[] { _firstMaterial }, new Bounds(centers[index], Vector3.one), new[] { Matrix4x4.Translate(centers[index]) }, new[] { Vector4.one }, index != 3));
            _renderer.BatchSet = PreparedSet(batches);
            XRayInstanceNativeRenderer backend = ActivateNative();
            Plane[] planes = { new Plane(Vector3.right, 0f), new Plane(Vector3.left, 0f) };
            CullingSplit[] splits = { new CullingSplit { cullingPlaneOffset = 0, cullingPlaneCount = 1 }, new CullingSplit { cullingPlaneOffset = 1, cullingPlaneCount = 1 } };
            Assert.That(SelectShadowSources(backend, planes, splits), Is.EqualTo(new ushort[] { 2, 1, 3, 0, 3, 3 }));
            Assert.That(BackendField<int>(backend, "_selectedCount"), Is.EqualTo(5));
            int[] selected = BackendField<int[]>(backend, "_selectedSources");
            Assert.That(new[] { selected[0], selected[1], selected[2], selected[3], selected[4] }, Is.EqualTo(new[] { 0, 1, 2, 4, 5 }));
            Assert.That(SelectShadowSources(backend, new[] { new Plane(Vector3.up, -100f) }, new[] { splits[0] }), Is.EqualTo(new ushort[centers.Length]));
            Assert.That(BackendField<int>(backend, "_selectedCount"), Is.Zero);
        }

        [Test]
        public void ValidShadowSplitsPreserveTheSixteenthBit()
        {
            XRayInstanceNativeRenderer backend = ActivateNative();
            Plane[] planes = { new Plane(Vector3.forward, -100f), new Plane(Vector3.forward, 0f) };
            var splits = new CullingSplit[16];
            for (int index = 0; index < splits.Length; index++) splits[index] = new CullingSplit { cullingPlaneOffset = index == 15 ? 1 : 0, cullingPlaneCount = 1 };
            Assert.That(SelectShadowSources(backend, planes, splits), Is.EqualTo(new ushort[] { 32768 }));
            Assert.That(BackendField<int>(backend, "_selectedCount"), Is.EqualTo(1));
        }

        [TestCase("MissingSplits")]
        [TestCase("EmptySplits")]
        [TestCase("TooManySplits")]
        [TestCase("MissingPlanes")]
        [TestCase("EmptyPlanes")]
        [TestCase("NegativeOffset")]
        [TestCase("NegativeCount")]
        [TestCase("ZeroCount")]
        [TestCase("PlaneRange")]
        [TestCase("ZeroNormal")]
        [TestCase("NonfiniteNormal")]
        [TestCase("NonfiniteDistance")]
        [TestCase("LaterMalformedSplit")]
        public void MalformedShadowSplitsKeepEveryCastingSource(string invalid)
        {
            _renderer.BatchSet = Set(_firstMaterial, _firstMaterial, _firstMaterial);
            typeof(XRayInstanceBatch).GetField("_castShadows", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_renderer.BatchSet.Batches[2], false);
            XRayInstanceNativeRenderer backend = ActivateNative();
            Plane[] planes = { new Plane(Vector3.up, -100f) };
            CullingSplit[] splits = { new CullingSplit { cullingPlaneOffset = 0, cullingPlaneCount = 1 } };
            Assert.That(SelectShadowSources(backend, planes, splits), Is.EqualTo(new ushort[3]));
            switch (invalid)
            {
                case "MissingSplits": splits = null; break;
                case "EmptySplits": splits = Array.Empty<CullingSplit>(); break;
                case "TooManySplits":
                    splits = new CullingSplit[17];
                    for (int index = 0; index < splits.Length; index++) splits[index] = new CullingSplit { cullingPlaneOffset = 0, cullingPlaneCount = 1 };
                    break;
                case "MissingPlanes": planes = null; break;
                case "EmptyPlanes": planes = Array.Empty<Plane>(); break;
                case "NegativeOffset": splits[0] = new CullingSplit { cullingPlaneOffset = -1, cullingPlaneCount = 1 }; break;
                case "NegativeCount": splits[0] = new CullingSplit { cullingPlaneOffset = 0, cullingPlaneCount = -1 }; break;
                case "ZeroCount": splits[0] = default; break;
                case "PlaneRange": splits[0] = new CullingSplit { cullingPlaneOffset = 1, cullingPlaneCount = 1 }; break;
                case "ZeroNormal": planes[0] = new Plane(Vector3.zero, 0f); break;
                case "NonfiniteNormal": planes[0] = new Plane(new Vector3(float.NaN, 0f, 0f), 0f); break;
                case "NonfiniteDistance": planes[0] = new Plane(Vector3.up, float.PositiveInfinity); break;
                case "LaterMalformedSplit": splits = new[] { splits[0], new CullingSplit { cullingPlaneOffset = 1, cullingPlaneCount = 1 } }; break;
                default: throw new ArgumentOutOfRangeException(nameof(invalid));
            }
            Assert.That(SelectShadowSources(backend, planes, splits), Is.EqualTo(new ushort[] { ushort.MaxValue, ushort.MaxValue, 0 }));
            Assert.That(BackendField<int>(backend, "_selectedCount"), Is.EqualTo(2));
            int[] selected = BackendField<int[]>(backend, "_selectedSources");
            Assert.That(new[] { selected[0], selected[1] }, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(_renderer.NativeSubmissionActive, Is.True);
            Assert.That(_renderer.LastSubmittedCalls, Is.Zero);
        }

        [Test]
        public void EveryPreparationRefreshesEffectiveShadowFlagsAndLegacyRenderingLayers()
        {
            XRayInstanceNativeRenderer backend = ActivateNative();
            GraphicsBuffer buffer = Buffer(backend);
            Plane[] planes = { new Plane(Vector3.forward, 0f) };
            CullingSplit[] splits = { new CullingSplit { cullingPlaneOffset = 0, cullingPlaneCount = 1 } };
            Assert.That(SelectShadowSources(backend, planes, splits), Is.EqualTo(new ushort[] { 1 }));
            Assert.That(BackendField<uint>(backend, "_renderingLayerMask"), Is.EqualTo(new RenderParams(_firstMaterial).renderingLayerMask));
            Assert.That(backend.Prepare(_renderer.BatchSet, _camera, _root.layer, false, null), Is.True);
            Assert.That(SelectShadowSources(backend, planes, splits), Is.EqualTo(new ushort[] { 0 }));
            Assert.That(backend.Prepare(_renderer.BatchSet, _camera, _root.layer, true, null), Is.True);
            Assert.That(SelectShadowSources(backend, planes, splits), Is.EqualTo(new ushort[] { 1 }));
            FieldInfo castShadows = typeof(XRayInstanceBatch).GetField("_castShadows", BindingFlags.Instance | BindingFlags.NonPublic);
            castShadows.SetValue(_renderer.BatchSet.Batches[0], false);
            Assert.That(backend.Prepare(_renderer.BatchSet, _camera, _root.layer, true, null), Is.True);
            Assert.That(SelectShadowSources(backend, planes, splits), Is.EqualTo(new ushort[] { 0 }));
            castShadows.SetValue(_renderer.BatchSet.Batches[0], true);
            Assert.That(backend.Prepare(_renderer.BatchSet, _camera, _root.layer, true, null), Is.True);
            Assert.That(SelectShadowSources(backend, planes, splits), Is.EqualTo(new ushort[] { 1 }));
            Assert.That(Buffer(backend), Is.SameAs(buffer));
        }

        [TestCase(null)]
        [TestCase(typeof(MissingShadowCameraScope))]
        [TestCase(typeof(MissingShadowVersionScope))]
        [TestCase(typeof(WrongShadowVersionScope))]
        [TestCase(typeof(WrongShadowVersionTypeScope))]
        [TestCase(typeof(WrongShadowCameraTypeScope))]
        public void ShadowCameraAccessorRejectsUnavailableOrInvalidContract(Type scope)
        {
            MethodInfo create = typeof(XRayInstanceNativeRenderer).GetMethod("CreateShadowCameraAccessor", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(create, Is.Not.Null);
            Assert.That(create.Invoke(null, new object[] { scope }), Is.Null);
        }

        [Test]
        public void ShadowCameraAccessorUsesTheCurrentCameraFromTheVersionedContract()
        {
            MethodInfo create = typeof(XRayInstanceNativeRenderer).GetMethod("CreateShadowCameraAccessor", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(create, Is.Not.Null);
            var accessor = create.Invoke(null, new object[] { typeof(ValidShadowCameraScope) }) as Func<Camera>;
            Assert.That(accessor, Is.Not.Null);
            try
            {
                ValidShadowCameraScope.CurrentCamera = _camera;
                Assert.That(accessor(), Is.SameAs(_camera));
                ValidShadowCameraScope.CurrentCamera = null;
                Assert.That(accessor(), Is.Null);
            }
            finally
            {
                ValidShadowCameraScope.CurrentCamera = null;
            }
        }

        [Test]
        public void NativeGlobalBoundsExpandOnlySourcesUsingCurrentOrReflectionWind()
        {
            XRayInstanceNativeRenderer backend = ActivateNative();
            MethodInfo currentBounds = typeof(XRayInstanceNativeRenderer).GetMethod("CurrentGlobalBounds", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(currentBounds, Is.Not.Null);
            Bounds original = (Bounds)currentBounds.Invoke(backend, null);
            Bounds physical = BackendField<Bounds[]>(backend, "_physicalSourceBounds")[0];
            Assert.That(original, Is.EqualTo(physical));
            var wind = new XRayFloraWindState { Profile = XRayFloraWindProfile.AnomalySsfx };
            wind.Reset(new XRayFloraWindWeather(0f, 1000f, 1f));
            Assert.That(XRayFloraWindBounds.Expand(original, wind, original.size.y).size.x, Is.GreaterThan(original.size.x));
            Assert.That(backend.Prepare(_renderer.BatchSet, _camera, _root.layer, true, wind), Is.True);
            Assert.That((Bounds)currentBounds.Invoke(backend, null), Is.EqualTo(original));
            _firstMaterial.SetFloat(XRayFloraWindBounds.MATERIAL_PROPERTY, (float)XRayFloraWindProfile.AnomalySsfx);
            Assert.That(backend.Prepare(_renderer.BatchSet, _camera, _root.layer, true, wind), Is.True);
            Bounds expanded = (Bounds)currentBounds.Invoke(backend, null);
            Assert.That(expanded, Is.EqualTo(XRayFloraWindBounds.Expand(physical, wind, physical.size.y)));
            Assert.That(expanded.size.x, Is.GreaterThan(original.size.x));
            Assert.That(expanded.size.z, Is.GreaterThan(original.size.z));
            Assert.That(backend.Prepare(_renderer.BatchSet, _camera, _root.layer, true, wind, material => _secondMaterial), Is.True);
            Assert.That((Bounds)currentBounds.Invoke(backend, null), Is.EqualTo(expanded));
        }

        [TestCase(XRayFloraWindProfile.AnomalySsfx)]
        [TestCase(XRayFloraWindProfile.TrueStalkerXRay)]
        public void VisibilityWindCacheMatchesFreshQueriesAcrossSameObjectMutations(XRayFloraWindProfile profile)
        {
            var batches = new List<XRayInstanceBatch>();
            for (int index = 0; index < 9; index++)
            {
                Vector3 center = new Vector3((index - 4) * 7f, index % 3, 4f);
                batches.Add(new XRayInstanceBatch("Wind cache source " + index, _mesh, new[] { _firstMaterial }, new Bounds(center, new Vector3(2f, index % 2 == 0 ? 30f : 2f, 2f)), new[] { Matrix4x4.Translate(center) }, new[] { Vector4.one }, true));
            }
            XRayInstanceBatchSet set = PreparedSet(batches);
            var cached = new XRayInstanceVisibilityIndex();
            cached.Ensure(set);
            var wind = new XRayFloraWindState { Profile = profile, TreesSetup = new Vector4(11f, 0.15f, 0.1f, 0.01f) };
            wind.Reset(new XRayFloraWindWeather(0f, 0f, 1f));
            AssertIndexMatchesFresh(cached, set, wind);
            if (profile == XRayFloraWindProfile.TrueStalkerXRay)
            {
                SetWindVectors(wind, new Vector4(1f, 0f, 0f, 5f), new Vector4(0f, 0f, 2f, 7f));
                AssertIndexMatchesFresh(cached, set, wind);
                int generation = CacheStamp(cached);
                SetWindVectors(wind, new Vector4(1f, 0f, 0f, 5f), new Vector4(0f, 0f, 3f, 7f));
                AssertIndexMatchesFresh(cached, set, wind);
                Assert.That(CacheStamp(cached), Is.EqualTo(generation + 1));
                SetWindVectors(wind, new Vector4(5f, 0f, 0f, 5f), new Vector4(0f, 0f, 3f, 7f));
                AssertIndexMatchesFresh(cached, set, wind);
                Assert.That(CacheStamp(cached), Is.EqualTo(generation + 2));
            }
            wind.TreesSetup = new Vector4(30f, 20f, 2f, 1f);
            wind.Reset(new XRayFloraWindWeather(1f, 1000f, 1f));
            SetWindVectors(wind, new Vector4(3f, 4f, 0f, 1f), new Vector4(0f, 0f, 2f, 2f));
            AssertIndexMatchesFresh(cached, set, wind);
            SetWindVectors(wind, new Vector4(0.01f, 0.02f, 0.03f, 7f), new Vector4(7f, 0f, 0f, 9f));
            wind.TreesSetup = new Vector4(11f, 0.15f, 0.2f, 0.05f);
            wind.Advance(0.1f, new XRayFloraWindWeather(2f, 50f, 0.5f));
            AssertIndexMatchesFresh(cached, set, wind);
            if (profile == XRayFloraWindProfile.AnomalySsfx) wind.TreesSetup = new Vector4(11f, 0.15f, float.NaN, 1f);
            else SetWindVectors(wind, new Vector4(float.NaN, 0f, 0f, 0f), Vector4.zero);
            AssertIndexMatchesFresh(cached, set, wind);
            wind.TreesSetup = new Vector4(11f, 0.15f, 0.5f, 0.15f);
            wind.Reset(new XRayFloraWindWeather(0f, 250f, 1f));
            SetWindVectors(wind, Vector4.zero, Vector4.zero);
            AssertIndexMatchesFresh(cached, set, wind);
            typeof(XRayFloraWindState).GetProperty(nameof(XRayFloraWindState.Initialized)).GetSetMethod(true).Invoke(wind, new object[] { false });
            AssertIndexMatchesFresh(cached, set, wind);
            AssertIndexMatchesFresh(cached, set, null);
            wind.Profile = XRayFloraWindProfile.Disabled;
            AssertIndexMatchesFresh(cached, set, wind);
        }

        [Test]
        public void AnomalyBoundsCacheReusesAnimationChangesAndInvalidatesExactInputsAndWrappedGeneration()
        {
            var batches = new List<XRayInstanceBatch>();
            foreach (float x in new[] { -4f, -6f })
            {
                Vector3 center = new Vector3(x, 0f, 4f);
                batches.Add(new XRayInstanceBatch("Wind cache wrap source", _mesh, new[] { _firstMaterial }, new Bounds(center, Vector3.one * 2f), new[] { Matrix4x4.Translate(center) }, new[] { Vector4.one }, true));
            }
            XRayInstanceBatchSet set = PreparedSet(batches);
            var cached = new XRayInstanceVisibilityIndex();
            cached.Ensure(set);
            var wind = new XRayFloraWindState { Profile = XRayFloraWindProfile.AnomalySsfx, TreesSetup = new Vector4(11f, 0.15f, 0.1f, 0.01f) };
            wind.Reset(new XRayFloraWindWeather(0f, 0f, 1f));
            cached.Select(new[] { new Plane(Vector3.up, 1000f) }, 1, wind);
            int generation = CacheStamp(cached);
            wind.Advance(0.1f, new XRayFloraWindWeather(2f, 0f, 0.5f));
            wind.TreesSetup = new Vector4(300f, 250f, wind.TreesSetup.z, wind.TreesSetup.w);
            SetWindVectors(wind, new Vector4(100f, 200f, 300f, 400f), new Vector4(500f, 600f, 700f, 800f));
            AssertIndexMatchesFresh(cached, set, wind);
            Assert.That(CacheStamp(cached), Is.EqualTo(generation));
            typeof(XRayInstanceVisibilityIndex).GetField("_windBoundsStamp", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(cached, int.MaxValue);
            wind.TreesSetup = new Vector4(300f, 250f, 2f, 1f);
            wind.Reset(new XRayFloraWindWeather(2f, 1000f, 1f));
            AssertIndexMatchesFresh(cached, set, wind);
            Assert.That(CacheStamp(cached), Is.EqualTo(1));
            Vector4 setup = wind.TreesSetup;
            setup.z = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(setup.z) + 1);
            wind.TreesSetup = setup;
            AssertIndexMatchesFresh(cached, set, wind);
            Assert.That(CacheStamp(cached), Is.EqualTo(2));
            setup = wind.TreesSetup;
            setup.w = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(setup.w) + 1);
            wind.TreesSetup = setup;
            AssertIndexMatchesFresh(cached, set, wind);
            Assert.That(CacheStamp(cached), Is.EqualTo(3));
            float velocity = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(wind.Weather.Velocity) + 1);
            wind.Advance(0.1f, new XRayFloraWindWeather(wind.Weather.DirectionRadians, velocity, wind.Weather.TreeAmplitudeIntensity));
            AssertIndexMatchesFresh(cached, set, wind);
            Assert.That(CacheStamp(cached), Is.EqualTo(4));
        }

        [Test]
        public void VisibilityWindCacheRebuildsForGeometryOwnershipAndNullRelease()
        {
            var cached = new XRayInstanceVisibilityIndex();
            Plane[] planes = { new Plane(Vector3.right, 0f) };
            var wind = new XRayFloraWindState { Profile = XRayFloraWindProfile.AnomalySsfx };
            wind.Reset(new XRayFloraWindWeather(0f, 1000f, 1f));
            cached.Select(planes, planes.Length, wind);
            Assert.That(cached.SelectedCount, Is.Zero);
            XRayInstanceBatchSet set = Set(_firstMaterial, _secondMaterial);
            cached.Ensure(set);
            AssertIndexMatchesFresh(cached, set, wind);
            var reordered = new List<XRayInstanceBatch> { set.Batches[1], set.Batches[0] };
            set.Initialize("Wind cache reordered", 16f, reordered);
            set.PrepareMatrices("Wind cache reordered ownership");
            cached.Ensure(set);
            AssertIndexMatchesFresh(cached, set, wind);
            Bounds[] conservative = { new Bounds(Vector3.one * 100f, Vector3.one), new Bounds(Vector3.one * -100f, Vector3.one) };
            cached.Ensure(set, conservative, 1);
            AssertIndexMatchesFresh(cached, set, wind, conservative, 1);
            conservative[0] = new Bounds(Vector3.zero, Vector3.one * 40f);
            cached.Ensure(set, conservative, 2);
            AssertIndexMatchesFresh(cached, set, wind, conservative, 2);
            cached.Ensure(null);
            Assert.That(cached.MemoryBytes, Is.Zero);
            cached.Select(planes, planes.Length, wind);
            Assert.That(cached.SelectedCount, Is.Zero);
            cached.Ensure(set);
            AssertIndexMatchesFresh(cached, set, wind);
        }

        [TestCase(XRayFloraWindProfile.AnomalySsfx)]
        [TestCase(XRayFloraWindProfile.TrueStalkerXRay)]
        public void NativeWindCacheMatchesFreshSelectionAcrossMutationAndGeometryRebuild(XRayFloraWindProfile profile)
        {
            _renderer.BatchSet = Set(_firstMaterial, _firstMaterial);
            _firstMaterial.SetFloat(XRayFloraWindBounds.MATERIAL_PROPERTY, (float)profile);
            var wind = new XRayFloraWindState { Profile = profile, TreesSetup = new Vector4(11f, 0.15f, 0.1f, 0.01f) };
            wind.Reset(new XRayFloraWindWeather(0f, 0f, 1f));
            var backend = new XRayInstanceNativeRenderer();
            _backends.Add(backend);
            Assert.That(backend.Prepare(_renderer.BatchSet, _camera, _root.layer, true, wind), Is.True, backend.FallbackReason);
            CaptureNativeSelection(backend, new[] { new Plane(Vector3.up, 1000f) });
            typeof(XRayInstanceNativeRenderer).GetField("_windBoundsStamp", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(backend, int.MaxValue);
            wind.TreesSetup = new Vector4(11f, 0.15f, 2f, 1f);
            wind.Reset(new XRayFloraWindWeather(0f, 1000f, 1f));
            SetWindVectors(wind, new Vector4(5f, 0f, 0f, 1f), new Vector4(0f, 0f, 2f, 1f));
            Plane[] planes = { new Plane(Vector3.right, -3.5f) };
            AssertNativeMatchesFresh(backend, wind, planes);
            Assert.That(CacheStamp(backend), Is.EqualTo(1));
            if (profile == XRayFloraWindProfile.TrueStalkerXRay)
            {
                int generation = CacheStamp(backend);
                SetWindVectors(wind, new Vector4(5f, 0f, 0f, 1f), new Vector4(0f, 0f, 7f, 1f));
                AssertNativeMatchesFresh(backend, wind, planes);
                Assert.That(CacheStamp(backend), Is.EqualTo(generation + 1));
                SetWindVectors(wind, new Vector4(9f, 0f, 0f, 1f), new Vector4(0f, 0f, 7f, 1f));
                AssertNativeMatchesFresh(backend, wind, planes);
                Assert.That(CacheStamp(backend), Is.EqualTo(generation + 2));
            }
            wind.TreesSetup = new Vector4(11f, 0.15f, 0.1f, 0.01f);
            wind.Reset(new XRayFloraWindWeather(0f, 0f, 1f));
            SetWindVectors(wind, Vector4.zero, Vector4.zero);
            AssertNativeMatchesFresh(backend, wind, planes);
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            Assert.That(backend.Prepare(_renderer.BatchSet, _camera, _root.layer, true, wind), Is.True, backend.FallbackReason);
            AssertNativeMatchesFresh(backend, wind, planes);
            _renderer.BatchSet.Batches[0].Matrices[0] = Matrix4x4.Translate(new Vector3(-12f, 0f, 4f));
            _renderer.BatchSet.PrepareMatrices("Native wind cache moved ownership");
            Assert.That(backend.Prepare(_renderer.BatchSet, _camera, _root.layer, true, wind), Is.True, backend.FallbackReason);
            AssertNativeMatchesFresh(backend, wind, planes);
            var reflectionObject = new GameObject("Native wind cache reflection camera");
            reflectionObject.transform.SetParent(_root.transform);
            Camera reflection = reflectionObject.AddComponent<Camera>();
            reflection.enabled = false;
            reflection.farClipPlane = 1000f;
            typeof(Camera).GetProperty(nameof(Camera.cameraType)).SetValue(reflection, CameraType.Reflection);
            Assert.That(backend.TryRegisterReflection(reflection), Is.True);
            AssertNativeMatchesFresh(backend, wind, planes, reflection);
            wind.TreesSetup = new Vector4(11f, 0.15f, 2f, 1f);
            wind.Reset(new XRayFloraWindWeather(0f, 1000f, 1f));
            SetWindVectors(wind, Vector4.one * 3f, Vector4.one * 2f);
            Assert.That(backend.TryRegisterReflection(reflection), Is.False);
            Assert.That(backend.Prepare(_renderer.BatchSet, _camera, _root.layer, true, wind), Is.True, backend.FallbackReason);
            Assert.That(backend.TryRegisterReflection(reflection), Is.True);
            AssertNativeMatchesFresh(backend, wind, planes, reflection);
            backend.Dispose();
            Assert.That(BackendField<Bounds[]>(backend, "_windBounds"), Is.Null);
            Assert.That(BackendField<int[]>(backend, "_windBoundsStamps"), Is.Null);
            Assert.That(backend.MemoryBytes, Is.Zero);
        }

        [Test]
        public void NativeWindFlagRefreshInvalidatesOnlyItsSourceRange()
        {
            _renderer.BatchSet = Set(_firstMaterial, _secondMaterial);
            _firstMaterial.SetFloat(XRayFloraWindBounds.MATERIAL_PROPERTY, 1f);
            _secondMaterial.SetFloat(XRayFloraWindBounds.MATERIAL_PROPERTY, 1f);
            var wind = new XRayFloraWindState { Profile = XRayFloraWindProfile.AnomalySsfx };
            wind.Reset(new XRayFloraWindWeather(0f, 1000f, 1f));
            var backend = new XRayInstanceNativeRenderer();
            _backends.Add(backend);
            Assert.That(backend.Prepare(_renderer.BatchSet, _camera, _root.layer, true, wind), Is.True, backend.FallbackReason);
            Plane[] planes = { new Plane(Vector3.up, 1000f) };
            AssertNativeMatchesFresh(backend, wind, planes);
            GraphicsBuffer buffer = Buffer(backend);
            int[] stamps = BackendField<int[]>(backend, "_windBoundsStamps");
            int unrelatedStamp = stamps[1];
            Assert.That(stamps[0], Is.GreaterThan(0));
            Assert.That(unrelatedStamp, Is.GreaterThan(0));
            _firstMaterial.SetFloat(XRayFloraWindBounds.MATERIAL_PROPERTY, 0f);
            Assert.That(backend.Prepare(_renderer.BatchSet, _camera, _root.layer, true, wind), Is.True, backend.FallbackReason);
            Assert.That(stamps[0], Is.Zero);
            Assert.That(stamps[1], Is.EqualTo(unrelatedStamp));
            AssertNativeMatchesFresh(backend, wind, new[] { new Plane(Vector3.right, -3.5f) });
            Assert.That(stamps[0], Is.Zero);
            Assert.That(stamps[1], Is.EqualTo(unrelatedStamp));
            _firstMaterial.SetFloat(XRayFloraWindBounds.MATERIAL_PROPERTY, 1f);
            Assert.That(backend.Prepare(_renderer.BatchSet, _camera, _root.layer, true, wind), Is.True, backend.FallbackReason);
            AssertNativeMatchesFresh(backend, wind, planes);
            Assert.That(stamps[0], Is.EqualTo(stamps[1]));
            Assert.That(Buffer(backend), Is.SameAs(buffer));
        }

        [Test]
        public void BatchReplacementRetainsNativeResourcesUntilContextEnd()
        {
            XRayInstanceNativeRenderer backend = ActivateNative();
            long bytes = backend.MemoryBytes;
            _renderer.BatchSet = Set(_secondMaterial);
            Assert.That(Field<XRayInstanceNativeRenderer>("_nativeRenderer"), Is.Null);
            Assert.That(Field<List<XRayInstanceNativeRenderer>>("_retiredNativeRenderers"), Has.Count.EqualTo(1));
            Assert.That(Field<List<XRayInstanceNativeRenderer>>("_retiredNativeRenderers")[0], Is.SameAs(backend));
            Assert.That(backend.MemoryBytes, Is.EqualTo(bytes));
            EndContext();
            Assert.That(Field<List<XRayInstanceNativeRenderer>>("_retiredNativeRenderers"), Is.Empty);
            Assert.That(backend.Active, Is.False);
            Assert.That(backend.MemoryBytes, Is.Zero);
            Assert.That(Field<bool>("_nativeContextActive"), Is.False);
            Assert.DoesNotThrow(EndContext);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HistoryResetPreservesStaticBufferAndExposureStillFallsBack(bool resetInsideContext)
        {
            XRayInstanceNativeRenderer backend = ActivateNative();
            GraphicsBuffer buffer = Buffer(backend);
            long bytes = backend.MemoryBytes;
            if (!resetInsideContext) EndContext();
            _renderer.ResetMotionHistory();
            Assert.That(Field<XRayInstanceNativeRenderer>("_nativeRenderer"), Is.SameAs(backend));
            Assert.That(Field<List<XRayInstanceNativeRenderer>>("_retiredNativeRenderers"), Is.Empty);
            Assert.That(Buffer(backend), Is.SameAs(buffer));
            Assert.That(backend.MemoryBytes, Is.EqualTo(bytes));
            Assert.That(backend.Active, Is.EqualTo(resetInsideContext));
            if (resetInsideContext) EndContext();
            BeginEligibleContext();
            Draw(_camera);
            Assert.That(_renderer.NativeSubmissionActive, Is.True);
            Assert.That(Field<XRayInstanceNativeRenderer>("_nativeRenderer"), Is.SameAs(backend));
            Assert.That(Buffer(backend), Is.SameAs(buffer));
            EndContext();
            Matrix4x4[] matrices = _renderer.BatchSet.Batches[0].Matrices;
            matrices[0] = Matrix4x4.Translate(new Vector3(0.25f, 0f, 4f));
            BeginEligibleContext();
            Draw(_camera);
            Assert.That(_renderer.NativeSubmissionActive, Is.False);
            Assert.That(_renderer.LastSubmittedCalls, Is.EqualTo(1));
            Assert.That(_renderer.LastSubmittedInstances, Is.EqualTo(1));
            Assert.That(Buffer(backend), Is.SameAs(buffer));
        }

        [Test]
        public void DisableDuringContextDefersGraphicsReleaseUntilContextEnd()
        {
            XRayInstanceNativeRenderer backend = ActivateNative();
            long bytes = backend.MemoryBytes;
            _renderer.enabled = false;
            Assert.That(Field<XRayInstanceNativeRenderer>("_nativeRenderer"), Is.Null);
            Assert.That(Field<List<XRayInstanceNativeRenderer>>("_retiredNativeRenderers"), Has.Count.EqualTo(1));
            Assert.That(backend.MemoryBytes, Is.EqualTo(bytes));
            EndContext();
            Assert.That(backend.MemoryBytes, Is.Zero);
            Assert.That(backend.Active, Is.False);
            Assert.That(Field<List<XRayInstanceNativeRenderer>>("_retiredNativeRenderers"), Is.Empty);
            Assert.That(Field<bool>("_nativeContextActive"), Is.False);
            Assert.That(Field<bool>("_nativeContextAllowed"), Is.False);
            Assert.That(Field<Camera>("_nativeContextCamera"), Is.Null);
        }

        [Test]
        public void ReenableBeforeContextEndKeepsOneSubscriberAndDisableRemovesIt()
        {
            XRayInstanceNativeRenderer backend = ActivateNative();
            Assert.That(EndContextSubscriptions(), Is.EqualTo(1));
            _renderer.enabled = false;
            Assert.That(EndContextSubscriptions(), Is.EqualTo(1));
            _renderer.enabled = true;
            Assert.That(EndContextSubscriptions(), Is.EqualTo(1));
            EndContext();
            Assert.That(backend.MemoryBytes, Is.Zero);
            Assert.That(Field<List<XRayInstanceNativeRenderer>>("_retiredNativeRenderers"), Is.Empty);
            Assert.That(EndContextSubscriptions(), Is.EqualTo(1));
            _renderer.enabled = false;
            Assert.That(EndContextSubscriptions(), Is.Zero);
        }

        [Test]
        public void StatefulResolverKeepsOriginalOrderAcrossNonadjacentPreparedSources()
        {
            XRayInstanceBatchSet set = Set(_firstMaterial, _secondMaterial, _firstMaterial);
            Bounds bounds = set.Batches[0].Bounds;
            bounds.Encapsulate(set.Batches[2].Bounds);
            set.PrepareSubmissionGroups(new List<XRayInstanceBatchGroup> { new XRayInstanceBatchGroup(new[] { 0, 2 }, bounds) }, "nonadjacent native fallback");
            _renderer.BatchSet = set;
            ActivateNative();
            EndContext();
            var calls = new List<Material>();
            var results = new List<Material>();
            _renderer.MaterialResolver = source =>
            {
                calls.Add(source);
                Material result = calls.Count == 2 ? null : calls.Count == 1 ? _firstMaterial : _secondMaterial;
                results.Add(result);
                return result;
            };
            BeginEligibleContext();
            Draw(_camera);
            Assert.That(_renderer.NativeSubmissionOverride, Is.True);
            Assert.That(Field<bool>("_nativeContextAllowed"), Is.True);
            Assert.That(Field<bool>("_nativeContextChecked"), Is.True);
            Assert.That(_renderer.NativeSubmissionActive, Is.False);
            Assert.That(calls, Is.EqualTo(new[] { _firstMaterial, _secondMaterial, _firstMaterial }));
            Assert.That(results, Is.EqualTo(new[] { _firstMaterial, null, _secondMaterial }));
            Assert.That(_renderer.LastSubmittedCalls, Is.EqualTo(2));
            Assert.That(_renderer.LastSubmittedInstances, Is.EqualTo(2));
        }

        private XRayInstanceNativeRenderer ActivateNative()
        {
            BeginEligibleContext();
            Draw(_camera);
            Assert.That(_renderer.NativeSubmissionActive, Is.True);
            XRayInstanceNativeRenderer backend = Field<XRayInstanceNativeRenderer>("_nativeRenderer");
            Assert.That(backend, Is.Not.Null);
            Assert.That(backend.MemoryBytes, Is.GreaterThan(0));
            if (!_backends.Contains(backend)) _backends.Add(backend);
            return backend;
        }

        private void BeginEligibleContext()
        {
            BEGIN.Invoke(_renderer, new object[] { default(ScriptableRenderContext), new List<Camera> { _camera } });
            Assert.That(Field<bool>("_nativeContextActive"), Is.True);
            Assert.That(Field<bool>("_nativeContextAllowed"), Is.True);
            Assert.That(Field<Camera>("_nativeContextCamera"), Is.SameAs(_camera));
            SetField("_nativeContextChecked", true);
        }

        private void EndContext()
        {
            END.Invoke(_renderer, new object[] { default(ScriptableRenderContext), new List<Camera> { _camera } });
        }

        private void Draw(Camera camera)
        {
            DRAW.Invoke(_renderer, new object[] { default(ScriptableRenderContext), camera });
        }

        private XRayInstanceBatchSet Set(params Material[] materials)
        {
            var batches = new List<XRayInstanceBatch>();
            for (int index = 0; index < materials.Length; index++)
            {
                Vector3 center = new Vector3(index * 2f, 0f, 4f);
                batches.Add(new XRayInstanceBatch("Native frontend source " + index, _mesh, new[] { materials[index] }, new Bounds(center, Vector3.one * 2f), new[] { Matrix4x4.Translate(center) }, new[] { new Vector4(index + 0.25f, 0.5f, 0.75f, 0f) }, true));
            }
            XRayInstanceBatchSet set = Own(ScriptableObject.CreateInstance<XRayInstanceBatchSet>());
            set.Initialize("Native frontend owned sources", 16f, batches);
            set.PrepareMatrices("Native frontend ownership " + materials.Length);
            return set;
        }

        private XRayInstanceBatchSet PreparedSet(List<XRayInstanceBatch> batches)
        {
            XRayInstanceBatchSet set = Own(ScriptableObject.CreateInstance<XRayInstanceBatchSet>());
            set.Initialize("Native shadow split sources", 16f, batches);
            set.PrepareMatrices("Native shadow split ownership");
            return set;
        }

        private static void AssertIndexMatchesFresh(XRayInstanceVisibilityIndex cached, XRayInstanceBatchSet set, XRayFloraWindState wind, Bounds[] conservative = null, int revision = 0)
        {
            Plane[][] queries = { new[] { new Plane(Vector3.right, 0f) }, new[] { new Plane(Vector3.right, -8f) }, new[] { new Plane(Vector3.left, 12f) }, new[] { new Plane(Vector3.right, -8f), new Plane(Vector3.left, 12f) }, Array.Empty<Plane>() };
            foreach (Plane[] planes in queries)
            {
                var fresh = new XRayInstanceVisibilityIndex();
                fresh.Ensure(set, conservative, revision);
                fresh.Select(planes, planes.Length, wind);
                cached.Select(planes, planes.Length, wind);
                Assert.That(cached.SelectedCount, Is.EqualTo(fresh.SelectedCount));
                Assert.That(cached.VisitedNodes, Is.EqualTo(fresh.VisitedNodes));
                var actual = new int[cached.SelectedCount];
                var expected = new int[fresh.SelectedCount];
                for (int index = 0; index < actual.Length; index++) actual[index] = cached.SelectedSource(index);
                for (int index = 0; index < expected.Length; index++) expected[index] = fresh.SelectedSource(index);
                Assert.That(actual, Is.EqualTo(expected));
                for (int index = 0; index < set.Batches.Count; index++) Assert.That(cached.IsCandidate(index), Is.EqualTo(fresh.IsCandidate(index)));
                fresh.Ensure(null);
            }
        }

        private void AssertNativeMatchesFresh(XRayInstanceNativeRenderer cached, XRayFloraWindState wind, Plane[] planes, Camera reflection = null)
        {
            var fresh = new XRayInstanceNativeRenderer();
            try
            {
                Assert.That(fresh.Prepare(_renderer.BatchSet, _camera, _root.layer, true, wind), Is.True, fresh.FallbackReason);
                if (reflection != null) Assert.That(fresh.TryRegisterReflection(reflection), Is.True);
                var expected = CaptureNativeSelection(fresh, planes, reflection != null ? 0 : -1);
                var actual = CaptureNativeSelection(cached, planes, reflection != null ? 0 : -1);
                Assert.That(actual.Masks, Is.EqualTo(expected.Masks));
                Assert.That(actual.Sources, Is.EqualTo(expected.Sources));
            }
            finally
            {
                fresh.Dispose();
            }
        }

        private static (ushort[] Masks, int[] Sources) CaptureNativeSelection(XRayInstanceNativeRenderer backend, Plane[] planes, int reflectionIndex = -1)
        {
            ushort[] masks = BackendField<ushort[]>(backend, "_instanceMasks");
            Array.Clear(masks, 0, masks.Length);
            typeof(XRayInstanceNativeRenderer).GetField("_cullingStamp", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(backend, BackendField<int>(backend, "_cullingStamp") + 1);
            typeof(XRayInstanceNativeRenderer).GetField("_selectedCount", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(backend, 0);
            MethodInfo select = typeof(XRayInstanceNativeRenderer).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(select, Is.Not.Null);
            using (var nativePlanes = new NativeArray<Plane>(planes, Allocator.Temp))
            {
                select.Invoke(backend, new object[] { nativePlanes, 0, planes.Length, reflectionIndex });
            }
            int count = BackendField<int>(backend, "_selectedCount");
            var sources = new int[count];
            Array.Copy(BackendField<int[]>(backend, "_selectedSources"), sources, count);
            return ((ushort[])masks.Clone(), sources);
        }

        private static int CacheStamp(object target)
        {
            return (int)target.GetType().GetField("_windBoundsStamp", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }

        private static void SetWindVectors(XRayFloraWindState wind, Vector4 current, Vector4 previous)
        {
            typeof(XRayFloraWindState).GetProperty(nameof(XRayFloraWindState.TreeWind)).GetSetMethod(true).Invoke(wind, new object[] { current });
            typeof(XRayFloraWindState).GetProperty(nameof(XRayFloraWindState.TreeWindPrev)).GetSetMethod(true).Invoke(wind, new object[] { previous });
        }

        private static ushort[] SelectShadowSources(XRayInstanceNativeRenderer backend, Plane[] planes, CullingSplit[] splits)
        {
            NativeArray<Plane> nativePlanes = default;
            NativeArray<CullingSplit> nativeSplits = default;
            try
            {
                if (planes != null) nativePlanes = new NativeArray<Plane>(planes, Allocator.Temp);
                if (splits != null) nativeSplits = new NativeArray<CullingSplit>(splits, Allocator.Temp);
                var context = (BatchCullingContext)Activator.CreateInstance(typeof(BatchCullingContext), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { nativePlanes, nativeSplits, default(LODParameters), Matrix4x4.identity, BatchCullingViewType.Light, default(BatchCullingProjectionType), default(BatchCullingFlags), 0UL, uint.MaxValue, ulong.MaxValue, (byte)0, 0, 0, IntPtr.Zero }, null);
                Assert.That(SHADOW_SELECTION, Is.Not.Null);
                SHADOW_SELECTION.Invoke(backend, new object[] { context });
                return (ushort[])BackendField<ushort[]>(backend, "_sourceSplitMasks").Clone();
            }
            finally
            {
                if (nativeSplits.IsCreated) nativeSplits.Dispose();
                if (nativePlanes.IsCreated) nativePlanes.Dispose();
            }
        }

        private static T BackendField<T>(XRayInstanceNativeRenderer backend, string name)
        {
            FieldInfo field = typeof(XRayInstanceNativeRenderer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return (T)field.GetValue(backend);
        }

        private Material CreateMaterial(string name)
        {
            Shader shader = Shader.Find("HDRP/MonolithLit");
            Assert.That(shader, Is.Not.Null);
            var material = Own(new Material(shader) { name = name, enableInstancing = true });
            material.SetFloat("_MonolithLevelWind", 0f);
            material.EnableKeyword("_MONOLITH_LEVEL");
            return material;
        }

        private T Own<T>(T value) where T : UnityEngine.Object
        {
            _owned.Add(value);
            return value;
        }

        private static class MissingShadowCameraScope
        {
            public static int ApiVersion => 1;
        }

        private static class MissingShadowVersionScope
        {
            public static Camera CurrentCamera => null;
        }

        private static class WrongShadowVersionScope
        {
            public static int ApiVersion => 2;
            public static Camera CurrentCamera => null;
        }

        private static class WrongShadowVersionTypeScope
        {
            public static string ApiVersion => "1";
            public static Camera CurrentCamera => null;
        }

        private static class WrongShadowCameraTypeScope
        {
            public static int ApiVersion => 1;
            public static UnityEngine.Object CurrentCamera => null;
        }

        private static class ValidShadowCameraScope
        {
            public static int ApiVersion => 1;
            public static Camera CurrentCamera { get; set; }
        }

        private T Field<T>(string name)
        {
            return (T)typeof(XRayLevelInstancedRenderer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_renderer);
        }

        private static GraphicsBuffer Buffer(XRayInstanceNativeRenderer renderer)
        {
            return (GraphicsBuffer)typeof(XRayInstanceNativeRenderer).GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(renderer);
        }

        private void SetField(string name, object value)
        {
            typeof(XRayLevelInstancedRenderer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_renderer, value);
        }

        private int EndContextSubscriptions()
        {
            FieldInfo field = typeof(RenderPipelineManager).GetField("endContextRendering", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var callbacks = field.GetValue(null) as Delegate;
            int count = 0;
            if (callbacks == null) return count;
            foreach (Delegate callback in callbacks.GetInvocationList())
            {
                if (ReferenceEquals(callback.Target, _renderer) && callback.Method == END) count++;
            }
            return count;
        }
    }
}

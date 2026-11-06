using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class XRayWetnessTests
    {
        private const string COMPUTE_PATH = "Assets/_Tools/X-Ray OGF OMG Importer/Runtime/Shaders/MonolithWetness.compute";
        private const float TOLERANCE = 0.00001f;

        private Scene _scene;
        private GameObject _owner;

        [SetUp]
        public void CreateOwnedScene()
        {
            _scene = EditorSceneManager.NewPreviewScene();
            _owner = new GameObject("Wetness regression owner");
            SceneManager.MoveGameObjectToScene(_owner, _scene);
        }

        [TearDown]
        public void ReleaseOwnedScene()
        {
            if (_scene.IsValid() && _scene.isLoaded) EditorSceneManager.ClosePreviewScene(_scene);
        }

        [Test]
        public void ExposedAndCoveredLayersAccumulateAndDryByIndependentRealTimeEquations()
        {
            PrecipitationConfig configuration = Configuration();
            Texture2D heights = Heights(2, 2, new Color(10.0f, 0.0f, 0.0f, 0.0f));
            var field = new XRayWetnessField(_owner);
            string before = JsonUtility.ToJson(configuration);
            try
            {
                Vector4 mapping = new Vector4(0.0f, 0.0f, 0.5f, 0.5f);
                field.Tick(configuration, heights, mapping, 1.0f, 0.0f);
                field.SetUniformState(0.8f, 0.2f);
                field.Tick(configuration, heights, mapping, 1.0f, 10.0f);
                Color covered = Read(field, 0, 0);
                Assert.That(covered.r, Is.EqualTo(0.728f).Within(TOLERANCE));
                Assert.That(covered.g, Is.EqualTo(0.182f).Within(TOLERANCE));
                Assert.That(covered.b, Is.EqualTo(0.956f).Within(TOLERANCE));
                Assert.That(covered.a, Is.EqualTo(0.239f).Within(TOLERANCE));
                field.Tick(configuration, heights, mapping, 0.0f, 10.0f);
                Color drying = Read(field, 0, 0);
                Assert.That(drying.r, Is.EqualTo(0.656f).Within(TOLERANCE));
                Assert.That(drying.g, Is.EqualTo(0.164f).Within(TOLERANCE));
                Assert.That(drying.b, Is.EqualTo(0.884f).Within(TOLERANCE));
                Assert.That(drying.a, Is.EqualTo(0.221f).Within(TOLERANCE));
                Assert.AreEqual(before, JsonUtility.ToJson(configuration));
                Assert.AreEqual(192L, field.EstimatedBytes);
            }
            finally
            {
                field.Dispose();
                Object.DestroyImmediate(heights);
                Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void BriefRainDoesNotCreateImmediateFullWetnessOrPuddles()
        {
            PrecipitationConfig configuration = Configuration();
            Texture2D heights = Heights(2, 2, Color.clear);
            var field = new XRayWetnessField(_owner);
            try
            {
                Vector4 mapping = new Vector4(0.0f, 0.0f, 0.5f, 0.5f);
                field.Tick(configuration, heights, mapping, 1.0f, 10.0f);
                Color state = Read(field, 0, 0);
                Assert.That(state.r, Is.EqualTo(0.156f).Within(TOLERANCE));
                Assert.That(state.b, Is.EqualTo(0.156f).Within(TOLERANCE));
                Assert.AreEqual(0.0f, state.g);
                Assert.AreEqual(0.0f, state.a);
                field.Tick(configuration, heights, mapping, 0.0f, 5.0f);
                state = Read(field, 0, 0);
                Assert.That(state.r, Is.EqualTo(0.12f).Within(TOLERANCE));
                field.Tick(configuration, heights, mapping, 1.0f, 1000.0f);
                state = Read(field, 0, 0);
                Assert.AreEqual(Color.white, state);
                field.Tick(configuration, heights, mapping, 0.0f, 1000.0f);
                Assert.AreEqual(Color.clear, Read(field, 0, 0));
            }
            finally
            {
                field.Dispose();
                Object.DestroyImmediate(heights);
                Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void RainRestartAndZeroDeltaPreservePuddlesWithoutInstantTargetClamping()
        {
            PrecipitationConfig configuration = Configuration();
            Texture2D heights = Heights(2, 2, Color.clear);
            var field = new XRayWetnessField(_owner);
            try
            {
                Vector4 mapping = new Vector4(0.0f, 0.0f, 0.5f, 0.5f);
                field.Tick(configuration, heights, mapping, 1.0f, 0.0f);
                field.SetUniformState(0.8f, 1.0f);
                field.Tick(configuration, heights, mapping, 1.0f, 0.0f);
                Assert.That(Read(field, 0, 0).g, Is.EqualTo(1.0f).Within(TOLERANCE));
                field.Tick(configuration, heights, mapping, 1.0f, 1.0f);
                Color state = Read(field, 0, 0);
                Assert.That(state.r, Is.EqualTo(0.8156f).Within(TOLERANCE));
                Assert.That(state.g, Is.EqualTo(0.9982f).Within(TOLERANCE));
            }
            finally
            {
                field.Dispose();
                Object.DestroyImmediate(heights);
                Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void WorldMappingMovementRetainsCoveredCellsAndKeepsMemoryBounded()
        {
            PrecipitationConfig configuration = Configuration();
            Texture2D heights = Heights(2, 2, Color.clear);
            var field = new XRayWetnessField(_owner);
            try
            {
                field.Tick(configuration, heights, new Vector4(-5.0f, -5.0f, 0.5f, 0.5f), 0.0f, 0.0f);
                field.SetUniformState(0.4f, 0.1f);
                field.Tick(configuration, heights, new Vector4(-4.0f, -5.0f, 0.5f, 0.5f), 0.0f, 0.0f);
                Color retained = Read(field, 0, 0);
                Assert.That(retained.r, Is.EqualTo(0.4f).Within(TOLERANCE));
                Assert.That(retained.g, Is.EqualTo(0.1f).Within(TOLERANCE));
                Assert.AreEqual(Color.clear, Read(field, 1, 0));
                long bytes = field.EstimatedBytes;
                for (int index = 0; index < 32; index++) field.Tick(configuration, heights, new Vector4(index * 20.0f, -5.0f, 0.5f, 0.5f), 0.0f, 0.0f);
                Assert.AreEqual(bytes, field.EstimatedBytes);
                Assert.AreEqual(192L, field.EstimatedBytes);
                field.Dispose();
                Assert.IsNull(field.Buffer);
                Assert.AreEqual(0L, field.EstimatedBytes);
            }
            finally
            {
                field.Dispose();
                Object.DestroyImmediate(heights);
                Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void InvalidHeightCellsRemainDryAndShaderGlobalsRestoreTheirPreviousOwner()
        {
            PrecipitationConfig configuration = Configuration();
            Texture2D heights = Heights(2, 2, new Color(-1.0f, 1.0f, 0.0f, 0.0f));
            var field = new XRayWetnessField(_owner);
            int activeId = Shader.PropertyToID("_MonolithWetnessActive");
            int ownerId = Shader.PropertyToID("_MonolithWetnessOwner");
            int patternsId = Shader.PropertyToID("_MonolithWetSurfaceTextures");
            int mappingId = Shader.PropertyToID("_MonolithWetnessMapping");
            int settingsId = Shader.PropertyToID("_MonolithWetnessSettings");
            int reflectionId = Shader.PropertyToID("_MonolithWetReflectionIntensity");
            float originalActive = Shader.GetGlobalFloat(activeId);
            int originalOwner = Shader.GetGlobalInteger(ownerId);
            Texture originalPatterns = Shader.GetGlobalTexture(patternsId);
            var previousPatterns = new Texture2DArray(2, 2, 2, TextureFormat.RGBA32, false);
            Vector4 originalMapping = Shader.GetGlobalVector(mappingId);
            Vector4 originalSettings = Shader.GetGlobalVector(settingsId);
            float originalReflection = Shader.GetGlobalFloat(reflectionId);
            Assert.AreEqual(0, originalOwner, "Preserve an existing published field owner.");
            Assert.LessOrEqual(originalActive, 0.5f, "Preserve an active field.");
            try
            {
                Vector4 mapping = new Vector4(0.0f, 0.0f, 0.5f, 0.5f);
                field.Tick(configuration, heights, mapping, 1.0f, 1.0f);
                field.SetUniformState(0.8f, 0.5f);
                field.Tick(configuration, heights, mapping, 1.0f, 1.0f);
                Assert.AreEqual(Color.clear, Read(field, 0, 0));
                Shader.SetGlobalFloat(activeId, 0.25f);
                Shader.SetGlobalTexture(patternsId, previousPatterns);
                Shader.SetGlobalVector(mappingId, Vector4.one);
                Shader.SetGlobalVector(settingsId, new Vector4(0.1f, 0.2f, 0.3f, 0.4f));
                Shader.SetGlobalFloat(reflectionId, 0.37f);
                field.Publish(configuration, 1.0f, 1.0f);
                Assert.AreSame(configuration.WetSurfaceTextures, Shader.GetGlobalTexture(patternsId));
                Assert.AreNotEqual(0, Shader.GetGlobalInteger(ownerId));
                Assert.AreEqual(configuration.ReflectionIntensity, Shader.GetGlobalFloat(reflectionId));
                field.Unpublish();
                Assert.AreEqual(0.25f, Shader.GetGlobalFloat(activeId));
                Assert.AreEqual(0, Shader.GetGlobalInteger(ownerId));
                Assert.AreSame(previousPatterns, Shader.GetGlobalTexture(patternsId));
                Assert.AreEqual(Vector4.one, Shader.GetGlobalVector(mappingId));
                Assert.AreEqual(new Vector4(0.1f, 0.2f, 0.3f, 0.4f), Shader.GetGlobalVector(settingsId));
                Assert.AreEqual(0.37f, Shader.GetGlobalFloat(reflectionId));
            }
            finally
            {
                field.Dispose();
                Shader.SetGlobalFloat(activeId, originalActive);
                Shader.SetGlobalInteger(ownerId, originalOwner);
                Shader.SetGlobalTexture(patternsId, originalPatterns);
                Shader.SetGlobalVector(mappingId, originalMapping);
                Shader.SetGlobalVector(settingsId, originalSettings);
                Shader.SetGlobalFloat(reflectionId, originalReflection);
                Object.DestroyImmediate(previousPatterns);
                Object.DestroyImmediate(heights);
                Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void DuplicateFieldOnTheSameOwnerCannotReplaceOrReleaseThePublishedField()
        {
            PrecipitationConfig configuration = Configuration();
            Texture2D heights = Heights(2, 2, Color.clear);
            var first = new XRayWetnessField(_owner);
            var second = new XRayWetnessField(_owner);
            int activeId = Shader.PropertyToID("_MonolithWetnessActive");
            int ownerId = Shader.PropertyToID("_MonolithWetnessOwner");
            int patternsId = Shader.PropertyToID("_MonolithWetSurfaceTextures");
            Assert.AreEqual(0, Shader.GetGlobalInteger(ownerId));
            Assert.LessOrEqual(Shader.GetGlobalFloat(activeId), 0.5f);
            try
            {
                var mapping = new Vector4(0.0f, 0.0f, 0.5f, 0.5f);
                first.Tick(configuration, heights, mapping, 1.0f, 1.0f);
                second.Tick(configuration, heights, mapping, 1.0f, 1.0f);
                first.Publish(configuration, 1.0f, 1.0f);
                int owner = Shader.GetGlobalInteger(ownerId);
                Texture patterns = Shader.GetGlobalTexture(patternsId);
                Assert.Throws<System.InvalidOperationException>(() => second.Publish(configuration, 1.0f, 1.0f));
                second.Dispose();
                Assert.AreEqual(owner, Shader.GetGlobalInteger(ownerId));
                Assert.AreEqual(1.0f, Shader.GetGlobalFloat(activeId));
                Assert.AreSame(patterns, Shader.GetGlobalTexture(patternsId));
                Assert.IsTrue(first.Buffer.IsValid());
                Assert.That(Read(first, 0, 0).r, Is.EqualTo(0.0156f).Within(TOLERANCE));
                first.Publish(configuration, 2.0f, 1.0f);
            }
            finally
            {
                second.Dispose();
                first.Dispose();
                Object.DestroyImmediate(heights);
                Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void QualityResizePreservesStateInWorldSpaceAndUsesTheDeclaredBufferBudget()
        {
            PrecipitationConfig configuration = Configuration();
            Texture2D small = Heights(2, 2, Color.clear);
            Texture2D large = Heights(4, 4, Color.clear);
            var field = new XRayWetnessField(_owner);
            try
            {
                var mapping = new Vector4(0.0f, 0.0f, 0.5f, 0.5f);
                field.Tick(configuration, small, mapping, 0.0f, 0.0f);
                field.SetUniformState(0.4f, 0.1f);
                field.Tick(configuration, large, mapping, 0.0f, 0.0f);
                Assert.AreEqual(4, field.Width);
                Assert.AreEqual(4, field.Height);
                Assert.AreEqual(768L, field.EstimatedBytes);
                Assert.That(Read(field, 2, 2).r, Is.EqualTo(0.4f).Within(TOLERANCE));
                Assert.That(Read(field, 2, 2).g, Is.EqualTo(0.1f).Within(TOLERANCE));
                Assert.IsTrue(field.TryReadCell(2, 2, out XRayWetnessCell cell));
                Assert.AreEqual(Vector2.zero, cell.Heights);
            }
            finally
            {
                field.Dispose();
                Object.DestroyImmediate(small);
                Object.DestroyImmediate(large);
                Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void SharedMaterialClonesRemainSharedAndExternalAssignmentsRestoreToTheirLatestSource()
        {
            PrecipitationConfig configuration = Configuration();
            var sourceX = new Material(Shader.Find("HDRP/MonolithLit")) { name = "asphalt regression source", enableInstancing = true };
            var sourceY = new Material(Shader.Find("HDRP/MonolithLit")) { name = "concrete regression source", enableInstancing = false };
            var registry = new XRayWetMaterialBindings();
            try
            {
                GameObject first = GameObject.CreatePrimitive(PrimitiveType.Cube);
                GameObject second = GameObject.CreatePrimitive(PrimitiveType.Cube);
                SceneManager.MoveGameObjectToScene(first, _scene);
                SceneManager.MoveGameObjectToScene(second, _scene);
                MeshRenderer rendererA = first.GetComponent<MeshRenderer>();
                MeshRenderer rendererB = second.GetComponent<MeshRenderer>();
                rendererA.sharedMaterial = sourceX;
                rendererB.sharedMaterial = sourceX;
                registry.Bind(_scene, configuration);
                Assert.AreEqual(1, registry.CloneCount);
                Assert.AreEqual(2, registry.BindingCount);
                Material cloneX = rendererB.sharedMaterial;
                Assert.AreNotSame(sourceX, cloneX);
                Assert.AreSame(cloneX, rendererA.sharedMaterial);
                Assert.IsTrue(cloneX.enableInstancing);
                rendererA.sharedMaterial = sourceY;
                registry.Bind(_scene, configuration);
                Assert.AreSame(cloneX, rendererB.sharedMaterial);
                Assert.AreNotSame(sourceY, rendererA.sharedMaterial);
                Assert.AreNotSame(cloneX, rendererA.sharedMaterial);
                Assert.IsFalse(rendererA.sharedMaterial.enableInstancing);
                Assert.AreEqual(2, registry.CloneCount);
                registry.Dispose();
                Assert.AreSame(sourceY, rendererA.sharedMaterial);
                Assert.AreSame(sourceX, rendererB.sharedMaterial);
            }
            finally
            {
                registry.Dispose();
                Object.DestroyImmediate(sourceX);
                Object.DestroyImmediate(sourceY);
                Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void SourceRoadblockUsesConcreteResponseWhileRoadAndExcludedSurfacesKeepTheirClassification()
        {
            var material = new Material(Shader.Find("HDRP/MonolithLit")) { name = "prop_roadblock1_l07_military_lmap_11_2" };
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { name = "prop_roadblock1" };
            try
            {
                material.SetTexture("_BaseColorMap", texture);
                Assert.AreEqual(XRayWetSurfaceType.Concrete, XRayWetMaterialBindings.Classify(material));
                material.name = "source level material";
                Assert.AreEqual(XRayWetSurfaceType.Concrete, XRayWetMaterialBindings.Classify(material));
                texture.name = "road_surface";
                Assert.AreEqual(XRayWetSurfaceType.Asphalt, XRayWetMaterialBindings.Classify(material));
                texture.name = "ground_asphalt_01";
                Assert.AreEqual(XRayWetSurfaceType.Asphalt, XRayWetMaterialBindings.Classify(material));
                texture.name = "prop_roadblock2";
                Assert.AreEqual(XRayWetSurfaceType.Asphalt, XRayWetMaterialBindings.Classify(material));
                texture.name = "water_prop_roadblock1";
                Assert.AreEqual(XRayWetSurfaceType.Excluded, XRayWetMaterialBindings.Classify(material));
                texture.name = "glass_prop_roadblock1";
                Assert.AreEqual(XRayWetSurfaceType.Excluded, XRayWetMaterialBindings.Classify(material));
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(texture);
            }
        }

        private static PrecipitationConfig Configuration()
        {
            PrecipitationConfig source = GameConfig.Load().Precipitation;
            Assert.IsNotNull(source);
            PrecipitationConfig configuration = Object.Instantiate(source);
            var serialized = new SerializedObject(configuration);
            serialized.FindProperty("_wetnessCompute").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ComputeShader>(COMPUTE_PATH);
            serialized.FindProperty("_wetnessEnabled").boolValue = true;
            serialized.FindProperty("_puddlesEnabled").boolValue = true;
            serialized.FindProperty("_wettingRate").floatValue = 0.0156f;
            serialized.FindProperty("_dryingRate").floatValue = 0.0072f;
            serialized.FindProperty("_maxWetness").floatValue = 1.0f;
            serialized.FindProperty("_puddleFormationRate").floatValue = 0.0039f;
            serialized.FindProperty("_puddleDryingRate").floatValue = 0.0018f;
            serialized.FindProperty("_maxPuddles").floatValue = 1.0f;
            serialized.FindProperty("_puddleWetnessThreshold").floatValue = 0.65f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsNotNull(configuration.WetnessCompute);
            return configuration;
        }

        private static Texture2D Heights(int width, int height, Color value)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            var pixels = new Color[width * height];
            for (int index = 0; index < pixels.Length; index++) pixels[index] = value;
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static Color Read(XRayWetnessField field, int x, int y)
        {
            Assert.IsTrue(field.TryReadCell(x, y, out XRayWetnessCell cell));
            return new Color(cell.Moisture.x, cell.Moisture.y, cell.Moisture.z, cell.Moisture.w);
        }
    }
}

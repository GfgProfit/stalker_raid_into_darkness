using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer.Editor;
using XRay.Importer.Sources.Shaders;

namespace XRay.Importer.Tests
{
    public sealed class MonolithMaterialTests
    {
        [TestCase(0u, 0.0, 0.0860215053548387)]
        [TestCase(1u, 0.0, 0.3440860214193548)]
        [TestCase(1u, 0.3, 0.43010752677419356)]
        [TestCase(2u, 0.85, 0.8172043008709677)]
        [TestCase(4u, 0.9, 1.118279569612903)]
        public void MaterialIdMatchesAuditPacking(uint id, double weight, double expected)
        {
            Assert.That(MonolithMaterialMath.DecodeMaterial(id, weight), Is.EqualTo(expected).Within(1e-9));
        }

        [TestCase(0.1, 0.71864, 0.8178630388542703)]
        [TestCase(0.1, 0.7556, 0.7933694713086101)]
        [TestCase(0.0, 0.71864, 1.0)]
        public void RoughnessMatchesAuditNumericExamples(double gloss, double dynamicGloss, double expected)
        {
            double material = MonolithMaterialMath.DecodeMaterial(0, 0.0);
            Assert.That(MonolithMaterialMath.Roughness(gloss, material, dynamicGloss), Is.EqualTo(expected).Within(1e-12));
        }

        [Test]
        public void MetalnessRespectsStrictMaterialGateAndGlossThreshold()
        {
            Assert.That(MonolithMaterialMath.Metalness(1.0, 0.751), Is.Zero);
            Assert.That(MonolithMaterialMath.Metalness(0.0, 0.8), Is.Zero);
            Assert.That(MonolithMaterialMath.Metalness(1.0, 0.8), Is.EqualTo(1.0));
        }

        [Test]
        public void HlslMatchesCpuAndPackedNormalFixture()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable.");
            }

            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/MonolithMaterialValidation.compute");
            var inputs = new Vector4[70];
            var outputs = new Vector4[inputs.Length];
            var normals = new Vector4[inputs.Length];
            float[] glosses = { 0.0f, 0.1f, 0.25f, 0.5f, 1.0f };
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i] = new Vector4(i / 10, i % 10 < 5 ? 0.0f : 0.85f, glosses[i % 5], i % 2 == 0 ? 0.71864f : 0.7556f);
            }

            using (var inputBuffer = new ComputeBuffer(inputs.Length, 16))
            using (var outputBuffer = new ComputeBuffer(inputs.Length, 16))
            using (var normalBuffer = new ComputeBuffer(inputs.Length, 16))
            {
                inputBuffer.SetData(inputs);
                int kernel = shader.FindKernel("ValidateMaterial");
                shader.SetBuffer(kernel, "_Inputs", inputBuffer);
                shader.SetBuffer(kernel, "_Outputs", outputBuffer);
                shader.SetBuffer(kernel, "_Normals", normalBuffer);
                shader.Dispatch(kernel, inputs.Length, 1, 1);
                outputBuffer.GetData(outputs);
                normalBuffer.GetData(normals);
            }

            for (int i = 0; i < inputs.Length; i++)
            {
                Vector4 input = inputs[i];
                double material = MonolithMaterialMath.DecodeMaterial((uint)input.x, input.y);
                Assert.That(outputs[i].x, Is.EqualTo(material).Within(2e-6), "Material " + i);
                Assert.That(outputs[i].y, Is.EqualTo(MonolithMaterialMath.Metalness(input.z, material)).Within(2e-5), "Metal " + i);
                Assert.That(outputs[i].z, Is.EqualTo(MonolithMaterialMath.Roughness(input.z, material, input.w)).Within(2e-5), "Rough " + i);
                Assert.That(outputs[i].w, Is.EqualTo(MonolithMaterialMath.Specular(0.5, 0.5, input.z, material)).Within(2e-5), "F0 " + i);
                Assert.That(normals[i].x, Is.EqualTo(0.0f).Within(1e-6));
                Assert.That(normals[i].y, Is.EqualTo(0.0f).Within(1e-6));
                Assert.That(normals[i].z, Is.EqualTo(1.0f).Within(1e-6));
            }
        }

        [Test]
        public void AugTestPreservesAtlasAndMaterialAssignments()
        {
            const string FIXTURE_FOLDER = "Assets/_Source/Items/Weapons/Tests/MonolithHDRP";
            const string MODEL_PATH = "Assets/_Source/Items/Weapons/Models/wpn_aug_a3.ogf";
            const string ALBEDO_PATH = "Assets/_Source/Items/Weapons/Models/Textures/aug_a3.png";
            Shader shader = Shader.Find(MonolithMaterialConfigurator.SHADER_NAME);
            Assert.That(shader, Is.Not.Null);
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
            {
                Assert.That(message.severity, Is.Not.EqualTo(UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error), message.message);
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FIXTURE_FOLDER + "/AUG_A3_Monolith_Test.prefab");
            var monolith = AssetDatabase.LoadAssetAtPath<Material>(FIXTURE_FOLDER + "/AUG_A3_Monolith_Test.mat");
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(ALBEDO_PATH);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(monolith, Is.Not.Null);
            Assert.That(albedo, Is.Not.Null);
            Assert.That(monolith.shader, Is.EqualTo(shader));
            Assert.That(monolith.GetTexture("_BaseColorMap"), Is.EqualTo(albedo));
            var bump = monolith.GetTexture("_MonolithBump") as Texture2D;
            Assert.That(bump, Is.Not.Null);
            Assert.That(bump.isDataSRGB, Is.False);
            Assert.That(monolith.GetFloat("_MonolithBumpFlipV"), Is.EqualTo(1.0f));

            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.enabled)
                {
                    Assert.That(renderer.sharedMaterials, Is.All.EqualTo(monolith), renderer.name);
                }
            }

            var production = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Source/Items/Weapons/Models/Materials/aug_a3.mat");
            Assert.That(production.shader, Is.EqualTo(shader));
            var importer = AssetImporter.GetAtPath(MODEL_PATH);
            Assert.That(importer.GetExternalObjectMap().Values, Does.Contain(production));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void ConversionPreservesSurfaceAndNativeNormalBinding(bool transparent, bool objectSpace)
        {
            var material = new Material(Shader.Find("HDRP/Lit"));
            var normal = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                Color color = new Color(0.3f, 0.5f, 0.7f, 0.4f);
                Color emission = new Color(0.1f, 0.2f, 0.3f, 1.0f);
                Vector2 scale = new Vector2(3.0f, 5.0f);
                Vector2 offset = new Vector2(0.2f, 0.4f);
                Vector4 uvMask = new Vector4(0.0f, 1.0f, 0.0f, 0.0f);
                Vector4 doubleSided = new Vector4(-1.0f, -1.0f, -1.0f, 0.0f);
                material.SetTexture("_BaseColorMap", Texture2D.whiteTexture);
                material.SetColor("_BaseColor", color);
                material.SetColor("_EmissiveColor", emission);
                material.SetTextureScale("_BaseColorMap", scale);
                material.SetTextureOffset("_BaseColorMap", offset);
                material.SetFloat("_UVBase", 1.0f);
                material.SetFloat("_DoubleSidedEnable", 1.0f);
                material.SetFloat("_DoubleSidedNormalMode", 0.0f);
                material.SetFloat("_NormalMapSpace", objectSpace ? 1.0f : 0.0f);
                material.SetTexture(objectSpace ? "_NormalMapOS" : "_NormalMap", normal);
                material.SetFloat("_NormalScale", 0.65f);
                HDMaterial.SetSurfaceType(material, transparent);
                HDMaterial.SetAlphaClipping(material, true);
                HDMaterial.SetAlphaCutoff(material, 0.37f);
                HDMaterial.ValidateMaterial(material);
                material.SetVector("_UVMappingMask", uvMask);
                material.SetVector("_DoubleSidedConstants", doubleSided);
                material.SetFloat("_StencilRef", 7.0f);
                material.SetFloat("_StencilWriteMask", 15.0f);
                material.renderQueue = transparent ? 3100 : 2475;
                float zWrite = material.GetFloat("_ZWrite");
                float zTest = material.GetFloat("_ZTestDepthEqualForOpaque");
                float srcBlend = material.GetFloat("_SrcBlend");
                float dstBlend = material.GetFloat("_DstBlend");
                float alphaSrcBlend = material.GetFloat("_AlphaSrcBlend");
                float alphaDstBlend = material.GetFloat("_AlphaDstBlend");
                float cull = material.GetFloat("_CullMode");
                float forwardCull = material.GetFloat("_CullModeForward");
                Color savedColor = ReadSavedColor(material, "_BaseColor");
                Color savedEmission = ReadSavedColor(material, "_EmissiveColor");
                Assert.That(material.IsKeywordEnabled("_NORMALMAP"), Is.True);
                MonolithMaterialConfigurator.ConfigurePreservingSurface(material, null, Texture2D.whiteTexture, null, null, true);
                Assert.That(material.shader.name, Is.EqualTo(MonolithMaterialConfigurator.SHADER_NAME));
                Assert.That(ReadSavedColor(material, "_BaseColor").Equals(savedColor), Is.True);
                Assert.That(ReadSavedColor(material, "_EmissiveColor").Equals(savedEmission), Is.True);
                Assert.That(material.GetTextureScale("_BaseColorMap"), Is.EqualTo(scale));
                Assert.That(material.GetTextureOffset("_BaseColorMap"), Is.EqualTo(offset));
                Assert.That(material.GetVector("_UVMappingMask"), Is.EqualTo(uvMask));
                Assert.That(material.GetVector("_DoubleSidedConstants"), Is.EqualTo(doubleSided));
                Assert.That(material.GetFloat("_SurfaceType"), Is.EqualTo(transparent ? 1.0f : 0.0f));
                Assert.That(material.GetFloat("_AlphaCutoff"), Is.EqualTo(0.37f));
                Assert.That(material.GetFloat("_DoubleSidedEnable"), Is.EqualTo(1.0f));
                Assert.That(material.GetFloat("_ZWrite"), Is.EqualTo(zWrite));
                Assert.That(material.GetFloat("_ZTestDepthEqualForOpaque"), Is.EqualTo(zTest));
                Assert.That(material.GetFloat("_SrcBlend"), Is.EqualTo(srcBlend));
                Assert.That(material.GetFloat("_DstBlend"), Is.EqualTo(dstBlend));
                Assert.That(material.GetFloat("_AlphaSrcBlend"), Is.EqualTo(alphaSrcBlend));
                Assert.That(material.GetFloat("_AlphaDstBlend"), Is.EqualTo(alphaDstBlend));
                Assert.That(material.GetFloat("_CullMode"), Is.EqualTo(cull));
                Assert.That(material.GetFloat("_CullModeForward"), Is.EqualTo(forwardCull));
                Assert.That(material.GetFloat("_StencilRef"), Is.EqualTo(7.0f));
                Assert.That(material.GetFloat("_StencilWriteMask"), Is.EqualTo(15.0f));
                Assert.That(material.renderQueue, Is.EqualTo(transparent ? 3100 : 2475));
                Assert.That(material.IsKeywordEnabled("_ALPHATEST_ON"), Is.True);
                Assert.That(material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), Is.EqualTo(transparent));
                Assert.That(material.IsKeywordEnabled("_NORMALMAP"), Is.True);
                Assert.That(material.IsKeywordEnabled("_NORMALMAP_TANGENT_SPACE"), Is.EqualTo(!objectSpace));
                Assert.That(material.GetTexture(objectSpace ? "_NormalMapOS" : "_NormalMap"), Is.SameAs(normal));
                Assert.That(material.GetFloat("_NormalScale"), Is.EqualTo(0.65f));
                Assert.That(material.GetFloat("_MonolithHasBump"), Is.Zero);
                Assert.That(material.GetFloat("_MonolithOriginalTbn"), Is.Zero);
                Assert.That(material.GetFloat("_MonolithSkinnedTbn"), Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(normal);
            }
        }

        [Test]
        public void PackedSourceNormalTakesPrecedenceOverLegacyNormalMap()
        {
            var material = new Material(Shader.Find("HDRP/Lit"));
            var normal = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            var packed = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                material.SetTexture("_NormalMap", normal);
                HDMaterial.ValidateMaterial(material);
                Assert.That(material.IsKeywordEnabled("_NORMALMAP"), Is.True);
                MonolithMaterialConfigurator.ConfigurePreservingSurface(material, null, Texture2D.whiteTexture, packed, null, true);
                Assert.That(material.GetTexture("_MonolithBump"), Is.SameAs(packed));
                Assert.That(material.GetFloat("_MonolithHasBump"), Is.EqualTo(1.0f));
                Assert.That(material.IsKeywordEnabled("_NORMALMAP"), Is.False);
                Assert.That(material.IsKeywordEnabled("_NORMALMAP_TANGENT_SPACE"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(normal);
                Object.DestroyImmediate(packed);
            }
        }

        [Test]
        public void ConversionPreservesExactSerializedTintWhenLegacyColorDiffersByOneUlp()
        {
            var material = new Material(Shader.Find("HDRP/Lit"));
            Color tint = new Color(0.62f, 0.6f, 0.55f, 1.0f);
            Color legacy = new Color(0.62f, 0.6f, 0.549999952f, 1.0f);
            try
            {
                material.SetColor("_BaseColor", tint);
                material.SetColor("_Color", legacy);
                using (var serialized = new SerializedObject(material))
                {
                    SerializedProperty colors = serialized.FindProperty("m_SavedProperties.m_Colors");
                    for (int i = 0; i < colors.arraySize; i++)
                    {
                        SerializedProperty entry = colors.GetArrayElementAtIndex(i);
                        if (entry.FindPropertyRelative("first").stringValue == "_BaseColor")
                        {
                            entry.FindPropertyRelative("second").colorValue = tint;
                        }
                    }

                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                MonolithMaterialConfigurator.ConfigurePreservingSurface(material, null, null, null, null, true);
                using (var serialized = new SerializedObject(material))
                {
                    SerializedProperty colors = serialized.FindProperty("m_SavedProperties.m_Colors");
                    int checkedColors = 0;
                    for (int i = 0; i < colors.arraySize; i++)
                    {
                        SerializedProperty entry = colors.GetArrayElementAtIndex(i);
                        string name = entry.FindPropertyRelative("first").stringValue;
                        if (name == "_BaseColor" || name == "_Color")
                        {
                            Color actual = entry.FindPropertyRelative("second").colorValue;
                            Color expected = name == "_BaseColor" ? tint : legacy;
                            Assert.That(actual.Equals(expected), Is.True, name);
                            checkedColors++;
                        }
                    }

                    Assert.That(checkedColors, Is.EqualTo(2));
                }
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [TestCase(0.0f)]
        [TestCase(0.15f)]
        [TestCase(0.5f)]
        [TestCase(0.9f)]
        [TestCase(1.0f)]
        public void ConversionPreservesRepresentableDryRoughness(float smoothness)
        {
            var material = new Material(Shader.Find("HDRP/Lit"));
            try
            {
                material.SetFloat("_Smoothness", smoothness);
                MonolithMaterialConfigurator.ConfigurePreservingSurface(material, null, Texture2D.whiteTexture, null, null, true);
                double decoded = MonolithMaterialMath.DecodeMaterial(1, 0.0);
                double dynamicGloss = material.GetFloat("_MonolithDynamicGloss");
                double minimumRoughness = MonolithMaterialMath.Roughness(1.0, decoded, dynamicGloss);
                double expected = System.Math.Max(1.0 - smoothness, minimumRoughness);
                double gloss = material.GetFloat("_MonolithFallbackGloss");
                Assert.That(MonolithMaterialMath.Roughness(gloss, decoded, dynamicGloss), Is.EqualTo(expected).Within(0.000001));
                Assert.That(MonolithMaterialMath.Roughness(MonolithMaterialMath.QuantizeGloss(gloss), decoded, dynamicGloss), Is.EqualTo(expected).Within(0.004));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void ConversionClearsIncompatibleNativeMaterialFeatures()
        {
            var material = new Material(Shader.Find("HDRP/Lit"));
            string[] keywords = { "_MATERIAL_FEATURE_SUBSURFACE_SCATTERING", "_MATERIAL_FEATURE_TRANSMISSION", "_MATERIAL_FEATURE_ANISOTROPY", "_MATERIAL_FEATURE_IRIDESCENCE", "_MATERIAL_FEATURE_CLEAR_COAT" };
            try
            {
                foreach (string keyword in keywords)
                {
                    material.EnableKeyword(keyword);
                }

                MonolithMaterialConfigurator.ConfigurePreservingSurface(material, null, Texture2D.whiteTexture, null, null, true);
                foreach (string keyword in keywords)
                {
                    Assert.That(material.IsKeywordEnabled(keyword), Is.False, keyword);
                }

                Assert.That(material.GetFloat("_MaterialID"), Is.EqualTo(4.0f));
                Assert.That(material.IsKeywordEnabled("_MATERIAL_FEATURE_SPECULAR_COLOR"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void ConvertedNativeNormalMapChangesRenderedWorldNormalWithoutPackedBump()
        {
            HdrpTestWarmup.Ensure();
            bool asyncCompilation = ShaderUtil.allowAsyncCompilation;
            Scene scene = EditorSceneManager.NewPreviewScene();
            var material = new Material(Shader.Find("HDRP/Lit"));
            var normal = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var target = new RenderTexture(32, 32, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var cameraObject = new GameObject("Monolith native normal regression camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            RenderTexture active = RenderTexture.active;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                SceneManager.MoveGameObjectToScene(quad, scene);
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                normal.SetPixel(0, 0, new Color(0.5f, 0.5f, 1.0f, 1.0f));
                normal.Apply();
                material.SetTexture("_NormalMap", normal);
                HDMaterial.ValidateMaterial(material);
                MonolithMaterialConfigurator.ConfigurePreservingSurface(material, null, Texture2D.whiteTexture, null, null, true);
                material.SetFloat("_MonolithDebug", 12.0f);
                quad.GetComponent<MeshRenderer>().sharedMaterial = material;
                camera.scene = scene;
                camera.enabled = false;
                camera.transform.position = new Vector3(0.0f, 0.0f, -2.0f);
                camera.orthographic = true;
                camera.orthographicSize = 0.4f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 10.0f;
                camera.targetTexture = target;
                HDAdditionalCameraData data = cameraObject.AddComponent<HDAdditionalCameraData>();
                data.volumeLayerMask = 0;
                data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
                data.backgroundColorHDR = Color.black;
                data.customRenderingSettings = true;
                data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.Postprocess, false);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.Postprocess] = true;
                target.Create();
                camera.Render();
                camera.Render();
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(16, 16, 1, 1), 0, 0);
                readback.Apply();
                Color flat = readback.GetPixel(0, 0);
                normal.SetPixel(0, 0, new Color(0.5f, 0.9f, 1.0f, 1.0f));
                normal.Apply();
                camera.Render();
                camera.Render();
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(16, 16, 1, 1), 0, 0);
                readback.Apply();
                Color perturbed = readback.GetPixel(0, 0);
                Assert.That(flat.maxColorComponent, Is.GreaterThan(0.1f));
                Assert.That(Mathf.Abs(flat.g - perturbed.g), Is.GreaterThan(0.1f));
                Assert.That(material.GetFloat("_MonolithHasBump"), Is.Zero);
            }
            finally
            {
                RenderTexture.active = active;
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                camera.targetTexture = null;
                target.Release();
                Object.DestroyImmediate(quad);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(normal);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(readback);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void GenerationModePreservesLegacyAndSurvivesReimport()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = AssetGenerationTests.CreateFixture(directory);
                using (var scope = new XRayImportTestScope(fixture.CreateConfiguration("Main")))
                {
                    scope.Options.Import.MaterialGenerationMode = XRayMaterialGenerationMode.HdrpLitLegacy;
                    string model = scope.ImportModel("wpn_monolith_test.ogf", AssetGenerationTests.CreateModel());
                    Material legacy = XRayImportTestScope.GetRendererMaterial(model, 0);
                    scope.Options.Import.MaterialGenerationMode = XRayMaterialGenerationMode.MonolithHdrp;
                    XRayAssetGenerationCoordinator.Process(model, scope.Options, true, out _);
                    scope.Settle();
                    Material monolith = XRayImportTestScope.GetRendererMaterial(model, 0);
                    Assert.That(monolith, Is.Not.SameAs(legacy));
                    Assert.That(monolith.shader.name, Is.EqualTo(MonolithMaterialConfigurator.SHADER_NAME));
                    Assert.That(legacy.shader.name, Is.EqualTo("HDRP/Lit"));
                    Assert.That(monolith.GetTexture("_MonolithBump"), Is.Not.Null);
                    Assert.That(((Texture2D)monolith.GetTexture("_MonolithBump")).isDataSRGB, Is.False);
                    scope.Reimport(model);
                    Assert.That(XRayImportTestScope.GetRendererMaterial(model, 0), Is.SameAs(monolith));
                    scope.Options.Import.MaterialGenerationMode = XRayMaterialGenerationMode.HdrpLitLegacy;
                    XRayAssetGenerationCoordinator.Process(model, scope.Options, true, out _);
                    scope.Settle();
                    Assert.That(XRayImportTestScope.GetRendererMaterial(model, 0), Is.SameAs(legacy));
                }
            }
        }

        private static Color ReadSavedColor(Material material, string name)
        {
            using (var serialized = new SerializedObject(material))
            {
                SerializedProperty colors = serialized.FindProperty("m_SavedProperties.m_Colors");
                for (int i = 0; i < colors.arraySize; i++)
                {
                    SerializedProperty entry = colors.GetArrayElementAtIndex(i);
                    if (entry.FindPropertyRelative("first").stringValue == name)
                    {
                        return entry.FindPropertyRelative("second").colorValue;
                    }
                }
            }

            throw new System.InvalidOperationException("The material does not contain a serialized color: " + name);
        }
    }
}

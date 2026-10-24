using NUnit.Framework;
using UnityEditor;
using UnityEngine;
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
            Assert.That(MonolithAugA3TestSetup.Validate(), Does.Contain("shaderErrors=0"));
            var legacy = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Source/Items/Weapons/Models/Materials/aug_a3.mat");
            Assert.That(legacy.shader.name, Is.EqualTo("HDRP/Lit"));
            var monolith = AssetDatabase.LoadAssetAtPath<Material>(MonolithAugA3TestSetup.MATERIAL_PATH);
            Assert.That(((Texture2D)monolith.GetTexture("_MonolithBump")).isDataSRGB, Is.False);
            Assert.That(monolith.GetFloat("_MonolithBumpFlipV"), Is.EqualTo(1.0f));
            var importer = AssetImporter.GetAtPath(MonolithAugA3TestSetup.MODEL_PATH);
            Assert.That(importer.GetExternalObjectMap().Values, Does.Contain(legacy));
        }

        [Test]
        public void GenerationModePreservesLegacyAndSurvivesReimport()
        {
            using (var directory = new TestDirectory())
            {
                Mo2Fixture fixture = AssetGenerationTests.CreateFixture(directory);
                using (var scope = new XRayImportTestScope(fixture.CreateConfiguration("Main")))
                {
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
    }
}

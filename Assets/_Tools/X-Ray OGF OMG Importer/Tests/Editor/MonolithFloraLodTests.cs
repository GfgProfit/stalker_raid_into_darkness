using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Sources.Shaders;

namespace XRay.Importer.Tests
{
    public sealed class MonolithFloraLodTests
    {
        private const string COMPUTE_PATH = "Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/MonolithMaterialValidation.compute";
        private const int FLORA_COUNT = 512;

        private static readonly string[] _realTextures = { "Assets/_Source/Levels/Textures/trees/trees_vetkabig_bump_packed.dds", "Assets/_Source/Levels/Textures/trees/trees_elka_bump_packed.dds", "Assets/_Source/Levels/Textures/trees/trees_a_bush_bump_packed.dds", "Assets/_Source/Levels/Textures/levels/escape/level_lods_nm_packed.png" };
        private static readonly float[] _edgeValues = { 0.0f, 64.0f / 255.0f, 128.0f / 255.0f, 200.0f / 255.0f, 1.0f };

        [TestCase(0.0, 0.0)]
        [TestCase(64.0 / 255.0, 16.0 / 255.0)]
        [TestCase(128.0 / 255.0, 64.0 / 255.0)]
        [TestCase(200.0 / 255.0, 157.0 / 255.0)]
        [TestCase(1.0, 1.0)]
        public void FloraGlossIsTrueStalkerSquare(double packed, double expected)
        {
            Assert.That(MonolithMaterialMath.QuantizeGloss(MonolithMaterialMath.FloraGloss(packed, 1.0)), Is.EqualTo(expected).Within(1e-12));
            Assert.That(MonolithMaterialMath.FloraGloss(packed, 1.0), Is.LessThanOrEqualTo(packed));
        }

        [Test]
        public void FloraGlossRaisesRoughnessOnlyBelowFullGloss()
        {
            double material = MonolithMaterialMath.DecodeMaterial(0, 0.1);
            foreach (float packed in _edgeValues)
            {
                double legacy = MonolithMaterialMath.Roughness(MonolithMaterialMath.QuantizeGloss(packed), material, 0.0);
                double flora = MonolithMaterialMath.Roughness(MonolithMaterialMath.QuantizeGloss(MonolithMaterialMath.FloraGloss(packed, 1.0)), material, 0.0);
                if (packed <= 0.0f || packed >= 1.0f)
                {
                    Assert.That(flora, Is.EqualTo(legacy).Within(1e-12));
                }
                else
                {
                    Assert.That(flora, Is.GreaterThan(legacy));
                }
            }
        }

        [TestCase(0.0f)]
        [TestCase(90.0f)]
        [TestCase(200.0f)]
        public void LodNormalIsViewSpaceAndFacesCamera(float yaw)
        {
            Matrix4x4 worldToView = WorldToView(yaw, 8.0f);
            Vector3 forward = Quaternion.Euler(8.0f, yaw, 0.0f) * Vector3.forward;
            Vector3 right = Quaternion.Euler(8.0f, yaw, 0.0f) * Vector3.right;
            Vector3 facing = Lod(new Vector3(0.5f, 0.5f, 0.0f), worldToView);
            Vector3 side = Lod(new Vector3(1.0f, 0.5f, 0.5f), worldToView);
            Assert.That(Vector3.Dot(facing, -forward), Is.EqualTo(1.0f).Within(1e-5f));
            Assert.That(Vector3.Dot(side, right), Is.EqualTo(1.0f).Within(1e-5f));
            Assert.That(Lod(Vector3.one * 0.5f, worldToView).y, Is.EqualTo(Vector3.Dot(Vector3.up, worldToView.GetRow(1))).Within(1e-5f));
        }

        [Test]
        public void LodNormalRotatesWithCamera()
        {
            Vector3 packed = new Vector3(0.6f, 0.8f, 0.2f);
            Vector3 front = Lod(packed, WorldToView(0.0f, 0.0f));
            Vector3 back = Lod(packed, WorldToView(180.0f, 0.0f));
            Assert.That(front.x, Is.EqualTo(-back.x).Within(1e-5f));
            Assert.That(front.y, Is.EqualTo(back.y).Within(1e-5f));
            Assert.That(front.z, Is.EqualTo(-back.z).Within(1e-5f));
            Assert.That(front.z, Is.LessThan(0.0f));
        }

        [Test]
        [Category("ImportedLevelIntegration")]
        public void HlslFloraAndLodMatchCpuOnRealTextures()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable.");
            }

            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(COMPUTE_PATH);
            Matrix4x4 worldToView = WorldToView(37.0f, 12.0f);
            int tested = 0;
            foreach (string path in _realTextures)
            {
                if (!System.IO.File.Exists(path))
                {
                    continue;
                }

                var bump = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.IsNotNull(bump, path);
                if (bump == null)
                {
                    continue;
                }

                tested++;
                var inputs = new Vector4[FLORA_COUNT];
                for (int i = 0; i < inputs.Length; i++)
                {
                    inputs[i] = new Vector4(_edgeValues[i % 5], _edgeValues[i / 5 % 5], _edgeValues[i / 25 % 5], _edgeValues[i / 125 % 5]);
                }

                var outputs = new Vector4[FLORA_COUNT * 2];
                var normals = new Vector4[FLORA_COUNT];
                var packed = new Vector4[FLORA_COUNT];
                using (var inputBuffer = new ComputeBuffer(FLORA_COUNT, 16))
                using (var outputBuffer = new ComputeBuffer(FLORA_COUNT * 2, 16))
                using (var normalBuffer = new ComputeBuffer(FLORA_COUNT, 16))
                using (var packedBuffer = new ComputeBuffer(FLORA_COUNT, 16))
                {
                    inputBuffer.SetData(inputs);
                    int kernel = shader.FindKernel("ValidateFlora");
                    shader.SetBuffer(kernel, "_Inputs", inputBuffer);
                    shader.SetBuffer(kernel, "_Outputs", outputBuffer);
                    shader.SetBuffer(kernel, "_Normals", normalBuffer);
                    shader.SetBuffer(kernel, "_Packed", packedBuffer);
                    shader.SetTexture(kernel, "_FloraBump", bump);
                    shader.SetVector("_ViewRight", worldToView.GetRow(0));
                    shader.SetVector("_ViewUp", worldToView.GetRow(1));
                    shader.SetVector("_ViewBack", worldToView.GetRow(2));
                    shader.SetFloat("_GlossScale", 1.0f);
                    shader.SetInt("_FloraWidth", bump.width);
                    shader.SetInt("_FloraHeight", bump.height);
                    shader.SetInt("_FloraCount", FLORA_COUNT);
                    shader.Dispatch(kernel, FLORA_COUNT / 64, 1, 1);
                    outputBuffer.GetData(outputs);
                    normalBuffer.GetData(normals);
                    packedBuffer.GetData(packed);
                }

                double material = MonolithMaterialMath.DecodeMaterial(0, 0.1);
                for (int i = 0; i < FLORA_COUNT; i++)
                {
                    Vector4 value = packed[i];
                    double gloss = MonolithMaterialMath.FloraGloss(value.x, 1.0);
                    double quantized = MonolithMaterialMath.QuantizeGloss(gloss);
                    string label = path + " " + i;
                    Assert.That(outputs[i + FLORA_COUNT].w, Is.EqualTo(gloss).Within(1e-6), "Gloss " + label);
                    Assert.That(outputs[i].x, Is.EqualTo(quantized).Within(1e-6), "Quantized " + label);
                    Assert.That(outputs[i].y, Is.EqualTo(MonolithMaterialMath.Roughness(quantized, material, 0.0)).Within(2e-5), "Rough " + label);
                    Assert.That(outputs[i].z, Is.EqualTo(MonolithMaterialMath.Specular(0.5, 0.5, quantized, material)).Within(2e-5), "F0 " + label);
                    Assert.That(outputs[i].w, Is.EqualTo(material).Within(2e-6), "Material " + label);
                    MonolithMaterialMath.FloraNormal(value.w, value.z, out double fx, out double fy, out double fz);
                    Assert.That(normals[i].x, Is.EqualTo(fx).Within(2e-6), "Flora normal x " + label);
                    Assert.That(normals[i].y, Is.EqualTo(fy).Within(2e-6), "Flora normal y " + label);
                    Assert.That(normals[i].z, Is.EqualTo(fz).Within(2e-6), "Flora normal z " + label);
                    Vector3 lod = Lod(new Vector3(value.x, value.y, value.z), worldToView);
                    Assert.That(outputs[i + FLORA_COUNT].x, Is.EqualTo(lod.x).Within(2e-5), "Lod normal x " + label);
                    Assert.That(outputs[i + FLORA_COUNT].y, Is.EqualTo(lod.y).Within(2e-5), "Lod normal y " + label);
                    Assert.That(outputs[i + FLORA_COUNT].z, Is.EqualTo(lod.z).Within(2e-5), "Lod normal z " + label);
                }
            }

            if (tested == 0)
            {
                Assert.Ignore("Optional fixtures missing: import True Stalker escape textures to Assets/_Source/Levels/Textures before running this integration test.");
            }
        }

        private static Matrix4x4 WorldToView(float yaw, float pitch)
        {
            Matrix4x4 cameraToWorld = Matrix4x4.TRS(new Vector3(3.0f, 4.0f, -5.0f), Quaternion.Euler(pitch, yaw, 0.0f), Vector3.one);
            return Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f)) * cameraToWorld.inverse;
        }

        private static Vector3 Lod(Vector3 packed, Matrix4x4 worldToView)
        {
            Vector4 right = worldToView.GetRow(0);
            Vector4 up = worldToView.GetRow(1);
            Vector4 back = worldToView.GetRow(2);
            MonolithMaterialMath.LodNormal(packed.x, packed.y, packed.z, new double[] { right.x, right.y, right.z }, new double[] { up.x, up.y, up.z }, new double[] { back.x, back.y, back.z }, out double x, out double y, out double z);
            return new Vector3((float)x, (float)y, (float)z);
        }
    }
}

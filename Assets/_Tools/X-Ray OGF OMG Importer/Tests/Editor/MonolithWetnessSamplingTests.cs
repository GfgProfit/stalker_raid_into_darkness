using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class MonolithWetnessSamplingTests
    {
        private const string SHADER_PATH = "Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/Fixtures/MonolithWetnessSampling.compute";
        private const float TOLERANCE = 0.00001f;

        [Test]
        public void SamplingIsContinuousAcrossCellBoundariesAndPreservesCellCenters()
        {
            var left = new WetCell(new Vector4(0.2f, 0.1f, 0.2f, 0.1f), Vector2.zero);
            var right = new WetCell(new Vector4(0.8f, 0.6f, 0.8f, 0.6f), Vector2.zero);
            var positions = new[] { new Vector4(0.5f, 0.0f, 1.0f), new Vector4(0.999f, 0.0f, 1.0f), new Vector4(1.0f, 0.0f, 1.0f), new Vector4(1.001f, 0.0f, 1.0f), new Vector4(1.5f, 0.0f, 1.0f) };
            Vector4[] results = Sample(new[] { left, right, left, right }, positions, Vector3.up);
            AssertMoisture(results[0], 0.2f, 0.1f);
            AssertMoisture(results[2], 0.5f, 0.35f);
            AssertMoisture(results[4], 0.8f, 0.6f);
            Assert.That(Mathf.Abs(results[3].x - results[1].x), Is.LessThan(0.005f));
            Assert.That(Mathf.Abs(results[3].y - results[1].y), Is.LessThan(0.005f));
        }

        [Test]
        public void SamplingResolvesEachShelteredLayerBeforeInterpolation()
        {
            var covered = new WetCell(new Vector4(0.1f, 0.05f, 0.9f, 0.7f), new Vector2(10.0f, 0.0f));
            var exposed = new WetCell(new Vector4(0.1f, 0.05f, 0.8f, 0.6f), Vector2.zero);
            var positions = new[] { new Vector4(1.0f, 0.0f, 1.0f), new Vector4(1.0f, 10.0f, 1.0f) };
            Vector4[] results = Sample(new[] { covered, exposed, covered, exposed }, positions, Vector3.up);
            AssertMoisture(results[0], 0.45f, 0.325f);
            AssertMoisture(results[1], 0.85f, 0.65f);
        }

        [Test]
        public void OpenSlopesUseTheSurfacePlaneAtEachCellCenter()
        {
            var left = new WetCell(new Vector4(0.1f, 0.05f, 0.9f, 0.7f), new Vector2(0.5f, 0.5f));
            var right = new WetCell(new Vector4(0.1f, 0.05f, 0.9f, 0.7f), new Vector2(1.5f, 1.5f));
            Vector4[] results = Sample(new[] { left, right, left, right }, new[] { new Vector4(1.0f, 1.0f, 1.0f) }, new Vector3(-1.0f, 1.0f, 0.0f).normalized);
            AssertMoisture(results[0], 0.9f, 0.7f);
        }

        [Test]
        public void InvalidColumnsStayDryAndFieldEdgesClampWithoutWrapping()
        {
            var invalid = new WetCell(Vector4.one, new Vector2(-1.0f, 1.0f));
            var valid = new WetCell(new Vector4(0.8f, 0.6f, 0.8f, 0.6f), Vector2.zero);
            var positions = new[] { new Vector4(0.01f, 0.0f, 1.0f), new Vector4(1.0f, 0.0f, 1.0f), new Vector4(1.99f, 0.0f, 1.0f), new Vector4(-0.01f, 0.0f, 1.0f), new Vector4(2.0f, 0.0f, 1.0f) };
            Vector4[] results = Sample(new[] { invalid, valid, invalid, valid }, positions, Vector3.up);
            AssertMoisture(results[0], 0.0f, 0.0f);
            AssertMoisture(results[1], 0.4f, 0.3f);
            AssertMoisture(results[2], 0.8f, 0.6f);
            AssertMoisture(results[3], 0.0f, 0.0f);
            AssertMoisture(results[4], 0.0f, 0.0f);
        }

        [Test]
        public void DryAndDisabledSurfacesPreserveAlbedoAndGlossWhileWallsRejectPuddles()
        {
            var positions = new[] { new Vector4(0.3f, 0.0f, 0.2f), new Vector4(1.3f, 0.0f, 1.2f) };
            var dry = new Vector4[positions.Length];
            SurfaceResult result = Surface(positions, dry, Vector3.up, true);
            AssertUnchanged(result);
            var wet = new[] { Vector4.one, Vector4.one };
            result = Surface(positions, wet, Vector3.up, false);
            AssertUnchanged(result);
            result = Surface(positions, wet, Vector3.right, true);
            foreach (Vector4 value in result.Response) Assert.That(value.y, Is.EqualTo(0.0f).Within(TOLERANCE));
        }

        [Test]
        public void StandingWaterGrowsContinuouslyAndNormalsRemainFinite()
        {
            var positions = new Vector4[25];
            var moisture = new Vector4[positions.Length];
            for (int index = 0; index < positions.Length; index++)
            {
                positions[index] = new Vector4(0.37f, 0.0f, 0.83f, 0.0f);
                moisture[index] = new Vector4(1.0f, index / (positions.Length - 1.0f), 0.0f, 0.0f);
            }
            SurfaceResult result = Surface(positions, moisture, Vector3.up, true);
            Assert.That(result.Response[0].y, Is.EqualTo(0.0f).Within(TOLERANCE));
            Assert.That(result.Response[result.Response.Length - 1].y, Is.GreaterThan(0.1f));
            bool intermediate = false;
            for (int index = 0; index < positions.Length; index++)
            {
                Vector4 response = result.Response[index];
                Assert.IsTrue(float.IsFinite(response.y));
                Assert.That(response.y, Is.InRange(0.0f, 1.0f));
                if (index > 0) Assert.That(response.y + TOLERANCE, Is.GreaterThanOrEqualTo(result.Response[index - 1].y));
                intermediate |= response.y > 0.01f && response.y < 0.99f;
                Vector4 normal = result.Normals[index];
                Assert.IsTrue(float.IsFinite(normal.x) && float.IsFinite(normal.y) && float.IsFinite(normal.z));
                Assert.That(new Vector3(normal.x, normal.y, normal.z).magnitude, Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(response.z, Is.InRange(0.25f, 1.0f));
            }
            Assert.IsTrue(intermediate, "Water coverage must have a gradual boundary.");
        }

        private static Vector4[] Sample(WetCell[] cells, Vector4[] positions, Vector3 normal)
        {
            ComputeShader shader = Shader();
            try
            {
                int kernel = shader.FindKernel("SampleMoisture");
                var normals = new Vector4[positions.Length];
                var results = new Vector4[positions.Length];
                for (int index = 0; index < normals.Length; index++) normals[index] = normal;
                using (var field = new ComputeBuffer(cells.Length, 24))
                using (var input = new ComputeBuffer(positions.Length, 16))
                using (var geometricNormals = new ComputeBuffer(normals.Length, 16))
                using (var output = new ComputeBuffer(results.Length, 16))
                {
                    field.SetData(cells);
                    input.SetData(positions);
                    geometricNormals.SetData(normals);
                    shader.SetBuffer(kernel, "_MonolithWetnessCells", field);
                    shader.SetBuffer(kernel, "_Positions", input);
                    shader.SetBuffer(kernel, "_Normals", geometricNormals);
                    shader.SetBuffer(kernel, "_Results", output);
                    shader.SetInt("_SampleCount", positions.Length);
                    shader.SetFloat("_MonolithWetnessActive", 1.0f);
                    shader.SetVector("_MonolithWetnessMapping", new Vector4(0.0f, 0.0f, 0.5f, 0.5f));
                    shader.SetVector("_MonolithWetnessCellSize", new Vector4(1.0f, 1.0f, 2.0f, 2.0f));
                    shader.SetVector("_MonolithWetnessSettings", new Vector4(1.0f, 1.0f, 0.1f, 1.0f));
                    shader.Dispatch(kernel, (positions.Length + 63) / 64, 1, 1);
                    output.GetData(results);
                }
                return results;
            }
            finally
            {
                Object.DestroyImmediate(shader);
            }
        }

        private static SurfaceResult Surface(Vector4[] positions, Vector4[] moisture, Vector3 normal, bool active)
        {
            ComputeShader shader = Shader();
            var textures = new Texture2DArray(4, 4, 2, TextureFormat.RGBAFloat, false, true);
            try
            {
                var pixels = new Color[16];
                for (int index = 0; index < pixels.Length; index++) pixels[index] = new Color(0.5f, 0.5f, 1.0f, 1.0f);
                textures.SetPixels(pixels, 0);
                textures.SetPixels(pixels, 1);
                textures.Apply(false, false);
                int kernel = shader.FindKernel("SampleSurface");
                var normals = new Vector4[positions.Length];
                var result = new SurfaceResult(positions.Length);
                for (int index = 0; index < normals.Length; index++) normals[index] = normal;
                using (var input = new ComputeBuffer(positions.Length, 16))
                using (var geometricNormals = new ComputeBuffer(normals.Length, 16))
                using (var inputMoisture = new ComputeBuffer(moisture.Length, 16))
                using (var output = new ComputeBuffer(positions.Length, 16))
                using (var albedo = new ComputeBuffer(positions.Length, 16))
                using (var surfaceNormals = new ComputeBuffer(positions.Length, 16))
                {
                    input.SetData(positions);
                    geometricNormals.SetData(normals);
                    inputMoisture.SetData(moisture);
                    shader.SetBuffer(kernel, "_Positions", input);
                    shader.SetBuffer(kernel, "_Normals", geometricNormals);
                    shader.SetBuffer(kernel, "_Moisture", inputMoisture);
                    shader.SetBuffer(kernel, "_Results", output);
                    shader.SetBuffer(kernel, "_Albedo", albedo);
                    shader.SetBuffer(kernel, "_SurfaceNormals", surfaceNormals);
                    shader.SetTexture(kernel, "_MonolithWetSurfaceTextures", textures);
                    shader.SetInt("_SampleCount", positions.Length);
                    shader.SetFloat("_MonolithWetnessActive", active ? 1.0f : 0.0f);
                    shader.SetFloat("_MonolithPuddleMaskSlice", -1.0f);
                    shader.SetVector("_MonolithWetnessSettings", new Vector4(1.0f, 1.0f, 0.1f, 0.8f));
                    shader.SetVector("_MonolithPuddleSettings", new Vector4(0.15f, 1.0f, 7.0f, 2.0f));
                    shader.SetVector("_MonolithWetResponse", new Vector4(0.65f, 1.0f, 1.0f, 1.0f));
                    shader.Dispatch(kernel, (positions.Length + 63) / 64, 1, 1);
                    output.GetData(result.Response);
                    albedo.GetData(result.Albedo);
                    surfaceNormals.GetData(result.Normals);
                }
                return result;
            }
            finally
            {
                Object.DestroyImmediate(textures);
                Object.DestroyImmediate(shader);
            }
        }

        private static ComputeShader Shader()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders are unavailable.");
            ComputeShader source = AssetDatabase.LoadAssetAtPath<ComputeShader>(SHADER_PATH);
            Assert.IsNotNull(source);
            return Object.Instantiate(source);
        }

        private static void AssertMoisture(Vector4 actual, float wetness, float puddles)
        {
            Assert.That(actual.x, Is.EqualTo(wetness).Within(TOLERANCE));
            Assert.That(actual.y, Is.EqualTo(puddles).Within(TOLERANCE));
            Assert.AreEqual(1.0f, actual.w);
        }

        private static void AssertUnchanged(SurfaceResult result)
        {
            for (int index = 0; index < result.Response.Length; index++)
            {
                AssertMoisture(result.Response[index], 0.0f, 0.0f);
                Assert.That(result.Response[index].z, Is.EqualTo(0.25f).Within(TOLERANCE));
                Assert.That(result.Albedo[index].x, Is.EqualTo(0.7f).Within(TOLERANCE));
                Assert.That(result.Albedo[index].y, Is.EqualTo(0.6f).Within(TOLERANCE));
                Assert.That(result.Albedo[index].z, Is.EqualTo(0.5f).Within(TOLERANCE));
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WetCell
        {
            private Vector4 _moisture;
            private Vector2 _heights;

            public WetCell(Vector4 moisture, Vector2 heights)
            {
                _moisture = moisture;
                _heights = heights;
            }
        }

        private sealed class SurfaceResult
        {
            public Vector4[] Response { get; }
            public Vector4[] Albedo { get; }
            public Vector4[] Normals { get; }

            public SurfaceResult(int count)
            {
                Response = new Vector4[count];
                Albedo = new Vector4[count];
                Normals = new Vector4[count];
            }
        }
    }
}

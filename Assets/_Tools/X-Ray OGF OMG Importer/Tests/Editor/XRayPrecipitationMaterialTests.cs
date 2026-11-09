using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class XRayPrecipitationMaterialTests
    {
        private const string BUBBLE_SHADER = "Assets/_Source/Anomalies/SpatialBubble/Presentation/Src/Shaders/SpatialBubbleMesh.shader";
        private const string MAIN_TEXTURE_SHADER = "Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/Shaders/XRayPrecipitationMainTexture.shader";

        [Test]
        public void TexturelessProceduralMaterialsKeepStableSignaturesAndUseOpaqueWhiteFallback()
        {
            Material material = CreateMaterial(AssetDatabase.LoadAssetAtPath<Shader>(BUBBLE_SHADER));
            try
            {
                Assert.That(material.HasProperty("_BaseColorMap"), Is.False);
                Assert.That(material.HasProperty("_MainTex"), Is.False);
                Assert.That(material.HasProperty("_DestinationViewTex"), Is.True);
                material.SetTexture("_DestinationViewTex", Texture2D.blackTexture);
                int signature = Signature(material);
                Assert.That(Signature(material), Is.EqualTo(signature));
                material.SetTexture("_DestinationViewTex", Texture2D.whiteTexture);
                Assert.That(Signature(material), Is.EqualTo(signature));
                Assert.That(TryProperties(material, out MaterialPropertyBlock transparent), Is.False);
                Assert.That(transparent, Is.Null);
                material.renderQueue = (int)RenderQueue.Geometry;
                Assert.That(TryProperties(material, out MaterialPropertyBlock opaque), Is.True);
                Assert.That(opaque.GetTexture("_XRayRainRoofTexture"), Is.EqualTo(Texture2D.whiteTexture));
                Assert.That(TryProperties(null, out MaterialPropertyBlock missing), Is.False);
                Assert.That(missing, Is.Null);
                Assert.DoesNotThrow(() => Signature(null));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void HdrpAndLegacyMainTexturesRemainBoundAndInvalidateGeometrySignatures()
        {
            foreach (string shaderName in new[] { "HDRP/Lit", "Unlit/Texture" })
            {
                Material material = CreateMaterial(Shader.Find(shaderName));
                try
                {
                    string property = shaderName == "HDRP/Lit" ? "_BaseColorMap" : "_MainTex";
                    Assert.That(material.HasProperty(property), Is.True);
                    material.renderQueue = (int)RenderQueue.Geometry;
                    material.SetTexture(property, Texture2D.whiteTexture);
                    int signature = Signature(material);
                    Assert.That(TryProperties(material, out MaterialPropertyBlock first), Is.True);
                    Assert.That(first.GetTexture("_XRayRainRoofTexture"), Is.EqualTo(Texture2D.whiteTexture));
                    material.SetTexture(property, Texture2D.blackTexture);
                    Assert.That(Signature(material), Is.Not.EqualTo(signature));
                    Assert.That(TryProperties(material, out MaterialPropertyBlock second), Is.True);
                    Assert.That(second.GetTexture("_XRayRainRoofTexture"), Is.EqualTo(Texture2D.blackTexture));
                }
                finally
                {
                    Object.DestroyImmediate(material);
                }
            }
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MainTextureAttributeTakesPriorityOverLegacyNamedTexture()
        {
            Material material = CreateMaterial(AssetDatabase.LoadAssetAtPath<Shader>(MAIN_TEXTURE_SHADER));
            try
            {
                material.renderQueue = (int)RenderQueue.Geometry;
                material.SetTexture("_SourceTexture", Texture2D.blackTexture);
                material.SetTexture("_MainTex", Texture2D.whiteTexture);
                int signature = Signature(material);
                Assert.That(TryProperties(material, out MaterialPropertyBlock first), Is.True);
                Assert.That(first.GetTexture("_XRayRainRoofTexture"), Is.EqualTo(Texture2D.blackTexture));
                material.SetTexture("_MainTex", Texture2D.blackTexture);
                Assert.That(Signature(material), Is.EqualTo(signature));
                material.SetTexture("_SourceTexture", Texture2D.whiteTexture);
                Assert.That(Signature(material), Is.Not.EqualTo(signature));
                Assert.That(TryProperties(material, out MaterialPropertyBlock second), Is.True);
                Assert.That(second.GetTexture("_XRayRainRoofTexture"), Is.EqualTo(Texture2D.whiteTexture));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
            LogAssert.NoUnexpectedReceived();
        }

        private static Material CreateMaterial(Shader shader)
        {
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;
            return material;
        }

        private static int Signature(Material material)
        {
            MethodInfo method = typeof(XRayPrecipitationGeometry).GetMethod("MaterialSignature", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (int)method.Invoke(null, new object[] { 17, material });
        }

        private static bool TryProperties(Material material, out MaterialPropertyBlock properties)
        {
            MethodInfo method = typeof(XRayPrecipitationGeometry).GetMethod("TryCreateProperties", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object[] arguments = { material, null };
            bool result = (bool)method.Invoke(null, arguments);
            properties = (MaterialPropertyBlock)arguments[1];
            return result;
        }
    }
}

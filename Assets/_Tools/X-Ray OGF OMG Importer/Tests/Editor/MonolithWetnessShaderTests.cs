using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class MonolithWetnessShaderTests
    {
        private const string SHADER_NAME = "HDRP/MonolithLit";
        private const string WET_KEYWORD = "_MONOLITH_PRECIPITATION";
        private const string SOURCE_KEYWORD = "_MONOLITH_WET_ANOMALY_TERRAIN";
        private const int MAX_WET_TEXTURE_REGISTER = 63;

        private static IEnumerable<TestCaseData> ForwardProfiles()
        {
            foreach (ShaderCompilerPlatform compiler in new[] { ShaderCompilerPlatform.D3D, ShaderCompilerPlatform.D3D12 })
            {
                foreach (bool clustered in new[] { false, true })
                {
                    foreach (bool instancing in new[] { false, true })
                    {
                        foreach (bool fourTargets in new[] { false, true }) yield return new TestCaseData(compiler, clustered, instancing, fourTargets);
                    }
                }
            }
        }

        [TestCaseSource(nameof(ForwardProfiles))]
        [Timeout(120000)]
        public void SpecializedWetForwardRetainsBoundedResourcesAndExactInactiveLayout(ShaderCompilerPlatform compiler, bool clustered, bool instancing, bool fourTargets)
        {
            Shader shader = Shader.Find(SHADER_NAME);
            Assert.NotNull(shader);
            ShaderData.Pass pass = Pass(shader, "ForwardOnly");
            List<string> keywords = Keywords(shader, clustered, instancing, fourTargets);
            ShaderData.VariantCompileInfo baseline = Compile(pass, keywords, compiler);
            keywords.Add(SOURCE_KEYWORD);
            ShaderData.VariantCompileInfo inactive = Compile(pass, keywords, compiler);
            CollectionAssert.AreEquivalent(Textures(baseline), Textures(inactive));
            CollectionAssert.AreEquivalent(Resources(baseline), Resources(inactive));
            keywords.Add(WET_KEYWORD);
            ShaderData.VariantCompileInfo wet = Compile(pass, keywords, compiler);
            var baselineNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShaderData.TextureBindingInfo texture in baseline.TextureBindings) baselineNames.Add(texture.Name);
            var wetNames = new HashSet<string>(StringComparer.Ordinal);
            int additions = 0;
            foreach (ShaderData.TextureBindingInfo texture in wet.TextureBindings)
            {
                wetNames.Add(texture.Name);
                Assert.That(texture.Index, Is.LessThanOrEqualTo(MAX_WET_TEXTURE_REGISTER), texture.Name);
                if (!baselineNames.Contains(texture.Name))
                {
                    Assert.AreEqual("_MonolithWetSurfaceTextures", texture.Name);
                    additions++;
                }
                Assert.That(texture.Name, Is.Not.EqualTo("_BaseColorMap"));
                Assert.That(texture.Name, Is.Not.EqualTo("_MonolithTrueStalkerMaterialLut"));
                Assert.That(texture.Name, Is.Not.EqualTo("_PreIntegratedFGD_GGXDisneyDiffuse"));
                Assert.That(texture.Name, Does.Not.StartWith("_MonolithTerrainDetail"));
            }
            Assert.AreEqual(1, additions);
            var unusedLegacyNames = new HashSet<string>(new[] { "_BaseColorMap", "_MonolithTerrainDetailR", "_MonolithTerrainDetailG", "_MonolithTerrainDetailB", "_MonolithTerrainDetailA", "_MonolithTrueStalkerMaterialLut", "_PreIntegratedFGD_GGXDisneyDiffuse" }, StringComparer.Ordinal);
            foreach (string name in baselineNames)
            {
                if (!unusedLegacyNames.Contains(name)) Assert.IsTrue(wetNames.Contains(name), "Required legacy texture disappeared: " + name);
            }
            AssertResource(wet, "_MonolithWetSurfaceTextures", "Texture");
            AssertResource(wet, "_MonolithWetnessCells", "Buffer");
            var baselineResources = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShaderData.ResourceBindingInfo resource in baseline.ResourceBindings) baselineResources.Add(resource.Name);
            int resourceAdditions = 0;
            foreach (ShaderData.ResourceBindingInfo resource in wet.ResourceBindings)
            {
                if (baselineResources.Contains(resource.Name)) continue;
                Assert.That(resource.Name, Is.EqualTo("_MonolithWetSurfaceTextures").Or.EqualTo("_MonolithWetnessCells"));
                resourceAdditions++;
            }
            Assert.AreEqual(2, resourceAdditions);
        }

        [TestCase(ShaderCompilerPlatform.D3D)]
        [TestCase(ShaderCompilerPlatform.D3D12)]
        [Timeout(120000)]
        public void WetDepthNormalPassCompilesWithBoundedReadOnlyResources(ShaderCompilerPlatform compiler)
        {
            Shader shader = Shader.Find(SHADER_NAME);
            Assert.NotNull(shader);
            ShaderData.Pass pass = Pass(shader, "DepthForwardOnly");
            List<string> keywords = Keywords(shader, false, true, true);
            ShaderData.VariantCompileInfo baseline = Compile(pass, keywords, compiler);
            keywords.Add(SOURCE_KEYWORD);
            ShaderData.VariantCompileInfo inactive = Compile(pass, keywords, compiler);
            CollectionAssert.AreEquivalent(Textures(baseline), Textures(inactive));
            CollectionAssert.AreEquivalent(Resources(baseline), Resources(inactive));
            keywords.Add(WET_KEYWORD);
            ShaderData.VariantCompileInfo wet = Compile(pass, keywords, compiler);
            foreach (ShaderData.TextureBindingInfo texture in wet.TextureBindings) Assert.That(texture.Index, Is.LessThanOrEqualTo(MAX_WET_TEXTURE_REGISTER), texture.Name);
            AssertResource(wet, "_MonolithWetSurfaceTextures", "Texture");
            AssertResource(wet, "_MonolithWetnessCells", "Buffer");
        }

        [TestCase("_MonolithSourceProfile", 0.0f)]
        [TestCase("_MonolithSourceTerrain", 0.0f)]
        [TestCase("_MonolithTerrainSource", 0.0f)]
        [TestCase("_MonolithTerrainSource", 2.0f)]
        [TestCase("_MonolithHasTerrainSourceColor", 0.0f)]
        [TestCase("_MonolithTrueStalkerSourceTerrain", 1.0f)]
        public void CachedMaterialRefreshDropsSpecializationWhenSourceFlagsChange(string property, float value)
        {
            Shader shader = Shader.Find(SHADER_NAME);
            Assert.NotNull(shader);
            var source = new Material(shader);
            var clone = new Material(shader);
            PrecipitationConfig config = ScriptableObject.CreateInstance<PrecipitationConfig>();
            MethodInfo configure = typeof(XRayWetMaterialBindings).GetMethod("Configure", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(configure);
            Vector4 response = new Vector4(0.61f, 0.42f, 0.23f, 1.0f);
            try
            {
                source.EnableKeyword("_MONOLITH_LEVEL_TERRAIN");
                source.SetFloat("_MonolithSourceProfile", 1.0f);
                source.SetFloat("_MonolithSourceTerrain", 1.0f);
                source.SetFloat("_MonolithTerrainSource", 1.0f);
                source.SetFloat("_MonolithHasTerrainSourceColor", 1.0f);
                source.SetFloat("_MonolithTrueStalkerSourceTerrain", 0.0f);
                source.SetVector("_MonolithWetResponse", response);
                configure.Invoke(null, new object[] { source, clone, config });
                Assert.IsTrue(clone.IsKeywordEnabled(SOURCE_KEYWORD));
                source.SetFloat(property, value);
                configure.Invoke(null, new object[] { source, clone, config });
                Assert.IsFalse(clone.IsKeywordEnabled(SOURCE_KEYWORD));
                Assert.AreEqual(response, clone.GetVector("_MonolithWetResponse"));
            }
            finally
            {
                Object.DestroyImmediate(clone);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void TerrainKeywordIsRequiredForSpecialization()
        {
            Shader shader = Shader.Find(SHADER_NAME);
            Assert.NotNull(shader);
            var material = new Material(shader);
            try
            {
                material.SetFloat("_MonolithSourceProfile", 1.0f);
                material.SetFloat("_MonolithSourceTerrain", 1.0f);
                material.SetFloat("_MonolithTerrainSource", 1.0f);
                material.SetFloat("_MonolithHasTerrainSourceColor", 1.0f);
                material.SetFloat("_MonolithTrueStalkerSourceTerrain", 0.0f);
                MethodInfo predicate = typeof(XRayWetMaterialBindings).GetMethod("HasAnomalyTerrainSource", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.NotNull(predicate);
                Assert.IsFalse((bool)predicate.Invoke(null, new object[] { material }));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        private static ShaderData.Pass Pass(Shader shader, string name)
        {
            ShaderData.Subshader subshader = ShaderUtil.GetShaderData(shader).GetSubshader(0);
            for (int i = 0; i < subshader.PassCount; i++)
            {
                ShaderData.Pass pass = subshader.GetPass(i);
                if (pass.Name == name) return pass;
            }
            throw new InvalidOperationException("Missing Monolith shader pass: " + name);
        }

        private static List<string> Keywords(Shader shader, bool clustered, bool instancing, bool fourTargets)
        {
            var available = new HashSet<string>(StringComparer.Ordinal);
            foreach (var keyword in shader.keywordSpace.keywords) available.Add(keyword.name);
            var keywords = new List<string>();
            foreach (string keyword in new[] { "_MATERIAL_FEATURE_SPECULAR_COLOR", "_MONOLITH_LEVEL", "_MONOLITH_LEVEL_TERRAIN", "_NORMALMAP_TANGENT_SPACE", "MATERIAL_QUALITY_HIGH", "PROBE_VOLUMES_L1", "PUNCTUAL_SHADOW_HIGH", "DIRECTIONAL_SHADOW_HIGH", "AREA_SHADOW_MEDIUM", "SCREEN_SPACE_SHADOWS_ON", "RENDERING_LAYERS", "UNITY_HDR_ON", clustered ? "USE_CLUSTERED_LIGHTLIST" : "USE_FPTL_LIGHTLIST", fourTargets ? "DECALS_4RT" : "DECALS_3RT" })
            {
                if (available.Contains(keyword)) keywords.Add(keyword);
            }
            if (instancing) keywords.Add("INSTANCING_ON");
            return keywords;
        }

        private static ShaderData.VariantCompileInfo Compile(ShaderData.Pass pass, List<string> keywords, ShaderCompilerPlatform compiler)
        {
            ShaderData.VariantCompileInfo result = pass.CompileVariant(ShaderType.Fragment, keywords.ToArray(), compiler, BuildTarget.StandaloneWindows64);
            Assert.IsTrue(result.Success, compiler + " " + string.Join(" ", keywords));
            Assert.That(result.ShaderData, Is.Not.Null.And.Not.Empty);
            return result;
        }

        private static List<string> Textures(ShaderData.VariantCompileInfo info)
        {
            var values = new List<string>();
            foreach (ShaderData.TextureBindingInfo binding in info.TextureBindings) values.Add(binding.Name + ":" + binding.Index + ":" + binding.SamplerIndex);
            return values;
        }

        private static List<string> Resources(ShaderData.VariantCompileInfo info)
        {
            var values = new List<string>();
            foreach (ShaderData.ResourceBindingInfo binding in info.ResourceBindings) values.Add(binding.Name + ":" + binding.Index + ":" + binding.Kind + ":" + binding.Writable);
            return values;
        }

        private static void AssertResource(ShaderData.VariantCompileInfo info, string name, string kind)
        {
            int matches = 0;
            foreach (ShaderData.ResourceBindingInfo binding in info.ResourceBindings)
            {
                if (binding.Name != name) continue;
                Assert.AreEqual(kind, binding.Kind.ToString());
                Assert.IsFalse(binding.Writable);
                matches++;
            }
            Assert.AreEqual(1, matches, name);
        }
    }
}

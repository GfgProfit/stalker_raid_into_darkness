using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class MonolithGrassBenderShaderTests
    {
        private const string SHADER_PATH = "Assets/_Tools/X-Ray OGF OMG Importer/Runtime/Shaders/MonolithLit.shader";

        private static readonly string[] REQUIRED_PASSES = { "ScenePickingPass", "SceneSelectionPass", "META", "ShadowCaster", "DepthForwardOnly", "MotionVectors", "ForwardOnly" };

        [Test]
        [Timeout(240000)]
        public void DetailPassesCompileWithoutInstancing()
        {
            CompileProfile(false);
        }

        [Test]
        [Timeout(240000)]
        public void DetailPassesCompileWithInstancing()
        {
            CompileProfile(true);
        }

        private static void CompileProfile(bool instancing)
        {
            Assert.IsFalse(EditorApplication.isPlayingOrWillChangePlaymode, "Grass shader compilation requires Edit Mode");
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(SHADER_PATH);
            Assert.NotNull(shader, SHADER_PATH);
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, enableInstancing = instancing };
            try
            {
                foreach (string property in new[] { "_MonolithGrassRuntime", "_MonolithGrassSource", "_MonolithLevelWind", "_MonolithGrassHeight", "_MonolithGrassHeightPrevious" })
                {
                    Assert.IsTrue(material.HasProperty(property), property);
                    material.SetFloat(property, 1f);
                }
                string[] keywords = Keywords(shader, instancing);
                material.shaderKeywords = keywords;
                ShaderData.Subshader subshader = ShaderUtil.GetShaderData(shader).GetSubshader(0);
                var passes = new HashSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < subshader.PassCount; index++)
                {
                    ShaderData.Pass pass = subshader.GetPass(index);
                    passes.Add(pass.Name);
                    foreach (ShaderCompilerPlatform platform in new[] { ShaderCompilerPlatform.D3D, ShaderCompilerPlatform.D3D12 })
                    {
                        foreach (ShaderType stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
                        {
                            ShaderData.VariantCompileInfo result = pass.CompileVariant(stage, keywords, platform, BuildTarget.StandaloneWindows64);
                            string context = Context(pass.Name, stage, platform, keywords, result);
                            Assert.IsTrue(result.Success, context);
                            Assert.That(result.ShaderData, Is.Not.Null.And.Not.Empty, context);
                            foreach (ShaderMessage message in result.Messages)
                            {
                                Assert.AreNotEqual("Error", message.severity.ToString(), context);
                            }
                        }
                    }
                }
                foreach (string pass in REQUIRED_PASSES)
                {
                    Assert.IsTrue(passes.Contains(pass), "Missing HDRP pass: " + pass);
                }
                Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "MonolithLit contains shader errors");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        private static string[] Keywords(Shader shader, bool instancing)
        {
            var available = new HashSet<string>(StringComparer.Ordinal);
            foreach (LocalKeyword keyword in shader.keywordSpace.keywords)
            {
                available.Add(keyword.name);
            }
            var keywords = new List<string> { "_MONOLITH_LEVEL_DETAIL", "_ALPHATEST_ON" };
            foreach (string required in keywords)
            {
                Assert.IsTrue(available.Contains(required), "Missing grass keyword: " + required);
            }
            foreach (string keyword in new[] { "MATERIAL_QUALITY_HIGH", "PROBE_VOLUMES_L1", "PUNCTUAL_SHADOW_HIGH", "DIRECTIONAL_SHADOW_HIGH", "AREA_SHADOW_MEDIUM", "SCREEN_SPACE_SHADOWS_OFF", "USE_FPTL_LIGHTLIST", "DECALS_OFF" })
            {
                if (available.Contains(keyword))
                {
                    keywords.Add(keyword);
                }
            }
            if (instancing)
            {
                keywords.Add("INSTANCING_ON");
            }
            keywords.Sort(StringComparer.Ordinal);
            return keywords.ToArray();
        }

        private static string Context(string pass, ShaderType stage, ShaderCompilerPlatform platform, string[] keywords, ShaderData.VariantCompileInfo result)
        {
            var builder = new StringBuilder();
            builder.Append(platform).Append(' ').Append(pass).Append(' ').Append(stage).Append(' ').Append(string.Join(" ", keywords));
            foreach (ShaderMessage message in result.Messages)
            {
                builder.AppendLine().Append(message.severity).Append(' ').Append(message.file).Append(':').Append(message.line).Append(' ').Append(message.message);
            }
            return builder.ToString();
        }
    }
}

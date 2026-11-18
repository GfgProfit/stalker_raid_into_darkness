using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor.Rendering;
using UnityEngine;
using XRay.Importer.Editor;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayNativeSubmissionBuildValidationTests
    {
        [Test]
        public void NativeStrippingRunsAfterPipelineStrippersAndKeepsEarlyBuildValidation()
        {
            Assert.That(new XRayNativeSubmissionShaderStripping().callbackOrder, Is.EqualTo(int.MaxValue));
            Assert.That(new XRayNativeSubmissionBuildValidation().callbackOrder, Is.EqualTo(int.MinValue));
        }

        [Test]
        public void OtherShadersKeepAllCompilerEntries()
        {
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);
            var variants = new List<ShaderCompilerData> { default, default };
            new XRayNativeSubmissionShaderStripping().OnProcessShader(shader, default, variants);
            Assert.That(variants.Count, Is.EqualTo(2));
        }

        [Test]
        public void NativeEligibilityRestrictionsKeepTraditionalShaderVariantsAvailable()
        {
            string[] lines = File.ReadAllLines("Assets/_Tools/X-Ray OGF OMG Importer/Runtime/Shaders/MonolithLit.shader");
            string source = string.Join("\n", lines);
            StringAssert.Contains("#pragma multi_compile_instancing", source);
            StringAssert.Contains("#pragma multi_compile _ DOTS_INSTANCING_ON", source);
            StringAssert.Contains("#pragma shader_feature_local _MONOLITH_LEVEL_DETAIL", source);
            StringAssert.Contains("#pragma shader_feature_local _MONOLITH_SKINNED_TBN", source);
            StringAssert.Contains("#pragma shader_feature_local _MONOLITH_LEVEL_LOD", source);
            StringAssert.Contains("#pragma multi_compile _ LIGHTMAP_ON", source);
            StringAssert.Contains("#pragma multi_compile _ LOD_FADE_CROSSFADE", source);
            foreach (string line in lines)
            {
                if (!line.TrimStart().StartsWith("#pragma skip_variants")) continue;
                string[] tokens = line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                foreach (string token in tokens)
                {
                    Assert.That(XRayInstanceNativeRenderer.UnsupportedShaderKeywords, Does.Not.Contain(token), token);
                }
            }
        }
    }
}

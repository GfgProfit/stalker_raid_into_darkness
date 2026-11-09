using NUnit.Framework;
using RaidIntoDarkness.Effects;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace RaidIntoDarkness.Player.Tests.Editor
{
    public sealed class SpatialBubbleParticleFramesTests
    {
        [Test]
        public void RandomStartUsesTheSourceThirtyTwoFramesWithinTheSixtyFourCellTexture()
        {
            for (uint seed = 0; seed < 1024; seed++)
            {
                float frame = SpatialBubbleParticleFrames.EvaluateFrame(10f, seed, 32, 32f, false, true, false);
                Assert.That(frame, Is.InRange(0f, 31f));
            }
        }

        [Test]
        public void FrameAdvancePreservesTheOriginalEightBitFraction()
        {
            float frame = SpatialBubbleParticleFrames.AdvanceFrame(0f, 45f, 64, false);
            Assert.That(frame, Is.EqualTo(378f / 255f));
            Assert.That(frame, Is.Not.EqualTo(45f * 0.033f));
        }

        [Test]
        public void LogicalFrameCountCanExceedThePhysicalAtlasWithoutPrematureWrapping()
        {
            float frame = SpatialBubbleParticleFrames.AdvanceFrame(31f, 66f, 64, false);
            Assert.That(frame, Is.GreaterThan(32f).And.LessThan(34f));
            frame = SpatialBubbleParticleFrames.AdvanceFrame(4f, 0f, 5, false);
            Assert.That(frame, Is.EqualTo(4f));
        }

        [Test]
        public void BackwardPlaybackWrapsAtTheSourceBoundary()
        {
            float frame = SpatialBubbleParticleFrames.AdvanceFrame(0f, 6f, 64, true);
            Assert.That(frame, Is.EqualTo(63.8f).Within(0.00001f));
            Assert.That(SpatialBubbleParticleFrames.AdvanceFrame(64f, 0f, 64, false), Is.EqualTo(64f));
        }

        [Test]
        public void PlaybackDirectionRemainsStableForEachParticle()
        {
            int forward = 0;
            int backward = 0;
            for (uint seed = 1; seed <= 1024; seed++)
            {
                float first = SpatialBubbleParticleFrames.EvaluateFrame(0f, seed, 64, 6f, true, false, true);
                float second = SpatialBubbleParticleFrames.EvaluateFrame(0.033f, seed, 64, 6f, true, false, true);
                if (first < 1f)
                {
                    forward++;
                    Assert.That(second, Is.GreaterThan(first));
                }
                else
                {
                    backward++;
                    Assert.That(second, Is.LessThan(first));
                }
            }
            Assert.That(forward, Is.GreaterThan(400));
            Assert.That(backward, Is.GreaterThan(400));
        }

        [Test]
        public void DistortionAtlasDefaultsKeepTheExistingFullTextureMaterials()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Source/Anomalies/Electra/Src/Shaders/ElectraDistortion.shader");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            try
            {
                Assert.That(material.GetVector("_FrameSize"), Is.EqualTo(new Vector4(1f, 1f, 0f, 0f)));
                Assert.That(material.GetFloat("_FrameColumns"), Is.EqualTo(1f));
                Material vortex = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Source/Anomalies/Vortex/Src/Materials/pfx_distortion.mat");
                Assert.That(vortex, Is.Not.Null);
                Assert.That(vortex.GetVector("_FrameSize"), Is.EqualTo(new Vector4(1f, 1f, 0f, 0f)));
                Assert.That(vortex.GetFloat("_FrameColumns"), Is.EqualTo(1f));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void DistortionPassCompilesForBothWindowsBackendsAndInstancingKeywords()
        {
            Assert.That(EditorApplication.isPlayingOrWillChangePlaymode, Is.False);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Source/Anomalies/Electra/Src/Shaders/ElectraDistortion.shader");
            Assert.That(shader, Is.Not.Null);
            ShaderData.Subshader subshader = ShaderUtil.GetShaderData(shader).GetSubshader(0);
            Assert.That(subshader.PassCount, Is.EqualTo(1));
            ShaderData.Pass pass = subshader.GetPass(0);
            Assert.That(pass.Name, Is.EqualTo("DistortionVectors"));
            foreach (bool instancing in new[] { false, true })
            {
                string[] keywords = instancing ? new[] { "INSTANCING_ON" } : new string[0];
                foreach (ShaderCompilerPlatform platform in new[] { ShaderCompilerPlatform.D3D, ShaderCompilerPlatform.D3D12 })
                {
                    foreach (ShaderType stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
                    {
                        ShaderData.VariantCompileInfo result = pass.CompileVariant(stage, keywords, platform, BuildTarget.StandaloneWindows64);
                        string context = platform + " " + stage + " " + instancing;
                        Assert.That(result.Success, Is.True, context);
                        Assert.That(result.ShaderData, Is.Not.Null.And.Not.Empty, context);
                        foreach (ShaderMessage message in result.Messages)
                        {
                            Assert.That(message.severity.ToString(), Is.Not.EqualTo("Error"), context + " " + message.message);
                        }
                    }
                }
            }
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
        }
    }
}

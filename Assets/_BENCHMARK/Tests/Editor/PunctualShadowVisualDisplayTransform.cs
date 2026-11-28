using System;

namespace Rvm.Benchmark.Tests
{
    [Serializable]
    internal sealed class PunctualShadowVisualDisplayTransform
    {
        public float white;
        public string mapping = "sRGB(clamp(raw RGB /256,0,1)); one predeclared fixed mapping for every A/B/no-caster/light-off, near/far and Point/Spot/Directional frame.";
        public string raw = "RGBA float32 OpenEXR, finite verified before saving; no per-frame normalization.";
    }
}

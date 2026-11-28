using System;
using UnityEngine;

namespace Rvm.Benchmark.Tests
{
    [Serializable]
    internal sealed class PunctualShadowVisualCaptureRange
    {
        public int width;
        public int height;
        public Color minimum;
        public Color maximum;
    }
}

using System;

namespace Rvm.Benchmark.Tests
{
    [Serializable]
    internal sealed class PunctualShadowVisualComparison
    {
        public string name;
        public string reference;
        public string candidate;
        public string precisionFormat;
        public int rawChangedPixels;
        public int visibleChangedPixels;
        public float maximumError;
        public float maximumResidual;
        public float maximumAlphaError;
    }
}

using System;

namespace Rvm.Benchmark.Tests
{
    [Serializable]
    internal sealed class PunctualShadowVisualComparisonReport
    {
        public string type;
        public PunctualShadowVisualComparison[] comparisons;
    }
}

using System;
using System.Collections.Generic;

namespace Rvm.Benchmark
{
    [Serializable]
    internal sealed class InstancedRendererLocalBenchmarkReport
    {
        public BenchmarkBuildIdentityEvidence identity;
        public List<int> sceneBatchCounts;
        public List<InstancedRendererLocalBenchmarkColdSample> cold;
        public List<InstancedRendererLocalBenchmarkResult> results;
        public string memoryNote;
    }
}

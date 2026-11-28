using System;
using System.Collections.Generic;

namespace Rvm.Benchmark
{
    [Serializable]
    internal sealed class BenchmarkParticleFrameReport
    {
        public string runId;
        public int sampledComponents;
        public List<BenchmarkParticleFrameGroup> groups;
        public BenchmarkParticleFrameCacheEvidence cache;
    }
}

using System;

namespace Rvm.Benchmark
{
    [Serializable]
    internal sealed class BenchmarkParticleFrameCacheEvidence
    {
        public bool present;
        public int configurations;
        public int sequences;
        public long arrayPayloadBytes;
    }
}

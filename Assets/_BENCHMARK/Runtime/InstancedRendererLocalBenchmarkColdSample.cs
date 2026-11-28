using System;

namespace Rvm.Benchmark
{
    [Serializable]
    internal sealed class InstancedRendererLocalBenchmarkColdSample
    {
        public int count;
        public string variant;
        public double ns;
        public long payloadBytes;
    }
}

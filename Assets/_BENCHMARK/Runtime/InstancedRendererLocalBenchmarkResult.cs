using System;

namespace Rvm.Benchmark
{
    [Serializable]
    internal sealed class InstancedRendererLocalBenchmarkResult
    {
        public int count;
        public string scenario;
        public int trial;
        public int calls;
        public double oldNsPerCallback;
        public double newNsPerCallback;
    }
}

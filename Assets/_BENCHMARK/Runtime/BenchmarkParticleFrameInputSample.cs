using System;

namespace Rvm.Benchmark
{
    [Serializable]
    internal sealed class BenchmarkParticleFrameInputSample
    {
        public float age;
        public uint seed;
        public bool infinite;
        public string lifetime;
    }
}

using System;
using System.Collections.Generic;

namespace Rvm.Benchmark
{
    [Serializable]
    internal sealed class BenchmarkParticleFrameGroup
    {
        public string key;
        public int frameCount;
        public float framesPerSecond;
        public bool animated;
        public bool randomStart;
        public bool randomPlayback;
        public int components;
        public long observations;
        public long empty;
        public long liveParticles;
        public long finiteParticles;
        public long infiniteParticles;
        public long sourceSteps;
        public long finiteSystems;
        public long infiniteSystems;
        public float maxAge;
        public float maxLifetime;
        public List<BenchmarkParticleFrameInputSample> samples = new List<BenchmarkParticleFrameInputSample>();
    }
}

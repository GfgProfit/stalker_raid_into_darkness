using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using RaidIntoDarkness.Effects;
using UnityEngine;

namespace Rvm.Benchmark
{
    public static class ParticleFrameLocalBenchmark
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Application.isEditor || Array.IndexOf(args, "-particle-frames-local") < 0)
            {
                return;
            }
            string output = BenchmarkOutput.ValidateDirectory(args[Array.IndexOf(args, "-benchmark-output") + 1]);
            Directory.CreateDirectory(output);
            try
            {
                var proof = BenchmarkBuildIdentity.Capture();
                if (!BenchmarkBuildIdentity.IsVerified(proof))
                {
                    throw new InvalidOperationException("Local benchmark Player provenance is not verified.");
                }
                var type = typeof(AnomalyParticleFrames).Assembly.GetType("RaidIntoDarkness.Effects.FiniteParticleFrameCache", true);
                var optimized = (Func<float, uint, int, float, bool, bool, bool, float>)Delegate.CreateDelegate(typeof(Func<float, uint, int, float, bool, bool, bool, float>), type.GetMethod("Evaluate", BindingFlags.NonPublic | BindingFlags.Static));
                Func<float, uint, int, float, bool, bool, bool, float> reference = ParticleFrameReference.EvaluateFrame;
                var workload = JsonUtility.FromJson<Workload>(File.ReadAllText(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(output), "A_Diagnostic"), "particle-workload.json", SearchOption.AllDirectories)[0]));
                var results = new List<Result>();
                int checkedValues = 0;
                foreach (var group in workload.groups)
                {
                    foreach (float age in new[] { 0f, 0.033f, group.maxAge, 10f, 100f, 140f })
                    {
                        uint seed = group.samples.Count > 0 ? group.samples[0].seed : 42u;
                        type.GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
                        long coldAlloc = GC.GetAllocatedBytesForCurrentThread();
                        long coldStart = Stopwatch.GetTimestamp();
                        float first = optimized(age, seed, group.frameCount, group.framesPerSecond, group.animated, group.randomStart, group.randomPlayback);
                        double coldNs = (Stopwatch.GetTimestamp() - coldStart) * (1000000000.0 / Stopwatch.Frequency);
                        coldAlloc = GC.GetAllocatedBytesForCurrentThread() - coldAlloc;
                        float expected = reference(age, seed, group.frameCount, group.framesPerSecond, group.animated, group.randomStart, group.randomPlayback);
                        if (BitConverter.SingleToInt32Bits(first) != BitConverter.SingleToInt32Bits(expected))
                        {
                            throw new InvalidOperationException("Independent oracle mismatch in local Player inputs.");
                        }
                        foreach (var input in group.samples)
                        {
                            if (input.infinite)
                            {
                                continue;
                            }
                            float actual = optimized(input.age, input.seed, group.frameCount, group.framesPerSecond, group.animated, group.randomStart, group.randomPlayback);
                            float old = reference(input.age, input.seed, group.frameCount, group.framesPerSecond, group.animated, group.randomStart, group.randomPlayback);
                            if (BitConverter.SingleToInt32Bits(actual) != BitConverter.SingleToInt32Bits(old))
                            {
                                throw new InvalidOperationException("Captured particle oracle mismatch.");
                            }
                            checkedValues++;
                        }
                        int iterations = Math.Max(256, Math.Min(16384, 2000000 / Math.Max(1, (int)(age / 0.033f))));
                        for (int warm = 0; warm < 16; warm++)
                        {
                            optimized(age, seed, group.frameCount, group.framesPerSecond, group.animated, group.randomStart, group.randomPlayback);
                            reference(age, seed, group.frameCount, group.framesPerSecond, group.animated, group.randomStart, group.randomPlayback);
                        }
                        for (int trial = 0; trial < 3; trial++)
                        {
                            var firstTimed = Measure(trial % 2 == 0 ? reference : optimized, group, age, seed, iterations);
                            var secondTimed = Measure(trial % 2 == 0 ? optimized : reference, group, age, seed, iterations);
                            var old = trial % 2 == 0 ? firstTimed : secondTimed;
                            var current = trial % 2 == 0 ? secondTimed : firstTimed;
                            if (old.checksum != current.checksum)
                            {
                                throw new InvalidOperationException("Timed checksums differ.");
                            }
                            results.Add(new Result { configuration = group.key, age = age, trial = trial, iterations = iterations, oldNs = old.ns, newNs = current.ns, oldBytes = old.bytes, newBytes = current.bytes, coldNs = coldNs, coldBytes = coldAlloc, checksum = current.checksum });
                        }
                    }
                }
                var whole = WholeComponent(type);
                BenchmarkOutput.WriteJson(Path.Combine(output, "local-results.json"), new Document { identity = proof, checkedCapturedInputs = checkedValues, results = results, component = whole });
                Application.Quit(0);
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(output, "failure.txt"), exception.ToString());
                Application.Quit(2);
            }
        }

        private static (double ns, long bytes, double checksum) Measure(Func<float, uint, int, float, bool, bool, bool, float> evaluate, Group group, float age, uint seed, int iterations)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            double checksum = 0;
            for (int i = 0; i < iterations; i++)
            {
                checksum += evaluate(age, seed, group.frameCount, group.framesPerSecond, group.animated, group.randomStart, group.randomPlayback);
            }
            return ((Stopwatch.GetTimestamp() - start) * (1000000000.0 / Stopwatch.Frequency) / iterations, GC.GetAllocatedBytesForCurrentThread() - allocated, checksum);
        }

        private static List<ComponentResult> WholeComponent(Type cacheType)
        {
            var results = new List<ComponentResult>();
            var mode = cacheType.GetProperty("UseReference", BindingFlags.NonPublic | BindingFlags.Static);
            var root = new GameObject("Local particle frame component benchmark");
            try
            {
                var system = root.AddComponent<ParticleSystem>();
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.GetComponent<ParticleSystemRenderer>().enabled = false;
                var main = system.main;
                main.maxParticles = 256;
                var emission = system.emission;
                emission.enabled = false;
                var custom = system.customData;
                custom.enabled = true;
                custom.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
                custom.SetVectorComponentCount(ParticleSystemCustomData.Custom1, 1);
                var component = root.AddComponent<AnomalyParticleFrames>();
                component.Configure(system, 64, 8, new Vector2(0.125f, 0.125f), 24, true, true, true);
                var update = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), component, typeof(AnomalyParticleFrames).GetMethod("UpdateFrames", BindingFlags.NonPublic | BindingFlags.Instance));
                foreach (int count in new[] { 0, 1, 16, 64, 256 })
                {
                    var particles = new ParticleSystem.Particle[count];
                    for (int i = 0; i < count; i++)
                    {
                        particles[i] = new ParticleSystem.Particle { startLifetime = 5, remainingLifetime = 2, randomSeed = (uint)(i % 17), startSize = 1 };
                    }
                    system.SetParticles(particles, count);
                    foreach (bool reference in new[] { true, false })
                    {
                        cacheType.GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
                        mode.SetValue(null, reference);
                        long coldAllocated = GC.GetAllocatedBytesForCurrentThread();
                        long coldStart = Stopwatch.GetTimestamp();
                        update(0);
                        double coldNs = (Stopwatch.GetTimestamp() - coldStart) * (1000000000.0 / Stopwatch.Frequency);
                        coldAllocated = GC.GetAllocatedBytesForCurrentThread() - coldAllocated;
                        for (int warm = 0; warm < 16; warm++)
                        {
                            update(0);
                        }
                        for (int trial = 0; trial < 3; trial++)
                        {
                            long allocated = GC.GetAllocatedBytesForCurrentThread();
                            long start = Stopwatch.GetTimestamp();
                            for (int iteration = 0; iteration < 512; iteration++)
                            {
                                update(0);
                            }
                            double ns = (Stopwatch.GetTimestamp() - start) * (1000000000.0 / Stopwatch.Frequency) / 512;
                            long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                            var frames = new List<Vector4>();
                            system.GetCustomParticleData(frames, ParticleSystemCustomData.Custom1);
                            for (int i = 0; i < count; i++)
                            {
                                float expected = Mathf.Floor(ParticleFrameReference.EvaluateFrame(3, particles[i].randomSeed, 64, 24, true, true, true));
                                if (frames[i].x != expected)
                                {
                                    throw new InvalidOperationException("Whole component Custom1 mismatch.");
                                }
                            }
                            results.Add(new ComponentResult { live = count, reference = reference, trial = trial, nsPerUpdate = ns, allocatedBytes = bytes, coldNs = coldNs, coldAllocatedBytes = coldAllocated });
                        }
                    }
                }
            }
            finally
            {
                mode.SetValue(null, false);
                UnityEngine.Object.DestroyImmediate(root);
            }
            return results;
        }

        [Serializable]
        private sealed class ComponentResult
        {
            public int live;
            public bool reference;
            public int trial;
            public double nsPerUpdate;
            public long allocatedBytes;
            public double coldNs;
            public long coldAllocatedBytes;
        }

        [Serializable]
        private sealed class Workload
        {
            public List<Group> groups;
        }

        [Serializable]
        private sealed class Group
        {
            public string key;
            public int frameCount;
            public float framesPerSecond;
            public bool animated;
            public bool randomStart;
            public bool randomPlayback;
            public float maxAge;
            public List<Input> samples;
        }

        [Serializable]
        private sealed class Input
        {
            public float age;
            public uint seed;
            public bool infinite;
        }

        [Serializable]
        private sealed class Result
        {
            public string configuration;
            public float age;
            public int trial;
            public int iterations;
            public double oldNs;
            public double newNs;
            public long oldBytes;
            public long newBytes;
            public double coldNs;
            public long coldBytes;
            public double checksum;
        }

        [Serializable]
        private sealed class Document
        {
            public BenchmarkBuildIdentityEvidence identity;
            public int checkedCapturedInputs;
            public List<Result> results;
            public List<ComponentResult> component;
        }
    }
}

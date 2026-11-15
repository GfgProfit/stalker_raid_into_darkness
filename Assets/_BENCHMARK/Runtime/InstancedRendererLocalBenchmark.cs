using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using XRay.Importer.Levels;

namespace Rvm.Benchmark
{
    public static class InstancedRendererLocalBenchmark
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Application.isEditor || Array.IndexOf(args, "-renderer-local") < 0) return;
            string output = BenchmarkOutput.ValidateDirectory(args[Array.IndexOf(args, "-benchmark-output") + 1]);
            Directory.CreateDirectory(output);
            try
            {
                var identity = BenchmarkBuildIdentity.Capture();
                if (!BenchmarkBuildIdentity.IsVerified(identity)) throw new InvalidOperationException("Local Player origin not verified.");
                var sizes = new List<int>();
                foreach (var renderer in UnityEngine.Object.FindObjectsByType<XRayLevelInstancedRenderer>(FindObjectsSortMode.None))
                {
                    if (renderer.BatchSet == null) continue;
                    foreach (var batch in renderer.BatchSet.Batches) sizes.Add(batch.Count);
                }
                sizes.Sort();
                var selected = new SortedSet<int> { 0, 1, 511, 512, 1023 };
                if (sizes.Count > 0)
                {
                    selected.Add(sizes[sizes.Count / 2]);
                    selected.Add(sizes[Math.Min(sizes.Count - 1, (int)(sizes.Count * 0.95))]);
                }
                var root = new GameObject("Renderer local benchmark");
                var old = root.AddComponent<BenchmarkRendererReference>();
                var current = root.AddComponent<BenchmarkRendererCandidate>();
                var results = new List<Result>();
                var cold = new List<Cold>();
                foreach (int count in selected)
                {
                    var matrices = new Matrix4x4[count];
                    for (int i = 0; i < count; i++) matrices[i] = Matrix4x4.TRS(new Vector3(i, i % 7, i % 19), Quaternion.Euler(i % 31, i % 17, i % 23), Vector3.one);
                    var original = (Matrix4x4[])matrices.Clone();
                    var batch = new XRayInstanceBatch("local", null, Array.Empty<Material>(), default, matrices, null, false);
                    foreach (bool optimized in new[] { false, true })
                    {
                        old.ResetMotionHistory();
                        current.ResetMotionHistory();
                        long begin = Stopwatch.GetTimestamp();
                        if (optimized) current.Observe(0, batch, 0);
                        else old.Observe(0, batch, 0);
                        cold.Add(new Cold { count = count, variant = optimized ? "B" : "A", ns = Nanoseconds(begin), payloadBytes = count * 128L });
                    }
                    foreach (string scenario in new[] { "StaticOneCamera", "MovingOneCamera", "StaticThreeCameras", "MutateBetweenThreeCameras", "Rebuild" })
                    {
                        for (int trial = -1; trial < 7; trial++)
                        {
                            double a = 0;
                            double b = 0;
                            int iterations = scenario == "Rebuild" ? 64 : Math.Max(128, 200000 / Math.Max(1, count));
                            string[] expected = null;
                            for (int order = 0; order < 2; order++)
                            {
                                bool optimized = (trial + order & 1) == 0;
                                Array.Copy(original, matrices, count);
                                old.ResetMotionHistory();
                                current.ResetMotionHistory();
                                int cameras = scenario.Contains("Three") ? 3 : 1;
                                for (int i = 0; i < 16; i++)
                                {
                                    if (optimized) current.Observe(0, batch, i);
                                    else old.Observe(0, batch, i);
                                }
                                long begin = Stopwatch.GetTimestamp();
                                for (int frame = 16; frame < iterations + 16; frame++)
                                {
                                    if (scenario == "Rebuild")
                                    {
                                        if (optimized) current.ResetMotionHistory();
                                        else old.ResetMotionHistory();
                                    }
                                    for (int camera = 0; camera < cameras; camera++)
                                    {
                                        if (count > 0 && (scenario == "MovingOneCamera" || scenario == "MutateBetweenThreeCameras")) matrices[(frame + camera) % count].m03 += 0.125f;
                                        if (optimized) current.Observe(0, batch, frame);
                                        else old.Observe(0, batch, frame);
                                    }
                                }
                                double ns = Nanoseconds(begin) / (iterations * cameras);
                                if (optimized) b = ns;
                                else a = ns;
                                string[] consumed = RendererSubmissionTrace.Payload(optimized ? current.Payload(0) : old.Payload(0));
                                if (expected == null) expected = consumed;
                                else if (!System.Linq.Enumerable.SequenceEqual(expected, consumed)) throw new InvalidOperationException("Timed payload checksums differ.");
                            }
                            if (trial >= 0) results.Add(new Result { count = count, scenario = scenario, trial = trial, calls = iterations * (scenario.Contains("Three") ? 3 : 1), oldNsPerCallback = a, newNsPerCallback = b });
                        }
                    }
                }
                byte[] positive = new byte[1024 * 1024];
                positive[0] = 1;
                GC.KeepAlive(positive);
                BenchmarkOutput.WriteJson(Path.Combine(output, "local-results.json"), new Document { identity = identity, sceneBatchCounts = sizes, cold = cold, results = results, memoryNote = "No new persistent cache. Both retain one 128-byte InstanceData per instance plus identical array/history/list overhead. Cold is one independent first call per size and variant; Rebuild has 7 alternating warmed trials. Thread allocation counter deliberately unused; memory/GC evidence comes from Diagnostic and capture GC.Alloc with positive control outside target." });
                UnityEngine.Object.Destroy(root);
                Application.Quit(0);
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(output, "failure.txt"), exception.ToString());
                Application.Quit(2);
            }
        }

        private static double Nanoseconds(long begin) => (Stopwatch.GetTimestamp() - begin) * (1000000000.0 / Stopwatch.Frequency);

        [Serializable]
        private sealed class Result
        {
            public int count;
            public string scenario;
            public int trial;
            public int calls;
            public double oldNsPerCallback;
            public double newNsPerCallback;
        }

        [Serializable]
        private sealed class Cold
        {
            public int count;
            public string variant;
            public double ns;
            public long payloadBytes;
        }

        [Serializable]
        private sealed class Document
        {
            public BenchmarkBuildIdentityEvidence identity;
            public List<int> sceneBatchCounts;
            public List<Cold> cold;
            public List<Result> results;
            public string memoryNote;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Rvm.Benchmark.Editor
{
    public static class BenchmarkCaptureAnalysis
    {
        public static string Analyze(string capture, string output)
        {
            Directory.CreateDirectory(output);
            if (UnityEngine.Profiling.Profiler.enableBinaryLog)
            {
                throw new InvalidOperationException("Do not load a capture while another binary recording is active.");
            }
            string backup = null;
            if (ProfilerDriver.lastFrameIndex >= ProfilerDriver.firstFrameIndex && ProfilerDriver.firstFrameIndex >= 0)
            {
                backup = Path.Combine(output, "previous-profiler-data.raw");
                if (!ProfilerDriver.SaveProfile(backup))
                {
                    throw new IOException("Cannot preserve existing Profiler data before loading capture.");
                }
            }
            if (!ProfilerDriver.LoadProfile(capture, false))
            {
                throw new IOException("Unity Profiler could not load " + capture);
            }
            EditorApplication.ExecuteMenuItem("Window/Analysis/Profiler");
            var result = new CaptureResult { capture = capture, previousProfilerBackup = backup, firstFrame = ProfilerDriver.firstFrameIndex, lastFrame = ProfilerDriver.lastFrameIndex, limitations = "First and last capture frames excluded. CPU self time subtracts direct children on the same thread only. Inconsistent nesting is reported, not clamped to zero. Inclusive scopes and parallel threads are never summed into frame duration. Capture overhead differs from Standard." };
            var aggregates = new Dictionary<string, Row>();
            for (int frame = result.firstFrame + 1; frame < result.lastFrame; frame++)
            {
                for (int thread = 0; thread < 256; thread++)
                {
                    using var view = ProfilerDriver.GetRawFrameDataView(frame, thread);
                    if (!view.valid)
                    {
                        break;
                    }
                    result.validThreadFrames++;
                    for (int sample = 0; sample < view.sampleCount; sample++)
                    {
                        string name = view.GetSampleName(sample);
                        if (!Target(name))
                        {
                            continue;
                        }
                        double inclusive = view.GetSampleTimeMs(sample);
                        double children = 0;
                        int childIndex = sample + 1;
                        int childCount = view.GetSampleChildrenCount(sample);
                        for (int child = 0; child < childCount && childIndex < view.sampleCount; child++)
                        {
                            double time = view.GetSampleTimeMs(childIndex);
                            children += time;
                            Add(aggregates, view.threadName + "#" + thread, name + " -> " + view.GetSampleName(childIndex), time, double.NaN, false);
                            childIndex += 1 + view.GetSampleChildrenCountRecursive(childIndex);
                        }
                        bool validSelf = inclusive >= 0 && !double.IsNaN(inclusive) && children <= inclusive + 0.0001;
                        Add(aggregates, view.threadName + "#" + thread, name, inclusive, validSelf ? Math.Max(0, inclusive - children) : double.NaN, validSelf);
                        if (!validSelf)
                        {
                            result.invalidNesting++;
                        }
                    }
                }
            }
            foreach (var pair in aggregates)
            {
                result.rows.Add(pair.Value);
            }
            result.rows.Sort((a, b) => b.inclusiveMs.CompareTo(a.inclusiveMs));
            BenchmarkOutput.WriteJson(Path.Combine(output, "capture-analysis.json"), result);
            var text = new StringBuilder("# CPU capture analysis\n\nCapture loaded in Unity Profiler: " + capture + "\n\nFrames: " + result.firstFrame + ".." + result.lastFrame + "; thread-frames=" + result.validThreadFrames + "; invalid nesting=" + result.invalidNesting + ".\n\n" + result.limitations + "\n\nThread | Marker / direct child | Calls | Inclusive total ms | Valid self total ms | Self samples\n--- | --- | ---: | ---: | ---: | ---:\n");
            foreach (var row in result.rows)
            {
                text.AppendLine(row.thread.Replace("|", "/") + " | " + row.marker.Replace("|", "/") + " | " + row.calls + " | " + BenchmarkOutput.Number(row.inclusiveMs) + " | " + (row.selfSamples > 0 ? BenchmarkOutput.Number(row.selfMs) : "unavailable") + " | " + row.selfSamples);
            }
            string path = Path.Combine(output, "capture-analysis.md");
            File.WriteAllText(path, text.ToString(), Encoding.UTF8);
            return path;
        }

        private static bool Target(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            foreach (string value in new[] { "AnomalyParticleFrames", "AnomalyParticleEmission", "RVM.XRay", "PhysicsFixedUpdate", "Physics.Simulate", "Physics.Processing", "XRayWeatherController.Update", "WaitForCompleted", "WaitForRenderJobs", "Gfx.WaitForPresent", "WaitForJobGroup" })
            {
                if (name.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static void Add(Dictionary<string, Row> rows, string thread, string name, double inclusive, double self, bool validSelf)
        {
            string key = thread + "/" + name;
            if (!rows.TryGetValue(key, out var row))
            {
                row = new Row { thread = thread, marker = name };
                rows.Add(key, row);
            }
            row.calls++;
            row.inclusiveMs += inclusive;
            if (validSelf)
            {
                row.selfMs += self;
                row.selfSamples++;
            }
        }

        [Serializable]
        private sealed class CaptureResult
        {
            public string capture;
            public string previousProfilerBackup;
            public string limitations;
            public int firstFrame;
            public int lastFrame;
            public int validThreadFrames;
            public int invalidNesting;
            public List<Row> rows = new List<Row>();
        }

        [Serializable]
        private sealed class Row
        {
            public string thread;
            public string marker;
            public int calls;
            public int selfSamples;
            public double inclusiveMs;
            public double selfMs;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Rvm.Benchmark
{
    public static class BenchmarkOutput
    {
        public const string ARCHIVE_ROOT = "E:/Projects/stalker_raid_into_darkness_Archive";
        public const string DEFAULT_ROOT = ARCHIVE_ROOT + "/PerformanceBenchmarks";

        public static string ValidateDirectory(string path)
        {
            string root = Path.GetFullPath(ARCHIVE_ROOT).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Benchmark output must be inside the project Archive: " + root);
            }
            for (var current = new DirectoryInfo(full); current != null; current = current.Parent)
            {
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Output path traverses a reparse point: " + current.FullName);
                }
            }
            return full;
        }

        public static string Prepare(string outputRoot, BenchmarkMetadata metadata)
        {
            string folder = ValidateDirectory(Path.Combine(outputRoot, metadata.runId));
            if (Directory.Exists(folder))
            {
                throw new IOException("Run folder already exists: " + folder);
            }
            Directory.CreateDirectory(folder);
            WriteJson(Path.Combine(folder, "metadata.json"), metadata);
            File.WriteAllText(Path.Combine(folder, "benchmark.log"), "PREPARATION " + metadata.startedUtc + Environment.NewLine, Encoding.UTF8);
            return folder;
        }

        public static BenchmarkSummary Summarize(BenchmarkMetadata metadata, BenchmarkFrame[] frames, int count, BenchmarkCollector collector, string error)
        {
            var result = new BenchmarkSummary { runId = metadata.runId, status = error == null ? "completed" : "aborted", error = error, plannedSeconds = metadata.positionSeconds * 4 };
            result.availability.AddRange(collector.Metrics);
            result.warnings.AddRange(metadata.warnings);
            if (collector.TimingOverflow)
            {
                result.warnings.Add("FrameTiming raw buffer overflow; some delayed timings are unavailable.");
            }
            result.warnings.Add("Frame timing alignment: " + collector.Alignment);
            for (int position = -1; position < 4; position++)
            {
                for (int stable = 0; stable < 2; stable++)
                {
                    var window = new BenchmarkWindow { position = position, window = stable == 0 ? "Full" : "Stable" };
                    var milliseconds = new List<double>();
                    var metricValues = new List<double>[BenchmarkCollector.METRIC_COUNT];
                    for (int metric = 0; metric < metricValues.Length; metric++)
                    {
                        metricValues[metric] = new List<double>();
                    }
                    for (int row = 0; row < count; row++)
                    {
                        var frame = frames[row];
                        if ((position >= 0 && frame.position != position) || (stable == 1 && frame.transition))
                        {
                            continue;
                        }
                        milliseconds.Add(frame.frameMs);
                        window.measuredSeconds += frame.frameMs / 1000;
                        window.gc0 += frame.gc0;
                        window.gc1 += frame.gc1;
                        window.gc2 += frame.gc2;
                        for (int metric = 0; metric < metricValues.Length; metric++)
                        {
                            double value = collector.Values[row * BenchmarkCollector.METRIC_COUNT + metric];
                            if (!double.IsNaN(value))
                            {
                                metricValues[metric].Add(value);
                            }
                        }
                    }
                    window.frames = BenchmarkStatistics.Frames(milliseconds.ToArray());
                    for (int metric = 0; metric < metricValues.Length; metric++)
                    {
                        window.metrics.Add(new BenchmarkMetricStats { id = collector.Metrics[metric].id, unit = collector.Metrics[metric].unit, reason = metricValues[metric].Count > 0 ? null : collector.Metrics[metric].reason ?? "No valid samples in this window.", values = BenchmarkStatistics.Distribution(metricValues[metric].ToArray()) });
                    }
                    result.windows.Add(window);
                }
            }
            result.measuredSeconds = result.windows[0].measuredSeconds;
            return result;
        }

        public static bool Save(string folder, BenchmarkMetadata metadata, BenchmarkSummary summary, BenchmarkFrame[] frames, int count, BenchmarkCollector collector)
        {
            var errors = new List<string>();
            TryWrite(() => WriteFrames(Path.Combine(folder, "frames.csv"), frames, count, collector), errors);
            TryWrite(() => WriteTimings(Path.Combine(folder, "timings.csv"), collector), errors);
            TryWrite(() => WriteJson(Path.Combine(folder, "metadata.json"), metadata), errors);
            TryWrite(() => WriteJson(Path.Combine(folder, "summary.json"), summary), errors);
            TryWrite(() => File.WriteAllText(Path.Combine(folder, "report.md"), Report(metadata, summary), Encoding.UTF8), errors);
            TryWrite(() => File.AppendAllText(Path.Combine(folder, "benchmark.log"), summary.status + " " + metadata.finishedUtc + Environment.NewLine + summary.error + Environment.NewLine + string.Join(Environment.NewLine, summary.warnings) + Environment.NewLine + string.Join(Environment.NewLine, errors), Encoding.UTF8), errors);
            if (errors.Count > 0)
            {
                Debug.LogError("Benchmark save failed: " + string.Join("; ", errors) + "; partial output: " + folder);
                return false;
            }
            foreach (string name in new[] { "metadata.json", "frames.csv", "summary.json", "report.md", "benchmark.log", "timings.csv" })
            {
                if (!File.Exists(Path.Combine(folder, name)) || new FileInfo(Path.Combine(folder, name)).Length == 0)
                {
                    return false;
                }
            }
            File.WriteAllText(Path.Combine(folder, "complete.json"), "{\"saved\":true,\"status\":\"" + summary.status + "\"}", Encoding.UTF8);
            return true;
        }

        public static void WriteJson(string path, object value)
        {
            string temporary = path + ".pending";
            File.WriteAllText(temporary, JsonUtility.ToJson(value, true), Encoding.UTF8);
            if (File.Exists(path))
            {
                File.Replace(temporary, path, null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }

        public static string Report(BenchmarkMetadata metadata, BenchmarkSummary summary)
        {
            var text = new StringBuilder();
            text.AppendLine("# RVM Benchmark " + metadata.runId);
            text.AppendLine("\nStatus: " + summary.status + "; " + summary.error);
            text.AppendLine("\nPlanned: " + Number(summary.plannedSeconds) + " s; measured: " + Number(summary.measuredSeconds) + " s. Position boundaries are quantized to whole frames; no pauses are inserted.");
            for (int position = -1; position < 4; position++)
            {
                text.AppendLine("\n## " + (position < 0 ? "Overall" : "POS " + (position + 1)));
                text.AppendLine("\nWindow | Frames | Seconds | Avg FPS | Min / Max FPS | 1% Low | 0.1% Low | Mean ms | Max ms | P50 | P90 | P95 | P99");
                text.AppendLine("--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---:");
                foreach (var window in summary.windows)
                {
                    if (window.position != position)
                    {
                        continue;
                    }
                    var f = window.frames;
                    text.AppendLine(window.window + " | " + f.frameTime.samples + " | " + Number(window.measuredSeconds) + " | " + Available(f.averageFps, f.frameTime.available) + " | " + Available(f.minFps, f.frameTime.available) + " / " + Available(f.maxFps, f.frameTime.available) + " | " + Available(f.low1Fps, f.low1Available) + " | " + Available(f.low01Fps, f.low01Available) + " | " + Available(f.frameTime.mean, f.frameTime.available) + " | " + Available(f.frameTime.max, f.frameTime.available) + " | " + Available(f.frameTime.p50, f.frameTime.available) + " | " + Available(f.frameTime.p90, f.frameTime.available) + " | " + Available(f.frameTime.p95, f.frameTime.available) + " | " + Available(f.frameTime.p99, f.frameTime.available));
                }
                text.AppendLine("\nMetric | Unit | Full mean / max / samples | Stable mean / max / samples");
                text.AppendLine("--- | --- | ---: | ---:");
                int baseIndex = (position + 1) * 2;
                for (int i = 0; i < summary.availability.Count; i++)
                {
                    text.AppendLine(summary.availability[i].id + " | " + summary.availability[i].unit + " | " + Describe(summary.windows[baseIndex].metrics[i]) + " | " + Describe(summary.windows[baseIndex + 1].metrics[i]));
                }
                var full = summary.windows[baseIndex];
                text.AppendLine("\nGC collections (process-wide generation counters, not additive across generations): gen0=" + full.gc0 + ", gen1=" + full.gc1 + ", gen2=" + full.gc2 + ".");
            }
            text.AppendLine("\n## Definitions and limitations");
            text.AppendLine("\nAverage FPS = 1000 / mean(frame ms). Min/Max FPS = 1000 / max/min(frame ms). N% Low = 1000 / mean(slowest ceil(N/100 * frame count) frame times). 1% needs >=100 frames, 0.1% >=1000. Percentiles use linear interpolation at (count-1)*p. Stable discards frames whose START is inside stabilization; Full retains all transition frames. Missing CSV cells are unavailable, never zero-filled.");
            foreach (string warning in summary.warnings)
            {
                text.AppendLine("\n- " + warning);
            }
            text.AppendLine("\n## Environment\n\n```json\n" + JsonUtility.ToJson(metadata, true) + "\n```");
            text.AppendLine("\n## Counter availability\n\nMetric | Unit | Available | Source | Reason\n--- | --- | --- | --- | ---");
            foreach (var metric in summary.availability)
            {
                text.AppendLine(metric.id + " | " + metric.unit + " | " + metric.available + " | " + metric.source + " | " + metric.reason);
            }
            return text.ToString();
        }

        public static string Number(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? "unavailable" : value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Available(double value, bool available)
        {
            return available ? Number(value) : "unavailable";
        }

        private static string Describe(BenchmarkMetricStats metric)
        {
            return metric.values.available ? Number(metric.values.mean) + " / " + Number(metric.values.max) + " / " + metric.values.samples : "unavailable: " + metric.reason;
        }

        private static void TryWrite(Action write, List<string> errors)
        {
            try
            {
                write();
            }
            catch (Exception exception)
            {
                errors.Add(exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static void WriteFrames(string path, BenchmarkFrame[] frames, int count, BenchmarkCollector collector)
        {
            using (var writer = new StreamWriter(path, false, Encoding.UTF8))
            {
                writer.Write("frame_index,unity_frame,timestamp_s,source_update_qpc,position_index,transition,frame_ms,collector_us,gc_gen0,gc_gen1,gc_gen2");
                foreach (var metric in collector.Metrics)
                {
                    writer.Write("," + metric.id + "_" + metric.unit);
                }
                writer.WriteLine();
                for (int row = 0; row < count; row++)
                {
                    var frame = frames[row];
                    writer.Write(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:R},{3},{4},{5},{6:R},{7:R},{8},{9},{10}", frame.frameIndex, frame.unityFrame, frame.timestamp, frame.startTicks, frame.position + 1, frame.transition ? "transition" : "stable", frame.frameMs, frame.collectorMicroseconds, frame.gc0, frame.gc1, frame.gc2));
                    for (int metric = 0; metric < BenchmarkCollector.METRIC_COUNT; metric++)
                    {
                        double value = collector.Values[row * BenchmarkCollector.METRIC_COUNT + metric];
                        writer.Write(',');
                        if (!double.IsNaN(value))
                        {
                            writer.Write(value.ToString("R", CultureInfo.InvariantCulture));
                        }
                    }
                    writer.WriteLine();
                }
            }
        }

        private static void WriteTimings(string path, BenchmarkCollector collector)
        {
            using (var writer = new StreamWriter(path, false, Encoding.UTF8))
            {
                writer.WriteLine("frame_start_timestamp,present_timestamp,cpu_total_ms,cpu_main_ms,cpu_render_ms,present_wait_ms,gpu_ms,width_scale,height_scale,sync_interval");
                for (int i = 0; i < collector.TimingCount; i++)
                {
                    var t = collector.Timings[i];
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:R},{3:R},{4:R},{5:R},{6:R},{7:R},{8:R},{9}", t.frameStartTimestamp, t.cpuTimePresentCalled, t.cpuFrameTime, t.cpuMainThreadFrameTime, t.cpuRenderThreadFrameTime, t.cpuMainThreadPresentWaitTime, t.gpuFrameTime, t.widthScale, t.heightScale, t.syncInterval));
                }
            }
        }
    }
}

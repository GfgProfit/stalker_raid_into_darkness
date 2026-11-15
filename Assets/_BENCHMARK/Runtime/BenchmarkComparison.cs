using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Rvm.Benchmark
{
    [Serializable]
    public sealed class BenchmarkDelta
    {
        public int position;
        public string window;
        public string metric;
        public string unit;
        public double baseline;
        public double candidate;
        public double percent;
        public bool available;
        public bool samplesAvailable;
        public string classification;
    }

    [Serializable]
    public sealed class BenchmarkComparisonResult
    {
        public string baseline;
        public string candidate;
        public bool comparable;
        public string comparabilityStatus;
        public string comparisonKind;
        public bool assetReferencesAnchored;
        public bool originVerified;
        public bool renderingVerified;
        public List<string> incompatibilities = new List<string>();
        public List<string> unverifiedConditions = new List<string>();
        public string baselineGit;
        public string candidateGit;
        public double practicalTolerancePercent;
        public List<string> warnings = new List<string>();
        public List<BenchmarkDelta> deltas = new List<BenchmarkDelta>();
    }

    [Serializable]
    public sealed class BenchmarkSeriesMetric
    {
        public int position;
        public string window;
        public string metric;
        public string unit;
        public int availableRuns;
        public double median;
        public double min;
        public double max;
    }

    [Serializable]
    public sealed class BenchmarkSeriesResult
    {
        public string seriesId;
        public string[] runs;
        public bool acceptedBaseline;
        public string validationStatus;
        public List<string> rejectionReasons = new List<string>();
        public List<string> warnings = new List<string>();
        public List<BenchmarkSeriesMetric> metrics = new List<BenchmarkSeriesMetric>();
    }

    public static class BenchmarkComparison
    {
        public static BenchmarkComparisonResult Compare(BenchmarkMetadata baselineMetadata, BenchmarkSummary baseline, BenchmarkMetadata candidateMetadata, BenchmarkSummary candidate, double tolerancePercent = 0)
        {
            if (tolerancePercent < 0 || double.IsNaN(tolerancePercent) || double.IsInfinity(tolerancePercent))
            {
                throw new ArgumentOutOfRangeException(nameof(tolerancePercent));
            }
            var result = new BenchmarkComparisonResult { baseline = baseline.runId, candidate = candidate.runId, practicalTolerancePercent = tolerancePercent };
            result.baselineGit = baselineMetadata.gitSha + "/" + baselineMetadata.gitDirty;
            result.candidateGit = candidateMetadata.gitSha + "/" + candidateMetadata.gitDirty;
            if (baselineMetadata.metadataVersion < 2 || candidateMetadata.metadataVersion < 2)
            {
                result.warnings.Add("Legacy metadata: refresh rate, timer availability and instrumentation cannot be verified.");
            }
            else if (string.IsNullOrEmpty(baselineMetadata.timerSignature) || string.IsNullOrEmpty(candidateMetadata.timerSignature) || string.IsNullOrEmpty(baselineMetadata.instrumentationSignature) || string.IsNullOrEmpty(candidateMetadata.instrumentationSignature))
            {
                result.warnings.Add("V2 timer/instrumentation metadata is incomplete.");
            }
            Difference(result, "Refresh/render interval", baselineMetadata.refreshRateHz + "/" + baselineMetadata.renderFrameInterval, candidateMetadata.refreshRateHz + "/" + candidateMetadata.renderFrameInterval);
            Difference(result, "Instrumentation", baselineMetadata.instrumentationSignature, candidateMetadata.instrumentationSignature);
            Difference(result, "Timer availability/alignment", baselineMetadata.timerSignature, candidateMetadata.timerSignature);
            Difference(result, "Fixed world scope", baselineMetadata.fixedWorldState, candidateMetadata.fixedWorldState);
            Difference(result, "Final frame pacing", baselineMetadata.finalFramePacing, candidateMetadata.finalFramePacing);
            Difference(result, "Final Quality", baselineMetadata.finalQuality, candidateMetadata.finalQuality);
            foreach (var metadata in new[] { baselineMetadata, candidateMetadata })
            {
                if (metadata.metadataVersion >= 2 && (metadata.finalQuality != metadata.quality || metadata.finalFramePacing != metadata.vsync + "/" + metadata.targetFrameRate + "/" + metadata.renderFrameInterval))
                {
                    result.incompatibilities.Add("Quality/pacing changed within run " + metadata.runId + ". Intermediate conditions cannot be reconstructed from start/end values.");
                }
            }
            Difference(result, "Graphics API", baselineMetadata.graphicsApi, candidateMetadata.graphicsApi);
            Difference(result, "Resolution", baselineMetadata.width + "x" + baselineMetadata.height + "/" + baselineMetadata.cameraPixelWidth + "x" + baselineMetadata.cameraPixelHeight, candidateMetadata.width + "x" + candidateMetadata.height + "/" + candidateMetadata.cameraPixelWidth + "x" + candidateMetadata.cameraPixelHeight);
            Difference(result, "Quality", baselineMetadata.quality, candidateMetadata.quality);
            Difference(result, "Scenario", baselineMetadata.scenario, candidateMetadata.scenario);
            Difference(result, "Scene", baselineMetadata.scene, candidateMetadata.scene);
            Difference(result, "Execution mode", baselineMetadata.execution, candidateMetadata.execution);
            Difference(result, "Profiling mode", baselineMetadata.mode + "/" + baselineMetadata.profilerCapture + "/" + baselineMetadata.profilerWasEnabled, candidateMetadata.mode + "/" + candidateMetadata.profilerCapture + "/" + candidateMetadata.profilerWasEnabled);
            Difference(result, "Shadow trace", baselineMetadata.shadowTrace.ToString(), candidateMetadata.shadowTrace.ToString());
            Difference(result, "Unity/HDRP", baselineMetadata.unity + "/" + baselineMetadata.hdrp, candidateMetadata.unity + "/" + candidateMetadata.hdrp);
            Difference(result, "Hardware", baselineMetadata.cpu + "/" + baselineMetadata.gpu + "/" + baselineMetadata.ramMb, candidateMetadata.cpu + "/" + candidateMetadata.gpu + "/" + candidateMetadata.ramMb);
            Difference(result, "VSync/target FPS/fullscreen", baselineMetadata.vsync + "/" + baselineMetadata.targetFrameRate + "/" + baselineMetadata.fullscreen, candidateMetadata.vsync + "/" + candidateMetadata.targetFrameRate + "/" + candidateMetadata.fullscreen);
            Difference(result, "Render scales", baselineMetadata.widthScale + "/" + baselineMetadata.heightScale, candidateMetadata.widthScale + "/" + candidateMetadata.heightScale);
            Difference(result, "Camera", baselineMetadata.cameraSettings, candidateMetadata.cameraSettings);
            Difference(result, "Pipeline", baselineMetadata.renderPipeline, candidateMetadata.renderPipeline);
            Difference(result, "Durations", baselineMetadata.positionSeconds + "/" + baselineMetadata.warmupSeconds + "/" + baselineMetadata.stabilizationSeconds, candidateMetadata.positionSeconds + "/" + candidateMetadata.warmupSeconds + "/" + candidateMetadata.stabilizationSeconds);
            Difference(result, "Tags", JsonUtility.ToJson(new TagList { tags = baselineMetadata.tags }), JsonUtility.ToJson(new TagList { tags = candidateMetadata.tags }));
            if (baselineMetadata.positions == null || candidateMetadata.positions == null || baselineMetadata.positions.Length != 4 || candidateMetadata.positions.Length != 4)
            {
                result.incompatibilities.Add("Four recorded positions are required.");
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    Difference(result, "Position " + (i + 1), JsonUtility.ToJson(baselineMetadata.positions[i]), JsonUtility.ToJson(candidateMetadata.positions[i]));
                }
            }
            bool firstOrigin = BenchmarkConditionProtocol.ValidateOrigin(baselineMetadata, out _);
            bool secondOrigin = BenchmarkConditionProtocol.ValidateOrigin(candidateMetadata, out _);
            result.originVerified = firstOrigin && secondOrigin;
            result.renderingVerified = baselineMetadata.renderedMeasurementFrames > 0 && candidateMetadata.renderedMeasurementFrames > 0;
            result.assetReferencesAnchored = result.originVerified && baselineMetadata.renderSettingsDigest == candidateMetadata.renderSettingsDigest;
            Difference(result, "Condition protocol", baselineMetadata.conditionProtocol, candidateMetadata.conditionProtocol);
            Difference(result, "Rendering settings provenance", baselineMetadata.renderSettingsDigest, candidateMetadata.renderSettingsDigest);
            CompareEnvironment(result, baselineMetadata.environment, candidateMetadata.environment);
            CompareEnvironment(result, baselineMetadata.endEnvironment, candidateMetadata.endEnvironment);
            if (baselineMetadata.runtimeErrors > 0 || candidateMetadata.runtimeErrors > 0)
            {
                result.incompatibilities.Add("Runtime errors occurred during a run; clean A/B comparison is not established.");
            }
            if (baseline.status != "completed" || candidate.status != "completed" || baseline.windows.Count != 10 || candidate.windows.Count != 10)
            {
                result.incompatibilities.Add("Only completed runs with ten Full/Stable windows can be compared.");
            }
            foreach (var metric in baseline.availability)
            {
                if (metric.unit == "ms")
                {
                    var other = candidate.availability.Find(value => value.id == metric.id);
                    if (other == null || other.available != metric.available || other.source != metric.source)
                    {
                        result.incompatibilities.Add("Timer differs: " + metric.id);
                    }
                }
            }
            bool identity = !string.IsNullOrEmpty(baseline.runId) && baseline.runId == candidate.runId;
            bool firstProtocol = BenchmarkConditionProtocol.Validate(baselineMetadata, out string firstReason);
            bool secondProtocol = BenchmarkConditionProtocol.Validate(candidateMetadata, out string secondReason);
            result.comparisonKind = identity ? "IdentityValidation" : "IndependentRuns";
            if (!firstProtocol)
            {
                result.unverifiedConditions.Add(firstReason);
            }
            if (!secondProtocol && secondReason != firstReason)
            {
                result.unverifiedConditions.Add(secondReason);
            }
            if (baselineMetadata.measurementVersion < 3 || candidateMetadata.measurementVersion < 3)
            {
                result.unverifiedConditions.Add("Legacy recorder semantics are unverified. QPC frame/FPS data remains readable; suspect recorder metrics are excluded.");
            }
            result.comparabilityStatus = !result.originVerified ? "UnverifiedOrigin" : !result.renderingVerified ? "UnverifiedRendering" : result.incompatibilities.Count > 0 ? "Incompatible" : identity && baselineMetadata.measurementVersion >= 3 ? "Identity" : firstProtocol && secondProtocol && baselineMetadata.measurementVersion >= 3 && candidateMetadata.measurementVersion >= 3 ? "ConfirmedProtocol" : "Conditional";
            result.comparable = result.comparabilityStatus == "ConfirmedProtocol" || result.comparabilityStatus == "Identity";
            foreach (string difference in result.incompatibilities)
            {
                if (!result.warnings.Contains(difference))
                {
                    result.warnings.Add(difference);
                }
            }
            result.warnings.AddRange(result.unverifiedConditions);
            result.warnings.Add("ConfirmedProtocol establishes the documented scenario configuration, not deterministic simulation or statistical significance. Conditional permits observed deltas only; Identity is not an independent A/B experiment. UnverifiedOrigin and UnverifiedRendering never establish an accepted baseline or confirmed comparison.");
            foreach (var window in baseline.windows)
            {
                var other = Find(candidate, window.position, window.window);
                if (other == null)
                {
                    continue;
                }
                Add(result, window, "average_fps", "fps", window.frames.averageFps, other.frames.averageFps, window.frames.frameTime.available && other.frames.frameTime.available && window.frames.frameTime.samples >= 2 && other.frames.frameTime.samples >= 2, true);
                Add(result, window, "p95", "ms", window.frames.frameTime.p95, other.frames.frameTime.p95, window.frames.frameTime.available && other.frames.frameTime.available && window.frames.frameTime.samples >= 30 && other.frames.frameTime.samples >= 30, false);
                Add(result, window, "p99", "ms", window.frames.frameTime.p99, other.frames.frameTime.p99, window.frames.frameTime.available && other.frames.frameTime.available && window.frames.frameTime.samples >= 30 && other.frames.frameTime.samples >= 30, false);
                Add(result, window, "low1_fps", "fps", window.frames.low1Fps, other.frames.low1Fps, window.frames.low1Available && other.frames.low1Available, true);
                foreach (var metric in window.metrics)
                {
                    var match = other.metrics.Find(value => value.id == metric.id && value.unit == metric.unit);
                    Add(result, window, metric.id, metric.unit, metric.values.mean, match == null ? 0 : match.values.mean, metric.values.available && match != null && match.values.available && metric.values.samples >= 2 && match.values.samples >= 2 && baselineMetadata.measurementVersion >= 3 && candidateMetadata.measurementVersion >= 3, false);
                }
            }
            return result;
        }

        public static BenchmarkDelta Delta(double baseline, double candidate, bool available, bool higherIsBetter, bool comparable, double tolerancePercent)
        {
            var result = new BenchmarkDelta { baseline = baseline, candidate = candidate, available = available && baseline > 0 && candidate >= 0 && !double.IsNaN(baseline) && !double.IsNaN(candidate) && !double.IsInfinity(baseline) && !double.IsInfinity(candidate), classification = "Incomparable / insufficient data" };
            result.samplesAvailable = available && !double.IsNaN(baseline) && !double.IsNaN(candidate) && !double.IsInfinity(baseline) && !double.IsInfinity(candidate);
            if (!result.available)
            {
                return result;
            }
            result.percent = 100 * (candidate - baseline) / baseline;
            if (comparable)
            {
                result.classification = Math.Abs(result.percent) <= tolerancePercent ? "No meaningful difference" : (higherIsBetter ? result.percent > 0 : result.percent < 0) ? "Improvement" : "Regression";
            }
            return result;
        }

        public static string SaveComparison(string baselinePath, string candidatePath, double tolerancePercent = 0)
        {
            ReadRun(baselinePath, out var baselineMetadata, out var baseline);
            ReadRun(candidatePath, out var candidateMetadata, out var candidate);
            var result = Compare(baselineMetadata, baseline, candidateMetadata, candidate, tolerancePercent);
            if (baselineMetadata.mode == "Diagnostic" || candidateMetadata.mode == "Diagnostic")
            {
                CompareDiagnostics(result, baselinePath, candidatePath, baselineMetadata, candidateMetadata);
            }
            if (result.comparabilityStatus == "Incompatible")
            {
                foreach (var delta in result.deltas)
                {
                    delta.classification = "Incomparable / insufficient data";
                }
            }
            string folder = NewReportDirectory("Comparisons");
            BenchmarkOutput.WriteJson(Path.Combine(folder, "comparison.json"), result);
            var text = new StringBuilder("# RVM comparison\n\nBaseline: " + result.baseline + "\n\nCandidate: " + result.candidate + "\n\nComparability: " + result.comparabilityStatus + "; " + result.comparisonKind + "; strict=" + result.comparable + "\n\nObserved direction only; no statistical significance inferred from one run. Practical tolerance supplied by operator: " + BenchmarkOutput.Number(tolerancePercent) + "%.\n");
            text.AppendLine("\nGit (different revisions are allowed): " + result.baselineGit + " -> " + result.candidateGit);
            foreach (string warning in result.warnings)
            {
                text.AppendLine("\n- " + warning);
            }
            text.AppendLine("\nPosition | Window | Metric | Unit | Baseline | Candidate | Change % | Classification\n--- | --- | --- | --- | ---: | ---: | ---: | ---");
            foreach (var delta in result.deltas)
            {
                text.AppendLine((delta.position < 0 ? "Overall" : "POS " + (delta.position + 1)) + " | " + delta.window + " | " + delta.metric + " | " + delta.unit + " | " + (delta.samplesAvailable ? BenchmarkOutput.Number(delta.baseline) : "unavailable") + " | " + (delta.samplesAvailable ? BenchmarkOutput.Number(delta.candidate) : "unavailable") + " | " + (delta.available ? BenchmarkOutput.Number(delta.percent) : "unavailable") + " | " + delta.classification);
            }
            File.WriteAllText(Path.Combine(folder, "comparison-report.md"), text.ToString(), Encoding.UTF8);
            return Path.Combine(folder, "comparison-report.md");
        }

        public static string SaveSeries(string seriesId, List<string> directories)
        {
            var summaries = new List<BenchmarkSummary>();
            var metadata = new List<BenchmarkMetadata>();
            foreach (string directory in directories)
            {
                ReadRun(directory, out var m, out var s);
                metadata.Add(m);
                summaries.Add(s);
            }
            var result = ValidateSeries(seriesId, metadata, summaries);
            result.runs = directories.ToArray();
            for (int i = 1; i < summaries.Count; i++)
            {
                result.warnings.AddRange(Compare(metadata[0], summaries[0], metadata[i], summaries[i]).warnings);
            }
            if (summaries.Count == 0)
            {
                throw new InvalidOperationException("No runs in series.");
            }
            foreach (var reference in summaries[0].windows)
            {
                var values = new Dictionary<string, List<double>>();
                var units = new Dictionary<string, string>();
                foreach (var summary in summaries)
                {
                    var window = Find(summary, reference.position, reference.window);
                    if (window == null)
                    {
                        continue;
                    }
                    SeriesValue(values, units, "average_fps", "fps", window.frames.averageFps, window.frames.frameTime.available);
                    SeriesValue(values, units, "p95", "ms", window.frames.frameTime.p95, window.frames.frameTime.available);
                    SeriesValue(values, units, "p99", "ms", window.frames.frameTime.p99, window.frames.frameTime.available);
                    SeriesValue(values, units, "low1_fps", "fps", window.frames.low1Fps, window.frames.low1Available);
                    foreach (var metric in window.metrics)
                    {
                        SeriesValue(values, units, metric.id, metric.unit, metric.values.mean, metric.values.available);
                    }
                }
                foreach (var pair in values)
                {
                    var distribution = BenchmarkStatistics.Distribution(pair.Value.ToArray());
                    result.metrics.Add(new BenchmarkSeriesMetric { position = reference.position, window = reference.window, metric = pair.Key, unit = units[pair.Key], availableRuns = distribution.samples, median = distribution.p50, min = distribution.min, max = distribution.max });
                }
            }
            string folder = NewReportDirectory("Series");
            BenchmarkOutput.WriteJson(Path.Combine(folder, "series.json"), result);
            var text = new StringBuilder("# RVM series " + seriesId + "\n\nBaseline acceptance: " + result.validationStatus + "; accepted=" + result.acceptedBaseline + ". Rejected series retain observed statistics for investigation and must not be used as an accepted baseline.\n\nRuns are retained separately. Median, minimum and maximum are calculated across per-run statistics, never pooled frames. Only metrics available in all runs are suitable for series comparison. Weather/time is reset only when explicitly enabled; other dynamic state evolves.\n");
            foreach (string reason in result.rejectionReasons)
            {
                text.AppendLine("\n- Baseline rejected: " + reason);
            }
            foreach (string directory in directories)
            {
                text.AppendLine("\n- " + directory);
            }

            foreach (string warning in result.warnings)
            {
                text.AppendLine("\n- " + warning);
            }
            text.AppendLine("\nPosition | Window | Metric | Unit | Available runs | Median | Min | Max\n--- | --- | --- | --- | ---: | ---: | ---: | ---:");
            foreach (var metric in result.metrics)
            {
                text.AppendLine((metric.position < 0 ? "Overall" : "POS " + (metric.position + 1)) + " | " + metric.window + " | " + metric.metric + " | " + metric.unit + " | " + metric.availableRuns + "/" + directories.Count + " | " + (metric.availableRuns > 0 ? BenchmarkOutput.Number(metric.median) : "unavailable") + " | " + (metric.availableRuns > 0 ? BenchmarkOutput.Number(metric.min) : "unavailable") + " | " + (metric.availableRuns > 0 ? BenchmarkOutput.Number(metric.max) : "unavailable"));
            }
            File.WriteAllText(Path.Combine(folder, "series-report.md"), text.ToString(), Encoding.UTF8);
            return Path.Combine(folder, "series-report.md");
        }

        public static bool IsBaselineEligible(BenchmarkMetadata metadata, BenchmarkSummary summary, out string reason)
        {
            if (!BenchmarkConditionProtocol.ValidateOrigin(metadata, out reason))
            {
                return false;
            }
            if (metadata.renderedMeasurementFrames <= 0)
            {
                reason = "Working-camera rendering is unverified: no rendered measurement frames were recorded.";
                return false;
            }
            if (summary == null || string.IsNullOrEmpty(metadata.runId) || summary.runId != metadata.runId || summary.status != "completed" || !string.IsNullOrEmpty(summary.error) || summary.windows == null || summary.windows.Count != 10 || metadata.positions == null || metadata.positions.Length != 4)
            {
                reason = "Run did not complete with a matching identity and ten Full/Stable windows.";
                return false;
            }
            foreach (var pose in metadata.positions)
            {
                if (pose == null)
                {
                    reason = "Four recorded camera poses are required.";
                    return false;
                }
            }
            if (metadata.execution != "Development Player" || metadata.mode != "Standard" || metadata.profilerCapture || metadata.profilerWasEnabled || metadata.shadowTrace || metadata.runtimeErrors != 0)
            {
                reason = "Accepted baseline requires a clean Development Player Standard run without Profiler, binary capture or shadow trace.";
                return false;
            }
            if (metadata.measurementVersion < 3 || string.IsNullOrEmpty(metadata.instrumentationSignature) || string.IsNullOrEmpty(metadata.timerSignature) || metadata.finalQuality != metadata.quality || metadata.finalFramePacing != metadata.vsync + "/" + metadata.targetFrameRate + "/" + metadata.renderFrameInterval)
            {
                reason = "Measurement protocol or final quality/frame pacing is absent or changed within the run.";
                return false;
            }
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var window in summary.windows)
            {
                if (window == null || window.position < -1 || window.position > 3 || (window.window != "Full" && window.window != "Stable") || !keys.Add(window.position + "/" + window.window) || window.frames == null || window.frames.frameTime == null || !window.frames.frameTime.available || window.frames.frameTime.samples < 2)
                {
                    reason = "Full/Stable window coverage or frame samples are incomplete/invalid.";
                    return false;
                }
            }
            reason = "Clean Standard Player run with independently verified build origin and complete Full/Stable results.";
            return true;
        }

        public static BenchmarkSeriesResult ValidateSeries(string seriesId, List<BenchmarkMetadata> metadata, List<BenchmarkSummary> summaries)
        {
            var result = new BenchmarkSeriesResult { seriesId = seriesId };
            if (string.IsNullOrEmpty(seriesId))
            {
                result.rejectionReasons.Add("A recorded series identity is required.");
            }
            if (metadata == null || summaries == null || metadata.Count != 3 || summaries.Count != 3)
            {
                result.rejectionReasons.Add("Exactly three recorded consecutive Standard runs are required.");
            }
            if (metadata != null && summaries != null)
            {
                var runIds = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < Math.Min(metadata.Count, summaries.Count); i++)
                {
                    var current = metadata[i];
                    if (!IsBaselineEligible(current, summaries[i], out string reason))
                    {
                        result.rejectionReasons.Add("Run " + (i + 1) + ": " + reason);
                        continue;
                    }
                    if (!runIds.Add(current.runId))
                    {
                        result.rejectionReasons.Add("Repeated Run ID is not an independent repetition: " + current.runId);
                    }
                    if (current.seriesId != seriesId || current.repetition != i + 1)
                    {
                        result.rejectionReasons.Add("Run " + (i + 1) + " is not the expected consecutive repetition of this series.");
                    }
                    if (i > 0 && IsBaselineEligible(metadata[0], summaries[0], out _))
                    {
                        var first = metadata[0];
                        if (current.buildIdentity.stamp.buildId != first.buildIdentity.stamp.buildId || current.buildIdentity.inputManifestSha256 != first.buildIdentity.inputManifestSha256 || current.buildIdentity.artifactsManifestSha256 != first.buildIdentity.artifactsManifestSha256 || current.buildIdentity.applicationBuildGuid != first.buildIdentity.applicationBuildGuid || current.buildIdentity.executableSha256 != first.buildIdentity.executableSha256)
                        {
                            result.rejectionReasons.Add("Run " + (i + 1) + " used a different actual Player build or binary artifact identity.");
                        }
                        if (SeriesProtocol(current) != SeriesProtocol(first))
                        {
                            result.rejectionReasons.Add("Run " + (i + 1) + " used a different measurement/configuration protocol.");
                        }
                    }
                }
            }
            result.acceptedBaseline = result.rejectionReasons.Count == 0;
            result.validationStatus = result.acceptedBaseline ? "Accepted" : "Rejected";
            return result;
        }

        private static string SeriesProtocol(BenchmarkMetadata metadata)
        {
            return metadata.scenario + "|" + metadata.scene + "|" + metadata.graphicsApi + "|" + metadata.width + "/" + metadata.height + "/" + metadata.cameraPixelWidth + "/" + metadata.cameraPixelHeight + "|" + metadata.quality + "|" + metadata.vsync + "/" + metadata.targetFrameRate + "/" + metadata.renderFrameInterval + "|" + metadata.fullscreen + "|" + metadata.widthScale + "/" + metadata.heightScale + "|" + metadata.cameraSettings + "|" + metadata.renderPipeline + "|" + metadata.positionSeconds + "/" + metadata.warmupSeconds + "/" + metadata.stabilizationSeconds + "|" + metadata.instrumentationSignature + "|" + metadata.timerSignature + "|" + metadata.fixedWorldState + "|" + JsonUtility.ToJson(new PoseList { positions = metadata.positions });
        }

        public static void ReadRun(string input, out BenchmarkMetadata metadata, out BenchmarkSummary summary)
        {
            string folder = Directory.Exists(input) ? input : Path.GetDirectoryName(input);
            if (!File.Exists(Path.Combine(folder, "complete.json")))
            {
                throw new IOException("Run has no successful-save manifest: " + folder);
            }
            metadata = JsonUtility.FromJson<BenchmarkMetadata>(File.ReadAllText(Path.Combine(folder, "metadata.json")));
            summary = JsonUtility.FromJson<BenchmarkSummary>(File.ReadAllText(Path.Combine(folder, "summary.json")));
            string attestation = Path.Combine(folder, "conditions-attestation.json");
            if (metadata != null && File.Exists(attestation))
            {
                metadata.conditionEvidence = File.ReadAllText(attestation);
                metadata.conditionProtocol = JsonUtility.FromJson<BenchmarkConditionEvidence>(metadata.conditionEvidence)?.protocolId;
            }
            if (metadata == null || summary == null || metadata.schemaVersion != 1 || summary.schemaVersion != 1 || metadata.runId != summary.runId)
            {
                throw new IOException("Unsupported or inconsistent benchmark schema.");
            }
            if (metadata.measurementVersion < 3)
            {
                foreach (var metric in summary.availability)
                {
                    if (metric.source == null || !metric.source.StartsWith("FrameTimingManager", StringComparison.Ordinal))
                    {
                        metric.available = false;
                        metric.reason = "Legacy recorder aggregation/freshness is unverified; original files remain unchanged.";
                        foreach (var window in summary.windows)
                        {
                            var value = window.metrics.Find(item => item.id == metric.id);
                            if (value != null)
                            {
                                value.values.available = false;
                                value.reason = metric.reason;
                            }
                        }
                    }
                }
            }
        }

        private static void SeriesValue(Dictionary<string, List<double>> values, Dictionary<string, string> units, string key, string unit, double value, bool available)
        {
            if (!values.TryGetValue(key, out var list))
            {
                list = new List<double>();
                values.Add(key, list);
                units.Add(key, unit);
            }
            if (available)
            {
                list.Add(value);
            }
        }

        private static void CompareDiagnostics(BenchmarkComparisonResult result, string baselinePath, string candidatePath, BenchmarkMetadata baseline, BenchmarkMetadata candidate)
        {
            if (!result.originVerified || !result.renderingVerified)
            {
                result.warnings.Add("Diagnostic-only comparisons skipped: actual Player origin or working-camera rendering is unverified.");
                return;
            }
            if (baseline.measurementVersion < 3 || candidate.measurementVersion < 3 || baseline.mode != "Diagnostic" || candidate.mode != "Diagnostic" || string.IsNullOrEmpty(baseline.instrumentationSignature) || baseline.instrumentationSignature != candidate.instrumentationSignature)
            {
                result.warnings.Add("Diagnostic-only metrics skipped: instrumented modes/signatures differ or are unavailable.");
                result.comparable = false;
                result.comparabilityStatus = "Incompatible";
                return;
            }
            foreach (string category in new[] { "cpu", "memory", "rendering" })
            {
                string first = Path.Combine(Directory.Exists(baselinePath) ? baselinePath : Path.GetDirectoryName(baselinePath), "diagnostic", category + ".json");
                string second = Path.Combine(Directory.Exists(candidatePath) ? candidatePath : Path.GetDirectoryName(candidatePath), "diagnostic", category + ".json");
                if (!File.Exists(first) || !File.Exists(second))
                {
                    result.warnings.Add("Diagnostic-only metrics unavailable: missing " + category + ".json");
                    result.comparable = false;
                result.comparabilityStatus = "Incompatible";
                    continue;
                }
                var a = JsonUtility.FromJson<BenchmarkDiagnosticDocument>(File.ReadAllText(first));
                var b = JsonUtility.FromJson<BenchmarkDiagnosticDocument>(File.ReadAllText(second));
                if (a.runId != baseline.runId || b.runId != candidate.runId || a.signature != b.signature || a.signature != baseline.instrumentationSignature)
                {
                    result.warnings.Add("Diagnostic document identity/signature mismatch: " + category);
                    result.comparable = false;
                result.comparabilityStatus = "Incompatible";
                    continue;
                }
                foreach (var marker in a.markers)
                {
                    var other = b.markers.Find(value => value.identity != null && marker.identity != null && value.identity.key == marker.identity.key && value.identity.kind == marker.identity.kind && value.unit == marker.unit);
                    if (other == null)
                    {
                        continue;
                    }
                    foreach (var window in marker.windows)
                    {
                        var match = other.windows.Find(value => value.position == window.position && value.window == window.window);
                        bool available = marker.available && other.available && match != null && window.inclusive.available && match.inclusive.available && window.inclusive.samples >= 2 && match.inclusive.samples >= 2;
                        var selector = new BenchmarkWindow { position = window.position, window = window.window };
                        Add(result, selector, "diagnostic/" + category + "/" + marker.name + "/mean", marker.unit, window.inclusive.mean, match == null ? 0 : match.inclusive.mean, available, false);
                        if (marker.unit == "ms")
                        {
                            Add(result, selector, "diagnostic/" + category + "/" + marker.name + "/p95", marker.unit, window.inclusive.p95, match == null ? 0 : match.inclusive.p95, available && window.inclusive.samples >= 30 && match.inclusive.samples >= 30, false);
                        }
                    }
                }
            }
            if (result.comparabilityStatus == "Incompatible")
            {
                foreach (var delta in result.deltas)
                {
                    delta.classification = "Incomparable / insufficient data";
                }
            }
        }

        private static BenchmarkWindow Find(BenchmarkSummary summary, int position, string window)
        {
            return summary.windows.Find(value => value.position == position && value.window == window);
        }

        private static void Add(BenchmarkComparisonResult result, BenchmarkWindow window, string metric, string unit, double baseline, double candidate, bool available, bool higherIsBetter)
        {
            var delta = Delta(baseline, candidate, available, higherIsBetter, result.comparable, result.practicalTolerancePercent);
            delta.position = window.position;
            delta.window = window.window;
            delta.metric = metric;
            delta.unit = unit;
            if (delta.available && result.comparabilityStatus == "Conditional")
            {
                delta.classification = "Observed delta; unverified conditions; no controlled A/B conclusion";
            }
            if (delta.available && result.comparabilityStatus == "UnverifiedOrigin")
            {
                delta.classification = "Observed data only; Player origin unverified; no accepted baseline or confirmed comparison";
            }
            if (delta.available && result.comparabilityStatus == "UnverifiedRendering")
            {
                delta.classification = "Observed data only; working-camera rendering unverified; no accepted baseline or confirmed comparison";
            }
            if (delta.available && result.comparabilityStatus == "Identity")
            {
                delta.classification = "Identity validation; not independent A/B";
            }
            if (delta.available && result.comparabilityStatus == "ConfirmedProtocol")
            {
                delta.classification = "Observed delta under verified configuration protocol; causality/statistical significance not established";
            }
            result.deltas.Add(delta);
        }

        private static void Difference(BenchmarkComparisonResult result, string label, string baseline, string candidate)
        {
            if ((baseline ?? "") != (candidate ?? ""))
            {
                string difference = label + " differs: " + baseline + " -> " + candidate;
                result.incompatibilities.Add(difference);
                result.warnings.Add(difference);
            }
        }

        private static void CompareEnvironment(BenchmarkComparisonResult result, List<BenchmarkEntry> baseline, List<BenchmarkEntry> candidate)
        {
            var first = baseline.FindAll(value => !value.key.StartsWith("observation.", StringComparison.Ordinal) && value.value != null && !value.value.StartsWith("unavailable", StringComparison.Ordinal));
            var second = candidate.FindAll(value => !value.key.StartsWith("observation.", StringComparison.Ordinal) && value.value != null && !value.value.StartsWith("unavailable", StringComparison.Ordinal));
            Difference(result, "Known world/camera conditions", NormalizeEntries(first, result.assetReferencesAnchored), NormalizeEntries(second, result.assetReferencesAnchored));
            foreach (var entry in baseline)
            {
                if (entry.value != null && entry.value.StartsWith("unavailable", StringComparison.Ordinal))
                {
                    result.unverifiedConditions.Add("Unverified condition: " + entry.key + ": " + entry.value);
                }
            }
        }

        private static string NormalizeEntries(List<BenchmarkEntry> entries, bool anchored)
        {
            var copy = new List<BenchmarkEntry>();
            foreach (var entry in entries)
            {
                string value = !anchored ? entry.value : System.Text.RegularExpressions.Regex.Replace(entry.value, "\"instanceID\":\\s*-?[0-9]+", "\"instanceID\":0");
                copy.Add(new BenchmarkEntry { key = entry.key, value = value });
            }
            copy.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
            return JsonUtility.ToJson(new EntryList { entries = copy });
        }

        private static string NewReportDirectory(string category)
        {
            string folder = BenchmarkOutput.ValidateDirectory(Path.Combine(BenchmarkOutput.DEFAULT_ROOT, category, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(folder);
            return folder;
        }

        [Serializable]
        private sealed class TagList
        {
            public string[] tags;
        }

        [Serializable]
        private sealed class PoseList
        {
            public BenchmarkPose[] positions;
        }

        [Serializable]
        private sealed class EntryList
        {
            public List<BenchmarkEntry> entries;
        }
    }
}

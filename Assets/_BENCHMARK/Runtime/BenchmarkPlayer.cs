using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using UnityEngine;

namespace Rvm.Benchmark
{
    public static class BenchmarkPlayer
    {
        private static BenchmarkRunner _runner;
        private static int _remaining;
        private static int _repetition;
        private static string _output;
        private static string _seriesId;
        private static BenchmarkBuildIdentityEvidence _identity;
        private static BenchmarkRouteControl _control;
        private static BenchmarkShadowTrace _controlTrace;
        private static bool _traceEnabled;
        private static readonly List<string> _rejections = new List<string>();
        private static readonly List<string> _runs = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            BenchmarkRunner.Completed -= OnCompleted;
            _runner = null;
            _runs.Clear();
            _remaining = 0;
            _control = null;
            _controlTrace?.Dispose();
            _controlTrace = null;
            _rejections.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            if (Application.isEditor || (!HasArgument("-benchmark") && !HasArgument("-benchmark-identity-only") && !HasArgument("-benchmark-control-route")))
            {
                return;
            }
            try
            {
                _identity = BenchmarkBuildIdentity.Capture();
                _output = BenchmarkOutput.ValidateDirectory(Argument("-benchmark-output", BenchmarkOutput.DEFAULT_ROOT));
                Directory.CreateDirectory(_output);
                BenchmarkOutput.WriteJson(Path.Combine(_output, "build-identity.json"), _identity);
                if (HasArgument("-benchmark-identity-only"))
                {
                    Application.Quit(BenchmarkBuildIdentity.IsVerified(_identity) ? 0 : 2);
                    return;
                }
                string scenario = Argument("-benchmark-scenario", null);
                foreach (var runner in UnityEngine.Object.FindObjectsByType<BenchmarkRunner>())
                {
                    if (runner.Scenario == null || (scenario != null && runner.Scenario.ScenarioId != scenario))
                    {
                        continue;
                    }
                    if (_runner != null)
                    {
                        throw new InvalidOperationException("Multiple benchmark runners match; use -benchmark-scenario ID.");
                    }
                    _runner = runner;
                }
                if (_runner == null)
                {
                    throw new InvalidOperationException("No configured BenchmarkRunner in the startup scene. Include the prepared scene in the Player build.");
                }
                var session = UnityEngine.Object.Instantiate(_runner.Scenario);
                var mode = (BenchmarkMode)Enum.Parse(typeof(BenchmarkMode), Argument("-benchmark-mode", session.Mode.ToString()), true);
                string fixedTime = Argument("-benchmark-fixed-time", null);
                string capture = Argument("-benchmark-cpu-capture", null);
                session.ConfigureSession(mode, fixedTime != null || session.FixedWeatherTime, fixedTime == null ? session.FixedTimeHours : double.Parse(fixedTime, CultureInfo.InvariantCulture), Argument("-benchmark-weather", session.ExpectedWeatherProfile), capture != null || session.ProfilerCapture, capture == null ? session.CaptureSeconds : double.Parse(capture, CultureInfo.InvariantCulture));
                _runner.Configure(session, _runner.WorkingCamera, _runner.Positions);
                string evidence = Argument("-benchmark-conditions", null);
                _runner.SetValidationContext(_runner.BuildSourceDigest, evidence == null ? null : File.ReadAllText(evidence), _runner.RenderSettingsDigest);
                bool trace = HasArgument("-benchmark-shadow-trace");
                _traceEnabled = trace;
                _remaining = int.Parse(Argument("-benchmark-repeat", "1"));
                if (_remaining != 1 && _remaining != 3)
                {
                    throw new ArgumentException("-benchmark-repeat must be 1 or 3.");
                }
                if (HasArgument("-benchmark-control-route"))
                {
                    StartControl();
                    return;
                }
                _runner.EnableShadowTrace(trace);
                _remaining = int.Parse(Argument("-benchmark-repeat", "1"));
                if (_remaining != 1 && _remaining != 3)
                {
                    throw new ArgumentException("-benchmark-repeat must be 1 or 3.");
                }
                _output = BenchmarkOutput.ValidateDirectory(Argument("-benchmark-output", BenchmarkOutput.DEFAULT_ROOT));
                _seriesId = Guid.NewGuid().ToString("N");
                _repetition = 1;
                BenchmarkRunner.Completed += OnCompleted;
                _runner.Begin(_output, _seriesId, _repetition);
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        private static void OnCompleted(BenchmarkRunner runner)
        {
            if (runner != _runner)
            {
                return;
            }
            try
            {
                if (!runner.Saved || runner.LastSummary == null || runner.LastSummary.status != "completed")
                {
                    throw new IOException("Benchmark aborted or output save failed. Partial results: " + runner.LastDirectory);
                }
                _runs.Add(runner.LastDirectory);
                if (runner.Scenario.Mode == BenchmarkMode.Standard && !BenchmarkComparison.IsBaselineEligible(runner.LastMetadata, runner.LastSummary, out string rejection))
                {
                    _rejections.Add(runner.LastSummary.runId + ": " + rejection);
                }
                if (runner.Scenario.Mode == BenchmarkMode.Diagnostic && (!BenchmarkBuildIdentity.IsVerified(runner.LastMetadata.buildIdentity, runner.LastMetadata.buildSourceDigest, runner.LastMetadata.renderSettingsDigest) || runner.LastMetadata.runtimeErrors != 0 || runner.LastMetadata.renderedMeasurementFrames <= 0))
                {
                    _rejections.Add(runner.LastSummary.runId + ": diagnostic has errors, unverified build identity or no rendered measurement frames.");
                }
                if (--_remaining > 0)
                {
                    _runner.Begin(_output, _seriesId, ++_repetition);
                    return;
                }
                if (_runs.Count > 1)
                {
                    Debug.Log("Benchmark series saved: " + BenchmarkComparison.SaveSeries(_seriesId, _runs));
                }
                BenchmarkRunner.Completed -= OnCompleted;
                BenchmarkOutput.WriteJson(Path.Combine(_output, "process-result.json"), new PlayerResult { accepted = _rejections.Count == 0, runs = _runs.ToArray(), reasons = _rejections.ToArray(), identity = _identity });
                Application.Quit(_rejections.Count == 0 ? 0 : 3);
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        private static void OnControlCompleted(BenchmarkRouteControl control)
        {
            control.Completed -= OnControlCompleted;
            try
            {
                _controlTrace?.Save(Path.Combine(control.LastDirectory, "shadow-trace.json"));
            }
            catch (Exception exception)
            {
                Debug.LogError("Control trace save failed: " + exception);
            }
            finally
            {
                _controlTrace?.Dispose();
                _controlTrace = null;
            }
            _runs.Add(control.LastDirectory);
            if (!control.Saved || control.Error != null || control.Metadata.runtimeErrors != 0 || !BenchmarkBuildIdentity.IsVerified(_identity))
            {
                _rejections.Add(control.RunId + ": control aborted, had errors, save failure or unverified identity.");
            }
            if (control.RenderedFrames <= 0)
            {
                _rejections.Add(control.RunId + ": control route completed without a rendered working-camera frame.");
            }
            if (--_remaining > 0)
            {
                UnityEngine.Object.Destroy(control);
                StartControl();
                return;
            }
            BenchmarkOutput.WriteJson(Path.Combine(_output, "process-result.json"), new PlayerResult { accepted = _rejections.Count == 0, runs = _runs.ToArray(), reasons = _rejections.ToArray(), identity = _identity });
            Application.Quit(_rejections.Count == 0 ? 0 : 3);
        }

        private static void StartControl()
        {
            _control = _runner.gameObject.AddComponent<BenchmarkRouteControl>();
            if (_traceEnabled)
            {
                _controlTrace = new BenchmarkShadowTrace(_runner.WorkingCamera, () => new BenchmarkTraceContext { runId = _control.RunId, phase = _control.Phase, position = _control.Position, transition = _control.Transition });
            }
            _control.Completed += OnControlCompleted;
            _control.Begin(_runner, _output, metadata =>
            {
                metadata.buildIdentity = _identity;
                metadata.shadowTrace = _traceEnabled;
                if (_identity.stamp != null)
                {
                    metadata.buildSourceDigest = _identity.stamp.sourceDigest;
                    metadata.renderSettingsDigest = _identity.stamp.renderSettingsDigest;
                    metadata.gitSha = _identity.stamp.gitSha;
                    metadata.gitDirty = _identity.stamp.gitDirty;
                    metadata.hdrp = _identity.stamp.hdrp;
                }
            });
        }

        [Serializable]
        private sealed class PlayerResult
        {
            public bool accepted;
            public string[] runs;
            public string[] reasons;
            public BenchmarkBuildIdentityEvidence identity;
        }

        private static bool HasArgument(string name)
        {
            return Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;
        }

        private static string Argument(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            if (index < 0)
            {
                return fallback;
            }
            if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
            {
                throw new ArgumentException("Missing value for " + name);
            }
            return args[index + 1];
        }

        private static void Fail(Exception exception)
        {
            BenchmarkRunner.Completed -= OnCompleted;
            Debug.LogError("Benchmark failed: " + exception);
            try
            {
                string folder = BenchmarkOutput.ValidateDirectory(Path.Combine(BenchmarkOutput.DEFAULT_ROOT, "StartupFailures", Guid.NewGuid().ToString("N")));
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "benchmark.log"), exception.ToString());
            }
            catch (Exception saveException)
            {
                Debug.LogError("Cannot save benchmark failure log: " + saveException.Message);
            }
            Application.Quit(2);
        }
    }
}

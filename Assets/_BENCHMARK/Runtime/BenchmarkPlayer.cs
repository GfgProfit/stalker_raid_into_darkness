using System;
using System.Collections.Generic;
using System.IO;
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
        private static readonly List<string> _runs = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            BenchmarkRunner.Completed -= OnCompleted;
            _runner = null;
            _runs.Clear();
            _remaining = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            if (Application.isEditor || !HasArgument("-benchmark"))
            {
                return;
            }
            try
            {
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
                Application.Quit(0);
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
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

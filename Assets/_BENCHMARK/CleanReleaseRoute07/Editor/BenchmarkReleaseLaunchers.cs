using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Rvm.CleanReleaseRoute07.Editor
{
    public static class BenchmarkReleaseLaunchers
    {
        [MenuItem("Tools/RVM/Build Benchmark Release")]
        public static void Build()
        {
            string root = @"E:\Projects\stalker_raid_into_darkness_Archive\BenchmarkPlayers\" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string previous = Environment.GetEnvironmentVariable("CLEAN_RELEASE_ROUTE07_OUTPUT");
            try
            {
                Environment.SetEnvironmentVariable("CLEAN_RELEASE_ROUTE07_OUTPUT", root);
                CleanReleaseBuild.BuildFromCommandLine();
                EditorUtility.RevealInFinder(Path.Combine(root, "Launchers"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("CLEAN_RELEASE_ROUTE07_OUTPUT", previous);
            }
        }

        public static void Write(string root, Rvm.Benchmark.BenchmarkScenario[] profiles)
        {
            string directory = Path.Combine(root, "Launchers");
            Directory.CreateDirectory(directory);
            File.Copy("Assets/_BENCHMARK/Launchers/run_benchmark.py", Path.Combine(directory, "run_benchmark.py"));
            File.Copy("Assets/_BENCHMARK/Launchers/run_pix_diagnostic.py", Path.Combine(directory, "run_pix_diagnostic.py"));
            File.Copy("Assets/_BENCHMARK/Launchers/Run-Series.ps1", Path.Combine(directory, "Run-Series.ps1"));
            foreach (var profile in profiles)
            {
                foreach (string mode in new[] { "U", "G1" })
                {
                    string command = "@echo off\r\npython \"%~dp0run_benchmark.py\" " + profile.ScenarioId + " " + mode + "\r\npause\r\n";
                    File.WriteAllText(Path.Combine(directory, profile.ScenarioId + "_" + mode + ".cmd"), command);
                }
            }
            File.WriteAllText(Path.Combine(directory, "Ordinary_Game.cmd"), "@echo off\r\nstart \"\" \"%~dp0..\\Player\\CleanRelease.exe\"\r\n");
            File.WriteAllText(Path.Combine(directory, "Fixed_Series.cmd"), "@echo off\r\npowershell.exe -NoProfile -ExecutionPolicy Bypass -File \"%~dp0Run-Series.ps1\"\r\npause\r\n");
        }
    }
}

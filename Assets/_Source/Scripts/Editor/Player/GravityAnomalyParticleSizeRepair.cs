using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RaidIntoDarkness.Player.Editor
{
    [Serializable]
    public sealed class GravityAnomalyParticleSizeState
    {
        public float StartMinimumX;
        public float StartMaximumX;
        public float StartMinimumY;
        public float StartMaximumY;
        public float CurveMultiplierX;
        public float CurveMultiplierY;
        public float MaximumAbsoluteSize;
    }

    [Serializable]
    public sealed class GravityAnomalyParticleSizeChange
    {
        public string PrefabPath;
        public string SystemPath;
        public string BackupPath;
        public bool Changed;
        public GravityAnomalyParticleSizeState Before;
        public GravityAnomalyParticleSizeState After;
    }

    [Serializable]
    public sealed class GravityAnomalyParticleSizeRepairReport
    {
        public bool Completed;
        public string ReportPath;
        public List<GravityAnomalyParticleSizeChange> Changes = new List<GravityAnomalyParticleSizeChange>();
        public List<string> Errors = new List<string>();
    }

    public static class GravityAnomalyParticleSizeRepair
    {
        public const string CAROUSEL_PREFAB = "Assets/_Source/Anomalies/Carousel/Carousel.prefab";
        public const string CAROUSEL_SYSTEM = "Idle/gravitat_dist_ring_single";
        public const string VORTEX_PREFAB = "Assets/_Source/Anomalies/Vortex/Vortex.prefab";
        public const string VORTEX_SYSTEM = "Idle/gravi_we_ground_dist";

        private const string TASK_ARCHIVE = "E:/Projects/stalker_raid_into_darkness_Archive/GravityDistortionFix_20261110";
        private const int CURVE_STEPS = 128;

        public static void Configure(ParticleSystem system, float minimum, float maximum, float target, float factor, float lifetime)
        {
            if (system == null)
            {
                throw new ArgumentNullException(nameof(system));
            }

            Validate(minimum, maximum, target, factor, lifetime);
            if (lifetime <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(lifetime));
            }

            var times = new SortedSet<float>();
            for (int index = 0; index <= CURVE_STEPS; index++)
            {
                times.Add((float)index / CURVE_STEPS);
            }

            AddCrossing(times, minimum, target, factor, lifetime);
            AddCrossing(times, maximum, target, factor, lifetime);
            AddCrossing(times, (minimum + maximum) * 0.5f, target, factor, lifetime);
            var low = new List<Keyframe>();
            var high = new List<Keyframe>();
            foreach (float time in times)
            {
                Vector2 bounds = EvaluateBounds(minimum, maximum, target, factor, time * lifetime);
                low.Add(new Keyframe(time, bounds.x));
                high.Add(new Keyframe(time, bounds.y));
            }

            AnimationCurve lower = LinearCurve(low);
            AnimationCurve upper = LinearCurve(high);
            ParticleSystem.MainModule main = system.main;
            main.startSize3D = true;
            main.startSizeX = 1f;
            main.startSizeY = 1f;
            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.separateAxes = true;
            size.x = new ParticleSystem.MinMaxCurve(1f, lower, upper);
            size.y = new ParticleSystem.MinMaxCurve(1f, lower, upper);
        }

        public static Vector2 EvaluateBounds(float minimum, float maximum, float target, float factor, float age)
        {
            Validate(minimum, maximum, target, factor, age);
            double decay = Math.Exp(-(double)factor * age);
            double first = target + ((double)minimum - target) * decay;
            double second = target + ((double)maximum - target) * decay;
            double lower = first <= 0.0 && second >= 0.0 ? 0.0 : Math.Min(Math.Abs(first), Math.Abs(second));
            return new Vector2((float)lower, (float)Math.Max(Math.Abs(first), Math.Abs(second)));
        }

        public static GravityAnomalyParticleSizeState Inspect(ParticleSystem system)
        {
            if (system == null)
            {
                throw new ArgumentNullException(nameof(system));
            }

            ParticleSystem.MainModule main = system.main;
            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            ParticleSystem.MinMaxCurve x = main.startSize3D ? main.startSizeX : main.startSize;
            ParticleSystem.MinMaxCurve y = main.startSize3D ? main.startSizeY : main.startSize;
            float maximum = 0f;
            for (int index = 0; index <= CURVE_STEPS; index++)
            {
                float time = (float)index / CURVE_STEPS;
                for (int initial = 0; initial <= 1; initial++)
                {
                    for (int lifetime = 0; lifetime <= 1; lifetime++)
                    {
                        float factorX = size.enabled ? (size.separateAxes ? size.x : size.size).Evaluate(time, lifetime) : 1f;
                        float factorY = size.enabled ? (size.separateAxes ? size.y : size.size).Evaluate(time, lifetime) : 1f;
                        maximum = Mathf.Max(maximum, Mathf.Abs(x.Evaluate(0f, initial) * factorX), Mathf.Abs(y.Evaluate(0f, initial) * factorY));
                    }
                }
            }

            return new GravityAnomalyParticleSizeState { StartMinimumX = x.Evaluate(0f, 0f), StartMaximumX = x.Evaluate(0f, 1f), StartMinimumY = y.Evaluate(0f, 0f), StartMaximumY = y.Evaluate(0f, 1f), CurveMultiplierX = size.separateAxes ? size.x.curveMultiplier : size.size.curveMultiplier, CurveMultiplierY = size.separateAxes ? size.y.curveMultiplier : size.size.curveMultiplier, MaximumAbsoluteSize = maximum };
        }

        public static GravityAnomalyParticleSizeRepairReport Apply(string reportFolder)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Исправление размеров частиц выполняется только вне Play Mode.");
            }

            string folder = ValidateReportFolder(reportFolder);
            string[] paths = { CAROUSEL_PREFAB, VORTEX_PREFAB };
            string[] systems = { CAROUSEL_SYSTEM, VORTEX_SYSTEM };
            var roots = new GameObject[paths.Length];
            var particles = new ParticleSystem[paths.Length];
            var report = new GravityAnomalyParticleSizeRepairReport { ReportPath = Path.Combine(folder, "particle-size-repair.json") };
            try
            {
                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                for (int index = 0; index < paths.Length; index++)
                {
                    if (stage != null && string.Equals(stage.assetPath, paths[index], StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Закройте Prefab Mode перед исправлением: " + paths[index]);
                    }

                    GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(paths[index]);
                    if (asset == null || EditorUtility.IsDirty(asset) || !File.Exists(paths[index] + ".meta"))
                    {
                        throw new InvalidOperationException("Префаб отсутствует, имеет несохранённые изменения или потерял meta: " + paths[index]);
                    }

                    roots[index] = PrefabUtility.LoadPrefabContents(paths[index]);
                    Transform node = roots[index].transform.Find(systems[index]);
                    particles[index] = node != null ? node.GetComponent<ParticleSystem>() : null;
                    if (particles[index] == null)
                    {
                        throw new InvalidOperationException("Не найдена исходная система частиц: " + paths[index] + "/" + systems[index]);
                    }
                }

                Directory.CreateDirectory(folder);
                string backups = Path.Combine(folder, "Backups");
                Directory.CreateDirectory(backups);
                for (int index = 0; index < paths.Length; index++)
                {
                    string backup = Path.Combine(backups, Path.GetFileName(paths[index]));
                    File.Copy(paths[index], backup, false);
                    File.Copy(paths[index] + ".meta", backup + ".meta", false);
                    report.Changes.Add(new GravityAnomalyParticleSizeChange { PrefabPath = paths[index], SystemPath = systems[index], BackupPath = backup, Before = Inspect(particles[index]) });
                }

                for (int index = 0; index < paths.Length; index++)
                {
                    string before = EditorJsonUtility.ToJson(particles[index]);
                    Configure(particles[index], index == 0 ? -2.5f : 0f, index == 0 ? 2.5f : 0f, index == 0 ? 3.3f : 2f, index == 0 ? 1f : 3f, index == 0 ? 0.3f : 1f);
                    report.Changes[index].Changed = before != EditorJsonUtility.ToJson(particles[index]);
                    report.Changes[index].After = Inspect(particles[index]);
                }

                for (int index = 0; index < paths.Length; index++)
                {
                    if (!report.Changes[index].Changed)
                    {
                        continue;
                    }

                    PrefabUtility.SaveAsPrefabAsset(roots[index], paths[index], out bool saved);
                    if (!saved)
                    {
                        throw new IOException("Не удалось сохранить префаб: " + paths[index]);
                    }
                }

                report.Completed = true;
                File.WriteAllText(report.ReportPath, JsonUtility.ToJson(report, true));
                return report;
            }
            catch (Exception exception)
            {
                report.Errors.Add(exception.GetType().Name + ": " + exception.Message);
                if (Directory.Exists(folder))
                {
                    File.WriteAllText(report.ReportPath, JsonUtility.ToJson(report, true));
                }

                throw;
            }
            finally
            {
                foreach (GameObject root in roots)
                {
                    if (root != null)
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }
            }
        }

        private static AnimationCurve LinearCurve(List<Keyframe> keys)
        {
            for (int index = 0; index < keys.Count; index++)
            {
                Keyframe key = keys[index];
                key.inTangent = index == 0 ? 0f : (key.value - keys[index - 1].value) / (key.time - keys[index - 1].time);
                key.outTangent = index == keys.Count - 1 ? 0f : (keys[index + 1].value - key.value) / (keys[index + 1].time - key.time);
                keys[index] = key;
            }

            return new AnimationCurve(keys.ToArray()) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
        }

        private static void AddCrossing(ISet<float> times, float initial, float target, float factor, float lifetime)
        {
            if (factor == 0f || target == 0f || initial == target)
            {
                return;
            }

            double ratio = -target / ((double)initial - target);
            if (ratio <= 0.0 || ratio >= 1.0)
            {
                return;
            }

            double time = -Math.Log(ratio) / factor;
            if (time > 0.0 && time < lifetime)
            {
                times.Add((float)(time / lifetime));
            }
        }

        private static void Validate(float minimum, float maximum, float target, float factor, float time)
        {
            if (!Finite(minimum) || !Finite(maximum) || !Finite(target) || !Finite(factor) || !Finite(time) || minimum > maximum || factor < 0f || time < 0f)
            {
                throw new ArgumentException("Требуются конечный диапазон размеров, неотрицательный коэффициент и неотрицательное время.");
            }
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string ValidateReportFolder(string reportFolder)
        {
            string archive = Path.GetFullPath(TASK_ARCHIVE).TrimEnd(Path.DirectorySeparatorChar);
            string folder = Path.GetFullPath(reportFolder ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(folder), archive, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(folder).StartsWith("Apply_", StringComparison.Ordinal) || Path.GetFileName(folder).Length <= "Apply_".Length)
            {
                throw new ArgumentException("Отчёт и резервные копии должны находиться в новой папке " + TASK_ARCHIVE + "/Apply_<timestamp>.", nameof(reportFolder));
            }

            for (DirectoryInfo parent = new DirectoryInfo(folder); parent != null; parent = parent.Parent)
            {
                if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Каталог отчёта не должен проходить через junction или reparse point: " + parent.FullName);
                }
            }

            if (Directory.Exists(folder) && (Directory.GetFiles(folder).Length != 0 || Directory.GetDirectories(folder).Length != 0))
            {
                throw new IOException("Каталог отчёта уже содержит результаты: " + folder);
            }

            return folder;
        }
    }
}

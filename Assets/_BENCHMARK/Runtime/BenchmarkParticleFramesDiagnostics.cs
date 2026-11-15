using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RaidIntoDarkness.Effects;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rvm.Benchmark
{
    public static class BenchmarkParticleFramesDiagnostics
    {
        private static readonly Dictionary<AnomalyParticleFrames, Group> COMPONENTS = new Dictionary<AnomalyParticleFrames, Group>();
        private static readonly Dictionary<string, Group> GROUPS = new Dictionary<string, Group>();

        private static int _sampleFrame = -1;
        private static double _nextSample;
        private static bool _visual;
        private static BenchmarkRunner _visualRunner;
        private static int _screenshots;
        private static double _nextScreenshot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            AnomalyParticleFrames.ParticlesObserved -= Observe;
            BenchmarkRunner.Completed -= Save;
            RenderPipelineManager.endCameraRendering -= CaptureVisual;
            AnomalyParticleFrames.DiagnosticScopesEnabled = false;
            COMPONENTS.Clear();
            GROUPS.Clear();
            _sampleFrame = -1;
            _nextSample = 0;
            _visual = false;
            _visualRunner = null;
            _screenshots = 0;
            _nextScreenshot = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            bool diagnostics = Array.IndexOf(Environment.GetCommandLineArgs(), "-particle-frames-diagnostics") >= 0;
            _visual = Array.IndexOf(Environment.GetCommandLineArgs(), "-particle-frames-visual") >= 0;
            if (Application.isEditor || !diagnostics && !_visual)
            {
                return;
            }
            AnomalyParticleFrames.DiagnosticScopesEnabled = diagnostics;
            if (diagnostics)
            {
                AnomalyParticleFrames.ParticlesObserved += Observe;
            }
            if (_visual)
            {
                RenderPipelineManager.endCameraRendering += CaptureVisual;
            }
            BenchmarkRunner.Completed += Save;
        }

        private static T Field<T>(AnomalyParticleFrames component, string name)
        {
            return (T)typeof(AnomalyParticleFrames).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(component);
        }

        private static void CaptureVisual(ScriptableRenderContext context, Camera camera)
        {
            if (_visualRunner == null)
            {
                _visualRunner = UnityEngine.Object.FindFirstObjectByType<BenchmarkRunner>();
            }
            if (_visualRunner == null || camera != _visualRunner.WorkingCamera || !_visualRunner.Running || _screenshots >= 6)
            {
                return;
            }
            var phase = _visualRunner.TraceContext;
            double now = Time.realtimeSinceStartupAsDouble;
            if (phase.phase != "Measurement" || phase.position != 0 || phase.transition || now < _nextScreenshot)
            {
                return;
            }
            string file = Path.Combine(_visualRunner.LastDirectory, "visual_" + _screenshots.ToString("D2") + "_requestFrame_" + Time.frameCount + ".png");
            ScreenCapture.CaptureScreenshot(file);
            if (_screenshots == 0)
            {
                _visualRunner.LastMetadata.warnings.Add("Separate visual screenshot sequence; not a performance baseline. Filenames identify screenshot request frames, not GPU source frames.");
            }
            _screenshots++;
            _nextScreenshot = now + 0.1;
        }

        private static void Observe(AnomalyParticleFrames component, ParticleSystem.Particle[] particles, int count)
        {
            int frame = Time.frameCount;
            if (frame != _sampleFrame)
            {
                if (Time.realtimeSinceStartupAsDouble < _nextSample)
                {
                    return;
                }
                _sampleFrame = frame;
                _nextSample = Time.realtimeSinceStartupAsDouble + 1;
            }
            if (!COMPONENTS.TryGetValue(component, out var group))
            {
                int frames = Field<int>(component, "_frameCount");
                float fps = Field<float>(component, "_framesPerSecond");
                bool animated = Field<bool>(component, "_animated");
                bool start = Field<bool>(component, "_randomStart");
                bool playback = Field<bool>(component, "_randomPlayback");
                string key = frames + "/" + fps.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "/" + animated + "/" + start + "/" + playback;
                if (!GROUPS.TryGetValue(key, out group))
                {
                    group = new Group { key = key, frameCount = frames, framesPerSecond = fps, animated = animated, randomStart = start, randomPlayback = playback };
                    GROUPS.Add(key, group);
                }
                COMPONENTS.Add(component, group);
                group.components++;
            }
            group.observations++;
            group.empty += count == 0 ? 1 : 0;
            group.liveParticles += count;
            bool finite = false;
            bool infinite = false;
            for (int i = 0; i < count; i++)
            {
                var particle = particles[i];
                bool persistent = float.IsPositiveInfinity(particle.startLifetime);
                float age = persistent ? 0 : particle.startLifetime - particle.remainingLifetime;
                if (persistent)
                {
                    infinite = true;
                    group.infiniteParticles++;
                }
                else
                {
                    finite = true;
                    group.finiteParticles++;
                    group.maxAge = Mathf.Max(group.maxAge, age);
                    group.maxLifetime = Mathf.Max(group.maxLifetime, particle.startLifetime);
                    group.sourceSteps += group.animated ? Math.Max(1, Mathf.FloorToInt(Mathf.Max(0, age) / 0.033f) + 1) : 0;
                }
                if (group.samples.Count < 16)
                {
                    group.samples.Add(new Input { age = age, seed = particle.randomSeed, infinite = persistent, lifetime = persistent ? "Infinity" : particle.startLifetime.ToString("R", System.Globalization.CultureInfo.InvariantCulture) });
                }
            }
            group.finiteSystems += finite ? 1 : 0;
            group.infiniteSystems += infinite ? 1 : 0;
        }

        private static void Save(BenchmarkRunner runner)
        {
            if (runner.LastDirectory != null)
            {
                BenchmarkOutput.WriteJson(Path.Combine(runner.LastDirectory, "particle-workload.json"), new Document { runId = runner.LastMetadata.runId, groups = new List<Group>(GROUPS.Values), sampledComponents = COMPONENTS.Count, cache = CacheSnapshot() });
            }
            COMPONENTS.Clear();
            GROUPS.Clear();
        }

        [Serializable]
        private sealed class CacheData
        {
            public bool present;
            public int configurations;
            public int sequences;
            public long arrayPayloadBytes;
        }

        private static CacheData CacheSnapshot()
        {
            var result = new CacheData();
            var type = typeof(AnomalyParticleFrames).Assembly.GetType("RaidIntoDarkness.Effects.FiniteParticleFrameCache");
            if (type == null)
            {
                return result;
            }
            result.present = true;
            var cache = (System.Collections.IDictionary)type.GetField("CACHE", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            result.configurations = cache.Count;
            foreach (System.Collections.DictionaryEntry pair in cache)
            {
                var value = pair.Value;
                var fields = value.GetType();
                var states = (float[][])fields.GetField("_checkpoints", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(value);
                result.arrayPayloadBytes += states.Length * IntPtr.Size + states.Length * sizeof(int);
                foreach (var state in states)
                {
                    if (state != null)
                    {
                        result.sequences++;
                        result.arrayPayloadBytes += state.Length * sizeof(float);
                    }
                }
            }
            return result;
        }

        [Serializable]
        private sealed class Document
        {
            public string runId;
            public int sampledComponents;
            public List<Group> groups;
            public CacheData cache;
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
            public List<Input> samples = new List<Input>();
        }

        [Serializable]
        private sealed class Input
        {
            public float age;
            public uint seed;
            public bool infinite;
            public string lifetime;
        }
    }
}

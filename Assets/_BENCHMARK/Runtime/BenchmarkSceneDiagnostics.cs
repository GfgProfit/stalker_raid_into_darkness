using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace Rvm.Benchmark
{
    public sealed class BenchmarkSceneDiagnostics
    {
        private readonly Camera _camera;
        private readonly List<Light> _lights = new List<Light>();
        private readonly List<ParticleSystem> _particles = new List<ParticleSystem>();
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<MonoBehaviour> _weather = new List<MonoBehaviour>();
        private readonly List<MonoBehaviour> _anomalies = new List<MonoBehaviour>();
        private readonly List<MonoBehaviour> _visuals = new List<MonoBehaviour>();
        private readonly List<MonoBehaviour> _instances = new List<MonoBehaviour>();
        private readonly List<Behaviour> _navigation = new List<Behaviour>();
        private readonly Plane[] _planes = new Plane[6];
        private readonly Dictionary<Type, Dictionary<string, MemberInfo>> _members = new Dictionary<Type, Dictionary<string, MemberInfo>>();

        public PropertyInfo RendererSwitch { get; private set; }

        public BenchmarkSceneDiagnostics(Camera camera, bool rendering)
        {
            _camera = camera;
            foreach (var root in camera.gameObject.scene.GetRootGameObjects())
            {
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null)
                    {
                        continue;
                    }
                    if (rendering && component is Light light)
                    {
                        _lights.Add(light);
                    }
                    if (rendering && component is ParticleSystem particle)
                    {
                        _particles.Add(particle);
                    }
                    if (rendering && component is Renderer renderer)
                    {
                        _renderers.Add(renderer);
                    }
                    string name = component.GetType().FullName;
                    if (component is Behaviour navigation && name == "UnityEngine.AI.NavMeshAgent")
                    {
                        _navigation.Add(navigation);
                    }
                    if (!(component is MonoBehaviour behaviour))
                    {
                        continue;
                    }
                    if (name == "XRay.Importer.Levels.XRayWeatherController")
                    {
                        _weather.Add(behaviour);
                    }
                    if (name == "RaidIntoDarkness.Player.AnomalyPlayerReaction")
                    {
                        _anomalies.Add(behaviour);
                    }
                    if (Inherits(component.GetType(), "RaidIntoDarkness.Effects.AnomalyVisual"))
                    {
                        _visuals.Add(behaviour);
                    }
                    if (name == "XRay.Importer.Levels.XRayLevelInstancedRenderer")
                    {
                        _instances.Add(behaviour);
                        RendererSwitch = behaviour.GetType().GetProperty("DiagnosticMarkersEnabled", BindingFlags.Static | BindingFlags.Public);
                    }
                }
            }
        }

        public BenchmarkDiagnosticSnapshot World(BenchmarkFrame frame)
        {
            var snapshot = Snapshot(frame);
            CollectWorld(snapshot.entries);
            return snapshot;
        }

        public void CollectWorld(List<BenchmarkEntry> entries)
        {
            Add(entries, "location.scene", _camera.gameObject.scene.path);
            Add(entries, "observation.unityFrame", Time.frameCount);
            for (int i = 0; i < _weather.Count; i++)
            {
                var weather = _weather[i];
                if (weather == null)
                {
                    Add(entries, "weather." + i, "unavailable: destroyed since cached inventory");
                    continue;
                }
                string prefix = "weather." + i + ".";
                Add(entries, prefix + "enabled", weather.isActiveAndEnabled);
                foreach (string property in new[] { "GameTimeSeconds", "Paused", "TimeScale", "ConfiguredTimeOfDay", "ActiveProfile", "ActiveFx", "FxPhase", "IsTransitioning", "TransitionProgress", "FogEnabled", "SunShaftsEnabled", "LensFlareEnabled", "Diagnostic" })
                {
                    Add(entries, prefix + property, Read(weather, property));
                }
                object weatherSnapshot = Read(weather, "Snapshot");
                Add(entries, prefix + "Snapshot", weatherSnapshot != null && weatherSnapshot.GetType().FullName == "XRay.Importer.Levels.XRayWeatherSnapshot" ? JsonUtility.ToJson(weatherSnapshot) : "unavailable: weather snapshot API");
            }
            Add(entries, "weather.controllerCount", _weather.Count);
            int enabled = 0;
            int sourceActive = 0;
            int sourceDisabled = 0;
            int sourceUnknown = 0;
            int cycles = 0;
            int missing = 0;
            foreach (var anomaly in _anomalies)
            {
                if (anomaly == null)
                {
                    missing++;
                    continue;
                }
                if (anomaly.isActiveAndEnabled)
                {
                    enabled++;
                }
                object source = Read(anomaly, "_sourceActive");
                if (source is bool active)
                {
                    if (active)
                    {
                        sourceActive++;
                    }
                    else
                    {
                        sourceDisabled++;
                    }
                }
                else
                {
                    sourceUnknown++;
                }
                object phase = Read(anomaly, "Phase");
                if (anomaly.isActiveAndEnabled && phase is Enum && phase.ToString() != "Idle")
                {
                    cycles++;
                }
            }
            int visuals = 0;
            int visible = 0;
            foreach (var visual in _visuals)
            {
                if (visual != null && visual.isActiveAndEnabled)
                {
                    visuals++;
                    if (Read(visual, "IsVisible") is bool value && value)
                    {
                        visible++;
                    }
                }
            }
            Add(entries, "anomalies.placedReactionComponents", _anomalies.Count);
            Add(entries, "anomalies.destroyedSinceInventory", missing);
            Add(entries, "anomalies.enabledReactionComponents", enabled);
            Add(entries, "anomalies.sourceActiveFlag", sourceActive);
            Add(entries, "anomalies.sourceDisabledFlag", sourceDisabled);
            Add(entries, "anomalies.sourceFlagUnavailable", sourceUnknown);
            Add(entries, "anomalies.enabledNonIdleCycles", cycles);
            Add(entries, "anomalies.enabledVisualComponents", visuals);
            Add(entries, "anomalies.visibleVisualComponents", visible);
            Add(entries, "anomalies.sourceSemantics", "Source flag is read independently of visual visibility; enabled component is separate. Reaction components count is not a claim that every possible anomaly implementation is covered.");
            int agents = 0;
            foreach (var agent in _navigation)
            {
                if (agent != null && agent.isActiveAndEnabled)
                {
                    agents++;
                }
            }
            Add(entries, "npc.activeNavMeshAgents", agents);
            Add(entries, "npc.otherAI", "unavailable: no authoritative NPC registry exposed to benchmark");
        }

        public BenchmarkDiagnosticSnapshot Rendering(BenchmarkFrame frame, bool rendering, bool memory)
        {
            var snapshot = Snapshot(frame);
            var entries = snapshot.entries;
            Add(entries, "observation.unityFrame", Time.frameCount);
            if (rendering)
            {
                GeometryUtility.CalculateFrustumPlanes(_camera, _planes);
                int activeLights = 0;
                int shadowedLights = 0;
                foreach (var light in _lights)
                {
                    if (light != null && light.isActiveAndEnabled)
                    {
                        activeLights++;
                        if (light.shadows != LightShadows.None)
                        {
                            shadowedLights++;
                        }
                    }
                }
                int activeParticles = 0;
                int playingParticles = 0;
                long particles = 0;
                foreach (var particle in _particles)
                {
                    if (particle != null && particle.gameObject.activeInHierarchy)
                    {
                        activeParticles++;
                        if (particle.isPlaying)
                        {
                            playingParticles++;
                        }
                        particles += particle.particleCount;
                    }
                }
                int activeRenderers = 0;
                int visibleAnyCamera = 0;
                int frustumCandidates = 0;
                foreach (var renderer in _renderers)
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    {
                        continue;
                    }
                    activeRenderers++;
                    if (renderer.isVisible)
                    {
                        visibleAnyCamera++;
                    }
                    if ((_camera.cullingMask & (1 << renderer.gameObject.layer)) != 0 && GeometryUtility.TestPlanesAABB(_planes, renderer.bounds))
                    {
                        frustumCandidates++;
                    }
                }
                Add(entries, "lights.active", activeLights);
                Add(entries, "lights.configuredShadowed", shadowedLights);
                Add(entries, "particles.activeSystems", activeParticles);
                Add(entries, "particles.playingSystems", playingParticles);
                Add(entries, "particles.liveCount", particles);
                Add(entries, "renderers.enabled", activeRenderers);
                Add(entries, "renderers.visibleAnyCamera", visibleAnyCamera);
                Add(entries, "renderers.benchmarkCameraFrustumCandidates", frustumCandidates);
                Add(entries, "cameras.activeAllScenes", Camera.allCamerasCount);
                for (int i = 0; i < _instances.Count; i++)
                {
                    var instance = _instances[i];
                    string prefix = "instancer." + i + ".";
                    if (instance == null)
                    {
                        Add(entries, prefix + "state", "unavailable: destroyed since inventory");
                        continue;
                    }
                    object batchSet = Read(instance, "BatchSet");
                    object batches = batchSet is UnityEngine.Object asset && asset != null ? Read(batchSet, "Batches") : null;
                    int serialized = batches is ICollection collection ? collection.Count : -1;
                    Add(entries, prefix + "serializedBatches", serialized >= 0 ? (object)serialized : "unavailable: batch list count API not exposed");
                    Add(entries, prefix + "registeredGpuBatches", "unavailable: no separate registration stage/counter");
                    Add(entries, prefix + "actualGpuDrawCalls", "unavailable: CPU commands expand to passes; use GPU capture");
                    bool aligned = ReferenceEquals(Read(instance, "DiagnosticCamera"), _camera) && Read(instance, "DiagnosticFrame") is int sourceFrame && sourceFrame == frame.unityFrame;
                    Add(entries, prefix + "sourceFrameMatchesBenchmarkCamera", aligned);
                    foreach (string property in new[] { "LastVisibleBatches", "LastVisibleInstances", "LastShadowOnlyBatches", "LastRejectedShadowBatches", "LastImpostors", "LastLodShadowOnlyInstances", "LastLodDiscardedInstances", "DiagnosticCpuSubmissions", "DiagnosticSubmittedBatches", "DiagnosticSubmissionInstanceReferences" })
                    {
                        Add(entries, prefix + property, aligned ? Read(instance, property) : "unavailable: last renderer sample belongs to a different camera/frame");
                    }
                    if (aligned && serialized >= 0 && Read(instance, "LastVisibleBatches") is int visible && Read(instance, "LastShadowOnlyBatches") is int shadowOnly)
                    {
                        Add(entries, prefix + "notVisibleOrShadowOnlyBatches", Math.Max(0, serialized - visible - shadowOnly));
                    }
                    Add(entries, prefix + "ReflectionSubmittedBatchesCumulative", Read(instance, "ReflectionSubmittedBatches"));
                }
            }
            if (memory)
            {
                foreach (string property in new[] { "currentTextureMemory", "desiredTextureMemory", "targetTextureMemory", "totalTextureMemory", "nonStreamingTextureMemory", "streamingTextureCount", "nonStreamingTextureCount", "streamingTexturePendingLoadCount", "streamingTextureLoadingCount" })
                {
                    PropertyInfo info = typeof(Texture).GetProperty(property, BindingFlags.Static | BindingFlags.Public);
                    Add(entries, "textureStreaming." + property, info == null ? "unavailable" : info.GetValue(null));
                }
                Add(entries, "textureStreaming.enabled", QualitySettings.streamingMipmapsActive);
                Add(entries, "textureStreaming.units", "*Memory=Unity estimated bytes; *Count=count; none is physical VRAM residency");
                Add(entries, "gc.collections.gen0", GC.CollectionCount(0));
                Add(entries, "gc.collections.gen1", GC.CollectionCount(1));
                Add(entries, "gc.collections.gen2", GC.CollectionCount(2));
            }
            return snapshot;
        }

        private static bool Inherits(Type type, string name)
        {
            while (type != null)
            {
                if (type.FullName == name)
                {
                    return true;
                }
                type = type.BaseType;
            }
            return false;
        }

        private object Read(object instance, string name)
        {
            if (instance == null)
            {
                return "unavailable";
            }
            Type type = instance.GetType();
            if (!_members.TryGetValue(type, out var members))
            {
                members = new Dictionary<string, MemberInfo>();
                _members.Add(type, members);
            }
            if (!members.TryGetValue(name, out var member))
            {
                member = (MemberInfo)type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) ?? type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                members.Add(name, member);
            }
            try
            {
                return member is PropertyInfo property ? property.GetValue(instance) : member is FieldInfo field ? field.GetValue(instance) : "unavailable: " + name;
            }
            catch (Exception exception)
            {
                return "unavailable: " + name + ": " + exception.GetType().Name;
            }
        }

        private static BenchmarkDiagnosticSnapshot Snapshot(BenchmarkFrame frame)
        {
            return new BenchmarkDiagnosticSnapshot { unityFrame = frame.unityFrame, position = frame.position, transition = frame.transition, elapsedSeconds = frame.timestamp };
        }

        private static void Add(List<BenchmarkEntry> entries, string name, object value)
        {
            string text = value is UnityEngine.Object asset ? asset == null ? "none" : asset.name : Convert.ToString(value, CultureInfo.InvariantCulture);
            BenchmarkEnvironment.Add(entries, name, text ?? "none");
        }
    }
}

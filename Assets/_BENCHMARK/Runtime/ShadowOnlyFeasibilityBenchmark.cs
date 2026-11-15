using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Levels;

namespace Rvm.Benchmark
{
    public sealed class ShadowOnlyFeasibilityBenchmark : MonoBehaviour
    {
        private const int ITERATIONS = 256;

        private readonly Plane[] _planes = new Plane[6];
        private XRayLevelInstancedRenderer[] _renderers;
        private Light[] _lights;
        private ParticleSystem[] _particles;
        private XRayShadowInfluence _influence;
        private XRayShadowInfluence _baselineInfluence;
        private Bounds[] _candidates;
        private string _output;
        private bool _evaluate;
        private bool _technical;
        private int _candidateCount;
        private int _potentialRejections;
        private int _pose;
        private Vector3 _position;
        private Quaternion _rotation;
        private double _changed;
        private bool _pending;
        private bool _hasPose;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-shadow-only-mode");
            if (Application.isEditor || index < 0)
            {
                return;
            }
            string mode = args[index + 1];
            if (mode != "A" && mode != "B") throw new ArgumentException("Expected shadow-only mode A or B.");
            int outputIndex = Array.IndexOf(args, "-clean-route-output");
            var observer = new GameObject("Benchmark Shadow Feasibility").AddComponent<ShadowOnlyFeasibilityBenchmark>();
            observer._output = BenchmarkOutput.ValidateDirectory(args[outputIndex + 1]);
            observer._technical = Array.IndexOf(args, "-shadow-only-technical") >= 0;
            observer._evaluate = mode == "B";
            observer._renderers = FindObjectsByType<XRayLevelInstancedRenderer>(FindObjectsInactive.Exclude);
            observer._lights = FindObjectsByType<Light>(FindObjectsInactive.Include);
            observer._particles = FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include);
            int capacity = 0;
            foreach (var renderer in observer._renderers)
            {
                if (renderer.BatchSet != null) capacity += renderer.BatchSet.Batches.Count;
            }
            observer._candidates = new Bounds[capacity];
            var ordered = new Light[observer._lights.Length];
            int next = 0;
            foreach (var light in observer._lights)
            {
                if (light.type == LightType.Directional) ordered[next++] = light;
            }
            foreach (var light in observer._lights)
            {
                if (light.type != LightType.Directional) ordered[next++] = light;
            }
            observer._influence = new XRayShadowInfluence(ordered, 0f);
            observer._baselineInfluence = new XRayShadowInfluence(observer._lights, 0f);
            BenchmarkOutput.WriteJson(Path.Combine(observer._output, "shadow-mode.json"), new Mode { mode = mode, candidateCapacity = capacity, startupLightComponents = observer._lights.Length, startupParticleSystems = observer._particles.Length, boundsPayloadBytes = capacity * 24L });
            if (observer._evaluate) RenderPipelineManager.beginCameraRendering += observer.Evaluate;
            if (observer._technical) RenderPipelineManager.endCameraRendering += observer.Record;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= Evaluate;
            RenderPipelineManager.endCameraRendering -= Record;
        }

        private void Evaluate(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType != CameraType.Game) return;
            Gather(camera);
            _influence.Prepare(camera);
            _potentialRejections = Check(_influence);
        }

        private void Gather(Camera camera)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, _planes);
            _candidateCount = 0;
            var wind = XRayFloraWindBounds.ActiveState();
            foreach (var renderer in _renderers)
            {
                if (renderer == null || !renderer.isActiveAndEnabled || renderer.BatchSet == null) continue;
                foreach (var batch in renderer.BatchSet.Batches)
                {
                    if (batch.Mesh == null || batch.Count == 0 || !batch.CastShadows) continue;
                    Bounds bounds = XRayFloraWindBounds.Uses(batch.Materials, wind) ? XRayFloraWindBounds.Expand(batch.Bounds, wind) : batch.Bounds;
                    if (!GeometryUtility.TestPlanesAABB(_planes, bounds) && _candidateCount < _candidates.Length) _candidates[_candidateCount++] = bounds;
                }
            }
        }

        private int Check(XRayShadowInfluence influence)
        {
            int rejected = 0;
            for (int index = 0; index < _candidateCount; index++)
            {
                if (!influence.Intersects(_candidates[index])) rejected++;
            }
            return rejected;
        }

        private void Record(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType != CameraType.Game) return;
            if (!_hasPose || camera.transform.position != _position || camera.transform.rotation != _rotation)
            {
                _hasPose = true;
                _position = camera.transform.position;
                _rotation = camera.transform.rotation;
                _changed = Time.realtimeSinceStartupAsDouble;
                _pending = true;
            }
            if (!_pending || Time.realtimeSinceStartupAsDouble - _changed < 1) return;
            _pending = false;
            Gather(camera);
            _influence.Prepare(camera);
            _potentialRejections = Check(_influence);
            var result = new Pose { position = _position, rotation = _rotation, farClip = camera.farClipPlane, candidates = _candidateCount, potentialRejections = _potentialRejections, retainedByFallback = _candidateCount, allocationCounter = AllocationCounterControl.Measure() };
            foreach (var renderer in _renderers)
            {
                result.visibleBatches += renderer.LastVisibleBatches;
                result.shadowOnlyBatches += renderer.LastShadowOnlyBatches;
                result.actualRejections += renderer.LastRejectedShadowBatches;
            }
            foreach (var light in _lights)
            {
                if (light == null || !light.isActiveAndEnabled || light.shadows == LightShadows.None) continue;
                result.shadowLightComponents++;
                if (light.type == LightType.Directional)
                {
                    result.directionalLights++;
                    result.direction = light.transform.forward;
                }
            }
            foreach (var particle in _particles)
            {
                if (particle == null || !particle.lights.enabled) continue;
                result.enabledParticleLightModules++;
                result.liveParticlesInLightModules += particle.particleCount;
            }
            for (int iteration = 0; iteration < 16; iteration++)
            {
                _influence.Prepare(camera);
                Check(_influence);
            }
            long start = Stopwatch.GetTimestamp();
            for (int iteration = 0; iteration < ITERATIONS; iteration++) _influence.Prepare(camera);
            result.prepareMicroseconds = (Stopwatch.GetTimestamp() - start) * 1000000.0 / Stopwatch.Frequency / ITERATIONS;
            start = Stopwatch.GetTimestamp();
            int total = 0;
            for (int iteration = 0; iteration < ITERATIONS; iteration++) total += Check(_influence);
            result.checkMicroseconds = (Stopwatch.GetTimestamp() - start) * 1000000.0 / Stopwatch.Frequency / ITERATIONS;
            result.checksum = total;
            _baselineInfluence.Prepare(camera);
            if (Check(_baselineInfluence) != _potentialRejections) throw new InvalidOperationException("Source ordering changed the influence union.");
            start = Stopwatch.GetTimestamp();
            for (int iteration = 0; iteration < ITERATIONS; iteration++) total += Check(_baselineInfluence);
            result.originalOrderCheckMicroseconds = (Stopwatch.GetTimestamp() - start) * 1000000.0 / Stopwatch.Frequency / ITERATIONS;
            start = Stopwatch.GetTimestamp();
            for (int iteration = 0; iteration < ITERATIONS; iteration++) Gather(camera);
            result.standaloneGatherMicroseconds = (Stopwatch.GetTimestamp() - start) * 1000000.0 / Stopwatch.Frequency / ITERATIONS;
            BenchmarkOutput.WriteJson(Path.Combine(_output, "shadow-pose" + _pose++ + ".json"), result);
            ScreenCapture.CaptureScreenshot(Path.Combine(_output, "shadow-pose" + (_pose - 1) + ".png"));
        }

        [Serializable]
        private sealed class Mode
        {
            public string mode;
            public int candidateCapacity;
            public int startupLightComponents;
            public int startupParticleSystems;
            public long boundsPayloadBytes;
            public string scope = "Rejected prototype, external benchmark only. A preserves submissions; B additionally evaluates the full-camera-far influence bound with directional components first. Source ordering preserves the influence union and avoids finite-light tests where the directional volume already retains the candidate. Both retain every candidate because source completeness and HDRP shadow/cache coverage are uncertified. No production rejection is enabled. Startup object searches are outside timed route; no scene search per frame.";
        }

        [Serializable]
        private sealed class Pose
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 direction;
            public float farClip;
            public int candidates;
            public int potentialRejections;
            public int retainedByFallback;
            public int visibleBatches;
            public int shadowOnlyBatches;
            public int actualRejections;
            public int shadowLightComponents;
            public int directionalLights;
            public int enabledParticleLightModules;
            public int liveParticlesInLightModules;
            public double prepareMicroseconds;
            public double checkMicroseconds;
            public double originalOrderCheckMicroseconds;
            public double standaloneGatherMicroseconds;
            public int checksum;
            public AllocationCounterControl.Result allocationCounter;
            public string scope = "Technical run only; warmed fixed-frame local costs, not real-frame inclusive timings. Full camera far distance, no receiver-distance truncation. Potential rejection from the experimental bound is not a proof of HDRP shadow safety. Cached shadows, particle/native sources, later creation and incomplete registry require keeping submissions. Gather duplicates renderer work and is reported separately.";
        }
    }
}

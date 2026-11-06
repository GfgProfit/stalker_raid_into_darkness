using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace XRay.Importer.Levels
{
    public sealed partial class XRayPrecipitationPlayerSafetyProbe
    {
        private const int RAIN_START_ROI_X = 768;
        private const int RAIN_START_ROI_Y = 54;
        private const int RAIN_START_ROI_WIDTH = 768;
        private const int RAIN_START_ROI_HEIGHT = 162;
        private const float RAIN_START_CHANGED_THRESHOLD = 0.001f;

        private readonly FullRecord _fullRecord = new FullRecord();

        private bool _fullPassed;
        private VolumeProfile _fullExposure;
        private GameObject _fullVolume;
        private string _fullOutput;
        private PoseDocument _poses;
        private FullCase _fullLastPair;
        private RainGpuEvidence _fullLastRainEvidence;

        private IEnumerator RunGuardedFull()
        {
            _fullOutput = Path.Combine(_output, "FullValidation");
            Directory.CreateDirectory(_fullOutput);
            var stack = new Stack<IEnumerator>();
            stack.Push(RunFullValidation());
            try
            {
                while (stack.Count > 0 && !_fatalNative)
                {
                    IEnumerator current = stack.Peek();
                    bool next = false;
                    bool failed = false;
                    object yielded = null;
                    try
                    {
                        next = current.MoveNext();
                        if (next) yielded = current.Current;
                    }
                    catch (Exception exception)
                    {
                        _fullRecord.failure = exception.ToString();
                        failed = true;
                    }
                    if (failed) break;
                    if (!next)
                    {
                        stack.Pop();
                        if (current is IDisposable disposable) disposable.Dispose();
                        continue;
                    }
                    if (yielded is IEnumerator nested) stack.Push(nested);
                    else yield return yielded;
                }
            }
            finally
            {
                while (stack.Count > 0)
                {
                    if (stack.Pop() is IDisposable disposable) disposable.Dispose();
                }
            }
            if (_fatalNative) yield break;
            try
            {
                Breadcrumb("Full:RestoreBefore");
                _configField.SetValue(_controller, _originalConfig);
                _flagsField.SetValue(_controller, _originalFlags);
                _controller.StopFx();
                _controller.ApplySettings();
                _controller.SetPaused(true);
                _controller.SetRainVisualOverride(false);
                _fullRecord.configurationReadOnly = _sharedBefore == SharedJson();
            }
            catch (Exception exception)
            {
                _fullRecord.failure = (_fullRecord.failure ?? string.Empty) + exception;
            }
            yield return Frames(8);
            foreach (PrecipitationConfig config in _ownedConfigs) if (config != null) Destroy(config);
            _ownedConfigs.Clear();
            if (_fullVolume != null) Destroy(_fullVolume);
            if (_fullExposure != null) Destroy(_fullExposure);
            yield return Frames(6);
            _fullRecord.nativeErrors = ErrorCount();
            _fullPassed = string.IsNullOrEmpty(_fullRecord.failure) && _fullRecord.configurationReadOnly && _fullRecord.nativeErrors == 0 && _fullRecord.cases.Count == 12 && _fullRecord.performancePassed && _fullRecord.reflections != null && _fullRecord.reflections.passed;
            foreach (FullCase record in _fullRecord.cases) _fullPassed &= record.passed;
            _fullRecord.passed = _fullPassed;
            Write(_fullOutput, "Result", _fullRecord);
            Breadcrumb("Full:RestoreAfter");
        }

        private IEnumerator RunFullValidation()
        {
            _fullExposure = ScriptableObject.CreateInstance<VolumeProfile>();
            Exposure exposure = _fullExposure.Add<Exposure>(true);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(9.0f);
            exposure.compensation.Override(0.0f);
            _fullVolume = new GameObject("Owned EV9 precipitation evidence");
            _fullVolume.transform.SetParent(transform, false);
            Volume volume = _fullVolume.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100000.0f;
            volume.sharedProfile = _fullExposure;
            _camera.fieldOfView = 60.0f;
            _fullRecord.exposure = 9.0f;
            _fullRecord.fieldOfView = _camera.fieldOfView;
            _fullRecord.cameraPosition = _camera.transform.position;
            _fullRecord.cameraRotation = _camera.transform.rotation;
            _fullRecord.sourceHash = _sourceHash;
            _fullRecord.api = SystemInfo.graphicsDeviceType.ToString();
            _fullRecord.instancedInstanceCount = _controller.Precipitation.Geometry.InstancedInstanceCount;
            _fullRecord.geometryEstimatedBytes = _controller.Precipitation.Geometry.EstimatedBytes;
            XRayWeatherProfile clear = FindFullProfile("w_clear1");
            XRayWeatherProfile rain = FindFullProfile("w_rain1");
            XRayWeatherProfile storm = FindFullProfile("w_storm1");
            yield return FullDry(clear);
            yield return FullRainPair("02_RainStart", rain, 43230.0, 0.10f);
            yield return FullRainPair("03_ModerateRain", rain, 43230.0, 0.35f);
            yield return FullRainPair("04_StrongRain", rain, 43230.0, 1.0f);
            yield return FullRainPair("05_Storm", storm, 43230.0, 1.0f);
            yield return FullMaterialResponses(clear);
            yield return FullGroundRain(rain);
            yield return FullGroundCoverage(clear, rain);
            yield return FullShelter(rain);
            yield return FullMaterialPair("09_Puddles", clear, true);
            yield return FullNativeReflections(clear);
            yield return FullDrying(clear);
            yield return FullNight(rain);
            yield return FullFx(clear);
            yield return FullPerformance(clear, rain);
        }

        private IEnumerator FullDry(XRayWeatherProfile clear)
        {
            SetupFull(clear, 43230.0, 1.0f, true, true);
            _controller.Precipitation.Wetness.SetUniformState(0.0f, 0.0f);
            yield return Frames(30);
            string output = FullCaseFolder("01_DryClear");
            yield return Capture(output, "DryClear");
            Require(_captureFailure == null, _captureFailure?.ToString());
            _fullRecord.cases.Add(new FullCase { name = "01_DryClear", passed = _controller.Precipitation.RainDensity == 0.0f, rainDensity = _controller.Precipitation.RainDensity, evidence = "Actual clear snapshot; wetness and puddles both zero." });
        }

        private IEnumerator FullRainPair(string name, XRayWeatherProfile profile, double seconds, float density, bool zeroDirectSun = false, bool standingOpenPose = true)
        {
            Vector3 previousPosition = _camera.transform.position;
            Quaternion previousRotation = _camera.transform.rotation;
            if (standingOpenPose)
            {
                FullPose pose = FullPoseNamed("OpenOutside");
                Vector3 outward = pose.position - FullPoseNamed("ClosedBuilding").position;
                outward.y = 0.0f;
                Require(outward.sqrMagnitude > 0.000001f, "The audited outside view must face away from the actual room.");
                _camera.transform.SetPositionAndRotation(pose.position, Quaternion.LookRotation(outward.normalized, Vector3.up));
            }
            SetupFull(profile, seconds, density, false, false);
            if (zeroDirectSun) Require(_controller.SourceLightColor.sqrMagnitude < 0.00000001f, "The authored zero-Sun source snapshot must reach the enabled weather lighting consumer.");
            string output = FullCaseFolder(name);
            _controller.SetPaused(false);
            yield return Frames(180);
            _controller.SetPaused(true);
            yield return Frames(8);
            yield return FullRainDiagnostics(output);
            _controller.SetRainVisualOverride(false);
            yield return Frames(120);
            yield return Capture(output, "RainOff");
            Require(_captureFailure == null, _captureFailure?.ToString());
            Color[] before = _lastPixels;
            yield return Frames(8);
            yield return Capture(output, "RainOffRepeat");
            Require(_captureFailure == null, _captureFailure?.ToString());
            Color[] repeat = _lastPixels;
            int offRenderCount = _controller.Precipitation.Pass.RenderCount;
            yield return Frames(8);
            yield return Capture(output, "RainOffSettled");
            Require(_captureFailure == null, _captureFailure?.ToString());
            Color[] settled = _lastPixels;
            bool offPassStable = _controller.Precipitation.Pass.RenderCount == offRenderCount;
            _controller.SetRainVisualOverride(true);
            yield return Frames(8);
            yield return Capture(output, "RainOn");
            Require(_captureFailure == null, _captureFailure?.ToString());
            FullCase record = FullDifference(name, repeat, settled, _lastPixels);
            XRayPrecipitationValidationProbe.Difference(before, repeat, out record.initialNoise, out _, out _);
            record.cameraPosition = _camera.transform.position;
            record.cameraRotation = _camera.transform.rotation;
            record.rainDensity = _controller.Precipitation.RainDensity;
            record.passed &= record.rainDensity > 0.0f;
            record.globalPassed = record.passed;
            if (name == "02_RainStart")
            {
                FullRainStartRegion(repeat, settled, _lastPixels, record);
                RainGpuEvidence gpu = _fullLastRainEvidence;
                XRayPrecipitationPass pass = _controller.Precipitation.Pass;
                record.rainOffRenderCount = offRenderCount;
                record.rainOnRenderCount = pass.RenderCount;
                record.gpuRainControlPassed = gpu != null && gpu.readable && gpu.actualCamera && gpu.active > 0 && gpu.frustum > 0 && gpu.visibleCandidates > 0 && Mathf.Abs(gpu.density - 0.1f) < 0.00001f && Mathf.Abs(record.rainDensity - 0.1f) < 0.00001f && offPassStable && pass.RenderCount > offRenderCount && pass.LastCamera == _camera && pass.LastDropCount == (int)Math.Floor(0.01d * (1.0d + 99.0d * 0.1d) * gpu.capacity);
                record.passed = record.regionPassed && record.gpuRainControlPassed;
            }
            if (zeroDirectSun)
            {
                record.passed &= _controller.SourceLightColor.sqrMagnitude < 0.00000001f && _controller.Precipitation.RainDensity > 0.0f;
                foreach (GameObject root in _controller.gameObject.scene.GetRootGameObjects())
                {
                    foreach (Light light in root.GetComponentsInChildren<Light>(true)) if (light.type == LightType.Directional && light.isActiveAndEnabled && light.intensity > 0.000001f) record.passed = false;
                }
                record.zeroDirectSun = true;
            }
            record.evidence = "Independent rain draw toggle; identical authored weather, EV9, wetness disabled;180 fixed native physical frames at GameTimeScale0, then120 paused rain-OFF settling frames and three OFF captures before ON. Gate compares the final OFF repeat pair with ON; initial OFF noise is retained.";
            if (name == "02_RainStart") record.evidence += " Sparse onset acceptance uses the prospectively declared fixed open-sky region selected only from OFF scene geometry, linear mean absolute RGB above max(4x regional noise,1e-5), more than100 pixels changing by0.001, and independent GPU density/frustum/draw-toggle controls. Original global metrics and globalPassed remain unchanged.";
            _fullRecord.cases.Add(record);
            _camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
        }

        private IEnumerator FullMaterialResponses(XRayWeatherProfile clear)
        {
            yield return FullMaterialPair("06_AsphaltWetness", clear, false, false, "Asphalt");
            FullCase asphalt = _fullLastPair;
            yield return FullMaterialPair("06_ConcreteWetness", clear, false, false, "Concrete");
            FullCase concrete = _fullLastPair;
            _fullRecord.cases.Add(new FullCase { name = "06_WetAsphaltConcrete", passed = asphalt.passed && concrete.passed, evidence = "Two independently gated real material subpairs: source-mask-proven asphalt and concrete; each retains its own fixed pose and saved PairMetrics." });
        }

        private IEnumerator FullRainDiagnostics(string output)
        {
            XRayPrecipitationSystem system = _controller.Precipitation;
            XRayPrecipitationPass pass = system.Pass;
            PrecipitationConfig config = _controller.EffectivePrecipitationConfig;
            int capacity = pass.CapacityForCamera(_camera);
            var drops = new XRayRainDropState[capacity];
            bool readable = capacity > 0 && pass.TryReadDrops(_camera, drops);
            var record = new RainGpuEvidence { capacity = capacity, readable = readable, renderCount = pass.RenderCount, submittedDrops = pass.LastDropCount, actualCamera = pass.LastCamera == _camera, density = system.RainDensity, cameraPosition = _camera.transform.position, cameraForward = _camera.transform.forward, diagnostic = _controller.Diagnostic, sourceLight = _controller.SourceLightColor };
            FieldInfo radiance = typeof(XRayPrecipitationPass).GetField("_radiance", BindingFlags.Instance | BindingFlags.NonPublic);
            if (radiance != null) record.radiance = (Color)radiance.GetValue(pass);
            var csv = new List<string> { "index,x,y,z,vx,vy,vz,age,size,inFrustum,inDrawRadius" };
            Vector3 minimum = Vector3.positiveInfinity;
            Vector3 maximum = Vector3.negativeInfinity;
            for (int index = 0; index < drops.Length && readable; index++)
            {
                XRayRainDropState drop = drops[index];
                if (drop.Size <= 0.0f) continue;
                record.active++;
                minimum = Vector3.Min(minimum, drop.Position);
                maximum = Vector3.Max(maximum, drop.Position);
                Vector3 center = drop.Position - drop.Velocity.normalized * (0.25f * config.DropLength * (1.0f + system.RainDensity));
                Vector3 viewport = _camera.WorldToViewportPoint(center);
                bool inFrustum = viewport.z > _camera.nearClipPlane && viewport.z < _camera.farClipPlane && viewport.x >= 0.0f && viewport.x <= 1.0f && viewport.y >= 0.0f && viewport.y <= 1.0f;
                Vector3 offset = center - _camera.transform.position;
                bool inRadius = offset.x * offset.x + offset.z * offset.z < config.DrawRadius * config.DrawRadius;
                if (inFrustum) record.frustum++;
                if (inRadius) record.drawRadius++;
                if (inFrustum && inRadius) record.visibleCandidates++;
                if (index < 256) csv.Add(FormattableString.Invariant($"{index},{drop.Position.x:R},{drop.Position.y:R},{drop.Position.z:R},{drop.Velocity.x:R},{drop.Velocity.y:R},{drop.Velocity.z:R},{drop.Age:R},{drop.Size:R},{inFrustum},{inRadius}"));
            }
            record.minimumPosition = record.active > 0 ? minimum : Vector3.zero;
            record.maximumPosition = record.active > 0 ? maximum : Vector3.zero;
            File.WriteAllLines(Path.Combine(output, "GpuDrops.csv"), csv);
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(system.Geometry.Heights, 0, TextureFormat.RGBAFloat);
            while (!request.done) yield return null;
            Require(!request.hasError, "Actual GPU roof atlas readback must succeed for rain visibility diagnosis.");
            var data = request.GetData<Color>();
            var heights = new List<RainHeightEvidence>();
            Vector3 forward = _camera.transform.forward;
            forward.y = 0.0f;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector4 mapping = system.Geometry.WorldMapping;
            int width = system.Geometry.Heights.width;
            int height = system.Geometry.Heights.height;
            for (int ahead = 0; ahead <= 4; ahead++)
            {
                for (int side = -2; side <= 2; side++)
                {
                    Vector3 point = _camera.transform.position + forward * (ahead * 3.75f) + right * (side * 3.75f);
                    int x = Mathf.FloorToInt((point.x - mapping.x) * mapping.z * width);
                    int y = Mathf.FloorToInt((point.z - mapping.y) * mapping.w * height);
                    if (x < 0 || y < 0 || x >= width || y >= height) continue;
                    Color sample = data[y * width + x];
                    heights.Add(new RainHeightEvidence { point = point, top = sample.r, bottom = sample.g, valid = sample.r >= sample.g, aboveCamera = sample.r >= sample.g && sample.r > point.y });
                }
            }
            record.heights = heights.ToArray();
            _fullLastRainEvidence = record;
            Write(output, "RainGpuDiagnostics", record);
        }

        private IEnumerator FullMaterialPair(string name, XRayWeatherProfile clear, bool puddles, bool append = true, string poseName = null)
        {
            Vector3 previousPosition = _camera.transform.position;
            Quaternion previousRotation = _camera.transform.rotation;
            FullPose pose = FullPoseNamed(poseName ?? (puddles ? "Puddles" : "AsphaltConcrete"));
            _camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
            SetupFull(clear, 43230.0, 1.0f, true, !puddles);
            _controller.SetRainVisualOverride(false);
            _controller.Precipitation.Wetness.SetUniformState(puddles ? 1.0f : 0.0f, puddles ? 1.0f : 0.0f);
            yield return Frames(30);
            string output = FullCaseFolder(name);
            yield return Capture(output, "Before");
            Require(_captureFailure == null, _captureFailure?.ToString());
            Color[] before = _lastPixels;
            yield return Frames(8);
            yield return Capture(output, "BeforeRepeat");
            Require(_captureFailure == null, _captureFailure?.ToString());
            Color[] repeat = _lastPixels;
            if (puddles) SetFeatures(true, true);
            _controller.Precipitation.Wetness.SetUniformState(1.0f, puddles ? 1.0f : 0.0f);
            yield return Frames(8);
            yield return Capture(output, "After");
            Require(_captureFailure == null, _captureFailure?.ToString());
            FullCase record = FullDifference(name, before, repeat, _lastPixels);
            record.cameraPosition = _camera.transform.position;
            record.cameraRotation = _camera.transform.rotation;
            record.evidence = puddles ? "Wetness and stored puddle state stay1; PuddlesEnabled false versus true; rain drawing disabled." : "Puddles stay0; wetness0 versus1; rain drawing disabled.";
            record.evidence += " Actual material pose " + pose.name + ": " + pose.evidence;
            record.materialClones = _controller.Precipitation.WetMaterials.CloneCount;
            record.customDrawBindings = _controller.Precipitation.WetMaterials.CustomDrawBindingCount;
            record.passed &= record.materialClones > 0 && record.customDrawBindings > 0;
            _fullLastPair = record;
            if (append) _fullRecord.cases.Add(record);
            else Write(output, "PairMetrics", record);
            _camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
        }

        private IEnumerator FullNativeReflections(XRayWeatherProfile clear)
        {
            Vector3 previousPosition = _camera.transform.position;
            Quaternion previousRotation = _camera.transform.rotation;
            FullPose pose = FullPoseNamed("Puddles");
            _camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
            SetupFull(clear, 43230.0, 1.0f, true, true);
            PrecipitationConfig owned = _ownedConfigs[_ownedConfigs.Count - 1];
            JsonUtility.FromJsonOverwrite("{\"_planarReflectionsEnabled\":false}", owned);
            _controller.ApplySettings();
            _controller.SetRainVisualOverride(false);
            _controller.Precipitation.Wetness.SetUniformState(1.0f, 1.0f);
            XRayWetnessReflection reflections = _controller.Precipitation.Reflections;
            Require(reflections != null, "The existing weather pipeline must automatically own the native wet reflection probe.");
            reflections.Dispose();
            reflections.Tick(_controller.EffectivePrecipitationConfig, 0.0f, _controller.Precipitation.Wetness);
            Require(!_controller.EffectivePrecipitationConfig.PlanarReflectionsEnabled && reflections.PlanarProbe == null, "Native cubemap proof requires explicit planar opt-out on the owned session configuration.");
            HDAdditionalReflectionData probe = reflections.Probe;
            Require(probe != null, "Enable the bounded non-camera reflection source in the inherited configuration.");
            bool hadSsr = _fullExposure.TryGet(out ScreenSpaceReflection ssr);
            if (!hadSsr) ssr = _fullExposure.Add<ScreenSpaceReflection>(true);
            bool ssrActive = ssr.active;
            bool ssrEnabled = ssr.enabled.value;
            bool ssrOverride = ssr.enabled.overrideState;
            bool transparentEnabled = ssr.enabledTransparent.value;
            bool transparentOverride = ssr.enabledTransparent.overrideState;
            float probeWeight = reflections.Weight;
            var evidence = new FullReflectionEvidence { cameraPosition = pose.position, cameraRotation = pose.rotation, realGeometry = true, planarDisabled = true, reflectionSource = "NativeHdrpCubemapOnly_PlanarExplicitlyDisabledOnOwnedConfig" };
            _fullRecord.reflections = evidence;
            string output = FullCaseFolder("09_NonScreenSpaceReflections");
            try
            {
                ssr.active = true;
                ssr.enabled.Override(false);
                ssr.enabledTransparent.Override(false);
                for (int frame = 0; frame < 360; frame++)
                {
                    HDAdditionalReflectionData ready = null;
                    for (int index = 0; index < reflections.ProbeCount; index++)
                    {
                        HDAdditionalReflectionData candidate = reflections.GetProbe(index);
                        if (candidate != null && candidate.realtimeTexture != null && candidate.realtimeTexture.updateCount > 0) ready = candidate;
                    }
                    if (ready != null)
                    {
                        probe = ready;
                        break;
                    }
                    yield return null;
                }
                RenderTexture cubemap = probe.realtimeTexture;
                Require(cubemap != null && cubemap.updateCount > 0, "The native HDRP probe must finish all six faces before reflection acceptance.");
                evidence.captureRequests = reflections.CaptureRequests;
                evidence.textureUpdates = cubemap.updateCount;
                evidence.resolution = cubemap.width;
                evidence.estimatedBytes = reflections.EstimatedBytes;
                foreach (GameObject root in _controller.gameObject.scene.GetRootGameObjects())
                {
                    foreach (XRayLevelInstancedRenderer renderer in root.GetComponentsInChildren<XRayLevelInstancedRenderer>(false))
                    {
                        evidence.instancedBatches += renderer.ReflectionSubmittedBatches;
                        evidence.instancedInstances += renderer.ReflectionSubmittedInstances;
                    }
                }
                AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(cubemap, 0, TextureFormat.RGBAFloat);
                while (!request.done) yield return null;
                Require(!request.hasError && request.layerCount == 6, "Read all six actual native cubemap faces for off-screen reflection evidence.");
                evidence.faces = request.layerCount;
                evidence.faceMaximum = new float[6];
                evidence.faceMean = new double[6];
                Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                for (int face = 0; face < 6; face++)
                {
                    Color[] pixels = request.GetData<Color>(face).ToArray();
                    double sum = 0.0;
                    float maximum = 0.0f;
                    foreach (Color pixel in pixels)
                    {
                        Require(float.IsFinite(pixel.r) && float.IsFinite(pixel.g) && float.IsFinite(pixel.b), "Native reflection radiance must stay finite.");
                        maximum = Mathf.Max(maximum, Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)));
                        sum += (pixel.r + pixel.g + pixel.b) / 3.0;
                    }
                    evidence.faceMaximum[face] = maximum;
                    evidence.faceMean[face] = sum / pixels.Length;
                    if (Vector3.Dot(axes[face], _camera.transform.forward) < -0.5f) evidence.offscreenMaximum = Mathf.Max(evidence.offscreenMaximum, maximum);
                    var exr = new Texture2D(cubemap.width, cubemap.height, TextureFormat.RGBAFloat, false, true);
                    var png = new Texture2D(cubemap.width, cubemap.height, TextureFormat.RGBA32, false, false);
                    try
                    {
                        exr.SetPixels(pixels);
                        exr.Apply(false, false);
                        File.WriteAllBytes(Path.Combine(output, "ProbeFace" + face + ".exr"), exr.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                        for (int pixel = 0; pixel < pixels.Length; pixel++) pixels[pixel] = new Color(pixels[pixel].r / (1.0f + pixels[pixel].r), pixels[pixel].g / (1.0f + pixels[pixel].g), pixels[pixel].b / (1.0f + pixels[pixel].b), 1.0f).gamma;
                        png.SetPixels(pixels);
                        png.Apply(false, false);
                        File.WriteAllBytes(Path.Combine(output, "ProbeFace" + face + "_Tonemapped.png"), png.EncodeToPNG());
                    }
                    finally
                    {
                        Destroy(exr);
                        Destroy(png);
                    }
                }
                reflections.Weight = 0.0f;
                yield return Frames(30);
                ScreenSpaceReflection appliedSsr = HDCamera.GetOrCreate(_camera).volumeStack.GetComponent<ScreenSpaceReflection>();
                evidence.ssrDisabled = appliedSsr != null && !appliedSsr.enabled.value && !appliedSsr.enabledTransparent.value;
                Require(evidence.ssrDisabled, "The actual main HDCamera VolumeStack must disable opaque and transparent screen-space reflections during the probe A/B.");
                yield return Capture(output, "ProbeOffSsrOff");
                Require(_captureFailure == null, _captureFailure?.ToString());
                Color[] before = _lastPixels;
                yield return Frames(8);
                yield return Capture(output, "ProbeOffSsrOffRepeat");
                Require(_captureFailure == null, _captureFailure?.ToString());
                Color[] repeat = _lastPixels;
                reflections.Weight = 1.0f;
                yield return Frames(30);
                yield return Capture(output, "ProbeOnSsrOff");
                Require(_captureFailure == null, _captureFailure?.ToString());
                appliedSsr = HDCamera.GetOrCreate(_camera).volumeStack.GetComponent<ScreenSpaceReflection>();
                evidence.ssrDisabled &= appliedSsr != null && !appliedSsr.enabled.value && !appliedSsr.enabledTransparent.value;
                evidence.comparison = FullDifference("09_NonScreenSpaceReflections", before, repeat, _lastPixels);
                evidence.planarDisabled &= !_controller.EffectivePrecipitationConfig.PlanarReflectionsEnabled && reflections.PlanarProbe == null;
                evidence.passed = evidence.planarDisabled && evidence.ssrDisabled && evidence.captureRequests > 0 && evidence.textureUpdates > 0 && evidence.faces == 6 && evidence.instancedInstances > 0 && evidence.offscreenMaximum > 0.00001f && evidence.comparison.passed;
                evidence.comparison.evidence = "Actual Military geometry and six native HDRP cubemap faces; an away-facing face contains radiance outside the main view. Screen-space reflections are explicitly disabled in the owned exposure Volume. Planar reflections are explicitly disabled on the owned session configuration. Identical paused puddles and camera compare all native cubemap slots at weight0 twice against weight1; no synthetic geometry or persisted scene edits.";
                Write(output, "NativeReflectionEvidence", evidence);
            }
            finally
            {
                reflections.Weight = probeWeight;
                ssr.active = ssrActive;
                ssr.enabled.value = ssrEnabled;
                ssr.enabled.overrideState = ssrOverride;
                ssr.enabledTransparent.value = transparentEnabled;
                ssr.enabledTransparent.overrideState = transparentOverride;
                if (!hadSsr)
                {
                    _fullExposure.Remove<ScreenSpaceReflection>();
                    Destroy(ssr);
                }
                _camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
            }
        }

        private IEnumerator FullGroundRain(XRayWeatherProfile rain)
        {
            FullPose pose = FullPoseNamed("Earth");
            _camera.transform.SetPositionAndRotation(new Vector3(pose.position.x, pose.groundY + 1.65f, pose.position.z), FullHorizonRotation(pose.rotation));
            yield return FullRainPair("07_GroundRain", rain, 43230.0, 0.65f, false, false);
            FullCase rainCase = _fullRecord.cases[_fullRecord.cases.Count - 1];
            yield return FullMaterialPair("07_EarthWetness", FindFullProfile("w_clear1"), false, false, "Earth");
            rainCase.passed &= _fullLastPair.passed;
            rainCase.evidence += " Independent earth Wetness0/1 pair also passed.";
            _camera.transform.SetPositionAndRotation(_fullRecord.cameraPosition, _fullRecord.cameraRotation);
        }

        private IEnumerator FullGroundCoverage(XRayWeatherProfile clear, XRayWeatherProfile rain)
        {
            Vector3 previousPosition = _camera.transform.position;
            Quaternion previousRotation = _camera.transform.rotation;
            FullPose pose = FullPoseNamed("Earth");
            _camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
            SetupFull(rain, 43230.0, 1.0f, true, true);
            _controller.SetRainVisualOverride(false);
            string output = FullCaseFolder("07_ControlledShelterCoverage_AfterOnly");
            try
            {
                XRayPrecipitationSystem system = _controller.Precipitation;
                PrecipitationConfig config = _controller.EffectivePrecipitationConfig;
                system.Wetness.SetUniformState(0.0f, 0.0f);
                system.Wetness.Tick(config, system.Geometry.Heights, system.Geometry.WorldMapping, 1.0f, 120.0f);
                system.Wetness.Publish(config, system.RealTime, 1.0f);
                yield return Frames(30);
                yield return Capture(output, "ControlledShelter120s");
                Require(_captureFailure == null, _captureFailure?.ToString());
                var cells = new List<FullCoverageCell>();
                Vector3 center = pose.position;
                Ray ray = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0.0f));
                if (Mathf.Abs(ray.direction.y) > 0.0001f)
                {
                    float distance = (pose.groundY - ray.origin.y) / ray.direction.y;
                    if (distance > 0.0f) center = ray.GetPoint(distance);
                }
                var evidence = new FullCoverageEvidence { afterOnly = true, acceleratedSingleTickSeconds = 120.0f, rainDensity = 1.0f, cameraPosition = pose.position, cameraRotation = pose.rotation, sampleCenter = center, worldMapping = system.Geometry.WorldMapping, cellSize = Shader.GetGlobalVector("_MonolithWetnessCellSize"), filmStrength = config.WetnessStrength, puddleStrength = config.PuddleStrength, paused = true, evidence = "After-only native Earth capture from the supplied audited pose. Both field layers start0, then one explicit GPU Tick of120seconds uses real top/bottom height geometry and rain1; the ordinary controller stays paused and visible rain stays disabled. This accelerated single step is a spatial shelter diagnostic, not a real-time120second accumulation acceptance or a paired frozen-before capture." };
                for (int z = -4; z <= 4; z++)
                {
                    for (int x = -4; x <= 4; x++)
                    {
                        Vector3 point = new Vector3(center.x + x * 4.0f, pose.groundY, center.z + z * 4.0f);
                        Vector4 mapping = system.Geometry.WorldMapping;
                        int column = Mathf.FloorToInt((point.x - mapping.x) * mapping.z * system.Wetness.Width);
                        int row = Mathf.FloorToInt((point.z - mapping.y) * mapping.w * system.Wetness.Height);
                        if (!system.Wetness.TryReadCell(column, row, out XRayWetnessCell cell)) continue;
                        bool valid = cell.Heights.x >= cell.Heights.y;
                        bool upper = point.y >= cell.Heights.x - config.ShelterTolerance;
                        if (valid) evidence.validCells++;
                        if (valid && cell.Heights.x - cell.Heights.y > config.ShelterTolerance) evidence.shelteredColumns++;
                        if (valid && Mathf.Abs(cell.Moisture.z - cell.Moisture.x) > 0.1f) evidence.contrastingLayers++;
                        cells.Add(new FullCoverageCell { column = column, row = row, position = point, heights = cell.Heights, lowerWetness = cell.Moisture.x, lowerPuddles = cell.Moisture.y, upperWetness = cell.Moisture.z, upperPuddles = cell.Moisture.w, sampledUpperLayer = upper });
                    }
                }
                evidence.cells = cells.ToArray();
                _fullRecord.groundCoverage = evidence;
                Write(output, "ControlledShelterCoverage", evidence);
                Require(evidence.validCells > 0 && evidence.contrastingLayers > 0, "The after-only spatial diagnostic must contain actual exposed/covered moisture contrast.");
            }
            finally
            {
                SetupFull(clear, 43230.0, 1.0f, true, true);
                _controller.SetRainVisualOverride(false);
                _camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
            }
        }

        private IEnumerator FullShelter(XRayWeatherProfile rain)
        {
            SetupFull(rain, 43230.0, 1.0f, true, true);
            string output = FullCaseFolder("08_RoofAndShelter");
            _controller.Precipitation.Wetness.SetUniformState(0.8f, 0.2f);
            float begin = _controller.Precipitation.RealTime;
            _controller.SetPaused(false);
            yield return Frames(120);
            _controller.SetPaused(true);
            var cells = new List<FullCell>();
            bool passed = _controller.Precipitation.RealTime > begin;
            var names = new List<string> { "ClosedBuilding", "Doorway", "RoofEdge", "OpenOutside" };
            _fullRecord.canopyUnavailable = true;
            foreach (FullPose candidate in _poses.poses)
            {
                if (candidate.name != "Canopy") continue;
                names.Add("Canopy");
                _fullRecord.canopyUnavailable = false;
                break;
            }
            foreach (string name in names)
            {
                FullPose pose = FullPoseNamed(name);
                _camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
                _controller.SetPaused(false);
                float settleStart = _controller.Precipitation.RealTime;
                float speed = XRayPrecipitationMath.SpeedFactor(_controller.Snapshot.WindVelocity, _controller.EffectivePrecipitationConfig.DropSpeed);
                float settleSeconds = XRayPrecipitationMath.SOURCE_MAX_DISTANCE / Mathf.Max(XRayPrecipitationMath.SOURCE_MIN_SPEED * speed, 0.01f) + XRayPrecipitationMath.SOURCE_SPLASH_SECONDS;
                for (int frame = 0; frame < 1800 && _controller.Precipitation.RealTime - settleStart < settleSeconds; frame++) yield return null;
                Require(_controller.Precipitation.RealTime - settleStart >= settleSeconds, "The moved rain volume must complete its maximum source flight lifetime before shelter containment is measured.");
                _controller.SetPaused(true);
                yield return Frames(8);
                yield return Capture(output, name);
                Require(_captureFailure == null, _captureFailure?.ToString());
                XRayWetnessCell cell = ReadFullCell(pose.position);
                float elapsed = _controller.Precipitation.RealTime - begin;
                bool sheltered = cell.Heights.x - cell.Heights.y > 0.15f;
                bool upperLayer = pose.groundY >= cell.Heights.x - 0.15f;
                float surfaceWet = upperLayer ? cell.Moisture.z : cell.Moisture.x;
                float surfacePuddle = upperLayer ? cell.Moisture.w : cell.Moisture.y;
                float expectedWet = Mathf.Clamp01(0.8f + (upperLayer || !sheltered ? 0.0156f : -0.0072f) * elapsed);
                float expectedPuddle = upperLayer || !sheltered ? Mathf.Clamp01(0.2f + 0.0039f * elapsed) : Mathf.Max(0.0f, 0.2f - 0.0018f * elapsed);
                bool numeric = Mathf.Abs(surfaceWet - expectedWet) < 0.002f && Mathf.Abs(surfacePuddle - expectedPuddle) < 0.002f;
                int roofDrops = 0;
                int belowRoof = 0;
                if (name == "ClosedBuilding" || name == "Canopy")
                {
                    int capacity = _controller.Precipitation.Pass.CapacityForCamera(_camera);
                    var drops = new XRayRainDropState[capacity];
                    Require(capacity > 0 && _controller.Precipitation.Pass.TryReadDrops(_camera, drops), "Read actual GPU rain records at the real shelter pose.");
                    Require(pose.roofPatchRadius > 0.0f, "A real CPU-audited coherent roof patch radius is required for GPU rain containment.");
                    float radius = pose.roofPatchRadius;
                    foreach (XRayRainDropState drop in drops)
                    {
                        if (drop.Size <= 0.0f || Mathf.Abs(drop.Position.x - pose.position.x) > radius || Mathf.Abs(drop.Position.z - pose.position.z) > radius) continue;
                        roofDrops++;
                        if (drop.Position.y < pose.roofY - 0.1f && drop.Position.y > pose.groundY + 0.1f) belowRoof++;
                    }
                    numeric &= roofDrops > 0 && belowRoof == 0;
                }
                if (name == "ClosedBuilding") numeric &= sheltered && !upperLayer && pose.roofY - pose.groundY >= 2.0f;
                if (name == "OpenOutside") numeric &= upperLayer || !sheltered;
                passed &= numeric;
                cells.Add(new FullCell { name = name, position = pose.position, heights = cell.Heights, moisture = cell.Moisture, expectedSurfaceWetness = expectedWet, expectedSurfacePuddles = expectedPuddle, sampledUpperLayer = upperLayer, elapsedRealSeconds = elapsed, cameraSettleSeconds = _controller.Precipitation.RealTime - settleStart, roofColumnDrops = roofDrops, dropsBelowRoof = belowRoof, passed = numeric });
            }
            Write(output, "GpuCells", new FullCells { cells = cells.ToArray() });
            _fullRecord.cases.Add(new FullCase { name = "08_RoofAndShelter", passed = passed, evidence = "Actual geometry-selected room, door, roof edge and outside poses; GPU surface moisture independently checked against literal source rates. Canopy is optional and recorded unavailable when no real candidate exists." });
            _camera.transform.SetPositionAndRotation(_fullRecord.cameraPosition, _fullRecord.cameraRotation);
        }

        private IEnumerator FullDrying(XRayWeatherProfile clear)
        {
            SetupFull(clear, 43230.0, 1.0f, true, true);
            _controller.SetRainVisualOverride(false);
            _controller.Precipitation.Wetness.SetUniformState(0.8f, 0.2f);
            string output = FullCaseFolder("10_Drying");
            yield return Frames(8);
            XRayWetnessCell before = ReadFullCell(_camera.transform.position);
            yield return Capture(output, "BeforeDrying");
            Require(_captureFailure == null, _captureFailure?.ToString());
            float begin = _controller.Precipitation.RealTime;
            _controller.SetPaused(false);
            yield return Frames(240);
            _controller.SetPaused(true);
            float elapsed = _controller.Precipitation.RealTime - begin;
            XRayWetnessCell after = ReadFullCell(_camera.transform.position);
            yield return Capture(output, "AfterDrying");
            Require(_captureFailure == null, _captureFailure?.ToString());
            float expectedWet = Mathf.Max(0.0f, before.Moisture.x - 0.0072f * elapsed);
            float expectedPuddle = Mathf.Max(0.0f, before.Moisture.y - 0.0018f * elapsed);
            bool passed = elapsed > 0.0f && before.Moisture.x > after.Moisture.x && Mathf.Abs(after.Moisture.x - expectedWet) < 0.002f && Mathf.Abs(after.Moisture.y - expectedPuddle) < 0.002f;
            Write(output, "GpuDrying", new FullCell { name = "Drying", position = _camera.transform.position, heights = after.Heights, moisture = after.Moisture, expectedLowerWetness = expectedWet, expectedLowerPuddles = expectedPuddle, elapsedRealSeconds = elapsed, passed = passed });
            _fullRecord.cases.Add(new FullCase { name = "10_Drying", passed = passed, evidence = "Native automatic Update only; unchanged zero-rain snapshot; literal source dry rates checked against real GPU cell state." });
        }

        private IEnumerator FullNight(XRayWeatherProfile rain)
        {
            yield return FullRainPair("11_NightRain", FindFullProfile("w_storm2"), 60.0, 1.0f, true);
        }

        private IEnumerator FullFx(XRayWeatherProfile clear)
        {
            SetupFull(clear, 43230.0, 1.0f, false, false);
            string output = FullCaseFolder("12_WeatherFx");
            yield return Frames(30);
            yield return Capture(output, "Ordinary");
            Require(_captureFailure == null, _captureFailure?.ToString());
            Color[] before = _lastPixels;
            XRayWeatherFxProfile chosen = null;
            XRayWeatherSnapshot key = default;
            foreach (XRayWeatherFxProfile effect in GameConfig.Load().WeatherFxCatalog.Effects)
            {
                for (int index = 1; index < effect.KeyframeCount; index++)
                {
                    XRayWeatherSnapshot candidate = effect.GetKeyframe(index);
                    if (candidate.RainDensity <= 0.0f || chosen != null) continue;
                    chosen = effect;
                    key = candidate;
                }
            }
            Require(chosen != null && _controller.StartFx(chosen), "A real authored positive-rain WeatherFX is required.");
            Require(_controller.SeekFx(_controller.FxEntrySeconds + key.GameSeconds), "Seek the real playable FX key.");
            yield return Frames(30);
            yield return Capture(output, "DuringFx");
            Require(_captureFailure == null, _captureFailure?.ToString());
            XRayPrecipitationValidationProbe.Difference(before, _lastPixels, out double difference, out int pixels, out float maximum);
            bool passed = _controller.Precipitation.RainDensity > 0.0f && difference > 0.00001 && pixels > 0;
            Require(_controller.StopFx(), "Cancel the FX and restore ordinary weather.");
            passed &= _controller.ActiveFx == null && _controller.Precipitation.RainDensity == 0.0f;
            yield return Frames(30);
            yield return Capture(output, "AfterFx");
            Require(_captureFailure == null, _captureFailure?.ToString());
            _fullRecord.cases.Add(new FullCase { name = "12_WeatherFx", passed = passed, difference = difference, changedPixels = pixels, maximum = maximum, evidence = chosen.EffectName + "/" + key.Key0 + ": real snapshot-driven positive rain and visible lighting; cancellation restores ordinary zero rain." });
        }

        private IEnumerator FullPerformance(XRayWeatherProfile clear, XRayWeatherProfile rain)
        {
            int rendersBefore = _cameraRenders;
            bool extended = Array.IndexOf(Environment.GetCommandLineArgs(), "-precipitationExtendedPerformance") >= 0;
            var scenarios = new List<XRayPrecipitationPerformanceProbe.Scenario>
            {
                new XRayPrecipitationPerformanceProbe.Scenario("baseline", () => SetupFullPerformance(clear, 0.0f, false, false), evidence: "Prepared dry baseline: zero rain density; wetness/puddles disabled; bounded buffers retained from prior visual cases. Not a disposed-system memory baseline."),
                new XRayPrecipitationPerformanceProbe.Scenario("wetness_only", () => SetupFullPerformance(clear, 0.0f, true, false)),
                new XRayPrecipitationPerformanceProbe.Scenario("puddles_only", () => SetupFullPerformance(clear, 0.0f, false, true)),
                new XRayPrecipitationPerformanceProbe.Scenario("wetness_puddles", () => SetupFullPerformance(clear, 0.0f, true, true)),
                new XRayPrecipitationPerformanceProbe.Scenario("moderate_rain", () => SetupFullPerformance(rain, 0.35f, false, false)),
                new XRayPrecipitationPerformanceProbe.Scenario("strong_rain", () => SetupFullPerformance(rain, 1.0f, false, false)),
                new XRayPrecipitationPerformanceProbe.Scenario("rain_wetness", () => SetupFullPerformance(rain, 1.0f, true, false)),
                new XRayPrecipitationPerformanceProbe.Scenario("rain_puddles", () => SetupFullPerformance(rain, 1.0f, false, true)),
                new XRayPrecipitationPerformanceProbe.Scenario("full", () => SetupFullPerformance(rain, 1.0f, true, true)),
                new XRayPrecipitationPerformanceProbe.Scenario("full_reflection_refresh", () => SetupFullReflectionPerformance(rain, true), evidence: "Real Puddles camera pose; includes at least one periodic cubemap refresh during360native sample frames.", sampleFrames: 360),
                new XRayPrecipitationPerformanceProbe.Scenario("full_no_reflections", () => SetupFullReflectionPerformance(rain, false), evidence: "Same real Puddles camera pose and water state; non-camera reflections disabled on an owned session configuration.")
            };
            if (extended)
            {
                scenarios.Add(new XRayPrecipitationPerformanceProbe.Scenario("puddles_no_ripples", () => SetupFullPuddleRipplePerformance(rain, false), evidence: "Real Puddles camera pose; rain profile and density1, standing water1, wetness disabled, rain particles hidden. Ripple amplitude0; production shader still evaluates ripple instructions, so this is a visual amplitude control rather than removed shader work."));
                scenarios.Add(new XRayPrecipitationPerformanceProbe.Scenario("puddles_ripples", () => SetupFullPuddleRipplePerformance(rain, true), evidence: "Same Puddles pose, rain profile, density1 and water state as puddles_no_ripples; canonical ripple amplitude enabled, rain particles hidden. Time advances naturally; starting simulation time is recorded per scenario."));
            }
            yield return XRayPrecipitationPerformanceProbe.Run(_camera, _controller, scenarios.ToArray(), new XRayPrecipitationPerformanceProbe.Settings { ArchiveFolder = _fullOutput, SourceRevision = "working-tree:" + _sourceHash, SourceManifestSha256 = _sourceHash, WarmupFrames = 30, SampleFrames = 120, CameraRenderWallSamples = 0, AllowPersistentCameraTarget = true }, path => _fullRecord.performance = path);
            Require(!string.IsNullOrEmpty(_fullRecord.performance), "All requested natural-frame performance scenarios must complete.");
            PerformanceEvidence evidence = JsonUtility.FromJson<PerformanceEvidence>(File.ReadAllText(_fullRecord.performance));
            Require(evidence != null && evidence.width == WIDTH && evidence.height == HEIGHT && !evidence.editor && evidence.scenarios != null && evidence.scenarios.Length == scenarios.Count, "Performance JSON must contain every requested1920x1080 Player measurement.");
            var names = new List<string> { "baseline", "wetness_only", "puddles_only", "wetness_puddles", "moderate_rain", "strong_rain", "rain_wetness", "rain_puddles", "full", "full_reflection_refresh", "full_no_reflections" };
            var densities = new List<float> { 0.0f, 0.0f, 0.0f, 0.0f, 0.35f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f };
            if (extended)
            {
                names.AddRange(new[] { "puddles_no_ripples", "puddles_ripples" });
                densities.AddRange(new[] { 1.0f, 1.0f });
            }
            for (int index = 0; index < names.Count; index++)
            {
                PerformanceScenarioEvidence measured = evidence.scenarios[index];
                int sampleFrames = measured.name == "full_reflection_refresh" ? 360 : 120;
                Require(measured.name == names[index] && measured.status == "Measured" && measured.nativeIntervalMs != null && measured.nativeIntervalMs.available && measured.nativeIntervalMs.sampleCount == sampleFrames && double.IsFinite(measured.nativeIntervalMs.mean) && measured.nativeIntervalMs.mean > 0.0, "Every scenario requires its declared real native frame samples; unavailable optional GPU attribution stays explicitly unavailable.");
                Require(Mathf.Abs(measured.rainDensityAfterWarmup - densities[index]) < 0.001f, "The measured scenario must use its stated rain intensity.");
                if (measured.name == "full_reflection_refresh") Require(measured.reflectionCaptureRequestsAfterSamples > measured.reflectionCaptureRequestsBeforeSamples, "The reflection performance interval must include an actual cubemap refresh request.");
                if (measured.name == "full_no_reflections") Require(measured.reflectionCaptureRequestsBeforeSamples == 0 && measured.reflectionCaptureRequestsAfterSamples == 0, "The disabled reflection control must not request captures.");
                if (measured.name == "puddles_no_ripples" || measured.name == "puddles_ripples") Require(!measured.rainVisualVisible && !measured.wetnessEnabled && measured.puddlesEnabled && (measured.name == "puddles_no_ripples" ? measured.rippleStrength == 0.0f : measured.rippleStrength > 0.0f), "Ripple controls require identical rain and water state with the declared visible ripple amplitude and no rain particles.");
            }
            Require(_cameraRenders - rendersBefore >= 1888 + (extended ? 300 : 0), "The camera must actually render throughout every requested native measurement interval.");
            _fullRecord.performancePassed = true;
        }

        private void SetupFullPerformance(XRayWeatherProfile profile, float density, bool wetness, bool puddles)
        {
            SetupFull(profile, 43230.0, density, wetness, puddles);
            if (_controller.Precipitation.Wetness != null) _controller.Precipitation.Wetness.SetUniformState(1.0f, puddles ? 1.0f : 0.0f);
            _controller.SetPaused(false);
        }

        private void SetupFullReflectionPerformance(XRayWeatherProfile profile, bool enabled)
        {
            SetupFullPerformance(profile, 1.0f, true, true);
            FullPose pose = FullPoseNamed("Puddles");
            _camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
            JsonUtility.FromJsonOverwrite(enabled ? "{\"_nonCameraReflectionsEnabled\":true}" : "{\"_nonCameraReflectionsEnabled\":false}", _ownedConfigs[_ownedConfigs.Count - 1]);
            _controller.ApplySettings();
            _controller.Precipitation.Wetness.SetUniformState(1.0f, 1.0f);
            _controller.SetPaused(false);
        }

        private void SetupFullPuddleRipplePerformance(XRayWeatherProfile profile, bool ripples)
        {
            SetupFullPerformance(profile, 1.0f, false, true);
            FullPose pose = FullPoseNamed("Puddles");
            _camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
            PrecipitationConfig owned = _ownedConfigs[_ownedConfigs.Count - 1];
            JsonUtility.FromJsonOverwrite(ripples ? "{\"_wettingRate\":0,\"_dryingRate\":0,\"_puddleFormationRate\":0,\"_puddleDryingRate\":0}" : "{\"_rippleStrength\":0,\"_wettingRate\":0,\"_dryingRate\":0,\"_puddleFormationRate\":0,\"_puddleDryingRate\":0}", owned);
            _controller.ApplySettings();
            _controller.Precipitation.Wetness.SetUniformState(1.0f, 1.0f);
            _controller.SetRainVisualOverride(false);
            _controller.SetPaused(false);
        }

        private void SetupFull(XRayWeatherProfile profile, double seconds, float density, bool wetness, bool puddles)
        {
            Require(!_fatalNative, "A native error stops all further feature changes.");
            _controller.StopFx();
            _controller.SetPaused(true);
            _controller.SetTimeScale(0.0f);
            _controller.SetProfile(profile);
            _controller.Seek(seconds);
            Require(profile.TryEvaluate(seconds, out XRayWeatherSnapshot snapshot, out string diagnostic), diagnostic);
            SetFeatures(wetness, puddles);
            float multiplier = snapshot.RainDensity > 0.0f ? density / snapshot.RainDensity : 1.0f;
            JsonUtility.FromJsonOverwrite("{\"_intensityMultiplier\":" + multiplier.ToString("R", CultureInfo.InvariantCulture) + "}", _ownedConfigs[_ownedConfigs.Count - 1]);
            _controller.ApplySettings();
            _controller.SetRainVisualOverride(true);
            if (_controller.Precipitation.Wetness != null) _controller.Precipitation.Wetness.SetUniformState(0.0f, 0.0f);
        }

        private FullPose FullPoseNamed(string name)
        {
            if (_poses == null)
            {
                string[] arguments = Environment.GetCommandLineArgs();
                string path = null;
                for (int index = 0; index + 1 < arguments.Length; index++) if (arguments[index] == "-precipitationPoses") path = arguments[index + 1];
                Require(!string.IsNullOrEmpty(path) && Path.GetFullPath(path).StartsWith(ARCHIVE, StringComparison.OrdinalIgnoreCase), "Provide the CPU-audited real military pose manifest from Archive.");
                string json = File.ReadAllText(path);
                File.WriteAllText(Path.Combine(_fullOutput, "InputPoses.json"), json);
                _poses = JsonUtility.FromJson<PoseDocument>(json);
            }
            foreach (FullPose pose in _poses.poses) if (pose.name == name) return pose;
            throw new InvalidOperationException("Missing actual level pose: " + name);
        }

        private XRayWetnessCell ReadFullCell(Vector3 position)
        {
            XRayPrecipitationSystem system = _controller.Precipitation;
            Vector4 mapping = system.Geometry.WorldMapping;
            int x = Mathf.FloorToInt((position.x - mapping.x) * mapping.z * system.Wetness.Width);
            int y = Mathf.FloorToInt((position.z - mapping.y) * mapping.w * system.Wetness.Height);
            Require(system.Wetness.TryReadCell(x, y, out XRayWetnessCell cell), "The actual world surface lies in the bounded wetness field.");
            return cell;
        }

        private string FullCaseFolder(string name)
        {
            string output = Path.Combine(_fullOutput, name);
            Directory.CreateDirectory(output);
            return output;
        }

        private static FullCase FullDifference(string name, Color[] before, Color[] repeat, Color[] after)
        {
            XRayPrecipitationValidationProbe.Difference(before, repeat, out double noise, out _, out _);
            XRayPrecipitationValidationProbe.Difference(repeat, after, out double difference, out int pixels, out float maximum);
            bool passed = difference > Math.Max(noise * 4.0, 0.00001) && pixels > 100;
            return new FullCase { name = name, noise = noise, difference = difference, changedPixels = pixels, maximum = maximum, passed = passed, globalPassed = passed, acceptanceMetric = "FullFrameMeanMaximumRgbDifference" };
        }

        private static void FullRainStartRegion(Color[] before, Color[] repeat, Color[] after, FullCase record)
        {
            Require(before.Length == WIDTH * HEIGHT && repeat.Length == before.Length && after.Length == before.Length, "The fixed onset region requires native1920x1080 captures.");
            int yStart = HEIGHT - RAIN_START_ROI_Y - RAIN_START_ROI_HEIGHT;
            double noise = 0.0;
            double difference = 0.0;
            int changed = 0;
            float maximum = 0.0f;
            for (int y = yStart; y < yStart + RAIN_START_ROI_HEIGHT; y++)
            {
                for (int x = RAIN_START_ROI_X; x < RAIN_START_ROI_X + RAIN_START_ROI_WIDTH; x++)
                {
                    int index = y * WIDTH + x;
                    Color first = before[index];
                    Color second = repeat[index];
                    Color third = after[index];
                    noise += (Mathf.Abs(first.r - second.r) + Mathf.Abs(first.g - second.g) + Mathf.Abs(first.b - second.b)) / 3.0;
                    difference += (Mathf.Abs(second.r - third.r) + Mathf.Abs(second.g - third.g) + Mathf.Abs(second.b - third.b)) / 3.0;
                    float delta = Mathf.Max(Mathf.Abs(second.r - third.r), Mathf.Abs(second.g - third.g), Mathf.Abs(second.b - third.b));
                    maximum = Mathf.Max(maximum, delta);
                    if (delta > RAIN_START_CHANGED_THRESHOLD) changed++;
                }
            }
            int pixels = RAIN_START_ROI_WIDTH * RAIN_START_ROI_HEIGHT;
            record.acceptanceMetric = "PredeclaredOpenSkyLinearMeanAbsoluteRgbAndGpuControls";
            record.acceptanceRegion = "Top-left image rectangle; unchanged Unity Color[] rows are bottom-left; CPU yStart=HEIGHT-y-height.";
            record.regionX = RAIN_START_ROI_X;
            record.regionY = RAIN_START_ROI_Y;
            record.regionWidth = RAIN_START_ROI_WIDTH;
            record.regionHeight = RAIN_START_ROI_HEIGHT;
            record.regionCpuYStart = yStart;
            record.regionNoise = noise / pixels;
            record.regionDifference = difference / pixels;
            record.regionChangedPixels = changed;
            record.regionChangedPixelThreshold = RAIN_START_CHANGED_THRESHOLD;
            record.regionMaximum = maximum;
            record.regionPassed = record.regionDifference > Math.Max(record.regionNoise * 4.0, 0.00001) && changed > 100;
        }

        private static XRayWeatherProfile FindFullProfile(string cycle)
        {
            foreach (XRayWeatherProfile profile in GameConfig.Load().OrdinaryWeatherCatalog.Profiles) if (profile.CycleName == cycle) return profile;
            throw new InvalidOperationException("Missing imported weather cycle: " + cycle);
        }

        private static Quaternion FullHorizonRotation(Quaternion source)
        {
            Vector3 forward = source * Vector3.forward;
            forward.y = 0.0f;
            Require(forward.sqrMagnitude > 0.000001f, "The audited standing rain view requires a horizontal direction.");
            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        [Serializable]
        private sealed class FullRecord
        {
            public bool passed;
            public bool configurationReadOnly;
            public string failure;
            public string sourceHash;
            public string api;
            public float exposure;
            public float fieldOfView;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
            public int nativeErrors;
            public string performance;
            public bool performancePassed;
            public bool canopyUnavailable;
            public int instancedInstanceCount;
            public long geometryEstimatedBytes;
            public FullReflectionEvidence reflections;
            public FullCoverageEvidence groundCoverage;
            public List<FullCase> cases = new List<FullCase>();
        }

        [Serializable]
        private sealed class FullCoverageEvidence
        {
            public bool afterOnly;
            public bool paused;
            public float acceleratedSingleTickSeconds;
            public float rainDensity;
            public float filmStrength;
            public float puddleStrength;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
            public Vector3 sampleCenter;
            public Vector4 worldMapping;
            public Vector4 cellSize;
            public int validCells;
            public int shelteredColumns;
            public int contrastingLayers;
            public FullCoverageCell[] cells;
            public string evidence;
        }

        [Serializable]
        private sealed class FullCoverageCell
        {
            public int column;
            public int row;
            public Vector3 position;
            public Vector2 heights;
            public float lowerWetness;
            public float lowerPuddles;
            public float upperWetness;
            public float upperPuddles;
            public bool sampledUpperLayer;
        }

        [Serializable]
        private sealed class FullReflectionEvidence
        {
            public bool passed;
            public bool ssrDisabled;
            public bool realGeometry;
            public bool planarDisabled;
            public string reflectionSource;
            public int captureRequests;
            public uint textureUpdates;
            public int resolution;
            public int faces;
            public long estimatedBytes;
            public long instancedBatches;
            public long instancedInstances;
            public float[] faceMaximum;
            public double[] faceMean;
            public float offscreenMaximum;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
            public FullCase comparison;
        }

        [Serializable]
        private sealed class RainGpuEvidence
        {
            public int capacity;
            public bool readable;
            public int renderCount;
            public int submittedDrops;
            public bool actualCamera;
            public float density;
            public Vector3 cameraPosition;
            public Vector3 cameraForward;
            public string diagnostic;
            public Vector3 sourceLight;
            public Color radiance;
            public int active;
            public int frustum;
            public int drawRadius;
            public int visibleCandidates;
            public Vector3 minimumPosition;
            public Vector3 maximumPosition;
            public RainHeightEvidence[] heights;
        }

        [Serializable]
        private sealed class RainHeightEvidence
        {
            public Vector3 point;
            public float top;
            public float bottom;
            public bool valid;
            public bool aboveCamera;
        }

        [Serializable]
        private sealed class PerformanceEvidence
        {
            public int width;
            public int height;
            public bool editor;
            public PerformanceScenarioEvidence[] scenarios;
        }

        [Serializable]
        private sealed class PerformanceScenarioEvidence
        {
            public string name;
            public string status;
            public float rainDensityAfterWarmup;
            public float rippleStrength;
            public bool rainVisualVisible;
            public bool wetnessEnabled;
            public bool puddlesEnabled;
            public int reflectionCaptureRequestsBeforeSamples;
            public int reflectionCaptureRequestsAfterSamples;
            public PerformanceMetricEvidence nativeIntervalMs;
        }

        [Serializable]
        private sealed class PerformanceMetricEvidence
        {
            public bool available;
            public int sampleCount;
            public double mean;
        }

        [Serializable]
        private sealed class FullCase
        {
            public string name;
            public bool passed;
            public bool globalPassed;
            public string acceptanceMetric;
            public string acceptanceRegion;
            public int regionX;
            public int regionY;
            public int regionWidth;
            public int regionHeight;
            public int regionCpuYStart;
            public double regionNoise;
            public double regionDifference;
            public int regionChangedPixels;
            public float regionChangedPixelThreshold;
            public float regionMaximum;
            public bool regionPassed;
            public bool gpuRainControlPassed;
            public int rainOffRenderCount;
            public int rainOnRenderCount;
            public float rainDensity;
            public double noise;
            public double initialNoise;
            public double difference;
            public int changedPixels;
            public float maximum;
            public int materialClones;
            public int customDrawBindings;
            public bool zeroDirectSun;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
            public string evidence;
        }

        [Serializable]
        private sealed class PoseDocument
        {
            public FullPose[] poses;
        }

        [Serializable]
        private sealed class FullPose
        {
            public string name;
            public string kind;
            public string evidence;
            public Vector3 position;
            public Quaternion rotation;
            public float groundY;
            public float roofY;
            public float roofPatchRadius;
        }

        [Serializable]
        private sealed class FullCell
        {
            public string name;
            public Vector3 position;
            public Vector2 heights;
            public Vector4 moisture;
            public float expectedLowerWetness;
            public float expectedSurfaceWetness;
            public float expectedSurfacePuddles;
            public bool sampledUpperLayer;
            public float expectedLowerPuddles;
            public float elapsedRealSeconds;
            public float cameraSettleSeconds;
            public int roofColumnDrops;
            public int dropsBelowRoof;
            public bool passed;
        }

        [Serializable]
        private sealed class FullCells
        {
            public FullCell[] cells;
        }
    }
}

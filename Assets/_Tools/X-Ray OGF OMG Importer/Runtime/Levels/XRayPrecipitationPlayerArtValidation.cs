using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace XRay.Importer.Levels
{
    public sealed partial class XRayPrecipitationPlayerSafetyProbe
    {
        private bool _artPassed;
        private string _artOutput;
        private ArtRecord _artRecord;
        private ArtSources _artSources;
        private bool _artEmissionEnabled = true;
        private float _artPuddleState;

        private readonly List<ArtLightState> _artLights = new List<ArtLightState>();
        private readonly List<ArtLightState> _artDirectionals = new List<ArtLightState>();
        private readonly List<ArtEmitterState> _artEmitters = new List<ArtEmitterState>();

        private IEnumerator RunGuardedArt()
        {
            _artOutput = Path.Combine(_output, "ArtValidation");
            Directory.CreateDirectory(_artOutput);
            _artRecord = new ArtRecord { sourceHash = _sourceHash, graphicsApi = SystemInfo.graphicsDeviceType.ToString(), status = "RUNNING", visualReview = "PENDING_USER_REVIEW" };
            Vector3 position = _camera.transform.position;
            Quaternion rotation = _camera.transform.rotation;
            float fov = _camera.fieldOfView;
            HDAdditionalCameraData cameraData = _camera.GetComponent<HDAdditionalCameraData>();
            HDAdditionalCameraData.AntialiasingMode antialiasing = cameraData != null ? cameraData.antialiasing : HDAdditionalCameraData.AntialiasingMode.None;
            string shared = SharedJson();
            var stack = new Stack<IEnumerator>();
            stack.Push(RunArtValidation());
            try
            {
                while (stack.Count > 0 && !_fatalNative)
                {
                    IEnumerator current = stack.Peek();
                    bool next = false;
                    object yielded = null;
                    try
                    {
                        next = current.MoveNext();
                        if (next) yielded = current.Current;
                    }
                    catch (Exception exception)
                    {
                        _artRecord.failure = exception.ToString();
                        break;
                    }
                    if (!next)
                    {
                        stack.Pop();
                        if (current is IDisposable disposable) disposable.Dispose();
                    }
                    else if (yielded is IEnumerator nested) stack.Push(nested);
                    else yield return yielded;
                }
            }
            finally
            {
                while (stack.Count > 0) if (stack.Pop() is IDisposable disposable) disposable.Dispose();
                foreach (ArtLightState light in _artLights) if (light.Light != null) light.Light.enabled = light.Enabled;
                SetArtDirectionals(true);
                RestoreArtEmitters();
                _camera.transform.SetPositionAndRotation(position, rotation);
                _camera.fieldOfView = fov;
                if (cameraData != null) cameraData.antialiasing = antialiasing;
                ReleaseControlledArtStudy();
                if (_controller.Precipitation != null) _controller.Precipitation.ArtProfileOverride = null;
                _configField.SetValue(_controller, _originalConfig);
                _flagsField.SetValue(_controller, _originalFlags);
                _controller.SetPaused(true);
                _controller.StopFx();
                _controller.ApplySettings();
                _controller.SetRainVisualOverride(false);
                if (_fullVolume != null) Destroy(_fullVolume);
                if (_fullExposure != null) Destroy(_fullExposure);
                foreach (PrecipitationConfig config in _ownedConfigs) if (config != null) Destroy(config);
                _ownedConfigs.Clear();
            }
            yield return Frames(8);
            if (_artRecord.controlledRequested)
            {
                _artRecord.controlledReflectionTexturesTracked = _artControlledNativeTextures.Count;
                _artRecord.controlledReflectionTexturesReleased = ReleasedReflectionTextureCount(_artControlledNativeTextures);
                _artRecord.controlledReflectionReleasePassed = _artRecord.controlledCubemapCaptured && _artRecord.controlledPlanarCaptured && _artRecord.controlledReflectionTexturesTracked > 0 && _artRecord.controlledReflectionTexturesReleased == _artRecord.controlledReflectionTexturesTracked;
                if (!_artRecord.controlledReflectionReleasePassed) _artRecord.failure = (_artRecord.failure ?? string.Empty) + " Controlled native cube/planar capture or actual reflection color/depth texture release evidence is incomplete.";
            }
            _artRecord.configurationReadOnly = shared == SharedJson();
            _artRecord.nativeErrors = ErrorCount();
            _artRecord.status = _fatalNative || !string.IsNullOrEmpty(_artRecord.failure) ? "FAILED" : "CAPTURES_WRITTEN";
            _artPassed = _artRecord.status == "CAPTURES_WRITTEN" && _artRecord.configurationReadOnly && _artRecord.nativeErrors == 0 && _artRecord.captures.Count > 0;
            _artRecord.captureAssertionsPassed = _artPassed;
            Write(_artOutput, "Result", _artRecord);
        }

        private IEnumerator RunArtValidation()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            string manifest = null;
            for (int index = 0; index + 1 < arguments.Length; index++) if (arguments[index] == "-precipitationArtSources") manifest = arguments[index + 1];
            Require(!string.IsNullOrEmpty(manifest) && Path.GetFullPath(manifest).StartsWith(ARCHIVE, StringComparison.OrdinalIgnoreCase), "Provide an inspected art study source manifest from Archive.");
            string json = File.ReadAllText(manifest);
            File.WriteAllText(Path.Combine(_artOutput, "InputSources.json"), json);
            _artSources = JsonUtility.FromJson<ArtSources>(json);
            Require(_artSources != null && _artSources.studies != null && _artSources.studies.Length > 0, "At least one explicit audited camera study is required.");
            _fullExposure = ScriptableObject.CreateInstance<VolumeProfile>();
            Exposure exposure = _fullExposure.Add<Exposure>(true);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(9.0f);
            exposure.compensation.Override(0.0f);
            ScreenSpaceReflection ssr = _fullExposure.Add<ScreenSpaceReflection>(true);
            ssr.enabled.Override(false);
            ssr.enabledTransparent.Override(false);
            _fullVolume = new GameObject("Owned puddle art EV9 controls");
            _fullVolume.transform.SetParent(transform, false);
            Volume volume = _fullVolume.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100001.0f;
            volume.sharedProfile = _fullExposure;
            _camera.fieldOfView = 60.0f;
            HDAdditionalCameraData cameraData = _camera.GetComponent<HDAdditionalCameraData>();
            Require(cameraData != null, "Native art capture requires HDRP camera controls.");
            cameraData.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            XRayWeatherProfile clear = FindFullProfile("w_clear1");
            XRayWeatherProfile rain = FindFullProfile("w_rain1");
            bool controlled = Array.IndexOf(arguments, "-precipitationArtControlled") >= 0;
            _artRecord.controlledRequested = controlled;
            int studyCount = _artSources.studies.Length + (controlled ? 1 : 0);
            for (int studyIndex = 0; studyIndex < studyCount; studyIndex++)
            {
                ArtStudy study = studyIndex < _artSources.studies.Length ? _artSources.studies[studyIndex] : CreateControlledArtStudy();
                Require(!string.IsNullOrEmpty(study.name) && Path.GetFileName(study.name) == study.name, "Study names must be simple directory names.");
                ResolveArtSources(study);
                string folder = Path.Combine(_artOutput, study.name);
                Directory.CreateDirectory(folder);
                Require(float.IsFinite(study.fov) && study.fov >= 10.0f && study.fov <= 150.0f, "Use the inspected study field of view.");
                _camera.fieldOfView = study.fov;
                _camera.transform.SetPositionAndRotation(study.position, study.rotation);
                SetupArt(clear, XRayPuddleArtProfile.AtmosphericZone, 0.0f, 0.0f, 0.0f, 43230.0);
                SetArtLights(-1);
                yield return ArtCapture(folder, "Dry_LocalOff", study, false, true);
                SetArtLights(-2);
                yield return ArtCapture(folder, "Dry_LocalOn", study, false, true);
                SetupArt(clear, XRayPuddleArtProfile.AtmosphericZone, 0.85f, 0.0f, 0.0f, 43230.0);
                SetArtLights(-1);
                yield return ArtCapture(folder, "Wet_LocalOff", study, false, true);
                for (int index = 0; index < _artLights.Count; index++)
                {
                    SetArtLights(index);
                    yield return ArtCapture(folder, "Wet_Source" + index, study, false, true);
                }
                SetArtLights(-2);
                yield return ArtCapture(folder, "Wet_AllSources", study, false, true);
                SetArtDirectionals(false);
                yield return ArtCapture(folder, "Wet_LocalOnly_DirectionalOff", study, false, true);
                SetArtLights(-1);
                yield return ArtCapture(folder, "Wet_AllDirectOff", study, false, true);
                SetArtDirectionals(true);
                yield return ArtCapture(folder, "Wet_DirectionalOnly_LocalOff", study, false, true);
                SetArtLights(-2);
                foreach (XRayPuddleArtProfile profile in new[] { XRayPuddleArtProfile.SubtleRealistic, XRayPuddleArtProfile.AtmosphericZone, XRayPuddleArtProfile.HeavyRain })
                {
                    SetupArt(rain, profile, 0.85f, 0.75f, 0.2f, 43230.0);
                    _controller.SetRainVisualOverride(false);
                    _camera.transform.SetPositionAndRotation(study.position, study.rotation);
                    yield return ArtCapture(folder, profile + "_Eye_WeakRain", study, false, true);
                    _camera.transform.SetPositionAndRotation(study.lowPosition, study.lowRotation);
                    yield return ArtCapture(folder, profile + "_Low_WeakRain", study, false, true);
                    SetupArt(rain, profile, 0.85f, 0.75f, 1.0f, 43230.0);
                    _controller.SetRainVisualOverride(false);
                    yield return ArtCapture(folder, profile + "_Low_StrongRain", study, false, true);
                }
                SetupArt(clear, XRayPuddleArtProfile.AtmosphericZone, 0.85f, 0.75f, 0.0f, 43230.0);
                SetArtLights(-2);
                SetArtEmitters(false);
                yield return RefreshArtProbe();
                yield return ArtCapture(folder, "Puddle_DirectOnly_SsrOff", study, false, false);
                SetArtLights(-1);
                SetArtDirectionals(false);
                SetArtEmitters(true);
                yield return RefreshArtProbe();
                yield return ArtCapture(folder, _artEmitters.Count > 0 ? "Puddle_EmissionAndEnvironment_SsrOff" : "Puddle_Environment_NoEmissionAvailable", study, false, true);
                SetArtLights(-2);
                yield return ArtCapture(folder, "Puddle_LocalOnly_DirectionalOff", study, false, true);
                SetArtLights(-1);
                SetArtDirectionals(true);
                yield return ArtCapture(folder, "Puddle_DirectionalOnly_LocalOff", study, false, true);
                SetArtLights(-2);
                yield return RefreshArtProbe();
                yield return ArtCapture(folder, "Puddle_All_SsrOff_CubeOnly", study, false, true, 30, 0.0f);
                yield return ArtCapture(folder, "Puddle_All_SsrOff_ProbeOn", study, false, true);
                yield return ArtCapture(folder, "Puddle_All_SsrOn_ProbeOn", study, true, true);
                yield return ArtCapture(folder, "Puddle_All_SsrOff_ProbeOff", study, false, false);
                if (_artLights.Count > 0)
                {
                    SetupArt(rain, XRayPuddleArtProfile.AtmosphericZone, 0.85f, 0.75f, 0.35f, 3600.0);
                    _controller.SetRainVisualOverride(true);
                    _controller.SetPaused(false);
                    yield return ArtSequence(folder, "NightRainMotion_None", study, true);
                    cameraData.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
                    yield return Frames(30);
                    yield return ArtSequence(folder, "NightRainMotion_Taa", study, true);
                    cameraData.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                    SetupArt(clear, XRayPuddleArtProfile.AtmosphericZone, 0.85f, 0.75f, 0.0f, 3600.0, false);
                    _controller.SetRainVisualOverride(false);
                    _controller.SetPaused(false);
                    yield return ArtSequence(folder, "RainStopped_None", study, false);
                }
                _controller.SetPaused(true);
                foreach (ArtLightState light in _artLights) light.Light.enabled = light.Enabled;
                SetArtDirectionals(true);
                RestoreArtEmitters();
            }
        }

        private void SetupArt(XRayWeatherProfile weather, XRayPuddleArtProfile profile, float wetness, float puddles, float rain, double seconds, bool calmCheckpoint = true)
        {
            SetupFull(weather, seconds, rain, true, true);
            PrecipitationConfig owned = _ownedConfigs[_ownedConfigs.Count - 1];
            JsonUtility.FromJsonOverwrite("{\"_wettingRate\":0,\"_dryingRate\":0,\"_puddleFormationRate\":0,\"_puddleDryingRate\":0}", owned);
            _controller.Precipitation.ArtProfileOverride = profile;
            XRayPrecipitationSystem system = _controller.Precipitation;
            if (calmCheckpoint) system.Wetness.Tick(owned, system.Geometry.Heights, system.Geometry.WorldMapping, 0.0f, owned.RippleDecaySeconds * 20.0f);
            _controller.ApplySettings();
            if (calmCheckpoint) Require(Mathf.Abs(system.Wetness.RippleRainEnvelope - Mathf.Clamp01(rain)) < 0.000001f, "Each fixed art checkpoint must use its requested rain ripple envelope rather than retain a previous stronger rain state.");
            _controller.Precipitation.Wetness.SetUniformState(wetness, puddles);
            _artPuddleState = puddles;
        }

        private IEnumerator ArtCapture(string folder, string label, ArtStudy study, bool ssrEnabled, bool probeEnabled, int settleFrames = 30, float planarWeight = 1.0f)
        {
            _fullExposure.TryGet(out ScreenSpaceReflection ssr);
            ssr.enabled.Override(ssrEnabled);
            ssr.enabledTransparent.Override(ssrEnabled);
            HDAdditionalReflectionData probe = CurrentArtProbe();
            if (_controller.Precipitation.Reflections != null) _controller.Precipitation.Reflections.Weight = _artControlledProbe == null && probeEnabled ? 1.0f : 0.0f;
            if (_controller.Precipitation.Reflections != null) _controller.Precipitation.Reflections.PlanarWeight = _artControlledProbe == null ? planarWeight : 0.0f;
            if (_artControlledProbe != null) _artControlledProbe.weight = probeEnabled ? 1.0f : 0.0f;
            if (_artControlledPlanar != null) _artControlledPlanar.weight = probeEnabled && planarWeight > 0.0f && _artPuddleState > 0.025f ? planarWeight : 0.0f;
            if (probeEnabled && planarWeight > 0.0f && _artPuddleState > 0.025f && (_artControlledPlanar != null || _controller.EffectivePrecipitationConfig.PlanarReflectionsEnabled))
            {
                for (int frame = 0; frame < 240; frame++)
                {
                    PlanarReflectionProbe current = CurrentArtPlanar();
                    if (current != null && current.realtimeTexture != null && current.realtimeTexture.updateCount > 0) break;
                    yield return null;
                }
            }
            yield return Frames(settleFrames);
            yield return Capture(folder, label);
            Require(_captureFailure == null, _captureFailure?.ToString());
            var record = new ArtCaptureRecord { study = study.name, context = study.context, surface = study.surface, label = label, cameraPosition = _camera.transform.position, cameraRotation = _camera.transform.rotation, fieldOfView = _camera.fieldOfView, exposure = 9.0f, width = WIDTH, height = HEIGHT, profile = _controller.Precipitation.ArtProfileOverride.ToString(), precipitationTime = _controller.Precipitation.RealTime, rainDensity = _controller.Precipitation.RainDensity, ssrRequested = ssrEnabled, probesRequested = probeEnabled, probeAvailable = probe != null, probeRequests = _controller.Precipitation.Reflections?.CaptureRequests ?? 0, emissiveSourceCount = _artEmitters.Count, emissiveEnabled = _artEmissionEnabled, sourceCount = _artLights.Count, directionalSourceCount = _artDirectionals.Count, nativeFrameAfterReadback = Time.frameCount, unscaledTimeAfterReadback = Time.unscaledTimeAsDouble, sampling = settleFrames == 0 ? "Native temporal samples; asynchronous readback latency and encoding separate samples, not consecutive display frames." : "Fixed-state capture after thirty native settling frames.", captureWritten = true, visualReview = "PENDING_USER_REVIEW", lights = new ArtLightRecord[_artLights.Count + _artDirectionals.Count] };
            ScreenSpaceReflection applied = HDCamera.GetOrCreate(_camera).volumeStack.GetComponent<ScreenSpaceReflection>();
            record.ssrApplied = applied != null && applied.enabled.value;
            record.rippleState = Shader.GetGlobalVector("_MonolithPuddleRipple");
            record.puddleShape = Shader.GetGlobalVector("_MonolithPuddleShape");
            record.puddleOptics = Shader.GetGlobalVector("_MonolithPuddleOptics");
            HDAdditionalCameraData cameraData = _camera.GetComponent<HDAdditionalCameraData>();
            record.antialiasing = cameraData != null ? cameraData.antialiasing.ToString() : "Missing";
            record.probeSource = _artControlledProbe != null ? "ControlledSyntheticSeparateNativeCubemapAndPlanar" : "ProductionAutomaticNativeCubemapPoolAndPlanar";
            record.probeRequests = _artControlledProbe != null ? _artControlledRequests : _controller.Precipitation.Reflections?.CaptureRequests ?? 0;
            record.cubeProbeCount = _artControlledProbe != null ? 1 : _controller.Precipitation.Reflections?.ProbeCount ?? 0;
            record.planarConfigured = _artControlledPlanar != null || _controller.EffectivePrecipitationConfig.PlanarReflectionsEnabled;
            record.planarRequested = probeEnabled && planarWeight > 0.0f && record.planarConfigured && _artPuddleState > 0.025f;
            PlanarReflectionProbe planar = CurrentArtPlanar();
            if (planar != null)
            {
                record.planarAvailable = true;
                record.planarPosition = planar.transform.position;
                record.planarInfluenceBox = planar.influenceVolume.boxSize;
                record.planarActualWeight = planar.weight;
                if (planar.realtimeTexture != null)
                {
                    record.planarTextureUpdates = planar.realtimeTexture.updateCount;
                    record.planarTextureWidth = planar.realtimeTexture.width;
                    record.planarTextureHeight = planar.realtimeTexture.height;
                }
            }
            if (_artControlledProbe != null && _artControlledProbe.realtimeTexture != null && _artControlledProbe.realtimeTexture.updateCount > 0) _artRecord.controlledCubemapCaptured = true;
            if (_artControlledPlanar != null && record.planarTextureUpdates > 0) _artRecord.controlledPlanarCaptured = true;
            record.reflectionMode = (_artControlledProbe != null ? "ControlledSynthetic_" : "Production_") + (!probeEnabled ? "NativeProbeCompositeOff" : planarWeight <= 0.0f ? "CubemapOnly_PlanarWeightZero" : "NativePlanarAndCubemap");
            if (record.planarRequested && _artPuddleState > 0.025f && record.planarTextureUpdates == 0) _artRecord.notRun.Add(study.name + "/" + label + ": native planar reflection unavailable after bounded wait; capture does not prove planar reflection.");
            for (int index = 0; index < record.lights.Length; index++)
            {
                ArtLightState source = index < _artLights.Count ? _artLights[index] : _artDirectionals[index - _artLights.Count];
                record.lights[index] = new ArtLightRecord { path = source.Path, type = source.Light.type.ToString(), enabled = source.Light.isActiveAndEnabled, position = source.Light.transform.position, color = source.Light.color, intensity = source.Light.intensity, unit = source.Light.lightUnit.ToString(), radius = source.Light.shapeRadius, directDiffuseEnabled = source.Data == null || source.Data.affectDiffuse, directSpecularEnabled = source.Data == null || source.Data.affectSpecular };
            }
            Write(folder, label + "_State", record);
            _artRecord.captures.Add(record);
        }

        private IEnumerator ArtSequence(string folder, string label, ArtStudy study, bool motion)
        {
            string output = Path.Combine(folder, label);
            Directory.CreateDirectory(output);
            double start = Time.unscaledTimeAsDouble;
            for (int frame = 0; frame < 12; frame++)
            {
                while (Time.unscaledTimeAsDouble < start + frame * 0.2) yield return null;
                float phase = frame / 11.0f;
                Vector3 position = study.lowPosition + (motion ? study.lowRotation * Vector3.right * (phase * 0.6f) : Vector3.zero);
                Quaternion rotation = study.lowRotation * Quaternion.Euler(0.0f, motion ? phase * 6.0f : 0.0f, 0.0f);
                _camera.transform.SetPositionAndRotation(position, rotation);
                yield return ArtCapture(output, "Sample" + frame.ToString("000", CultureInfo.InvariantCulture), study, false, true, 0);
            }
        }

        private IEnumerator RefreshArtProbe()
        {
            if (_artControlledProbe != null)
            {
                uint previous = _artControlledProbe.realtimeTexture != null ? _artControlledProbe.realtimeTexture.updateCount : 0;
                _artControlledProbe.weight = 1.0f;
                if (_artControlledPlanar != null)
                {
                    _artControlledPlanar.weight = 1.0f;
                    _artControlledPlanar.RequestRenderNextUpdate();
                }
                _artControlledProbe.RequestRenderNextUpdate();
                _artControlledRequests++;
                for (int frame = 0; frame < 240; frame++)
                {
                    bool planarReady = _artControlledPlanar == null || _artControlledPlanar.realtimeTexture != null && _artControlledPlanar.realtimeTexture.updateCount > 0;
                    if (_artControlledProbe.realtimeTexture != null && _artControlledProbe.realtimeTexture.updateCount != previous && planarReady) yield break;
                    yield return null;
                }
                throw new InvalidOperationException("The separate controlled native probe must complete its requested source-isolation capture.");
            }
            XRayWetnessReflection reflections = _controller.Precipitation.Reflections;
            if (reflections == null) yield break;
            reflections.Weight = 1.0f;
            reflections.Dispose();
            reflections.Tick(_controller.EffectivePrecipitationConfig, 0.0f, _controller.Precipitation.Wetness);
            for (int frame = 0; frame < 240; frame++)
            {
                HDAdditionalReflectionData probe = CurrentArtProbe();
                if (probe != null && probe.realtimeTexture != null && probe.realtimeTexture.updateCount > 0) yield break;
                yield return null;
            }
            throw new InvalidOperationException("The bounded native cubemap must complete after source isolation changes.");
        }

        private HDAdditionalReflectionData CurrentArtProbe()
        {
            if (_artControlledProbe != null) return _artControlledProbe;
            XRayWetnessReflection reflections = _controller.Precipitation.Reflections;
            if (reflections == null) return null;
            for (int index = 0; index < reflections.ProbeCount; index++)
            {
                HDAdditionalReflectionData probe = reflections.GetProbe(index);
                if (probe != null && probe.realtimeTexture != null && probe.realtimeTexture.updateCount > 0) return probe;
            }
            return reflections.Probe;
        }

        private PlanarReflectionProbe CurrentArtPlanar()
        {
            return _artControlledPlanar != null ? _artControlledPlanar : _controller.Precipitation.Reflections?.PlanarProbe;
        }

        private void ResolveArtSources(ArtStudy study)
        {
            _artLights.Clear();
            _artDirectionals.Clear();
            _artEmitters.Clear();
            _artEmissionEnabled = true;
            foreach (string path in study.lights ?? Array.Empty<string>())
            {
                Transform transform = ArtTransform(path);
                Light light = transform.GetComponent<Light>();
                Require(light != null && light.type != LightType.Directional, "Explicit local light paths must identify actual punctual or area sources: " + path);
                _artLights.Add(new ArtLightState { Light = light, Enabled = light.enabled, Path = path, Data = light.GetComponent<HDAdditionalLightData>() });
            }
            foreach (string path in study.directionalLights ?? Array.Empty<string>())
            {
                Light light = ArtTransform(path).GetComponent<Light>();
                Require(light != null && light.type == LightType.Directional, "Directional controls must identify an inspected real directional source: " + path);
                HDAdditionalLightData data = light.GetComponent<HDAdditionalLightData>();
                Require(data != null, "Directional ablation requires HDRP lobe controls while the weather source stays enabled.");
                _artDirectionals.Add(new ArtLightState { Light = light, Enabled = light.enabled, Path = path, Data = data, Diffuse = data.affectDiffuse, Specular = data.affectSpecular });
            }
            foreach (string path in study.emitters ?? Array.Empty<string>())
            {
                Renderer renderer = ArtTransform(path).GetComponent<Renderer>();
                Require(renderer != null, "Explicit emission paths must identify a real renderer: " + path);
                Material[] original = renderer.sharedMaterials;
                var muted = new Material[original.Length];
                var owned = new List<Material>();
                for (int index = 0; index < original.Length; index++)
                {
                    Material material = original[index];
                    muted[index] = material;
                    if (material == null) continue;
                    string property = material.HasProperty("_EmissiveColor") ? "_EmissiveColor" : material.HasProperty("_EmissionColor") ? "_EmissionColor" : null;
                    if (property == null || material.GetColor(property).maxColorComponent <= 0.0f) continue;
                    Material clone = new Material(material) { hideFlags = HideFlags.HideAndDontSave };
                    clone.SetColor(property, Color.black);
                    muted[index] = clone;
                    owned.Add(clone);
                }
                Require(owned.Count > 0, "Emission controls require positive actual material emission; named wood alone is not flame geometry: " + path);
                _artEmitters.Add(new ArtEmitterState { Renderer = renderer, Original = original, Muted = muted, Owned = owned.ToArray() });
            }
            if (_artLights.Count == 0) _artRecord.notRun.Add(study.name + ": local white/orange source isolation NOT_RUN; no inspected light paths supplied.");
            if (_artEmitters.Count == 0) _artRecord.notRun.Add(study.name + ": flame emission shape isolation NOT_RUN; no inspected emissive renderer supplied.");
        }

        private Transform ArtTransform(string path)
        {
            foreach (GameObject root in _controller.gameObject.scene.GetRootGameObjects())
            {
                if (path == root.name) return root.transform;
                if (!path.StartsWith(root.name + "/", StringComparison.Ordinal)) continue;
                Transform result = root.transform.Find(path.Substring(root.name.Length + 1));
                if (result != null) return result;
            }
            throw new InvalidOperationException("Inspected source is absent from the real study scene: " + path);
        }

        private void SetArtLights(int selected)
        {
            for (int index = 0; index < _artLights.Count; index++) _artLights[index].Light.enabled = _artLights[index].Enabled && (selected == -2 || selected == index);
        }

        private void SetArtEmitters(bool enabled)
        {
            _artEmissionEnabled = enabled;
            foreach (ArtEmitterState emitter in _artEmitters) emitter.Renderer.sharedMaterials = enabled ? emitter.Original : emitter.Muted;
        }

        private void RestoreArtEmitters()
        {
            foreach (ArtEmitterState emitter in _artEmitters)
            {
                if (emitter.Renderer != null) emitter.Renderer.sharedMaterials = emitter.Original;
                foreach (Material material in emitter.Owned) if (material != null) Destroy(material);
                emitter.Owned = Array.Empty<Material>();
            }
        }

        private void SetArtDirectionals(bool enabled)
        {
            foreach (ArtLightState light in _artDirectionals)
            {
                if (light.Data == null) continue;
                light.Data.affectDiffuse = light.Diffuse && enabled;
                light.Data.affectSpecular = light.Specular && enabled;
            }
        }

        [Serializable]
        private sealed class ArtSources
        {
            public ArtStudy[] studies;
        }

        [Serializable]
        private sealed class ArtStudy
        {
            public string name;
            public string context;
            public string surface;
            public float fov;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 lowPosition;
            public Quaternion lowRotation;
            public string[] lights;
            public string[] directionalLights;
            public string[] emitters;
        }

        private sealed class ArtLightState
        {
            public Light Light { get; set; }
            public bool Enabled { get; set; }
            public string Path { get; set; }
            public HDAdditionalLightData Data { get; set; }
            public bool Diffuse { get; set; }
            public bool Specular { get; set; }
        }

        private sealed class ArtEmitterState
        {
            public Renderer Renderer { get; set; }
            public Material[] Original { get; set; }
            public Material[] Muted { get; set; }
            public Material[] Owned { get; set; }
        }

        [Serializable]
        private sealed class ArtRecord
        {
            public string sourceHash;
            public string graphicsApi;
            public string status;
            public string failure;
            public string visualReview;
            public bool captureAssertionsPassed;
            public bool configurationReadOnly;
            public int nativeErrors;
            public bool controlledRequested;
            public int controlledReflectionTexturesTracked;
            public int controlledReflectionTexturesReleased;
            public bool controlledReflectionReleasePassed;
            public bool controlledCubemapCaptured;
            public bool controlledPlanarCaptured;
            public List<string> notRun = new List<string>();
            public List<ArtCaptureRecord> captures = new List<ArtCaptureRecord>();
        }

        [Serializable]
        private sealed class ArtCaptureRecord
        {
            public string study;
            public string context;
            public string surface;
            public string label;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
            public float fieldOfView;
            public float exposure;
            public int width;
            public int height;
            public string profile;
            public float precipitationTime;
            public float rainDensity;
            public Vector4 rippleState;
            public Vector4 puddleShape;
            public Vector4 puddleOptics;
            public bool ssrRequested;
            public bool ssrApplied;
            public bool probesRequested;
            public bool probeAvailable;
            public int probeRequests;
            public int sourceCount;
            public int directionalSourceCount;
            public int nativeFrameAfterReadback;
            public double unscaledTimeAfterReadback;
            public string sampling;
            public string antialiasing;
            public string probeSource;
            public string reflectionMode;
            public int cubeProbeCount;
            public bool planarConfigured;
            public bool planarRequested;
            public bool planarAvailable;
            public Vector3 planarPosition;
            public Vector3 planarInfluenceBox;
            public float planarActualWeight;
            public uint planarTextureUpdates;
            public int planarTextureWidth;
            public int planarTextureHeight;
            public int emissiveSourceCount;
            public bool emissiveEnabled;
            public bool captureWritten;
            public string visualReview;
            public ArtLightRecord[] lights;
        }

        [Serializable]
        private sealed class ArtLightRecord
        {
            public string path;
            public string type;
            public bool enabled;
            public Vector3 position;
            public Color color;
            public float intensity;
            public string unit;
            public float radius;
            public bool directDiffuseEnabled;
            public bool directSpecularEnabled;
        }
    }
}

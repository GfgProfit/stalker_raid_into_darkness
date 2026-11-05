using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Levels
{
    public sealed class XRayPrecipitationValidationProbe : MonoBehaviour
    {
        private const string ARCHIVE = @"E:\Projects\stalker_raid_into_darkness_Archive\";
        private const int WIDTH = 1920;
        private const int HEIGHT = 1080;

        private static XRayPrecipitationValidationProbe _instance;

        [SerializeField] private Camera _camera;
        [SerializeField] private XRayWeatherController _controller;
        [SerializeField] private string _sourceHash;

        private readonly List<string> _errors = new List<string>();
        private string _output;
        private bool _stagePassed;

        public void Configure(Camera camera, XRayWeatherController controller, string sourceHash)
        {
            _camera = camera;
            _controller = controller;
            _sourceHash = sourceHash;
        }

        public static string RunRainCases(Camera camera, XRayWeatherController controller, string output)
        {
            GuardOutput(output);
            Directory.CreateDirectory(output);
            var records = new List<RainCase>();
            var result = new RainResult { api = SystemInfo.graphicsDeviceType.ToString(), width = WIDTH, height = HEIGHT, cameraPosition = camera.transform.position, cameraRotation = camera.transform.rotation };
            try
            {
                Require(controller.Precipitation != null && controller.Precipitation.Enabled, "The shared precipitation system must be enabled.");
                Require(controller.EffectivePrecipitationConfig == GameConfig.Load().Precipitation, "The level inherits canonical precipitation settings.");
                controller.SetTimeScale(0.0f);
                controller.SetPrecipitationOverride(true);
                controller.SetCelestialEffectOverrides(false, false);
                XRayWeatherProfile day = FindRain(controller.Catalog, false, out double daySeconds);
                XRayWeatherProfile night = FindRain(controller.Catalog, true, out double nightSeconds);
                controller.SetProfile(day);
                controller.Seek(daySeconds);
                records.Add(CaptureRainPair(camera, controller, output, "Day_Outdoor"));
                result.geometry = InspectGeometry(controller.Precipitation.Geometry, camera.transform.position, output);
                result.gpu = InspectDrops(camera, controller, output);
                Vector3 originalPosition = camera.transform.position;
                Quaternion originalRotation = camera.transform.rotation;
                camera.transform.position += new Vector3(3.0f, 0.0f, 0.0f);
                records.Add(CaptureRainPair(camera, controller, output, "Day_CameraMoved"));
                camera.transform.position = originalPosition;
                if (result.geometry.hasCoveredPose)
                {
                    camera.transform.position = result.geometry.coveredPose;
                    Vector3 towardOpen = result.geometry.exposedPose - result.geometry.coveredPose;
                    towardOpen.y = 0.0f;
                    camera.transform.rotation = Quaternion.LookRotation(towardOpen);
                    records.Add(CaptureRainPair(camera, controller, output, "Day_Indoor", false));
                    result.roof = InspectRoofDrops(camera, controller);
                    camera.transform.position = result.geometry.exposedPose;
                    camera.transform.rotation = Quaternion.LookRotation(-towardOpen);
                    records.Add(CaptureRainPair(camera, controller, output, "Day_ExposedNearRoof"));
                    camera.transform.position = originalPosition;
                    camera.transform.rotation = originalRotation;
                }
                controller.SetProfile(night);
                controller.Seek(nightSeconds);
                records.Add(CaptureRainPair(camera, controller, output, "Night_Outdoor"));
                result.passed = records.TrueForAll(value => value.passed) && result.gpu.passed && result.geometry.validPixels > 0 && result.roof != null && result.roof.passed;
            }
            catch (Exception exception)
            {
                result.error = exception.ToString();
                File.WriteAllText(Path.Combine(output, "Failure.txt"), result.error);
            }
            result.cases = records.ToArray();
            string path = Path.Combine(output, "RainResult.json");
            File.WriteAllText(path, JsonUtility.ToJson(result, true));
            return path;
        }

        public static RainCase CaptureRainPair(Camera camera, XRayWeatherController controller, string output, string name, bool expectVisible = true)
        {
            controller.SetRainVisualOverride(true);
            controller.SetPaused(false);
            Advance(camera, controller, 12, 1.0f / 30.0f);
            controller.SetPaused(true);
            var record = new RainCase { name = name, expectVisible = expectVisible, profile = controller.ActiveProfile.CycleName, seconds = controller.GameTimeSeconds, density = controller.Snapshot.RainDensity, rainColor = controller.Snapshot.RainColor, sunColor = controller.Snapshot.SunColor, wind = controller.Snapshot.WindVelocity, windDirection = controller.Snapshot.WindDirectionRadians, cameraPosition = camera.transform.position, cameraRotation = camera.transform.rotation };
            controller.SetRainVisualOverride(false);
            Color[] first = XRayWeatherFogValidationProbe.Capture(camera, Path.Combine(output, name + "_RainOff"), WIDTH, HEIGHT, 4);
            Color[] repeat = XRayWeatherFogValidationProbe.Capture(camera, Path.Combine(output, name + "_RainOffRepeat"), WIDTH, HEIGHT, 2);
            controller.SetRainVisualOverride(true);
            Color[] rain = XRayWeatherFogValidationProbe.Capture(camera, Path.Combine(output, name + "_RainOn"), WIDTH, HEIGHT, 2);
            Difference(first, repeat, out record.noiseMean, out record.noiseChangedPixels, out record.noiseMaximum);
            Difference(repeat, rain, out record.rainMean, out record.rainChangedPixels, out record.rainMaximum);
            record.dropCount = controller.Precipitation.Pass.LastDropCount;
            record.renderCount = controller.Precipitation.Pass.RenderCount;
            record.magentaBefore = MagentaPixels(first);
            record.magentaAfter = MagentaPixels(rain);
            record.passed = record.magentaBefore < 1000 && record.magentaAfter < 1000 && record.density > 0.0f && record.dropCount > 0 && (!expectVisible || record.rainChangedPixels >= 100 && record.rainMean > Math.Max(record.noiseMean * 4.0, 0.000000001));
            File.WriteAllText(Path.Combine(output, name + "_Pair.json"), JsonUtility.ToJson(record, true));
            return record;
        }

        public static void Advance(Camera camera, XRayWeatherController controller, int frames, float delta)
        {
            RenderTexture previous = camera.targetTexture;
            RenderTexture target = RenderTexture.GetTemporary(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            try
            {
                camera.targetTexture = target;
                for (int index = 0; index < frames; index++)
                {
                    controller.TickSimulation(delta);
                    camera.Render();
                }
            }
            finally
            {
                camera.targetTexture = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        public static void Difference(Color[] before, Color[] after, out double mean, out int changed, out float maximum)
        {
            Require(before.Length == after.Length, "Image dimensions must match.");
            double sum = 0.0;
            maximum = 0.0f;
            changed = 0;
            for (int index = 0; index < before.Length; index++)
            {
                float delta = Mathf.Max(Mathf.Abs(before[index].r - after[index].r), Mathf.Abs(before[index].g - after[index].g), Mathf.Abs(before[index].b - after[index].b));
                sum += delta;
                maximum = Mathf.Max(maximum, delta);
                if (delta > 0.00001f) changed++;
            }
            mean = sum / before.Length;
        }

        private static int MagentaPixels(Color[] pixels)
        {
            int count = 0;
            foreach (Color pixel in pixels) if (pixel.r > 0.02f && pixel.b > 0.02f && pixel.g < Mathf.Min(pixel.r, pixel.b) * 0.4f && Mathf.Abs(pixel.r - pixel.b) < Mathf.Max(pixel.r, pixel.b) * 0.25f) count++;
            return count;
        }

        private static GpuRecord InspectDrops(Camera camera, XRayWeatherController controller, string output)
        {
            XRayPrecipitationSystem rain = controller.Precipitation;
            int capacity = rain.Pass.CapacityForCamera(camera);
            var first = new XRayRainDropState[capacity];
            var frozen = new XRayRainDropState[capacity];
            var moved = new XRayRainDropState[capacity];
            Require(capacity > 0 && rain.Pass.TryReadDrops(camera, first), "Read back actual GPU drop records.");
            float time = rain.RealTime;
            controller.SetPaused(true);
            Advance(camera, controller, 2, 0.25f);
            bool pausedClockUnchanged = rain.RealTime == time;
            Require(rain.Pass.TryReadDrops(camera, frozen), "Read back paused GPU drops.");
            int freezeDifferences = 0;
            for (int index = 0; index < capacity; index++) if (first[index].Position != frozen[index].Position || first[index].Age != frozen[index].Age || first[index].SplashAge != frozen[index].SplashAge) freezeDifferences++;
            controller.SetPaused(false);
            Advance(camera, controller, 1, 0.01f);
            controller.SetPaused(true);
            Require(rain.Pass.TryReadDrops(camera, moved), "Read back advanced GPU drops.");
            float wind = Mathf.Clamp01(Mathf.Max(0.0f, controller.Snapshot.WindVelocity) * 0.001f);
            float factor = controller.EffectivePrecipitationConfig.DropSpeed * Mathf.Clamp(wind * 1.5f, 0.5f, 1.0f);
            int visible = 0;
            int movedCount = 0;
            int invalidSpeed = 0;
            float minSpeed = float.PositiveInfinity;
            float maxSpeed = 0.0f;
            Vector3 meanVelocity = Vector3.zero;
            var lines = new List<string> { "index,x,y,z,vx,vy,vz,age,splashX,splashY,splashZ,splashAge,size" };
            for (int index = 0; index < capacity; index++)
            {
                XRayRainDropState drop = moved[index];
                if (drop.Size <= 0.0f) continue;
                visible++;
                float speed = drop.Velocity.magnitude;
                minSpeed = Mathf.Min(minSpeed, speed);
                maxSpeed = Mathf.Max(maxSpeed, speed);
                meanVelocity += drop.Velocity;
                if (!float.IsFinite(speed) || speed < 40.0f * factor - 0.001f || speed > 80.0f * factor + 0.001f || drop.Velocity.y >= 0.0f) invalidSpeed++;
                if (drop.Position != frozen[index].Position) movedCount++;
                if (index < 128) lines.Add(FormattableString.Invariant($"{index},{drop.Position.x:R},{drop.Position.y:R},{drop.Position.z:R},{drop.Velocity.x:R},{drop.Velocity.y:R},{drop.Velocity.z:R},{drop.Age:R},{drop.SplashPosition.x:R},{drop.SplashPosition.y:R},{drop.SplashPosition.z:R},{drop.SplashAge:R},{drop.Size:R}"));
            }
            File.WriteAllLines(Path.Combine(output, "GpuDropReadback.csv"), lines);
            controller.SetPaused(false);
            controller.TickSimulation(2.0f);
            Advance(camera, controller, 1, 0.0f);
            controller.SetPaused(true);
            Require(rain.Pass.TryReadDrops(camera, moved), "Read the camera after a long rendering gap.");
            int phaseCount = 0;
            foreach (XRayRainDropState drop in moved) if (drop.Size > 0.0f && drop.Age > 0.0001f) phaseCount++;
            return new GpuRecord { capacity = capacity, active = visible, minimumSpeed = minSpeed, maximumSpeed = maxSpeed, expectedMinimumSpeed = factor * 40.0f, expectedMaximumSpeed = factor * 80.0f, meanVelocity = visible > 0 ? meanVelocity / visible : Vector3.zero, invalidSpeed = invalidSpeed, pausedDifferences = freezeDifferences, pausedClockUnchanged = pausedClockUnchanged, moved = movedCount, longGapRandomPhase = phaseCount, passed = visible > 0 && invalidSpeed == 0 && freezeDifferences == 0 && pausedClockUnchanged && movedCount > 0 && phaseCount > visible / 2 };
        }

        private static GeometryRecord InspectGeometry(XRayPrecipitationGeometry geometry, Vector3 reference, string output)
        {
            RenderTexture previous = RenderTexture.active;
            var pixels = new Texture2D(geometry.Heights.width, geometry.Heights.height, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = geometry.Heights;
                pixels.ReadPixels(new Rect(0, 0, pixels.width, pixels.height), 0, 0);
                pixels.Apply(false, false);
                var result = new GeometryRecord { drawCount = geometry.GeometryCount, width = pixels.width, mapping = geometry.WorldMapping, bounds = geometry.Bounds, captures = geometry.CaptureCount, bytes = geometry.EstimatedBytes };
                Color[] heights = pixels.GetPixels();
                foreach (Color value in heights)
                {
                    if (value.r < value.g) continue;
                    result.validPixels++;
                    if (value.r - value.g > 2.0f) result.coveredPixels++;
                }
                float coveredDistance = float.PositiveInfinity;
                for (int z = 2; z < pixels.height - 2; z++)
                {
                    for (int x = 2; x < pixels.width - 2; x++)
                    {
                        Color value = heights[z * pixels.width + x];
                        if (value.r - value.g < 2.5f || value.r - value.g > 6.0f) continue;
                        bool coherent = true;
                        for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++) if (Mathf.Abs(heights[(z + dz) * pixels.width + x + dx].r - value.r) > 0.5f || Mathf.Abs(heights[(z + dz) * pixels.width + x + dx].g - value.g) > 0.5f) coherent = false;
                        if (!coherent) continue;
                        Vector3 point = WorldPoint(x, z, pixels.width, geometry.WorldMapping, value.g + 1.6f);
                        float distance = (point - reference).sqrMagnitude;
                        if (distance >= coveredDistance) continue;
                        coveredDistance = distance;
                        result.hasCoveredPose = true;
                        result.coveredPose = point;
                        result.roofRange = new Vector2(value.g, value.r);
                    }
                }
                float exposedDistance = float.PositiveInfinity;
                if (result.hasCoveredPose)
                {
                    for (int z = 2; z < pixels.height - 2; z++)
                    {
                        for (int x = 2; x < pixels.width - 2; x++)
                        {
                            Color value = heights[z * pixels.width + x];
                            if (value.r < value.g || value.r - value.g > 0.15f) continue;
                            bool coherent = true;
                            for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++) if (Mathf.Abs(heights[(z + dz) * pixels.width + x + dx].r - value.r) > 0.3f || heights[(z + dz) * pixels.width + x + dx].r - heights[(z + dz) * pixels.width + x + dx].g > 0.15f) coherent = false;
                            if (!coherent) continue;
                            Vector3 point = WorldPoint(x, z, pixels.width, geometry.WorldMapping, value.r + 1.6f);
                            float distance = (point - result.coveredPose).sqrMagnitude;
                            if (distance < 64.0f || distance >= exposedDistance) continue;
                            exposedDistance = distance;
                            result.exposedPose = point;
                        }
                    }
                }
                File.WriteAllBytes(Path.Combine(output, "GeometryHeights.exr"), pixels.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                CoreUtils.Destroy(pixels);
            }
        }

        private static Vector3 WorldPoint(int x, int z, int resolution, Vector4 mapping, float y)
        {
            return new Vector3(mapping.x + (x + 0.5f) / resolution / mapping.z, y, mapping.y + (z + 0.5f) / resolution / mapping.w);
        }

        private static RoofGpuRecord InspectRoofDrops(Camera camera, XRayWeatherController controller)
        {
            XRayPrecipitationSystem system = controller.Precipitation;
            XRayPrecipitationGeometry geometry = system.Geometry;
            int capacity = system.Pass.CapacityForCamera(camera);
            var drops = new XRayRainDropState[capacity];
            Require(system.Pass.TryReadDrops(camera, drops), "Read the actual sheltered camera GPU buffer.");
            RenderTexture previous = RenderTexture.active;
            var texture = new Texture2D(geometry.Heights.width, geometry.Heights.height, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = geometry.Heights;
                texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                texture.Apply(false, false);
                Color[] heights = texture.GetPixels();
                Vector4 mapping = geometry.WorldMapping;
                var result = new RoofGpuRecord();
                foreach (XRayRainDropState drop in drops)
                {
                    if (drop.Size <= 0.0f) continue;
                    int x = Mathf.FloorToInt((drop.Position.x - mapping.x) * mapping.z * texture.width);
                    int z = Mathf.FloorToInt((drop.Position.z - mapping.y) * mapping.w * texture.height);
                    if (x < 0 || z < 0 || x >= texture.width || z >= texture.height) continue;
                    Color range = heights[z * texture.width + x];
                    if (range.r < range.g) continue;
                    result.sampledDrops++;
                    if (drop.Position.y < range.r - 0.02f) result.belowSurface++;
                    if (range.r - range.g > 2.0f)
                    {
                        result.coveredColumnDrops++;
                        if (drop.Position.y < range.r - 0.02f && drop.Position.y > range.g + 0.02f) result.insideShelteredLayer++;
                    }
                    if (drop.SplashAge < 0.3f) result.liveSplashes++;
                }
                result.passed = result.sampledDrops > 0 && result.coveredColumnDrops > 0 && result.belowSurface == 0 && result.insideShelteredLayer == 0;
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                CoreUtils.Destroy(texture);
            }
        }

        private static XRayWeatherProfile FindRain(XRayWeatherCatalog catalog, bool night, out double seconds)
        {
            XRayWeatherProfile found = null;
            float density = -1.0f;
            double bestDistance = double.PositiveInfinity;
            seconds = 0.0;
            foreach (XRayWeatherProfile profile in catalog.Profiles)
            {
                for (int index = 0; index < profile.KeyframeCount; index++)
                {
                    XRayWeatherSnapshot frame = profile.GetKeyframe(index);
                    bool isNight = frame.GameSeconds < 18000.0 || frame.GameSeconds >= 75600.0;
                    double distance = Math.Abs(frame.GameSeconds - (night ? 0.0 : 43200.0));
                    if (isNight != night || frame.RainDensity <= 0.0f || distance > bestDistance || distance == bestDistance && frame.RainDensity <= density) continue;
                    bestDistance = distance;
                    density = frame.RainDensity;
                    seconds = frame.GameSeconds;
                    found = profile;
                }
            }
            Require(found != null, "The source catalog contains an applicable rainy key.");
            return found;
        }

        private void Awake()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index + 1 < arguments.Length; index++) if (arguments[index] == "-precipitationEvidence") _output = arguments[index + 1];
            if (string.IsNullOrEmpty(_output))
            {
                enabled = false;
                return;
            }
            if (_instance != null && _instance != this)
            {
                _instance.Configure(_camera, _controller, _sourceHash);
                Destroy(gameObject);
                return;
            }
            GuardOutput(_output);
            if (Directory.Exists(_output)) throw new IOException("Use a new Player evidence directory.");
            Directory.CreateDirectory(_output);
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private IEnumerator Start()
        {
            if (_instance != this || string.IsNullOrEmpty(_output)) yield break;
            Application.logMessageReceived += ObserveLog;
            File.WriteAllText(Path.Combine(_output, "System.json"), JsonUtility.ToJson(new SystemRecord { unity = Application.unityVersion, api = SystemInfo.graphicsDeviceType.ToString(), gpu = SystemInfo.graphicsDeviceName, sourceHash = _sourceHash }, true));
            for (int index = 0; index < 8; index++) yield return null;
            yield return RunStage("Initial");
            bool initial = _stagePassed;
            string scene = _controller.gameObject.scene.name;
            yield return SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            for (int index = 0; index < 8; index++) yield return null;
            yield return RunStage("Reload");
            bool passed = initial && _stagePassed && _errors.Count == 0;
            File.WriteAllText(Path.Combine(_output, "Result.json"), JsonUtility.ToJson(new PlayerRecord { passed = passed, initial = initial, reload = _stagePassed, errors = _errors.ToArray() }, true));
            Application.logMessageReceived -= ObserveLog;
            Application.Quit(passed ? 0 : 1);
        }

        private IEnumerator RunStage(string stage)
        {
            _stagePassed = false;
            string output = Path.Combine(_output, stage);
            Directory.CreateDirectory(output);
            string before = JsonUtility.ToJson(GameConfig.Load()) + JsonUtility.ToJson(GameConfig.Load().Precipitation);
            try
            {
                string result = RunRainCases(_camera, _controller, output);
                _stagePassed = JsonUtility.FromJson<RainResult>(File.ReadAllText(result)).passed;
                Require(before == JsonUtility.ToJson(GameConfig.Load()) + JsonUtility.ToJson(GameConfig.Load().Precipitation), "Shared configuration remains read-only in Player.");
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(output, "StageFailure.txt"), exception.ToString());
                _stagePassed = false;
            }
            XRayPrecipitationSystem system = _controller.Precipitation;
            _controller.enabled = false;
            yield return null;
            bool released = system != null && !system.Enabled && system.Pass.ViewCount == 0 && system.Pass.EstimatedBytes == 0;
            _controller.enabled = true;
            yield return null;
            _stagePassed &= released && _controller.Precipitation.Enabled;
            File.WriteAllText(Path.Combine(output, "Lifecycle.json"), JsonUtility.ToJson(new LifecycleRecord { passed = _stagePassed, released = released, reenabled = _controller.Precipitation.Enabled, configurationReadOnly = before == JsonUtility.ToJson(GameConfig.Load()) + JsonUtility.ToJson(GameConfig.Load().Precipitation) }, true));
        }

        private void ObserveLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) _errors.Add(message + "\n" + stack);
        }

        private static void GuardOutput(string output)
        {
            if (!Path.GetFullPath(output).StartsWith(ARCHIVE, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Precipitation diagnostics require an Archive directory.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        [Serializable]
        public sealed class RainResult
        {
            public bool passed;
            public string api;
            public int width;
            public int height;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
            public RainCase[] cases;
            public GpuRecord gpu;
            public GeometryRecord geometry;
            public RoofGpuRecord roof;
            public string error;
        }

        [Serializable]
        public sealed class RainCase
        {
            public string name;
            public bool passed;
            public bool expectVisible;
            public string profile;
            public double seconds;
            public float density;
            public Color rainColor;
            public Color sunColor;
            public float wind;
            public float windDirection;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
            public double noiseMean;
            public int noiseChangedPixels;
            public float noiseMaximum;
            public double rainMean;
            public int rainChangedPixels;
            public float rainMaximum;
            public int dropCount;
            public int renderCount;
            public int magentaBefore;
            public int magentaAfter;
        }

        [Serializable]
        public sealed class GpuRecord
        {
            public bool passed;
            public int capacity;
            public int active;
            public float minimumSpeed;
            public float maximumSpeed;
            public float expectedMinimumSpeed;
            public float expectedMaximumSpeed;
            public Vector3 meanVelocity;
            public int invalidSpeed;
            public int pausedDifferences;
            public bool pausedClockUnchanged;
            public int moved;
            public int longGapRandomPhase;
        }

        [Serializable]
        public sealed class GeometryRecord
        {
            public int drawCount;
            public int width;
            public Vector4 mapping;
            public Bounds bounds;
            public int captures;
            public long bytes;
            public int validPixels;
            public int coveredPixels;
            public bool hasCoveredPose;
            public Vector3 coveredPose;
            public Vector3 exposedPose;
            public Vector2 roofRange;
        }

        [Serializable]
        public sealed class RoofGpuRecord
        {
            public bool passed;
            public int sampledDrops;
            public int coveredColumnDrops;
            public int belowSurface;
            public int insideShelteredLayer;
            public int liveSplashes;
        }

        [Serializable]
        private sealed class SystemRecord
        {
            public string unity;
            public string api;
            public string gpu;
            public string sourceHash;
        }

        [Serializable]
        private sealed class PlayerRecord
        {
            public bool passed;
            public bool initial;
            public bool reload;
            public string[] errors;
        }

        [Serializable]
        private sealed class LifecycleRecord
        {
            public bool passed;
            public bool released;
            public bool reenabled;
            public bool configurationReadOnly;
        }
    }
}

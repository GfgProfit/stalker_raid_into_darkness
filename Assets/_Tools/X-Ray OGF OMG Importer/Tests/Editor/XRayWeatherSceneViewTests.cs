using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class XRayWeatherSceneViewTests
    {
        private XRayWeatherSceneFixture _skyFixture;
        private NativeCapture _skyCapture;
        private AOVRequestDataCollection _skyRequests;
        private RTHandle _skyRaw;
        private RTHandle _skyOutput;

        [SetUp]
        public void RequireSceneViewFixture()
        {
            if (Application.isBatchMode)
            {
                Assert.Ignore("Native Scene View validation requires a GUI Editor.");
            }
            if (AssetDatabase.LoadAssetAtPath<XRayInstanceBatchSet>(XRayWeatherSceneFixture.BATCH_PATH) == null)
            {
                Assert.Ignore("Native Scene View validation requires the imported Anomaly escape vegetation.");
            }
        }

        [TearDown]
        public void RestoreSkyCaptureResources()
        {
            if (_skyCapture != null)
            {
                _skyCapture.Dispose();
                _skyCapture = null;
            }
            if (_skyFixture != null)
            {
                _skyFixture.Dispose();
                _skyFixture = null;
            }
            _skyRequests?.Dispose();
            _skyRequests = null;
            if (_skyRaw != null) RTHandles.Release(_skyRaw);
            if (_skyOutput != null) RTHandles.Release(_skyOutput);
            _skyRaw = null;
            _skyOutput = null;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator SerializedSkyCyclesRenderInGameAndNativeSceneView()
        {
            HdrpTestWarmup.Ensure();
            GameConfig config = GameConfig.Load();
            var profiles = config != null ? config.OrdinaryWeatherCatalog?.Profiles : null;
            XRayWeatherProfile clear = profiles?.FirstOrDefault(profile => profile != null && profile.CycleName == "w_clear1");
            XRayWeatherProfile rain = profiles?.FirstOrDefault(profile => profile != null && profile.CycleName == "w_rain1");
            if (clear == null || rain == null)
            {
                Assert.Ignore("This integration test requires the prepared clear and rain weather profiles.");
            }

            RTHandle gameRaw = null;
            RTHandle gameOutput = null;
            AOVRequestDataCollection requests = null;
            try
            {
                _skyFixture = new XRayWeatherSceneFixture(clear);
                _skyCapture = new NativeCapture(_skyFixture, true);
                var fixture = _skyFixture;
                var native = _skyCapture;
                {
                    fixture.Root.GetComponentInChildren<MeshRenderer>().enabled = false;
                    fixture.SourceMaterial.SetFloat("_MonolithDebug", 1.0f);
                    Volume volume = fixture.Root.GetComponent<Volume>();
                    volume.sharedProfile.TryGet(out Exposure exposure);
                    exposure.fixedExposure.Override(0.0f);
                    XRayWeatherSkyRenderer sky = fixture.Root.AddComponent<XRayWeatherSkyRenderer>();
                    sky.Configure(volume, Shader.Find("XRay/Monolith Weather Sky"));
                    fixture.Controller.ConfigureSky(sky);
                    native.ConfigureSkyView();
                    yield return native.MatchPairedViewport();
                    Assert.IsTrue(fixture.Controller.BeginPreview(), fixture.Controller.Diagnostic);
                    fixture.Controller.SetTimeScale(0.0f);
                    fixture.Controller.SetPaused(true);
                    Camera game = fixture.Camera;
                    HDAdditionalCameraData data = game.GetComponent<HDAdditionalCameraData>();
                    native.Data.CopyTo(data);
                    data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;
                    game.allowHDR = true;
                    _skyRaw = gameRaw = RTHandles.Alloc(640, 640, colorFormat: GraphicsFormat.R32G32B32A32_SFloat, name: "Weather sky Game raw");
                    _skyOutput = gameOutput = RTHandles.Alloc(640, 640, colorFormat: GraphicsFormat.R32G32B32A32_SFloat, name: "Weather sky Game output");
                    var request = AOVRequest.NewDefault();
                    request.SetOverrideRenderFormat(true);
                    var builder = new AOVRequestBuilder();
                    int callbacks = 0;
                    builder.Add(request, buffer => buffer == AOVBuffers.Color ? gameRaw : gameOutput, null, new[] { AOVBuffers.Color, AOVBuffers.Output }, (command, textures, properties) => callbacks++);
                    _skyRequests = requests = builder.Build();
                    data.SetAOVRequests(requests);
                    Color[] first = null;
                    for (int sample = 0; sample < 15; sample++)
                    {
                        if (sample < 12)
                        {
                            XRayWeatherProfile profile = sample < 6 ? clear : rain;
                            fixture.Controller.SetProfile(profile);
                            double[] hours = { 0.0, 6.0, 12.0, 18.0, 23.9, 24.1 };
                            fixture.Controller.Seek(hours[sample % 6] * 3600.0);
                        }
                        else
                        {
                            if (sample == 12)
                            {
                                fixture.Controller.SetProfile(clear);
                                fixture.Controller.Seek(59400.0);
                                Assert.IsTrue(fixture.Controller.TransitionTo(rain, 1.0f));
                            }
                            else
                            {
                                fixture.Controller.SetPaused(false);
                                fixture.Controller.TickPreview(0.5f);
                                fixture.Controller.SetPaused(true);
                            }
                        }
                        Color[] scene = null;
                        yield return native.Frame(colors => scene = colors, 0.0f);
                        Color[] sceneRaw = ReadPairedFloat(native.ColorTexture);
                        MatchPairedPose(native.Camera, game);
                        int before = callbacks;
                        game.Render();
                        game.Render();
                        Assert.Greater(callbacks, before);
                        Color[] gameColors = ReadPairedFloat(gameOutput.rt);
                        Color[] gameRawColors = ReadPairedFloat(gameRaw.rt);
                        double spread = 0.0;
                        for (int i = 0; i < gameColors.Length; i++)
                        {
                            for (int channel = 0; channel < 3; channel++)
                            {
                                Assert.IsFalse(float.IsNaN(gameColors[i][channel]) || float.IsInfinity(gameColors[i][channel]));
                                Assert.IsFalse(float.IsNaN(scene[i][channel]) || float.IsInfinity(scene[i][channel]));
                                float rawDifference = Mathf.Abs(gameRawColors[i][channel] - sceneRaw[i][channel]);
                                Assert.IsFalse(float.IsNaN(rawDifference) || float.IsInfinity(rawDifference));
                                spread += Mathf.Abs(gameColors[i][channel] - gameColors[0][channel]);
                            }
                        }
                        Assert.Greater(spread, 0.01, "The serialized sky must produce visible nonflat pixels.");
                        if (sample == 0) first = gameColors;
                        if (sample == 2)
                        {
                            double change = 0.0;
                            for (int i = 0; i < gameColors.Length; i++) change += Mathf.Abs(gameColors[i].r - first[i].r);
                            Assert.Greater(change, 10.0, "Changing weather time must change sky pixels.");
                        }
                        XRayWeatherSnapshot snapshot = fixture.Controller.Snapshot;
                        if (sample == 13)
                        {
                            Assert.AreEqual(4, Enumerable.Count(new[] { 0, 1, 2, 3 }, index => snapshot.SkyResources.Weights[index] > 0.0f));
                        }
                        Assert.IsTrue(fixture.Root.GetComponentInChildren<MonolithWeatherInputs>().EnvironmentActive);
                    }
                    data.SetAOVRequests(null);
                    fixture.Controller.EndPreview();
                    Assert.IsFalse(sky.HasWeather);
                    Assert.IsFalse(volume.HasInstantiatedProfile());
                    Assert.AreEqual(0.0f, Shader.GetGlobalFloat("_MonolithWeatherEnvironmentActive"));
                }
            }
            finally
            {
                RestoreSkyCaptureResources();
            }
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator NativeSceneViewShowsImportedWeatherAndWindWithFixedCamera()
        {
            HdrpTestWarmup.Ensure();
            XRayWeatherProfile clear = XRayWeatherImporter.ImportCurrent("w_clear1");
            XRayWeatherProfile rain = XRayWeatherImporter.ImportCurrent("w_rain1");
            try
            {
                using (var fixture = new XRayWeatherSceneFixture(clear))
                using (var capture = new NativeCapture(fixture))
                {
                    XRayWeatherController controller = fixture.Controller;
                    controller.BeginPreview();
                    Assert.IsTrue(controller.PreviewActive, controller.Diagnostic);
                    controller.SetTimeScale(0.0f);
                    foreach (double seconds in new[] { 25200.0, 46800.0, 72000.0 })
                    {
                        controller.Seek(seconds);
                        Color[] pixels = null;
                        double time = controller.GameTimeSeconds;
                        int ticks = fixture.Wind.State.Ticks;
                        yield return capture.Frame(value => pixels = value);
                        Assert.AreEqual(time, controller.GameTimeSeconds);
                        Assert.AreEqual(ticks, fixture.Wind.State.Ticks, "Native Scene View camera repaints must not tick weather or wind.");
                        Assert.IsNotNull(pixels);
                    }
                    controller.Seek(46800.0);
                    Color[] clearPixels = null;
                    yield return capture.Frame(value => clearPixels = value);
                    controller.SetProfile(rain);
                    Color[] rainPixels = null;
                    yield return capture.Frame(value => rainPixels = value);
                    Assert.Greater(Difference(clearPixels, rainPixels), 0.00001f, "Confirmed clear/rain source light inputs must visibly change native Scene View pixels at fixed exposure.");
                    controller.SetProfile(clear);
                    fixture.SourceMaterial.SetFloat("_MonolithDebug", 34.0f);
                    controller.TickPreview(0.1f);
                    Color[] wind0 = null;
                    yield return capture.Frame(value => wind0 = value);
                    for (int tick = 0; tick < 20; tick++) controller.TickPreview(0.1f);
                    Color[] wind1 = null;
                    yield return capture.Frame(value => wind1 = value);
                    Assert.Greater(Difference(wind0, wind1), 0.00001f, "Imported tree geometry must visibly move under source weather wind during native repaints.");
                    fixture.SourceMaterial.SetFloat("_MonolithDebug", 0.0f);
                    controller.TransitionTo(rain, 2.0f);
                    for (int step = 0; step < 3; step++)
                    {
                        if (step > 0) controller.TickPreview(1.0f);
                        Color[] pixels = null;
                        yield return capture.Frame(value => pixels = value);
                        Assert.IsNotNull(pixels);
                    }
                    Assert.AreSame(rain, controller.ActiveProfile);
                    Assert.IsFalse(controller.IsTransitioning);
                    controller.EndPreview();
                    Assert.IsFalse(controller.PreviewActive);
                }
            }
            finally
            {
                Object.DestroyImmediate(clear);
                Object.DestroyImmediate(rain);
            }
        }


        private static void MatchPairedPose(Camera source, Camera destination)
        {
            destination.orthographic = source.orthographic;
            destination.orthographicSize = source.orthographicSize;
            destination.nearClipPlane = source.nearClipPlane;
            destination.farClipPlane = source.farClipPlane;
            destination.aspect = source.aspect;
            destination.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            destination.worldToCameraMatrix = source.worldToCameraMatrix;
            destination.projectionMatrix = source.projectionMatrix;
        }

        private static Color[] ReadPairedFloat(RenderTexture texture)
        {
            Assert.IsNotNull(texture);
            Assert.AreEqual(640, texture.width);
            Assert.AreEqual(640, texture.height);
            var pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGBAFloat, false, true);
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                pixels.Apply(false, false);
                Color[] colors = pixels.GetPixels();
                return colors;
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(pixels);
            }
        }

        private static float Difference(Color[] before, Color[] after)
        {
            float maximum = 0.0f;
            for (int i = 0; i < before.Length; i++) maximum = Mathf.Max(maximum, Mathf.Abs(before[i].r - after[i].r), Mathf.Abs(before[i].g - after[i].g), Mathf.Abs(before[i].b - after[i].b));
            return maximum;
        }

        private sealed class NativeCapture : IDisposable
        {
            private const int SIZE = 640;

            private readonly WeatherSceneView _view;
            private readonly Camera _camera;
            private readonly HDAdditionalCameraData _data;
            private readonly Material _material;
            private readonly Volume _volume;
            private readonly Texture2D _pixels;
            private readonly AOVRequestDataCollection _requests;
            private readonly RTHandle _color;
            private readonly RTHandle _rawColor;
            private readonly ulong _sceneCullingMask;
            private RTHandle _source;
            private RTHandle _rawSource;
            private int _version;

            public Camera Camera => _camera;
            public HDAdditionalCameraData Data => _data;
            public RenderTexture ColorTexture => _rawSource.rt;

            public NativeCapture(XRayWeatherSceneFixture fixture, bool pairedCapture = false)
            {
                _view = ScriptableObject.CreateInstance<WeatherSceneView>();
                _view.position = new Rect(100.0f, 100.0f, 680.0f, 680.0f);
                _view.ShowUtility();
                _view.Bind(fixture.Scene);
                _view.cameraMode = SceneView.GetBuiltinCameraMode(DrawCameraMode.Textured);
                _material = fixture.SourceMaterial;
                _volume = fixture.Root.GetComponent<Volume>();
                Assert.IsNotNull(_volume);
                _volume.enabled = false;
                foreach (Transform transform in fixture.Root.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = 31;
                _volume.enabled = true;
                _camera = _view.camera;
                _camera.transform.SetParent(fixture.Root.transform);
                _camera.scene = fixture.Scene;
                _camera.cullingMask = 1 << 31;
                _sceneCullingMask = UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(fixture.Scene);
                Assert.AreNotEqual(0ul, _sceneCullingMask);
                _camera.overrideSceneCullingMask = _sceneCullingMask;
                _camera.enabled = false;
                _camera.orthographic = true;
                _camera.nearClipPlane = fixture.Camera.nearClipPlane;
                _camera.farClipPlane = fixture.Camera.farClipPlane;
                _camera.allowHDR = true;
                _camera.useOcclusionCulling = false;
                _data = _camera.GetComponent<HDAdditionalCameraData>();
                if (_data == null) _data = _camera.gameObject.AddComponent<HDAdditionalCameraData>();
                _data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                _data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
                _data.backgroundColorHDR = new Color(0.02f, 0.03f, 0.04f, 1.0f);
                _data.customRenderingSettings = true;
                _data.volumeLayerMask = 1 << 31;
                foreach (FrameSettingsField field in new[] { FrameSettingsField.SSR, FrameSettingsField.SSAO, FrameSettingsField.SSGI, FrameSettingsField.ContactShadows, FrameSettingsField.Volumetrics, FrameSettingsField.Bloom })
                {
                    _data.renderingPathCustomFrameSettings.SetEnabled(field, false);
                    _data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
                }
                foreach (FrameSettingsField field in new[] { FrameSettingsField.Postprocess, FrameSettingsField.ExposureControl })
                {
                    _data.renderingPathCustomFrameSettings.SetEnabled(field, true);
                    _data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
                }
                Bounds bounds = fixture.Root.GetComponentInChildren<MeshFilter>().sharedMesh.bounds;
                _view.orthographic = true;
                _view.LookAtDirect(bounds.center, Quaternion.identity, fixture.Camera.orthographicSize);
                _color = RTHandles.Alloc(SIZE, SIZE, colorFormat: GraphicsFormat.R32G32B32A32_SFloat, name: "Weather Scene View output");
                if (pairedCapture) _rawColor = RTHandles.Alloc(SIZE, SIZE, colorFormat: GraphicsFormat.R32G32B32A32_SFloat, name: "Weather Scene View raw");
                _pixels = new Texture2D(SIZE, SIZE, TextureFormat.RGBAFloat, false, true);
                var request = AOVRequest.NewDefault();
                request.SetOverrideRenderFormat(pairedCapture);
                var builder = new AOVRequestBuilder();
                builder.Add(request, buffer => buffer == AOVBuffers.Color ? _rawColor : _color, null, pairedCapture ? new[] { AOVBuffers.Color, AOVBuffers.Output } : new[] { AOVBuffers.Output }, (command, textures, properties) =>
                {
                    if (pairedCapture) _rawSource = textures[0];
                    _source = textures[pairedCapture ? 1 : 0];
                    _version++;
                });
                _requests = builder.Build();
                _data.SetAOVRequests(_requests);
            }

            public IEnumerator MatchPairedViewport()
            {
                _view.Focus();
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    _view.Repaint();
                    yield return null;
                    if (_camera.pixelWidth == SIZE && _camera.pixelHeight == SIZE) break;
                    Rect position = _view.position;
                    float scale = EditorGUIUtility.pixelsPerPoint;
                    position.width += (SIZE - _camera.pixelWidth) / scale;
                    position.height += (SIZE - _camera.pixelHeight) / scale;
                    _view.position = position;
                }
                _view.Repaint();
                yield return null;
                Assert.AreEqual(SIZE, _camera.pixelWidth);
                Assert.AreEqual(SIZE, _camera.pixelHeight);
            }

            public void ConfigureSkyView()
            {
                _view.orthographic = false;
                _view.LookAtDirect(Vector3.zero, Quaternion.Euler(-15.0f, 0.0f, 0.0f), 10.0f);
                _data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;
            }

            public IEnumerator Frame(Action<Color[]> receive, float fixedExposure = XRayWeatherSceneFixture.FIXED_EXPOSURE_EV100)
            {
                int version = _version;
                double limit = EditorApplication.timeSinceStartup + 15.0;
                while (_version < version + 3 && EditorApplication.timeSinceStartup < limit)
                {
                    _view.Repaint();
                    yield return null;
                }
                Assert.GreaterOrEqual(_version, version + 3, "Actual GUI Scene View repaints must produce three settled HDRP AOV callbacks without Camera.Render.");
                Assert.IsNotNull(_source);
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = _source.rt;
                    _pixels.ReadPixels(new Rect(0, 0, SIZE, SIZE), 0, 0);
                    _pixels.Apply(false, false);
                }
                finally
                {
                    RenderTexture.active = previous;
                }
                Color[] colors = _pixels.GetPixels();
                string statistics = XRayWeatherSceneFixture.ColorImageStatistics(colors, out bool readable);
                HDCamera hdCamera = HDCamera.GetOrCreate(_camera);
                FrameSettings actual = hdCamera.frameSettings;
                Exposure exposure = hdCamera.volumeStack.GetComponent<Exposure>();
                var overrideField = typeof(HDAdditionalCameraData).GetField("doesSceneViewOverrideExposure", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(exposure);
                Assert.IsNotNull(overrideField);
                bool sceneOverride = (bool)overrideField.GetValue(_data);
                bool volumeRegistered = Array.IndexOf(VolumeManager.instance.GetVolumes(_data.volumeLayerMask), _volume) >= 0;
                bool volumeVisible = UnityEditor.SceneManagement.StageUtility.IsGameObjectRenderedByCamera(_volume.gameObject, _camera);
                Assert.IsTrue(volumeRegistered && volumeVisible, "The task-owned native fixture Volume must be registered and visible to its Scene View camera.");
                Assert.AreEqual(_sceneCullingMask, _camera.overrideSceneCullingMask, "Native GUI repaints must preserve the task-owned preview scene culling mask.");
                Assert.IsFalse(sceneOverride, "A Scene View exposure override would invalidate the shared fixed-volume exposure contract.");
                Assert.AreEqual(ExposureMode.Fixed, exposure.mode.value);
                Assert.That(exposure.fixedExposure.value, Is.EqualTo(fixedExposure).Within(0.0001f));
                Assert.That(exposure.compensation.value, Is.EqualTo(0.0f).Within(0.000001f));
                Assert.IsTrue(actual.IsEnabled(FrameSettingsField.ExposureControl));
                Assert.IsFalse(actual.IsEnabled(FrameSettingsField.Bloom));
                if (_material.GetFloat("_MonolithDebug") < 0.5f) Assert.IsTrue(readable, "Native weather capture must contain nonflat visible foliage with fewer than 90% surface pixels at or above the 0.99 display-white threshold: " + statistics);
                receive(colors);
            }

            public void Dispose()
            {
                if (_data != null) _data.SetAOVRequests(null);
                _requests.Dispose();
                if (RenderTexture.active == _color.rt) RenderTexture.active = null;
                RTHandles.Release(_color);
                if (_rawColor != null) RTHandles.Release(_rawColor);
                Object.DestroyImmediate(_pixels);
                if (_view != null) _view.Close();
            }
        }

        private sealed class WeatherSceneView : SceneView
        {
            public void Bind(Scene scene)
            {
                customScene = scene;
                drawGizmos = false;
                showGrid = false;
                sceneLighting = true;
                sceneViewState.fxEnabled = true;
                sceneViewState.alwaysRefresh = true;
            }
        }
    }
}

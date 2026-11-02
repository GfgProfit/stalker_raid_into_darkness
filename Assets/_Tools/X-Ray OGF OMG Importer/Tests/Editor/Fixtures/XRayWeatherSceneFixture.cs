using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayWeatherSceneFixture : IDisposable
    {
        public const float FIXED_EXPOSURE_EV100 = 6.0f;
        public const float DIAGNOSTIC_SUN_LUX = 3.0f;

        private const string BATCH_PATH = "Assets/_Source/Levels/Anomaly/1v2_DX11_v_12gb_2.5_168_662acc8d/l01_escape/Meshes/l01_escape_StaticProps.asset";
        private const int SIZE = 640;

        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private readonly Scene _scene;
        private readonly GameObject _root;
        private readonly Camera _camera;
        private readonly Material _material;
        private readonly RenderTexture _target;
        private readonly Texture2D _pixels;

        public XRayWeatherController Controller { get; }
        public XRayFloraWindProvider Wind { get; }
        public GameObject Root => _root;
        public Camera Camera => _camera;
        public Scene Scene => _scene;
        public Material SourceMaterial => _material;

        public XRayWeatherSceneFixture(XRayWeatherProfile profile)
        {
            _scene = EditorSceneManager.NewPreviewScene();
            _root = Own(new GameObject("Phase07A weather fixture"));
            SceneManager.MoveGameObjectToScene(_root, _scene);
            XRayInstanceBatchSet set = AssetDatabase.LoadAssetAtPath<XRayInstanceBatchSet>(BATCH_PATH);
            if (set == null) throw new InvalidOperationException("The existing imported Anomaly escape vegetation is required.");
            XRayInstanceBatch selected = null;
            foreach (XRayInstanceBatch batch in set.Batches)
            {
                if (batch.Mesh == null || batch.Materials.Length == 0 || batch.Materials[0] == null || !batch.Materials[0].name.Contains("tree_leaves")) continue;
                if (selected == null || batch.Mesh.vertexCount > selected.Mesh.vertexCount) selected = batch;
            }
            if (selected == null) throw new InvalidOperationException("A real imported tree-leaf batch is required.");
            _material = Own(new Material(selected.Materials[0]));
            var tree = new GameObject("Transient imported tree leaves");
            tree.transform.SetParent(_root.transform, false);
            tree.AddComponent<MeshFilter>().sharedMesh = selected.Mesh;
            tree.AddComponent<MeshRenderer>().sharedMaterial = _material;
            Bounds bounds = selected.Mesh.bounds;
            var cameraObject = new GameObject("Fixed weather camera");
            cameraObject.transform.SetParent(_root.transform, false);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.scene = _scene;
            _camera.enabled = false;
            _camera.orthographic = true;
            _camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.25f + 0.5f;
            _camera.transform.SetPositionAndRotation(bounds.center + Vector3.back * (bounds.extents.z + 40.0f), Quaternion.identity);
            _camera.farClipPlane = bounds.size.z + 100.0f;
            _camera.nearClipPlane = 0.1f;
            _camera.useOcclusionCulling = false;
            _camera.allowHDR = true;
            HDAdditionalCameraData data = cameraObject.AddComponent<HDAdditionalCameraData>();
            data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            data.backgroundColorHDR = new Color(0.02f, 0.03f, 0.04f, 1.0f);
            data.customRenderingSettings = true;
            data.volumeLayerMask = ~0;
            foreach (FrameSettingsField field in new[] { FrameSettingsField.SSR, FrameSettingsField.SSAO, FrameSettingsField.SSGI, FrameSettingsField.ContactShadows, FrameSettingsField.Volumetrics })
            {
                data.renderingPathCustomFrameSettings.SetEnabled(field, false);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
            }
            foreach (FrameSettingsField field in new[] { FrameSettingsField.Postprocess, FrameSettingsField.ExposureControl })
            {
                data.renderingPathCustomFrameSettings.SetEnabled(field, true);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
            }
            var volumeProfile = Own(ScriptableObject.CreateInstance<VolumeProfile>());
            volumeProfile.Add<Fog>(true).enabled.Override(false);
            Exposure exposure = volumeProfile.Add<Exposure>(true);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(FIXED_EXPOSURE_EV100);
            volumeProfile.Add<Tonemapping>(true).mode.Override(TonemappingMode.None);
            Volume volume = _root.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10000.0f;
            volume.sharedProfile = volumeProfile;
            _target = Own(new RenderTexture(SIZE, SIZE, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            _target.Create();
            _camera.targetTexture = _target;
            _pixels = Own(new Texture2D(SIZE, SIZE, TextureFormat.RGBAFloat, false, true));
            XRayLevelSceneBuilder.ConfigureWeather(_root, profile);
            Light sun = _root.GetComponentInChildren<Light>();
            HDAdditionalLightData lightData = sun.GetComponent<HDAdditionalLightData>();
            if (lightData == null) lightData = sun.gameObject.AddComponent<HDAdditionalLightData>();
            lightData.SetIntensity(DIAGNOSTIC_SUN_LUX, LightUnit.Lux);
            Controller = _root.GetComponentInChildren<XRayWeatherController>();
            Wind = _root.GetComponentInChildren<XRayFloraWindProvider>();
        }

        public Color[] Capture(string path, float debug = 0.0f)
        {
            float original = _material.GetFloat("_MonolithDebug");
            RenderTexture active = RenderTexture.active;
            try
            {
                _material.SetFloat("_MonolithDebug", debug);
                _camera.Render();
                _camera.Render();
                RenderTexture.active = _target;
                _pixels.ReadPixels(new Rect(0, 0, SIZE, SIZE), 0, 0);
                _pixels.Apply(false, false);
                Color[] captured = _pixels.GetPixels();
                string statistics = ColorImageStatistics(captured, out bool readable);
                if (path != null)
                {
                    File.WriteAllText(path + "_ImageMetrics.txt", statistics + "\nFixedExposureEV100=" + FIXED_EXPOSURE_EV100 + "\nDiagnosticSunLux=" + DIAGNOSTIC_SUN_LUX + "\nPostprocess=true\nExposureControl=true\nTonemapping=None\nCamera=" + _camera.transform.position.ToString("F8") + "\nRotation=" + _camera.transform.rotation.ToString("F8") + "\nProjection=" + _camera.projectionMatrix.ToString("F8"));
                    File.WriteAllBytes(path + ".exr", _pixels.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                    var png = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false, false);
                    try
                    {
                        Color[] colors = (Color[])captured.Clone();
                        for (int i = 0; i < colors.Length; i++) colors[i] = colors[i].gamma;
                        png.SetPixels(colors);
                        png.Apply();
                        File.WriteAllBytes(path + ".png", png.EncodeToPNG());
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(png);
                    }
                }
                if (debug < 0.5f && !readable) throw new InvalidOperationException("Weather color capture requires nonflat visible foliage and fewer than 90% surface pixels at or above the 0.99 display-white threshold: " + statistics);
                return captured;
            }
            finally
            {
                _material.SetFloat("_MonolithDebug", original);
                RenderTexture.active = active;
            }
        }

        public static string ColorImageStatistics(Color[] colors, out bool readable)
        {
            Color background = colors[0];
            int covered = 0;
            int white = 0;
            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;
            double sum = 0.0;
            double squares = 0.0;
            foreach (Color color in colors)
            {
                if (float.IsNaN(color.r) || float.IsNaN(color.g) || float.IsNaN(color.b) || float.IsInfinity(color.r) || float.IsInfinity(color.g) || float.IsInfinity(color.b)) throw new InvalidOperationException("Weather capture contains a non-finite RGB value.");
                float difference = Mathf.Max(Mathf.Abs(color.r - background.r), Mathf.Abs(color.g - background.g), Mathf.Abs(color.b - background.b));
                if (difference <= 0.00001f) continue;
                covered++;
                if (color.r >= 0.99f && color.g >= 0.99f && color.b >= 0.99f) white++;
                float luminance = color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
                minimum = Mathf.Min(minimum, luminance);
                maximum = Mathf.Max(maximum, luminance);
                sum += luminance;
                squares += luminance * luminance;
            }
            double mean = covered > 0 ? sum / covered : 0.0;
            double deviation = covered > 0 ? Math.Sqrt(Math.Max(0.0, squares / covered - mean * mean)) : 0.0;
            readable = covered > colors.Length * 0.005 && white < covered * 0.9 && maximum - minimum > 0.0000001f;
            return FormattableString.Invariant($"Pixels={colors.Length}\nCovered={covered}\nSurfaceAboveDisplayWhite={white}\nMinSurfaceLuminance={minimum:R}\nMaxSurfaceLuminance={maximum:R}\nMeanSurfaceLuminance={mean:R}\nSurfaceLuminanceStdDev={deviation:R}\nReadable={readable}");
        }

        public void RenderSecondCamera()
        {
            GameObject second = UnityEngine.Object.Instantiate(_camera.gameObject, _root.transform);
            try
            {
                Camera camera = second.GetComponent<Camera>();
                camera.enabled = false;
                camera.scene = _scene;
                camera.Render();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        public void Dispose()
        {
            if (Controller != null) Controller.EndPreview();
            if (_camera != null) _camera.targetTexture = null;
            if (_target != null) _target.Release();
            for (int i = _owned.Count - 1; i >= 0; i--) if (_owned[i] != null) UnityEngine.Object.DestroyImmediate(_owned[i]);
            if (_scene.IsValid() && _scene.isLoaded) EditorSceneManager.ClosePreviewScene(_scene);
        }

        private T Own<T>(T value) where T : UnityEngine.Object
        {
            _owned.Add(value);
            return value;
        }
    }
}

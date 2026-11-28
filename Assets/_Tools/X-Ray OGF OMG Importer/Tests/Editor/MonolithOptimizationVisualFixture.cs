using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    internal sealed class MonolithOptimizationVisualFixture : IDisposable
    {
        private readonly int _size;
        private readonly List<Object> _owned = new List<Object>();
        private readonly Dictionary<string, float> _floats = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, Vector4> _vectors = new Dictionary<string, Vector4>(StringComparer.Ordinal);
        private readonly Dictionary<string, Texture> _textures = new Dictionary<string, Texture>(StringComparer.Ordinal);
        private readonly List<Light> _lights = new List<Light>();
        private readonly Scene _scene;
        private readonly Material _material;
        private readonly Camera _camera;
        private readonly GameObject _surface;
        private readonly RenderTexture _target;
        private readonly Texture2D _readback;
        private readonly ComputeBuffer _cells;
        private readonly bool _async;

        public MonolithOptimizationVisualFixture(int size)
        {
            _size = size;
            Assert.That(Shader.GetGlobalFloat("_MonolithWetnessActive"), Is.LessThan(0.5f), "Close a live wetness preview before the isolated test.");
            _async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            _scene = EditorSceneManager.NewPreviewScene();
            _surface = Own(GameObject.CreatePrimitive(PrimitiveType.Quad));
            SceneManager.MoveGameObjectToScene(_surface, _scene);
            _surface.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            _surface.transform.localScale = new Vector3(8.0f, 8.0f, 1.0f);
            _material = Own(new Material(Shader.Find("HDRP/MonolithLit")));
            _material.SetFloat("_MaterialID", 4.0f);
            _material.EnableKeyword("_MATERIAL_FEATURE_SPECULAR_COLOR");
            HDMaterial.ValidateMaterial(_material);
            _material.EnableKeyword("_MONOLITH_ORIGINAL_ENVIRONMENT");
            _material.EnableKeyword("_MONOLITH_PRECIPITATION");
            _material.SetFloat("_MonolithEnvironmentMode", 1.0f);
            _material.SetFloat("_MonolithSourceProfile", 1.0f);
            _material.SetFloat("_MonolithMaterialId", 1.0f);
            _material.SetFloat("_MonolithFallbackGloss", 0.7f);
            _material.SetFloat("_MonolithAlbedoSrgb", 0.0f);
            _material.SetVector("_MonolithWetResponse", new Vector4(0.8f, 0.75f, 1.0f, 1.0f));
            var albedo = Own(new Texture2D(8, 8, TextureFormat.RGBA32, true, true));
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) albedo.SetPixel(x, y, new Color(0.3f + x * 0.025f, 0.25f + y * 0.025f, 0.2f + (x + y) * 0.0125f, 1.0f));
            albedo.Apply();
            _material.SetTexture("_BaseColorMap", albedo);
            _material.SetTexture("_MonolithEnv0", Cube(new Color(0.3f, 0.12f, 0.05f)));
            _material.SetTexture("_MonolithEnv1", Cube(new Color(0.04f, 0.15f, 0.35f)));
            _material.SetVector("_MonolithEnvColor", new Vector4(0.7f, 0.8f, 0.9f, 0.4f));
            _material.SetVector("_MonolithEnvAmbient", new Vector4(0.04f, 0.03f, 0.02f, 0.0f));
            _surface.GetComponent<MeshRenderer>().sharedMaterial = _material;
            var sunObject = Own(new GameObject("Monolith optimization directional light"));
            SceneManager.MoveGameObjectToScene(sunObject, _scene);
            sunObject.transform.rotation = Quaternion.Euler(45.0f, -30.0f, 0.0f);
            Light sun = sunObject.AddComponent<Light>();
            _lights.Add(sun);
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.None;
            sun.color = new Color(1.0f, 0.85f, 0.7f);
            sunObject.AddComponent<HDAdditionalLightData>();
            sun.intensity = 3.0f;
            sun.lightUnit = LightUnit.Lux;
            var pointObject = Own(new GameObject("Monolith optimization point light"));
            SceneManager.MoveGameObjectToScene(pointObject, _scene);
            pointObject.transform.position = new Vector3(-1.0f, 1.5f, 0.3f);
            Light point = pointObject.AddComponent<Light>();
            _lights.Add(point);
            point.type = LightType.Point;
            point.shadows = LightShadows.None;
            point.range = 8.0f;
            point.color = new Color(0.5f, 0.7f, 1.0f);
            pointObject.AddComponent<HDAdditionalLightData>();
            point.intensity = LightUnitUtils.ConvertIntensity(point, 20.0f, LightUnit.Lumen, LightUnit.Candela);
            point.lightUnit = LightUnit.Lumen;
            var spotObject = Own(new GameObject("Monolith optimization spot light"));
            SceneManager.MoveGameObjectToScene(spotObject, _scene);
            spotObject.transform.SetPositionAndRotation(new Vector3(1.0f, 2.0f, 0.3f), Quaternion.Euler(90.0f, 0.0f, 0.0f));
            Light spot = spotObject.AddComponent<Light>();
            _lights.Add(spot);
            spot.type = LightType.Spot;
            spot.shadows = LightShadows.None;
            spot.range = 8.0f;
            spot.spotAngle = 100.0f;
            spot.color = new Color(0.7f, 1.0f, 0.5f);
            spotObject.AddComponent<HDAdditionalLightData>();
            spot.intensity = LightUnitUtils.ConvertIntensity(spot, 20.0f, LightUnit.Lumen, LightUnit.Candela);
            spot.lightUnit = LightUnit.Lumen;
            var cameraObject = Own(new GameObject("Monolith optimization preview camera"));
            SceneManager.MoveGameObjectToScene(cameraObject, _scene);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.scene = _scene;
            _camera.orthographic = true;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 200.0f;
            _camera.allowHDR = true;
            _camera.useOcclusionCulling = false;
            HDAdditionalCameraData data = cameraObject.AddComponent<HDAdditionalCameraData>();
            data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            data.backgroundColorHDR = Color.black;
            data.volumeLayerMask = 0;
            data.customRenderingSettings = true;
            foreach (FrameSettingsField field in new[] { FrameSettingsField.Postprocess, FrameSettingsField.ExposureControl, FrameSettingsField.SSR, FrameSettingsField.SSAO, FrameSettingsField.SSGI, FrameSettingsField.ContactShadows, FrameSettingsField.Volumetrics, FrameSettingsField.MotionVectors })
            {
                data.renderingPathCustomFrameSettings.SetEnabled(field, false);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
            }
            _target = Own(new RenderTexture(_size, _size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
            _target.Create();
            _camera.targetTexture = _target;
            _readback = Own(new Texture2D(_size, _size, TextureFormat.RGBAFloat, false, true));
            for (int index = 0; index < 4; index++) Texture("_MonolithWeatherEnvironment" + index, Cube(new Color(0.1f + index * 0.15f, 0.4f - index * 0.08f, 0.15f + index * 0.12f)));
            Float("_MonolithWeatherActive", 1.0f);
            Float("_MonolithTrueStalkerActive", 0.0f);
            Float("_MonolithSourceLightingMode", 0.0f);
            Float("_MonolithWetReflectionIntensity", 1.0f);
            Vector("_MonolithWeatherEnvironmentColor", new Vector4(0.8f, 0.85f, 0.9f, 0.0f));
            Vector("_MonolithWeatherAmbient", new Vector4(0.04f, 0.03f, 0.02f, 0.0f));
            Vector("_MonolithWeatherState", new Vector4(0.71864f, 0.0f, 0.0f, 0.0f));
            Vector("_MonolithWeatherSun", Vector4.one);
            Vector("_MonolithWeatherLightSetup", new Vector4(0.5f, 1.0f, 0.0f, 0.0f));
            Vector("_MonolithWeatherFloraFixes", new Vector4(1.5f, 1.0f, 0.0f, 0.0f));
            Vector("_MonolithWeatherNightAmbient", Vector4.zero);
            Vector("_MonolithWeatherFxAmbient", Vector4.zero);
            Vector("_MonolithWetnessMapping", new Vector4(-64.0f, -64.0f, 1.0f / 128.0f, 1.0f / 128.0f));
            Vector("_MonolithWetnessCellSize", new Vector4(32.0f, 32.0f, 4.0f, 4.0f));
            Vector("_MonolithPuddleShape", new Vector4(0.075f, 0.004f, 1.0f, 0.085f));
            Vector("_MonolithPuddleOptics", new Vector4(5.0f, 0.32f, 0.12f, 1.0f));
            Vector("_MonolithPuddleSettings", new Vector4(0.15f, 1.0f, 11.0f, 2.0f));
            Float("_MonolithPuddleRainTime", 11.0f);
            var wetTexture = Own(new Texture2DArray(8, 8, 2, TextureFormat.RGBA32, true, true));
            wetTexture.wrapMode = TextureWrapMode.Repeat;
            var values = new Color[64];
            for (int index = 0; index < values.Length; index++) values[index] = new Color(0.5f + Mathf.Sin(index * 1.7f) * 0.2f, 0.5f + Mathf.Cos(index * 1.3f) * 0.2f, 0.5f, 1.0f);
            wetTexture.SetPixels(values, 0);
            wetTexture.SetPixels(values, 1);
            wetTexture.Apply();
            Texture("_MonolithWetSurfaceTextures", wetTexture);
            _cells = new ComputeBuffer(16, 24);
            Shader.SetGlobalBuffer("_MonolithWetnessCells", _cells);
            Surface(Vector4.zero, 0.0f, false);
        }

        public void Weather(bool active, Vector4 weights)
        {
            Float("_MonolithWeatherEnvironmentActive", active ? 1.0f : 0.0f);
            Vector("_MonolithWeatherEnvironmentWeights", weights);
        }

        public void Surface(Vector4 moisture, float rain, bool distant)
        {
            var cells = new MonolithOptimizationWetCell[16];
            for (int index = 0; index < cells.Length; index++) cells[index] = new MonolithOptimizationWetCell(moisture);
            _cells.SetData(cells);
            Float("_MonolithWetnessActive", 1.0f);
            Vector("_MonolithWetnessSettings", new Vector4(1.0f, 1.0f, 0.1f, rain));
            Vector("_MonolithPuddleRipple", new Vector4(rain, 0.0f, 0.2f, 1.0f));
            _camera.orthographicSize = distant ? 18.0f : 2.0f;
            _camera.transform.SetPositionAndRotation(new Vector3(0.0f, distant ? 32.0f : 3.0f, distant ? -32.0f : -3.0f), Quaternion.Euler(45.0f, 0.0f, 0.0f));
            _surface.transform.localScale = new Vector3(distant ? 64.0f : 8.0f, distant ? 64.0f : 8.0f, 1.0f);
        }

        public void Direct(float material, float weight)
        {
            _material.SetFloat("_MonolithMaterialId", material);
            _material.SetFloat("_MonolithMaterialWeight", weight);
        }

        public void Lights(bool enabled)
        {
            foreach (Light light in _lights) light.enabled = enabled;
        }

        public void LevelHemisphere(float value, Vector4 radiance)
        {
            _material.DisableKeyword("_MONOLITH_ORIGINAL_ENVIRONMENT");
            _material.DisableKeyword("_MONOLITH_PRECIPITATION");
            _material.EnableKeyword("_MONOLITH_LEVEL");
            _material.SetFloat("_MonolithSourceProfile", 0.0f);
            _material.SetFloat("_MonolithLevelAmbientHemi", 1.0f);
            _material.SetFloat("_MonolithLevelHemiMode", 1.0f);
            var hemisphere = Own(new Texture2D(1, 1, TextureFormat.RGBA32, false, true));
            hemisphere.SetPixel(0, 0, new Color(0.0f, 0.0f, 0.0f, value));
            hemisphere.Apply(false, false);
            _material.SetTexture("_MonolithLevelHemiMap", hemisphere);
            Weather(true, new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
            Vector("_MonolithWeatherAmbientRadiance", radiance);
            Vector("_MonolithDiagnosticOptions", Vector4.zero);
        }

        public void OutdoorSurface(bool terrain)
        {
            _material.DisableKeyword(terrain ? "_MONOLITH_LEVEL_FLORA" : "_MONOLITH_LEVEL_TERRAIN");
            _material.EnableKeyword(terrain ? "_MONOLITH_LEVEL_TERRAIN" : "_MONOLITH_LEVEL_FLORA");
            _material.SetFloat("_MonolithLevelHemiMode", terrain ? 3.0f : 2.0f);
            _material.SetFloat("_MonolithSourceProfile", terrain ? 0.0f : 1.0f);
            _material.SetTexture("_BaseColorMap", SolidTexture(new Color(0.4f, 0.35f, 0.28f, terrain ? 0.55f : 1.0f)));
            if (terrain)
            {
                _material.SetTexture("_MonolithTerrainMask", SolidTexture(new Color(1.0f, 0.0f, 0.0f, 0.0f)));
                _material.SetTexture("_MonolithTerrainDetailR", SolidTexture(new Color(0.5f, 0.5f, 0.5f, 1.0f)));
                _material.SetTexture("_MonolithTerrainBumpR", SolidTexture(new Color(0.1f, 1.0f, 0.5f, 0.5f)));
            }
            else
            {
                _material.EnableKeyword("_ALPHATEST_ON");
                _material.SetFloat("_AlphaCutoffEnable", 1.0f);
                _material.SetFloat("_AlphaCutoff", 0.78f);
                _material.SetFloat("_MonolithFloraSource", 1.0f);
                _material.SetFloat("_MonolithHasBump", 1.0f);
                _material.SetTexture("_MonolithBump", SolidTexture(new Color(0.1f, 0.5f, 0.5f, 0.5f)));
                MeshFilter filter = _surface.GetComponent<MeshFilter>();
                Mesh mesh = Own(Object.Instantiate(filter.sharedMesh));
                var hemisphere = new Vector2[mesh.vertexCount];
                Array.Fill(hemisphere, new Vector2(0.0f, 0.55f));
                mesh.uv3 = hemisphere;
                filter.sharedMesh = mesh;
            }
        }

        public Color[] Capture(float debug = 0.0f)
        {
            _material.SetFloat("_MonolithDebug", debug);
            RenderTexture original = RenderTexture.active;
            try
            {
                _camera.Render();
                _camera.Render();
                RenderTexture.active = _target;
                _readback.ReadPixels(new Rect(0, 0, _size, _size), 0, 0);
                _readback.Apply(false, false);
                return _readback.GetPixels();
            }
            finally
            {
                RenderTexture.active = original;
            }
        }

        public void Dispose()
        {
            _camera.targetTexture = null;
            _target.Release();
            foreach (KeyValuePair<string, float> value in _floats) Shader.SetGlobalFloat(value.Key, value.Value);
            foreach (KeyValuePair<string, Vector4> value in _vectors) Shader.SetGlobalVector(value.Key, value.Value);
            foreach (KeyValuePair<string, Texture> value in _textures) Shader.SetGlobalTexture(value.Key, value.Value);
            Shader.SetGlobalBuffer("_MonolithWetnessCells", (ComputeBuffer)null);
            _cells.Dispose();
            for (int index = _owned.Count - 1; index >= 0; index--) if (_owned[index] != null) Object.DestroyImmediate(_owned[index]);
            EditorSceneManager.ClosePreviewScene(_scene);
            ShaderUtil.allowAsyncCompilation = _async;
        }

        private Cubemap Cube(Color color)
        {
            var cube = Own(new Cubemap(8, TextureFormat.RGBA32, true));
            var pixels = new Color[64];
            for (int face = 0; face < 6; face++)
            {
                for (int index = 0; index < pixels.Length; index++) pixels[index] = color * (0.4f + face * 0.1f + index / 630.0f);
                cube.SetPixels(pixels, (CubemapFace)face);
            }
            cube.Apply();
            return cube;
        }

        private Texture2D SolidTexture(Color color)
        {
            var texture = Own(new Texture2D(1, 1, TextureFormat.RGBA32, false, true));
            texture.SetPixel(0, 0, color);
            texture.Apply(false, false);
            return texture;
        }

        private void Float(string name, float value)
        {
            if (!_floats.ContainsKey(name)) _floats.Add(name, Shader.GetGlobalFloat(name));
            Shader.SetGlobalFloat(name, value);
        }

        private void Vector(string name, Vector4 value)
        {
            if (!_vectors.ContainsKey(name)) _vectors.Add(name, Shader.GetGlobalVector(name));
            Shader.SetGlobalVector(name, value);
        }

        private void Texture(string name, Texture value)
        {
            if (!_textures.ContainsKey(name)) _textures.Add(name, Shader.GetGlobalTexture(name));
            Shader.SetGlobalTexture(name, value);
        }

        private T Own<T>(T value) where T : Object
        {
            _owned.Add(value);
            return value;
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Tests
{
    internal sealed class MonolithFloraWindRig
    {
        private readonly int _size;
        private readonly MonolithFloraWindSceneView _sceneView;
        private readonly Camera _camera;
        private readonly HDAdditionalCameraData _data;
        private readonly GameObject _tree;
        private readonly MeshFilter _filter;
        private readonly MeshRenderer _renderer;
        private readonly GameObject _receiver;
        private readonly RenderTexture _target;
        private readonly Texture2D _pixels;
        private readonly Dictionary<AOVBuffers, RTHandle> _aov = new Dictionary<AOVBuffers, RTHandle>();
        private readonly AOVRequestDataCollection _requests;
        private readonly List<Light> _disabledFixtureLights = new List<Light>();
        private readonly List<Camera> _disabledFixtureCameras = new List<Camera>();
        private bool _captureAov;
        private RTHandle _depthSource;
        private RTHandle _motionSource;
        private RTHandle _colorSource;
        private int _aovVersion;
        private readonly RenderTexture _initialActive;
        private bool _released;

        public Camera Camera => _camera;

        public MonolithFloraWindRig(GameObject root, Scene scene, List<UnityEngine.Object> temporary, bool shadows, bool sceneView, int size, float background)
        {
            _size = size;
            _initialActive = RenderTexture.active;
            if (sceneView)
            {
                _sceneView = ScriptableObject.CreateInstance<MonolithFloraWindSceneView>();
                _sceneView.ShowUtility();
                _sceneView.Bind(scene);
                _camera = _sceneView.camera;
            }
            else _camera = new GameObject("Wind Camera").AddComponent<Camera>();
            _camera.transform.SetParent(root.transform);
            _camera.scene = scene;
            if (sceneView)
            {
                _camera.cullingMask = 1 << 31;
                _camera.overrideSceneCullingMask = ulong.MaxValue;
                root.layer = 31;
            }
            _camera.enabled = false;
            _camera.orthographic = true;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 200.0f;
            _camera.allowHDR = true;
            _camera.useOcclusionCulling = false;
            _data = _camera.GetComponent<HDAdditionalCameraData>();
            if (_data == null) _data = _camera.gameObject.AddComponent<HDAdditionalCameraData>();
            _data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            _data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            _data.backgroundColorHDR = new Color(background, background, background, 1.0f);
            _data.customRenderingSettings = true;
            _data.volumeLayerMask = ~0;
            foreach (FrameSettingsField field in new[] { FrameSettingsField.SSR, FrameSettingsField.SSAO, FrameSettingsField.SSGI, FrameSettingsField.ContactShadows, FrameSettingsField.Volumetrics, FrameSettingsField.Postprocess, FrameSettingsField.ExposureControl })
            {
                _data.renderingPathCustomFrameSettings.SetEnabled(field, false);
                _data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
            }
            foreach (FrameSettingsField field in new[] { FrameSettingsField.MotionVectors, FrameSettingsField.ObjectMotionVectors, FrameSettingsField.ShadowMaps })
            {
                _data.renderingPathCustomFrameSettings.SetEnabled(field, field != FrameSettingsField.ShadowMaps || shadows);
                _data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
            }
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            temporary.Add(profile);
            profile.Add<Fog>(true).enabled.Override(false);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.None);
            Exposure exposure = profile.Add<Exposure>(true);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(0);
            Volume volume = root.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10000;
            volume.sharedProfile = profile;
            Light sun = new GameObject("Wind Sun").AddComponent<Light>();
            sun.transform.SetParent(root.transform);
            if (sceneView) sun.gameObject.layer = 31;
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(35.0f, 15.0f, 0.0f);
            sun.shadows = shadows ? LightShadows.Hard : LightShadows.None;
            sun.gameObject.AddComponent<HDAdditionalLightData>();
            sun.intensity = 3;
            sun.lightUnit = LightUnit.Lux;
            if (shadows)
            {
                foreach (Camera camera in UnityEngine.Object.FindObjectsByType<Camera>())
                {
                    if (camera == _camera || camera.cameraType != CameraType.Game || !camera.enabled || camera.gameObject.scene.path != string.Empty || camera.name != "Main Camera") continue;
                    _disabledFixtureCameras.Add(camera);
                    camera.enabled = false;
                }
                foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>())
                {
                    if (light == sun || light.type != LightType.Directional || light.shadows == LightShadows.None || !light.enabled || light.gameObject.scene.path != string.Empty || light.name != "Directional Light") continue;
                    _disabledFixtureLights.Add(light);
                    light.enabled = false;
                }
            }
            _tree = new GameObject("Wind Tree");
            _tree.transform.SetParent(root.transform);
            if (sceneView) _tree.layer = 31;
            _filter = _tree.AddComponent<MeshFilter>();
            _renderer = _tree.AddComponent<MeshRenderer>();
            _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            _receiver = GameObject.CreatePrimitive(PrimitiveType.Quad);
            SceneManager.MoveGameObjectToScene(_receiver, scene);
            temporary.Add(_receiver);
            if (sceneView) _receiver.layer = 31;
            var receiverMaterial = new Material(Shader.Find("HDRP/Lit"));
            temporary.Add(receiverMaterial);
            _receiver.GetComponent<MeshRenderer>().sharedMaterial = receiverMaterial;
            _receiver.SetActive(false);
            _target = new RenderTexture(_size, _size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            _target.Create();
            temporary.Add(_target);
            _pixels = new Texture2D(_size, _size, TextureFormat.RGBAFloat, false, true);
            temporary.Add(_pixels);
            _camera.targetTexture = _target;
            _camera.aspect = 1.0f;
            var builder = new AOVRequestBuilder();
            builder.Add(AOVRequest.NewDefault(), buffer =>
            {
                if (_aov.TryGetValue(buffer, out RTHandle handle)) return handle;
                handle = RTHandles.Alloc(_size, _size, colorFormat: GraphicsFormat.R32G32B32A32_SFloat, name: "Wind AOV " + buffer);
                _aov.Add(buffer, handle);
                return handle;
            }, null, new[] { AOVBuffers.DepthStencil, AOVBuffers.MotionVectors, AOVBuffers.Color }, (command, textures, properties) =>
            {
                if (!_captureAov) return;
                _depthSource = textures[0];
                _motionSource = textures[1];
                _colorSource = textures[2];
                _aovVersion++;
            });
            _requests = builder.Build();
            _data.SetAOVRequests(_requests);
        }

        public void Release()
        {
            if (_released) return;
            _released = true;
            if (_data != null) _data.SetAOVRequests(null);
            _requests.Dispose();
            bool ownedActive = RenderTexture.active == _target;
            foreach (RTHandle handle in _aov.Values) ownedActive |= RenderTexture.active == handle.rt;
            if (ownedActive) RenderTexture.active = _initialActive;
            if (_camera != null) _camera.targetTexture = null;
            foreach (RTHandle handle in _aov.Values) RTHandles.Release(handle);
            _aov.Clear();
            if (_sceneView != null) _sceneView.Close();
            foreach (Light light in _disabledFixtureLights) if (light != null) light.enabled = true;
            _disabledFixtureLights.Clear();
            foreach (Camera camera in _disabledFixtureCameras) if (camera != null) camera.enabled = true;
            _disabledFixtureCameras.Clear();
        }

        public float PixelSize => _camera.orthographicSize * 2.0f / _size;

        public void Frame(Bounds bounds)
        {
            _camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.25f + 0.5f;
            if (_sceneView != null)
            {
                _sceneView.orthographic = true;
                _sceneView.LookAtDirect(bounds.center, Quaternion.identity, _camera.orthographicSize);
            }
            _camera.transform.position = bounds.center + Vector3.back * 60.0f;
            _camera.transform.rotation = Quaternion.identity;
            _receiver.transform.position = bounds.center + new Vector3(0.0f, 0.0f, Mathf.Max(3.0f, bounds.extents.z + 2.0f));
            _receiver.transform.rotation = Quaternion.identity;
            _receiver.transform.localScale = Vector3.one * (Mathf.Max(bounds.size.x, bounds.size.y) * 3.0f + 4.0f);
        }

        public void Show(Mesh mesh, Material material, Matrix4x4 matrix, ShadowCastingMode shadows)
        {
            _tree.SetActive(true);
            _filter.sharedMesh = mesh;
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = shadows;
            Vector3 x = matrix.GetColumn(0);
            Vector3 y = matrix.GetColumn(1);
            Vector3 z = matrix.GetColumn(2);
            float sign = Vector3.Dot(Vector3.Cross(x, y), z) < 0.0f ? -1.0f : 1.0f;
            _tree.transform.SetPositionAndRotation(matrix.GetColumn(3), Quaternion.LookRotation(z, y));
            _tree.transform.localScale = new Vector3(sign * x.magnitude, y.magnitude, z.magnitude);
            Matrix4x4 actual = _tree.transform.localToWorldMatrix;
            for (int i = 0; i < 16; i++) Assert.AreEqual(matrix[i], actual[i], 1e-4f, "Test transform must reproduce the reference matrix.");
        }

        public void Hide()
        {
            _tree.SetActive(false);
        }

        public void Receiver(bool visible, int layer = -1)
        {
            if (layer >= 0) _receiver.layer = layer;
            _receiver.SetActive(visible);
        }

        public Color[] Debug(float mode)
        {
            Material material = _renderer.sharedMaterial;
            float previous = material.GetFloat("_MonolithDebug");
            material.SetFloat("_MonolithDebug", mode);
            _camera.Render();
            _camera.Render();
            material.SetFloat("_MonolithDebug", previous);
            return Read(_target);
        }

        public Color[] Raw()
        {
            _camera.Render();
            _camera.Render();
            return Read(_target);
        }

        public Color[] RawMotion()
        {
            _captureAov = true;
            _camera.Render();
            _camera.Render();
            _captureAov = false;
            return Read(_motionSource.rt);
        }

        public (Color[], Color[]) InstanceFrame(Material material)
        {
            material.SetFloat("_MonolithDebug", 35.0f);
            _captureAov = true;
            _camera.Render();
            _captureAov = false;
            Color[] uvs = Read(_target);
            Color[] vectors = Read(_motionSource.rt);
            material.SetFloat("_MonolithDebug", 0.0f);
            return (vectors, uvs);
        }

        public IEnumerator NativeFrame(int debug, Action<Color[]> receive, bool motion = false)
        {
            Assert.IsNotNull(_sceneView);
            Material material = _renderer.sharedMaterial;
            float previous = material != null ? material.GetFloat("_MonolithDebug") : 0.0f;
            if (debug >= 0 && material != null) material.SetFloat("_MonolithDebug", debug);
            int version = _aovVersion;
            _captureAov = true;
            _sceneView.Repaint();
            double limit = EditorApplication.timeSinceStartup + 10.0;
            while (_aovVersion < version + 2 && EditorApplication.timeSinceStartup < limit)
            {
                _sceneView.Repaint();
                yield return null;
            }
            _captureAov = false;
            Assert.GreaterOrEqual(_aovVersion, version + 2, "Actual Scene View GUI repaints must deliver settled AOV callbacks without Camera.Render.");
            Color[] pixels = Read(motion ? _motionSource.rt : _colorSource.rt);
            if (debug >= 0 && material != null) material.SetFloat("_MonolithDebug", previous);
            receive?.Invoke(pixels);
        }

        public float[] Depth()
        {
            _captureAov = true;
            _camera.Render();
            _captureAov = false;
            Color[] pixels = Read(_depthSource.rt);
            var depth = new float[pixels.Length];
            for (int p = 0; p < pixels.Length; p++) depth[p] = pixels[p].r;
            return depth;
        }

        public (Matrix4x4, Matrix4x4, Color[], Color[]) Motion(Vector3 cameraShift)
        {
            Material material = _renderer.sharedMaterial;
            Vector3 start = _camera.transform.position;
            _camera.Render();
            Matrix4x4 previousViewProjection = GL.GetGPUProjectionMatrix(_camera.projectionMatrix, true) * _camera.worldToCameraMatrix;
            _camera.transform.position = start + cameraShift;
            material.SetFloat("_MonolithDebug", 35.0f);
            _captureAov = true;
            _camera.Render();
            _captureAov = false;
            material.SetFloat("_MonolithDebug", 0.0f);
            Matrix4x4 viewProjection = GL.GetGPUProjectionMatrix(_camera.projectionMatrix, true) * _camera.worldToCameraMatrix;
            Color[] uvs = Read(_target);
            Color[] vectors = Read(_motionSource.rt);
            _camera.transform.position = start;
            return (previousViewProjection, viewProjection, vectors, uvs);
        }

        private Color[] Read(RenderTexture texture)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            _pixels.ReadPixels(new Rect(0, 0, _size, _size), 0, 0);
            _pixels.Apply();
            RenderTexture.active = previous;
            return _pixels.GetPixels();
        }
    }
}

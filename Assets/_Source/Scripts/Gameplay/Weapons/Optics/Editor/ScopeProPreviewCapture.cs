using System;
using System.Collections.Generic;
using System.IO;
using RaidIntoDarkness.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Weapons.Optics.Editor
{
    public static class ScopeProPreviewCapture
    {
        private const string ARCHIVE_ROOT = "E:\\Projects\\stalker_raid_into_darkness_Archive";
        private const string PREFAB_PATH = "Assets/_Source/Items/Addons/Prefabs/march_f_shorty.prefab";
        private const string PROFILE_PATH = "Assets/_Source/Settings/Player/StalkerMovementProfile.asset";
        private const string SHADER_NAME = "RaidIntoDarkness/Optics/ScopeProLens";
        private const int CAPTURE_SIZE = 1024;
        private const int GRID_SIZE = 512;
        private const float EYE_DISTANCE = 0.124698281f;

        [Serializable]
        private sealed class EditorState
        {
            [SerializeField] private string _activeScene;
            [SerializeField] private string _activeSceneHandle;
            [SerializeField] private bool _isPlaying;
            [SerializeField] private List<string> _loadedScenes = new List<string>();
            [SerializeField] private List<bool> _dirtyScenes = new List<bool>();

            public EditorState()
            {
                Scene active = SceneManager.GetActiveScene();
                _activeScene = active.path;
                _activeSceneHandle = active.handle.GetRawData().ToString();
                _isPlaying = Application.isPlaying;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    _loadedScenes.Add(scene.handle.GetRawData() + ":" + scene.path);
                    _dirtyScenes.Add(scene.isDirty);
                }
            }

            public bool Matches(EditorState other)
            {
                if (other == null || _activeSceneHandle != other._activeSceneHandle || _activeScene != other._activeScene || _isPlaying != other._isPlaying || _loadedScenes.Count != other._loadedScenes.Count)
                {
                    return false;
                }

                for (int i = 0; i < _loadedScenes.Count; i++)
                {
                    if (_loadedScenes[i] != other._loadedScenes[i] || _dirtyScenes[i] != other._dirtyScenes[i])
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        [Serializable]
        private sealed class ImageReport
        {
            [SerializeField] private string _path;
            [SerializeField] private float _lateralEyeOffsetMeters;
            [SerializeField] private float _minimumChannel;
            [SerializeField] private float _maximumChannel;
            [SerializeField] private int _coloredPixels;

            public ImageReport(string path, float lateralEyeOffsetMeters, Color[] pixels)
            {
                _path = path;
                _lateralEyeOffsetMeters = lateralEyeOffsetMeters;
                _minimumChannel = 1f;
                foreach (Color color in pixels)
                {
                    float minimum = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
                    float maximum = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
                    _minimumChannel = Mathf.Min(_minimumChannel, minimum);
                    _maximumChannel = Mathf.Max(_maximumChannel, maximum);
                    if (maximum - minimum > 0.12f)
                    {
                        _coloredPixels++;
                    }
                }
            }
        }

        [Serializable]
        private sealed class CaptureReport
        {
            [SerializeField] private string _sourcePrefab = PREFAB_PATH;
            [SerializeField] private string _sourceShader = SHADER_NAME;
            [SerializeField] private string _transport = "Synthetic linear calibration grid through the real ocular mesh and ScopePro material";
            [SerializeField] private string _pngTransfer = "Linear HDR readback converted to sRGB";
            [SerializeField] private int _resolution = CAPTURE_SIZE;
            [SerializeField] private float _eyeDistanceMeters = EYE_DISTANCE;
            [SerializeField] private float _hudScale;
            [SerializeField] private float _physicalLensHalfHeightMeters;
            [SerializeField] private float _cameraVerticalFieldOfView;
            [SerializeField] private float _renderSize;
            [SerializeField] private EditorState _before;
            [SerializeField] private EditorState _after;
            [SerializeField] private bool _sceneStatePreserved;
            [SerializeField] private List<ImageReport> _images;

            public CaptureReport(EditorState before, EditorState after, List<ImageReport> images, float hudScale, float physicalHalfHeight, float fieldOfView, float renderSize)
            {
                _before = before;
                _after = after;
                _sceneStatePreserved = before.Matches(after);
                _images = images;
                _hudScale = hudScale;
                _physicalLensHalfHeightMeters = physicalHalfHeight;
                _cameraVerticalFieldOfView = fieldOfView;
                _renderSize = renderSize;
            }
        }

        public static string Capture(string outputDirectory)
        {
            if (Application.isPlaying)
            {
                throw new InvalidOperationException("ScopePro preview capture requires Edit Mode and never changes Play Mode.");
            }

            string output = ValidateOutputDirectory(outputDirectory);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            StalkerMovementProfile profile = AssetDatabase.LoadAssetAtPath<StalkerMovementProfile>(PROFILE_PATH);
            if (prefab == null || profile == null)
            {
                throw new InvalidOperationException("The configured March Shorty prefab and player movement profile are required.");
            }

            float hudScale = Mathf.Tan(profile.Camera.FieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(profile.Camera.HudFieldOfView * profile.Camera.HudFieldOfViewScale * 0.5f * Mathf.Deg2Rad);
            if (!float.IsFinite(hudScale) || hudScale <= 0f)
            {
                throw new InvalidOperationException("The player profile requires a finite positive HUD scale.");
            }

            float physicalHalfHeight = 0f;
            float fieldOfView = 0f;
            float renderSize = 0f;
            var before = new EditorState();
            var images = new List<ImageReport>();
            var owned = new List<UnityEngine.Object>();
            Scene preview = EditorSceneManager.NewPreviewScene();
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
                root.transform.localScale = Vector3.Scale(root.transform.localScale, new Vector3(hudScale, hudScale, 1f));
                ScopeOpticsRenderer optics = root.GetComponent<ScopeOpticsRenderer>();
                if (optics == null)
                {
                    throw new InvalidOperationException("March Shorty requires its configured ScopeOpticsRenderer.");
                }

                var settings = new SerializedObject(optics);
                Renderer lens = settings.FindProperty("_lensRenderer").objectReferenceValue as Renderer;
                Transform axis = settings.FindProperty("_opticalAxis").objectReferenceValue as Transform;
                Camera camera = settings.FindProperty("_scopeCamera").objectReferenceValue as Camera;
                int materialIndex = settings.FindProperty("_materialIndex").intValue;
                if (lens == null || axis == null || camera == null || lens.gameObject.scene != preview || axis.gameObject.scene != preview || camera.gameObject.scene != preview)
                {
                    throw new InvalidOperationException("Preview camera, ocular and optical axis must belong to the isolated prefab clone.");
                }

                Material[] materials = lens.sharedMaterials;
                if (materialIndex < 0 || materialIndex >= materials.Length || materials[materialIndex] == null || materials[materialIndex].shader.name != SHADER_NAME)
                {
                    throw new InvalidOperationException("March Shorty must use the configured ScopeProLens material before preview capture.");
                }

                var lensMaterial = Own(new Material(materials[materialIndex]), owned);
                renderSize = lensMaterial.GetFloat("_RenderSize");
                materials[materialIndex] = lensMaterial;
                lens.sharedMaterials = materials;
                optics.enabled = false;
                lens.enabled = true;
                lens.forceRenderingOff = false;
                ConfigureBody(root, lens, owned);
                Texture2D grid = Own(CreateGrid(), owned);
                var source = Own(new RenderTexture(GRID_SIZE, GRID_SIZE, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear), owned);
                source.filterMode = FilterMode.Bilinear;
                source.wrapMode = TextureWrapMode.Clamp;
                source.Create();
                Graphics.Blit(grid, source);
                var target = Own(new RenderTexture(CAPTURE_SIZE, CAPTURE_SIZE, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear), owned);
                target.Create();
                var readback = Own(new Texture2D(CAPTURE_SIZE, CAPTURE_SIZE, TextureFormat.RGBA32, false, true), owned);
                camera.transform.SetParent(null, true);
                camera.transform.localScale = Vector3.one;
                physicalHalfHeight = settings.FindProperty("_lensHalfSizeMeters").vector2Value.y * axis.TransformVector(Vector3.up).magnitude;
                fieldOfView = 2f * Mathf.Atan(physicalHalfHeight / EYE_DISTANCE / 0.8f) * Mathf.Rad2Deg;
                ConfigureCamera(camera, preview, fieldOfView);
                Vector3 right = axis.TransformVector(Vector3.right).normalized;
                Vector3 up = axis.TransformVector(Vector3.up).normalized;
                Vector3 forward = Vector3.Cross(right, up).normalized;
                var properties = new MaterialPropertyBlock();
                properties.SetTexture("_RenderTexture", source);
                properties.SetFloat("_ScopeAvailable", 1f);
                properties.SetVector("_ScopeRightWS", right);
                properties.SetVector("_ScopeUpWS", up);
                properties.SetFloat("_ReticleRuntimeScale", 1f);
                lens.SetPropertyBlock(properties, materialIndex);
                string stem = "scopepro_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
                foreach (float lateralOffset in new[] { 0f, 0.004f })
                {
                    camera.transform.SetPositionAndRotation(axis.position - forward * EYE_DISTANCE + right * lateralOffset, Quaternion.LookRotation(forward, up));
                    camera.ResetWorldToCameraMatrix();
                    camera.ResetProjectionMatrix();
                    camera.ResetCullingMatrix();
                    var request = new RenderPipeline.StandardRequest { destination = target };
                    if (!RenderPipeline.SupportsRenderRequest(camera, request))
                    {
                        throw new InvalidOperationException("The active HDRP pipeline does not support a standard preview render request.");
                    }

                    RenderPipeline.SubmitRenderRequest(camera, request);
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, CAPTURE_SIZE, CAPTURE_SIZE), 0, 0);
                    readback.Apply(false, false);
                    Color[] pixels = readback.GetPixels();
                    string path = Path.Combine(output, stem + (lateralOffset == 0f ? "_center.png" : "_offset4mm.png"));
                    images.Add(new ImageReport(path, lateralOffset, pixels));
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        pixels[i] = pixels[i].gamma;
                    }

                    readback.SetPixels(pixels);
                    readback.Apply(false, false);
                    File.WriteAllBytes(path, readback.EncodeToPNG());
                }
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (preview.IsValid())
                {
                    foreach (GameObject root in preview.GetRootGameObjects())
                    {
                        UnityEngine.Object.DestroyImmediate(root);
                    }

                    EditorSceneManager.ClosePreviewScene(preview);
                }

                for (int i = owned.Count - 1; i >= 0; i--)
                {
                    if (owned[i] != null)
                    {
                        if (owned[i] is RenderTexture texture)
                        {
                            texture.Release();
                        }

                        UnityEngine.Object.DestroyImmediate(owned[i]);
                    }
                }
            }

            var after = new EditorState();
            if (!before.Matches(after))
            {
                throw new InvalidOperationException("Editor scene or Play Mode state changed during the isolated preview capture.");
            }

            return JsonUtility.ToJson(new CaptureReport(before, after, images, hudScale, physicalHalfHeight, fieldOfView, renderSize), true);
        }

        private static string ValidateOutputDirectory(string directory)
        {
            string root = Path.GetFullPath(ARCHIVE_ROOT).TrimEnd(Path.DirectorySeparatorChar);
            string output = Path.GetFullPath(directory);
            if (!output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("ScopePro preview output must be inside the canonical Archive directory.", nameof(directory));
            }

            for (DirectoryInfo current = new DirectoryInfo(output); current != null && current.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase); current = current.Parent)
            {
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException("Preview output cannot traverse a reparse point.");
                }
            }

            Directory.CreateDirectory(output);
            return output;
        }

        private static T Own<T>(T value, List<UnityEngine.Object> owned) where T : UnityEngine.Object
        {
            value.hideFlags = HideFlags.HideAndDontSave;
            owned.Add(value);
            return value;
        }

        private static Texture2D CreateGrid()
        {
            var texture = new Texture2D(GRID_SIZE, GRID_SIZE, TextureFormat.RGBA32, false, true);
            var pixels = new Color32[GRID_SIZE * GRID_SIZE];
            for (int y = 0; y < GRID_SIZE; y++)
            {
                for (int x = 0; x < GRID_SIZE; x++)
                {
                    bool line = x % 64 < 2 || y % 64 < 2;
                    bool alternate = (x / 64 + y / 64) % 2 == 0;
                    pixels[y * GRID_SIZE + x] = line ? new Color32(230, 230, 230, 255) : alternate ? new Color32(40, 130, 180, 255) : new Color32(180, 100, 35, 255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static void ConfigureBody(GameObject root, Renderer lens, List<UnityEngine.Object> owned)
        {
            Shader shader = Shader.Find("HDRP/Unlit");
            if (shader == null)
            {
                return;
            }

            var material = Own(new Material(shader), owned);
            material.SetColor("_UnlitColor", new Color(0.07f, 0.08f, 0.09f, 1f));
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != lens && renderer.name.IndexOf("mesh_00", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    renderer.sharedMaterial = material;
                }
            }
        }

        private static void ConfigureCamera(Camera camera, Scene preview, float fieldOfView)
        {
            camera.enabled = false;
            camera.scene = preview;
            camera.orthographic = false;
            camera.fieldOfView = fieldOfView;
            camera.aspect = 1f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 2f;
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            camera.cullingMask = ~0;
            camera.useOcclusionCulling = false;
            camera.allowHDR = true;
            camera.allowMSAA = false;
            camera.allowDynamicResolution = false;
            HDAdditionalCameraData data = camera.GetComponent<HDAdditionalCameraData>();
            if (data == null)
            {
                throw new InvalidOperationException("The authored preview camera requires HDAdditionalCameraData.");
            }

            data.customRenderingSettings = true;
            data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            data.backgroundColorHDR = new Color(0.025f, 0.03f, 0.04f, 1f);
            data.volumeLayerMask = 0;
            data.volumeAnchorOverride = camera.transform;
            data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            data.allowDynamicResolution = false;
            data.xrRendering = false;
            foreach (FrameSettingsField field in new[] { FrameSettingsField.Postprocess, FrameSettingsField.ExposureControl, FrameSettingsField.Antialiasing, FrameSettingsField.CustomPass, FrameSettingsField.Volumetrics, FrameSettingsField.SSR, FrameSettingsField.SSAO, FrameSettingsField.SSGI, FrameSettingsField.ShadowMaps, FrameSettingsField.ContactShadows })
            {
                data.renderingPathCustomFrameSettings.SetEnabled(field, false);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
            }
        }
    }
}

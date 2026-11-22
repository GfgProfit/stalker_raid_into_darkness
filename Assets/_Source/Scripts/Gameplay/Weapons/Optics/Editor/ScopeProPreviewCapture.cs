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
        private const string PREFAB_PATH = ScopeOpticsPrefabSetup.MARCH_PREFAB;
        private const string PROFILE_PATH = "Assets/_Source/Settings/Player/StalkerMovementProfile.asset";
        private const string SHADER_NAME = "RaidIntoDarkness/Optics/ScopeProLens";
        private const string SDF_PATH = ScopeOpticsPrefabSetup.OPTICS_DIRECTORY + "/Textures/reticle_sdf.png";
        private const string PNG_PATH = ScopeOpticsPrefabSetup.OPTICS_DIRECTORY + "/Textures/reticle_png.png";
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
            [SerializeField] private string _label;
            [SerializeField] private float _magnification;
            [SerializeField] private int _redWidthPixels;

            public int RedWidthPixels => _redWidthPixels;

            public ImageReport(string path, float lateralEyeOffsetMeters, Color[] pixels, string label = "LegacyPNG", float magnification = 1f)
            {
                _path = path;
                _lateralEyeOffsetMeters = lateralEyeOffsetMeters;
                _minimumChannel = 1f;
                _label = label;
                _magnification = magnification;
                int minimumRedX = CAPTURE_SIZE;
                int maximumRedX = -1;
                int index = 0;
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

                    if (!float.IsFinite(color.r) || !float.IsFinite(color.g) || !float.IsFinite(color.b))
                    {
                        throw new InvalidOperationException("Reticle capture contains non-finite pixels.");
                    }

                    if (color.r > 0.2f && color.r > color.g * 1.8f && color.r > color.b * 1.8f)
                    {
                        minimumRedX = Mathf.Min(minimumRedX, index % CAPTURE_SIZE);
                        maximumRedX = Mathf.Max(maximumRedX, index % CAPTURE_SIZE);
                    }

                    index++;
                }

                _redWidthPixels = Mathf.Max(0, maximumRedX - minimumRedX + 1);
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
            [SerializeField] private List<string> _checks;

            public CaptureReport(EditorState before, EditorState after, List<ImageReport> images, float hudScale, float physicalHalfHeight, float fieldOfView, float renderSize, List<string> checks)
            {
                _before = before;
                _after = after;
                _sceneStatePreserved = before.Matches(after);
                _images = images;
                _hudScale = hudScale;
                _physicalLensHalfHeightMeters = physicalHalfHeight;
                _cameraVerticalFieldOfView = fieldOfView;
                _renderSize = renderSize;
                _checks = checks;
            }
        }

        public static string Capture(string outputDirectory)
        {
            return CaptureInternal(outputDirectory, false);
        }

        public static string CaptureSdf(string outputDirectory)
        {
            return CaptureInternal(outputDirectory, true);
        }

        private static string CaptureInternal(string outputDirectory, bool validateSdf)
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
            var checks = new List<string>();
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
                if (validateSdf)
                {
                    Color[] pixels = grid.GetPixels();
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        float gray = pixels[i].grayscale;
                        pixels[i] = new Color(gray, gray, gray, 1f);
                    }

                    grid.SetPixels(pixels);
                    grid.Apply(false, false);
                    Graphics.Blit(grid, source);
                }
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
                if (validateSdf)
                {
                    camera.transform.SetPositionAndRotation(axis.position - forward * EYE_DISTANCE, Quaternion.LookRotation(forward, up));
                    CaptureSdfCases(output, stem, lensMaterial, optics.Configuration, camera, source, target, readback, lens, materialIndex, properties, renderSize, physicalHalfHeight, owned, images, checks);
                }
                else
                {
                    foreach (float lateralOffset in new[] { 0f, 0.004f })
                    {
                        camera.transform.SetPositionAndRotation(axis.position - forward * EYE_DISTANCE + right * lateralOffset, Quaternion.LookRotation(forward, up));
                        Color[] pixels = ReadImage(camera, target, readback);
                        string path = Path.Combine(output, stem + (lateralOffset == 0f ? "_center.png" : "_offset4mm.png"));
                        images.Add(new ImageReport(path, lateralOffset, pixels));
                        SaveImage(path, readback, pixels);
                    }
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

            return JsonUtility.ToJson(new CaptureReport(before, after, images, hudScale, physicalHalfHeight, fieldOfView, renderSize, checks), true);
        }

        private static void CaptureSdfCases(string output, string stem, Material material, ScopeOpticsConfig configuration, Camera camera, RenderTexture source, RenderTexture target, Texture2D readback, Renderer lens, int materialIndex, MaterialPropertyBlock properties, float renderSize, float halfHeight, List<UnityEngine.Object> owned, List<ImageReport> images, List<string> checks)
        {
            Texture2D sdf = AssetDatabase.LoadAssetAtPath<Texture2D>(SDF_PATH);
            Texture2D png = AssetDatabase.LoadAssetAtPath<Texture2D>(PNG_PATH);
            if (sdf == null || png == null)
            {
                throw new InvalidOperationException("SDF validation requires the existing reticle_sdf.png and reticle_png.png inputs.");
            }

            var config = Own(UnityEngine.Object.Instantiate(configuration), owned);
            var settings = new SerializedObject(config);
            settings.FindProperty("_minMagnification").floatValue = 1f;
            settings.FindProperty("_maxMagnification").floatValue = 8f;
            settings.FindProperty("_calibrationMagnification").floatValue = 1f;
            settings.FindProperty("_projectionMode").enumValueIndex = (int)ScopeProjectionMode.CalibratedFieldOfView;
            settings.FindProperty("_calibrationVerticalFov").floatValue = 60f;
            settings.FindProperty("_trueOnePowerCalibration").floatValue = 1f;
            settings.FindProperty("_fieldOfViewMultiplier").animationCurveValue = AnimationCurve.Linear(1f, 1f, 8f, 1f);
            settings.ApplyModifiedPropertiesWithoutUndo();
            int lastRedWidth = 0;
            string[] extensionProperties = { "_ReticleExtendLeft", "_ReticleExtendRight", "_ReticleExtendUp", "_ReticleExtendDown" };
            foreach (string property in extensionProperties)
            {
                material.SetFloat(property, 0f);
            }

            Color[] CaptureShot(string label, float magnification)
            {
                material.SetFloat("_RenderSize", renderSize / magnification);
                properties.SetFloat("_ReticleRuntimeScale", ScopeOpticsMath.GetReticleScale(config, magnification, halfHeight, EYE_DISTANCE));
                properties.SetFloat("_ReticleSdfAvailable", material.GetTexture("_ReticleSdf") != null ? 1f : 0f);
                lens.SetPropertyBlock(properties, materialIndex);
                Color[] pixels = ReadImage(camera, target, readback);
                string path = Path.Combine(output, stem + "_" + label + ".png");
                var image = new ImageReport(path, 0f, pixels, label, magnification);
                lastRedWidth = image.RedWidthPixels;
                images.Add(image);
                SaveImage(path, readback, pixels);
                return pixels;
            }

            material.SetTexture("_Reticle", png);
            material.SetTexture("_ReticleSdf", sdf);
            material.SetFloat("_ReticleMode", 0f);
            Color[] legacy = CaptureShot("png_sdf_assigned", 1f);
            material.SetFloat("_ReticleMode", 1f);
            material.SetTexture("_ReticleSdf", null);
            Color[] fallback = CaptureShot("sdf_missing_fallback", 1f);
            for (int i = 0; i < legacy.Length; i++)
            {
                if (Mathf.Abs(legacy[i].r - fallback[i].r) > 0.0001f || Mathf.Abs(legacy[i].g - fallback[i].g) > 0.0001f || Mathf.Abs(legacy[i].b - fallback[i].b) > 0.0001f)
                {
                    throw new InvalidOperationException("Missing SDF did not reproduce the LegacyPNG image.");
                }
            }

            checks.Add("LegacyPNG with an assigned SDF and missing-SDF fallback are pixel-identical.");
            material.SetTexture("_ReticleSdf", sdf);
            material.SetColor("_ReticleColor", Color.black);
            material.SetColor("_IlluminationColor", Color.red);
            material.SetFloat("_IlluminationIntensity", 1f);
            foreach (ScopeReticlePlane plane in new[] { ScopeReticlePlane.FirstFocalPlane, ScopeReticlePlane.SecondFocalPlane })
            {
                settings.FindProperty("_reticlePlane").enumValueIndex = (int)plane;
                settings.ApplyModifiedPropertiesWithoutUndo();
                int onePowerWidth = 0;
                foreach (float power in new[] { 1f, 2f, 4f, 8f })
                {
                    string label = (plane == ScopeReticlePlane.FirstFocalPlane ? "ffp_" : "sfp_") + power.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "x";
                    CaptureShot(label, power);
                    if (power == 1f)
                    {
                        onePowerWidth = lastRedWidth;
                    }

                    if (onePowerWidth <= 0 || plane == ScopeReticlePlane.SecondFocalPlane && Mathf.Abs(lastRedWidth - onePowerWidth) > 2 || plane == ScopeReticlePlane.FirstFocalPlane && power == 2f && Mathf.Abs(lastRedWidth - onePowerWidth * 2) > 4)
                    {
                        throw new InvalidOperationException("SDF reticle focal-plane pixel bounds failed for " + label + ": " + lastRedWidth + " versus one-power " + onePowerWidth);
                    }

                    checks.Add(label + ": finite image and expected reticle scaling.");
                }
            }

            material.SetFloat("_IlluminationIntensity", 0f);
            CaptureShot("sdf_illumination_off", 1f);
            if (lastRedWidth != 0)
            {
                throw new InvalidOperationException("SDF illumination remained colored at zero intensity.");
            }

            checks.Add("Illumination off retains the etched ink without a red glow.");
            material.SetFloat("_IlluminationIntensity", 1f);
            foreach (string property in extensionProperties)
            {
                material.SetFloat(property, 1f);
            }

            CaptureShot("sdf_extended_lines_1x", 1f);
            foreach (string property in extensionProperties)
            {
                material.SetFloat(property, 0f);
            }

            RenderTexture.active = source;
            GL.Clear(true, true, Color.black);
            var uniform = Own(new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true), owned);
            uniform.filterMode = FilterMode.Bilinear;
            uniform.wrapMode = TextureWrapMode.Clamp;
            material.SetTexture("_ReticleSdf", uniform);
            material.SetColor("_ReticleColor", new Color(0f, 1f, 0f, 0.5f));
            material.SetColor("_IlluminationColor", new Color(1f, 0f, 0f, 0.5f));
            properties.SetFloat("_ReticleRuntimeScale", 1f);
            properties.SetFloat("_ReticleSdfAvailable", 1f);
            lens.SetPropertyBlock(properties, materialIndex);

            void CheckPixel(Color field, Color expected, float intensity, string label)
            {
                uniform.SetPixel(0, 0, field);
                uniform.Apply(false, false);
                material.SetFloat("_IlluminationIntensity", intensity);
                Color[] pixels = ReadImage(camera, target, readback);
                Color actual = pixels[CAPTURE_SIZE / 2 * CAPTURE_SIZE + CAPTURE_SIZE / 2];
                if (!float.IsFinite(actual.r) || !float.IsFinite(actual.g) || !float.IsFinite(actual.b) || Mathf.Abs(actual.r - expected.r) > 0.015f || Mathf.Abs(actual.g - expected.g) > 0.015f || Mathf.Abs(actual.b - expected.b) > 0.015f)
                {
                    throw new InvalidOperationException(label + ": expected " + expected + ", received " + actual);
                }

                checks.Add(label);
            }

            CheckPixel(new Color(1f, 0f, 1f, 0f), new Color(0f, 0.5f, 0f, 1f), 1f, "R-only decoding ignores B/A.");
            CheckPixel(new Color(0f, 1f, 1f, 0f), new Color(0.5f, 0f, 0f, 1f), 1f, "G-only decoding ignores B/A.");
            CheckPixel(new Color(1f, 1f, 1f, 0f), new Color(0.5f, 0f, 0f, 1f), 1f, "G wins at R/G overlap without doubled opacity.");
            CheckPixel(new Color(1f, 1f, 0f, 1f), new Color(0f, 0.5f, 0f, 1f), 0f, "Disabled illumination uses ordinary ink.");
            CheckPixel(new Color(0f, 0f, 1f, 1f), Color.black, 1f, "Exterior distances remain transparent at zero derivatives.");
            CheckPixel(new Color(0.5f, 0f, 0f, 1f), new Color(0f, 0.25f, 0f, 1f), 1f, "The 0.5 contour has half coverage at zero derivatives.");
            CheckPixel(new Color(0f, 1f, 0f, 1f), new Color(0.75f, 0f, 0f, 1f), 1.5f, "Illumination intensity scales channel G independently.");
            uniform.SetPixel(0, 0, new Color(1f, 0f, 0f, 1f));
            uniform.Apply(false, false);
            material.SetFloat("_ReticleSize", 5f);
            Color[] clipped = ReadImage(camera, target, readback);
            Color outside = clipped[CAPTURE_SIZE / 2 * CAPTURE_SIZE + CAPTURE_SIZE * 3 / 4];
            if (Mathf.Max(outside.r, Mathf.Max(outside.g, outside.b)) > 0.015f)
            {
                throw new InvalidOperationException("SDF ink escaped the UV bounds.");
            }

            checks.Add("SDF sampling outside UV 0..1 is transparent.");
            Vector2Int[] extensionPixels = { new Vector2Int(CAPTURE_SIZE / 4, CAPTURE_SIZE / 2), new Vector2Int(CAPTURE_SIZE * 3 / 4, CAPTURE_SIZE / 2), new Vector2Int(CAPTURE_SIZE / 2, CAPTURE_SIZE * 3 / 4), new Vector2Int(CAPTURE_SIZE / 2, CAPTURE_SIZE / 4) };
            for (int i = 0; i < extensionProperties.Length; i++)
            {
                material.SetFloat(extensionProperties[i], 1f);
                Color[] extended = ReadImage(camera, target, readback);
                Vector2Int pixel = extensionPixels[i];
                Color present = extended[pixel.y * CAPTURE_SIZE + pixel.x];
                Vector2Int oppositePixel = extensionPixels[i ^ 1];
                Color opposite = extended[oppositePixel.y * CAPTURE_SIZE + oppositePixel.x];
                if (Mathf.Abs(present.g - 0.5f) > 0.015f || Mathf.Max(opposite.r, Mathf.Max(opposite.g, opposite.b)) > 0.015f)
                {
                    throw new InvalidOperationException("Independent SDF line extension failed for " + extensionProperties[i]);
                }

                checks.Add(extensionProperties[i] + ": extends only the selected edge at Reticle Size 5.");
                material.SetFloat(extensionProperties[i], 0f);
            }

            var asymmetric = Own(new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true), owned);
            asymmetric.filterMode = FilterMode.Bilinear;
            asymmetric.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    asymmetric.SetPixel(x, y, new Color(y == 1 || y == 2 ? 1f : 0f, 0f, 0f, 1f));
                }
            }

            asymmetric.Apply(false, false);
            material.SetTexture("_ReticleSdf", asymmetric);
            foreach (string property in extensionProperties)
            {
                material.SetFloat(property, 1f);
            }

            Color[] asymmetricPixels = ReadImage(camera, target, readback);
            for (int i = 0; i < extensionPixels.Length; i++)
            {
                Vector2Int pixel = extensionPixels[i];
                Color actual = asymmetricPixels[pixel.y * CAPTURE_SIZE + pixel.x];
                float expectedGreen = i < 2 ? 0.5f : 0f;
                if (Mathf.Abs(actual.g - expectedGreen) > 0.015f)
                {
                    throw new InvalidOperationException("SDF edge extension invented or lost a line at " + extensionProperties[i]);
                }
            }

            checks.Add("A reticle with only horizontal border strokes does not acquire upper or lower rays, even with every extension enabled.");
        }

        private static Color[] ReadImage(Camera camera, RenderTexture target, Texture2D readback)
        {
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
            return readback.GetPixels();
        }

        private static void SaveImage(string path, Texture2D readback, Color[] pixels)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = pixels[i].gamma;
            }

            readback.SetPixels(pixels);
            readback.Apply(false, false);
            File.WriteAllBytes(path, readback.EncodeToPNG());
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

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Weapons.Optics.Editor
{
    public static class ScopeOpticsPrefabSetup
    {
        internal const string OPTICS_DIRECTORY = "Assets/_Source/Resources/Items/Addons/Scopes/march_f_shorty";
        internal const string MARCH_PREFAB = OPTICS_DIRECTORY + "/march_f_shorty_world.prefab";
        internal const string MARCH_ITEM = OPTICS_DIRECTORY + "/march_f_shorty_definition.asset";

        private const string MARCH_MODEL = OPTICS_DIRECTORY + "/march_f_shorty.ogf";
        private const string MARCH_RETICLE = OPTICS_DIRECTORY + "/Textures/march_f_shorty.png";
        private const string CONFIGURATION_PATH = OPTICS_DIRECTORY + "/march_f_shorty_optics.asset";
        private const string MATERIAL_PATH = OPTICS_DIRECTORY + "/Materials/march_f_shorty_lens.mat";

        private static readonly Vector3 OCULAR_CENTER = new Vector3(0.0001934683f, 0.03417937f, -0.0187098235f);
        private static readonly Vector2 OCULAR_HALF_SIZE = new Vector2(0.0158353858f, 0.0158353858f);

        public static string InspectMarchShorty()
        {
            return JsonUtility.ToJson(new InspectionReport(), true);
        }

        public static string CleanupMarchParameters()
        {
            AssertAssetEditing();
            ScopeOpticsConfig config = AssetDatabase.LoadAssetAtPath<ScopeOpticsConfig>(CONFIGURATION_PATH);
            if (config == null)
            {
                throw new InvalidOperationException("March optics configuration is unavailable.");
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssetIfDirty(config);
            GameObject root = PrefabUtility.LoadPrefabContents(MARCH_PREFAB);
            try
            {
                PrefabUtility.SaveAsPrefabAsset(root, MARCH_PREFAB, out bool saved);
                if (!saved)
                {
                    throw new InvalidOperationException("March prefab could not be saved after parameter cleanup.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return "March optics parameters reserialized without changing retained settings.";
        }

        public static string ConfigureMarchWheelZoom()
        {
            AssertAssetEditing();
            ScopeOpticsConfig config = AssetDatabase.LoadAssetAtPath<ScopeOpticsConfig>(CONFIGURATION_PATH);
            var settings = new SerializedObject(config);
            settings.FindProperty("_projectionMode").enumValueIndex = (int)ScopeProjectionMode.CalibratedFieldOfView;
            settings.FindProperty("_minMagnification").floatValue = 1f;
            settings.FindProperty("_maxMagnification").floatValue = 8f;
            settings.FindProperty("_calibrationMagnification").floatValue = 1f;
            settings.FindProperty("_calibrationVerticalFov").floatValue = 60f;
            settings.FindProperty("_fieldOfViewMultiplier").animationCurveValue = AnimationCurve.Linear(1f, 1f, 8f, 1f);
            settings.FindProperty("_trueOnePowerCalibration").floatValue = 1f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(config);
            GameObject root = PrefabUtility.LoadPrefabContents(MARCH_PREFAB);
            try
            {
                var binding = new SerializedObject(root.GetComponent<ScopeOpticsWeaponBinding>());
                binding.FindProperty("_magnification").floatValue = 1f;
                binding.FindProperty("_magnificationPerScrollNotch").floatValue = 0.35f;
                binding.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, MARCH_PREFAB);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return "March wheel zoom configured: 1-8x, optical FOV 60 degrees at 1x, continuous target, exponential smoothing, ADS only.";
        }

        public static string ConfigureMarchShortyScopePro()
        {
            AssertAssetEditing();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MATERIAL_PATH);
            Shader shader = Shader.Find("RaidIntoDarkness/Optics/ScopeProLens");
            Texture2D reticle = AssetDatabase.LoadAssetAtPath<Texture2D>(MARCH_RETICLE);
            if (material == null || shader == null || reticle == null)
            {
                throw new InvalidOperationException("March ScopePro setup requires the existing March material, its PNG and ScopeProLens shader.");
            }

            material.shader = shader;
            material.SetTexture("_Reticle", reticle);
            material.SetColor("_ReticleColor", Color.white);
            material.SetFloat("_ReticleSize", 0.8f);
            material.SetFloat("_ScopeDepth", 0.94f);
            material.SetFloat("_ScopeApperture", 1.01f);
            material.SetFloat("_ScopeFade", 0.5f);
            material.SetFloat("_RenderSize", 0.5f);
            material.SetFloat("_CullMode", (float)CullMode.Back);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return JsonUtility.ToJson(new ScopeProSetupReport(material), true);
        }

        [Serializable]
        private sealed class ScopeProSetupReport
        {
            [SerializeField] private string _material;
            [SerializeField] private string _shader;
            [SerializeField] private string _reticle;
            [SerializeField] private float _reticleSize;
            [SerializeField] private float _scopeDepth;
            [SerializeField] private float _scopeAperture;
            [SerializeField] private float _scopeFade;
            [SerializeField] private float _renderSize;

            public ScopeProSetupReport(Material material)
            {
                _material = AssetDatabase.GetAssetPath(material);
                _shader = material.shader.name;
                _reticle = AssetDatabase.GetAssetPath(material.GetTexture("_Reticle"));
                _reticleSize = material.GetFloat("_ReticleSize");
                _scopeDepth = material.GetFloat("_ScopeDepth");
                _scopeAperture = material.GetFloat("_ScopeApperture");
                _scopeFade = material.GetFloat("_ScopeFade");
                _renderSize = material.GetFloat("_RenderSize");
            }
        }

        public static string ConfigureMarchShorty(float eyeReliefMillimeters, Vector3 aimOffsetPosition)
        {
            AssertAssetEditing();
            if (!float.IsFinite(eyeReliefMillimeters) || eyeReliefMillimeters < 20f || eyeReliefMillimeters > 1000f)
            {
                throw new ArgumentOutOfRangeException(nameof(eyeReliefMillimeters), "March camera placement requires finite eye relief between 20 and 1000 millimeters.");
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(MARCH_PREFAB);
            try
            {
                Renderer lens = FindOcular(contents);
                if (lens == null || AssetDatabase.LoadAssetAtPath<Texture2D>(MARCH_RETICLE) == null)
                {
                    throw new InvalidOperationException("March Shorty requires its existing ocular mesh and march_f_shorty.png.");
                }

                EnsureDirectory(OPTICS_DIRECTORY + "/Materials");
                ScopeOpticsConfig configuration = ConfigureOptics();
                Material material = AssetDatabase.LoadAssetAtPath<Material>(MATERIAL_PATH);
                Shader shader = Shader.Find("RaidIntoDarkness/Optics/ScopeProLens");
                if (shader == null)
                {
                    throw new InvalidOperationException("ScopeLens shader is unavailable.");
                }

                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, MATERIAL_PATH);
                }

                material.shader = shader;
                material.SetTexture("_Reticle", AssetDatabase.LoadAssetAtPath<Texture2D>(MARCH_RETICLE));
                material.SetFloat("_CullMode", (float)CullMode.Back);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                lens.sharedMaterial = material;
                lens.enabled = true;
                lens.shadowCastingMode = ShadowCastingMode.Off;
                lens.receiveShadows = false;
                foreach (Renderer renderer in contents.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == lens)
                    {
                        continue;
                    }

                    foreach (Material legacyMaterial in renderer.sharedMaterials)
                    {
                        if (legacyMaterial != null && (legacyMaterial.name.IndexOf("scope_back", StringComparison.OrdinalIgnoreCase) >= 0 || legacyMaterial.name.IndexOf("scope_zwrite", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            renderer.enabled = false;
                        }
                    }
                }

                Transform axis = FindOrCreateChild(contents.transform, "OpticalAxis");
                axis.localPosition = OCULAR_CENTER;
                axis.localRotation = Quaternion.identity;
                axis.localScale = Vector3.one;
                Transform cameraTransform = FindOrCreateChild(contents.transform, "ScopeCamera");
                cameraTransform.localPosition = OCULAR_CENTER - Vector3.forward * (eyeReliefMillimeters * 0.001f);
                cameraTransform.localRotation = Quaternion.identity;
                cameraTransform.localScale = Vector3.one;
                Camera camera = cameraTransform.GetComponent<Camera>();
                if (camera == null)
                {
                    camera = cameraTransform.gameObject.AddComponent<Camera>();
                }

                camera.enabled = false;
                camera.tag = "Untagged";
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 10000f;
                camera.fieldOfView = 4f;
                camera.orthographic = false;
                camera.allowHDR = true;
                camera.allowMSAA = false;
                camera.allowDynamicResolution = false;
                camera.cullingMask = ~(1 << LayerMask.NameToLayer("UI"));
                camera.targetTexture = null;
                HDAdditionalCameraData data = camera.GetComponent<HDAdditionalCameraData>();
                if (data == null)
                {
                    data = camera.gameObject.AddComponent<HDAdditionalCameraData>();
                }

                data.volumeLayerMask = camera.cullingMask;
                data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                data.allowDynamicResolution = false;
                data.xrRendering = false;
                ScopeOpticsRenderer optics = contents.GetComponent<ScopeOpticsRenderer>();
                if (optics == null)
                {
                    optics = contents.AddComponent<ScopeOpticsRenderer>();
                }

                var rendererSettings = new SerializedObject(optics);
                rendererSettings.FindProperty("_configuration").objectReferenceValue = configuration;
                rendererSettings.FindProperty("_scopeCamera").objectReferenceValue = camera;
                rendererSettings.FindProperty("_scopeCameraData").objectReferenceValue = data;
                rendererSettings.FindProperty("_opticalAxis").objectReferenceValue = axis;
                rendererSettings.FindProperty("_lensRenderer").objectReferenceValue = lens;
                rendererSettings.FindProperty("_materialIndex").intValue = 0;
                rendererSettings.FindProperty("_lensHalfSizeMeters").vector2Value = OCULAR_HALF_SIZE;
                rendererSettings.FindProperty("_followOpticalAxisScale").boolValue = true;
                rendererSettings.FindProperty("_resolution").intValue = 768;
                rendererSettings.ApplyModifiedPropertiesWithoutUndo();
                optics.enabled = false;
                ScopeOpticsWeaponBinding binding = contents.GetComponent<ScopeOpticsWeaponBinding>();
                if (binding == null)
                {
                    binding = contents.AddComponent<ScopeOpticsWeaponBinding>();
                }

                var bindingSettings = new SerializedObject(binding);
                bindingSettings.FindProperty("_renderer").objectReferenceValue = optics;
                bindingSettings.FindProperty("_activationAimFactor").floatValue = 0.95f;
                bindingSettings.FindProperty("_magnification").floatValue = 8f;
                bindingSettings.ApplyModifiedPropertiesWithoutUndo();
                binding.enabled = true;
                PrefabUtility.SaveAsPrefabAsset(contents, MARCH_PREFAB, out bool saved);
                if (!saved)
                {
                    throw new InvalidOperationException("March Shorty prefab could not be saved.");
                }

                ConfigureAimOffset(aimOffsetPosition);
                return VerifyMarchShorty();
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        public static string VerifyMarchShorty()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MARCH_PREFAB);
            ScopeOpticsRenderer optics = prefab != null ? prefab.GetComponent<ScopeOpticsRenderer>() : null;
            ScopeOpticsWeaponBinding binding = prefab != null ? prefab.GetComponent<ScopeOpticsWeaponBinding>() : null;
            Camera camera = prefab != null ? prefab.GetComponentInChildren<Camera>(true) : null;
            if (optics == null || binding == null || camera == null || optics.Configuration == null || optics.enabled || camera.enabled || !binding.enabled)
            {
                throw new InvalidOperationException("March prefab requires a configured, inactive renderer and camera, plus an enabled owner binding.");
            }

            if (!optics.Configuration.TryValidate(out string diagnostic))
            {
                throw new InvalidOperationException(diagnostic);
            }

            return JsonUtility.ToJson(new SetupReport(prefab, optics, binding, camera), true);
        }

        private static ScopeOpticsConfig ConfigureOptics()
        {
            ScopeOpticsConfig configuration = AssetDatabase.LoadAssetAtPath<ScopeOpticsConfig>(CONFIGURATION_PATH);
            if (configuration == null)
            {
                configuration = ScriptableObject.CreateInstance<ScopeOpticsConfig>();
                AssetDatabase.CreateAsset(configuration, CONFIGURATION_PATH);
            }

            var settings = new SerializedObject(configuration);
            settings.FindProperty("_minMagnification").floatValue = 1f;
            settings.FindProperty("_maxMagnification").floatValue = 8f;
            settings.FindProperty("_calibrationMagnification").floatValue = 8f;
            settings.FindProperty("_projectionMode").enumValueIndex = (int)ScopeProjectionMode.GeometricLens;
            settings.FindProperty("_trueOnePowerCalibration").floatValue = 1f;
            settings.FindProperty("_fieldOfViewMultiplier").animationCurveValue = AnimationCurve.Linear(1f, 1f, 8f, 1f);
            settings.FindProperty("_reticlePlane").enumValueIndex = (int)ScopeReticlePlane.SecondFocalPlane;
            settings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(configuration);
            AssetDatabase.SaveAssetIfDirty(configuration);
            return configuration;
        }

        private static void ConfigureAimOffset(Vector3 offset)
        {
            UnityEngine.Object item = AssetDatabase.LoadMainAssetAtPath(MARCH_ITEM);
            var settings = new SerializedObject(item);
            SerializedProperty components = settings.FindProperty("_components");
            for (int i = 0; i < components.arraySize; i++)
            {
                SerializedProperty component = components.GetArrayElementAtIndex(i);
                SerializedProperty section = component.FindPropertyRelative("_section");
                if (section != null && section.stringValue == "march_f_shorty")
                {
                    component.FindPropertyRelative("_overrideAimOffsetPosition").boolValue = true;
                    component.FindPropertyRelative("_aimOffsetPosition").vector3Value = offset;
                    settings.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(item);
                    AssetDatabase.SaveAssetIfDirty(item);
                    return;
                }
            }

            throw new InvalidOperationException("March item definition lacks its existing WeaponAddonComponent.");
        }

        private static Renderer FindOcular(GameObject root)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : filter != null ? filter.sharedMesh : null;
                if (mesh != null && mesh.name.IndexOf("shorty", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return renderer;
                }
            }

            return null;
        }

        private static Transform FindOrCreateChild(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            if (child == null)
            {
                child = new GameObject(childName).transform;
                child.SetParent(parent, false);
            }

            return child;
        }

        private static void EnsureDirectory(string directory)
        {
            if (AssetDatabase.IsValidFolder(directory))
            {
                return;
            }

            string parent = System.IO.Path.GetDirectoryName(directory).Replace('\\', '/');
            EnsureDirectory(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(directory));
        }

        private static void AssertAssetEditing()
        {
            string project = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..")).TrimEnd('\\', '/');
            if (Application.isPlaying || !project.Equals("E:\\Projects\\stalker_raid_into_darkness", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("March setup requires Edit Mode in the production project.");
            }

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == MARCH_PREFAB)
            {
                throw new InvalidOperationException("Close the March prefab stage before configuring its asset.");
            }
        }

        [Serializable]
        private sealed class SetupReport
        {
            [SerializeField] private string _prefab;
            [SerializeField] private string _configuration;
            [SerializeField] private string _reticle;
            [SerializeField] private string _material;
            [SerializeField] private bool _rendererDisabled;
            [SerializeField] private bool _cameraDisabled;
            [SerializeField] private bool _bindingEnabled;
            [SerializeField] private Vector2 _localHalfSize;

            public SetupReport(GameObject prefab, ScopeOpticsRenderer renderer, ScopeOpticsWeaponBinding binding, Camera camera)
            {
                _prefab = AssetDatabase.GetAssetPath(prefab);
                _configuration = AssetDatabase.GetAssetPath(renderer.Configuration);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(MATERIAL_PATH);
                _reticle = material != null ? AssetDatabase.GetAssetPath(material.GetTexture("_Reticle")) : string.Empty;
                _material = MATERIAL_PATH;
                _rendererDisabled = !renderer.enabled;
                _cameraDisabled = !camera.enabled;
                _bindingEnabled = binding.enabled;
                _localHalfSize = OCULAR_HALF_SIZE;
            }
        }

        [Serializable]
        private sealed class InspectionReport
        {
            [SerializeField] private string _project;
            [SerializeField] private string _prefabStage;
            [SerializeField] private string _activeScene;
            [SerializeField] private int _dirtyScenes;
            [SerializeField] private List<MeshObservation> _meshes = new List<MeshObservation>();

            public InspectionReport()
            {
                _project = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                _prefabStage = stage != null ? stage.assetPath : string.Empty;
                _activeScene = SceneManager.GetActiveScene().path;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    if (SceneManager.GetSceneAt(i).isDirty)
                    {
                        _dirtyScenes++;
                    }
                }

                Observe(MARCH_PREFAB);
                Observe(MARCH_MODEL);
            }

            private void Observe(string assetPath)
            {
                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (root == null)
                {
                    throw new InvalidOperationException("Scope asset is unavailable: " + assetPath);
                }

                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh != null)
                    {
                        _meshes.Add(new MeshObservation(assetPath, root.transform, filter.transform, filter.sharedMesh, filter.GetComponent<Renderer>()));
                    }
                }

                foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer.sharedMesh != null)
                    {
                        _meshes.Add(new MeshObservation(assetPath, root.transform, renderer.transform, renderer.sharedMesh, renderer));
                    }
                }
            }
        }

        [Serializable]
        private sealed class MeshObservation
        {
            [SerializeField] private string _asset;
            [SerializeField] private string _path;
            [SerializeField] private string _mesh;
            [SerializeField] private int _vertexCount;
            [SerializeField] private bool _readable;
            [SerializeField] private bool _active;
            [SerializeField] private string[] _materials;
            [SerializeField] private Vector3 _boundsCenter;
            [SerializeField] private Vector3 _boundsSize;
            [SerializeField] private Vector3 _averageNormal;
            [SerializeField] private Vector2 _uvMinimum;
            [SerializeField] private Vector2 _uvMaximum;
            [SerializeField] private Vector3[] _positions;
            [SerializeField] private string _readError;

            public MeshObservation(string assetPath, Transform root, Transform transform, Mesh mesh, Renderer renderer)
            {
                _asset = assetPath;
                _path = AnimationUtility.CalculateTransformPath(transform, root);
                _mesh = mesh.name;
                _vertexCount = mesh.vertexCount;
                _readable = mesh.isReadable;
                _active = transform.gameObject.activeSelf;
                Material[] materials = renderer != null ? renderer.sharedMaterials : Array.Empty<Material>();
                _materials = new string[materials.Length];
                for (int i = 0; i < materials.Length; i++)
                {
                    _materials[i] = materials[i] != null ? materials[i].name : string.Empty;
                }

                Matrix4x4 matrix = root.worldToLocalMatrix * transform.localToWorldMatrix;
                _boundsCenter = matrix.MultiplyPoint3x4(mesh.bounds.center);
                _boundsSize = mesh.bounds.size;
                try
                {
                    Vector3[] positions = mesh.vertices;
                    Bounds bounds = new Bounds(matrix.MultiplyPoint3x4(positions[0]), Vector3.zero);
                    for (int i = 0; i < positions.Length; i++)
                    {
                        positions[i] = matrix.MultiplyPoint3x4(positions[i]);
                        bounds.Encapsulate(positions[i]);
                    }

                    _boundsCenter = bounds.center;
                    _boundsSize = bounds.size;
                    if (positions.Length <= 256)
                    {
                        _positions = positions;
                    }

                    foreach (Vector3 normal in mesh.normals)
                    {
                        _averageNormal += matrix.MultiplyVector(normal);
                    }

                    _averageNormal.Normalize();
                    Vector2[] uv = mesh.uv;
                    if (uv.Length > 0)
                    {
                        _uvMinimum = uv[0];
                        _uvMaximum = uv[0];
                        foreach (Vector2 coordinate in uv)
                        {
                            _uvMinimum = Vector2.Min(_uvMinimum, coordinate);
                            _uvMaximum = Vector2.Max(_uvMaximum, coordinate);
                        }
                    }
                }
                catch (Exception exception)
                {
                    _readError = exception.Message;
                }
            }
        }
    }
}

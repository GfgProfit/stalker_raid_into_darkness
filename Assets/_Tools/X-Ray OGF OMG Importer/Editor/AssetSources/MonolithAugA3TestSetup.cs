using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using XRay.Importer.Core.Ogf;
using XRay.Importer.Sources.Dds;
using XRay.Importer.Sources.Textures;
using XRay.Importer.Sources.Vfs;
#if XRAY_HDRP
using UnityEngine.Rendering.HighDefinition;
#endif

namespace XRay.Importer.Editor
{
    public static class MonolithAugA3TestSetup
    {
        public const string FOLDER = "Assets/_Source/Items/Weapons/Tests/MonolithHDRP";
        public const string MODEL_PATH = "Assets/_Source/Items/Weapons/Models/wpn_aug_a3.ogf";
        public const string ALBEDO_PATH = "Assets/_Source/Items/Weapons/Models/Textures/aug_a3.png";
        public const string MATERIAL_PATH = FOLDER + "/AUG_A3_Monolith_Test.mat";
        public const string SCENE_PATH = FOLDER + "/AUG_A3_Monolith_Comparison.unity";
        public const string PREFAB_PATH = FOLDER + "/AUG_A3_Monolith_Test.prefab";

        public static string RestoreLegacyMaterial()
        {
            var reference = AssetDatabase.LoadAssetAtPath<Material>(FOLDER + "/AUG_A3_Legacy_Reference.mat");
            var original = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Source/Items/Weapons/Models/Materials/aug_a3.mat");
            if (reference == null || reference.shader.name != "HDRP/Lit")
            {
                throw new InvalidOperationException("The legacy material reference is unavailable.");
            }

            EditorUtility.CopySerialized(reference, original);
            EditorUtility.SetDirty(original);
            AssetDatabase.SaveAssetIfDirty(original);
            return original.shader.name;
        }

        public static string Inspect()
        {
            XRayTextureResolution resolution = Resolve();
            OgfModel model = OgfReader.ReadFile(File.ReadAllBytes(MODEL_PATH), MODEL_PATH);
            var lines = new List<string>();
            lines.Add("Project=" + Path.GetFullPath("."));
            lines.Add("Unity=" + Application.unityVersion);
            lines.Add("Pipeline=" + GraphicsSettings.currentRenderPipeline?.name);
            lines.Add("HDRPLitLookup=" + AssetDatabase.GetAssetPath(Shader.Find("HDRP/Lit")));
            lines.Add("MonolithLookup=" + AssetDatabase.GetAssetPath(Shader.Find(MonolithMaterialConfigurator.SHADER_NAME)));
            lines.Add("Diffuse=" + resolution.Diffuse.Winner?.FullPath);
            lines.Add("Metadata=" + resolution.DescriptionOrigin);
            XRayTextureDescription d = resolution.Description;
            lines.Add($"THM id={d?.MaterialId}, weight={d?.MaterialWeight:R}, bumpMode={d?.BumpMode}, virtualHeight={d?.VirtualHeight:R}, flags={d?.Flags}, detail={d?.DetailName}");
            lines.Add("Bump=" + resolution.Bump?.Winner?.FullPath);
            lines.Add("Correction=" + resolution.BumpCorrection?.Winner?.FullPath);
            byte[] diffuseData = File.ReadAllBytes(resolution.Diffuse.Winner.FullPath);
            DdsHeader diffuseHeader = DdsHeader.Parse(diffuseData);
            byte[] diffusePixels = DdsDecoder.DecodeTopLevel(diffuseData, diffuseHeader);
            byte alphaMin = 255;
            byte alphaMax = 0;
            for (int i = 3; i < diffusePixels.Length; i += 4)
            {
                alphaMin = Math.Min(alphaMin, diffusePixels[i]);
                alphaMax = Math.Max(alphaMax, diffusePixels[i]);
            }

            lines.Add($"SourceDiffuse={diffuseHeader.Description}, alpha=[{alphaMin},{alphaMax}]");
            foreach (OgfVisual visual in model.GetMeshVisuals())
            {
                lines.Add($"Mesh vertices={visual.VertexCount}, texture={visual.Texture}, shader={visual.Shader}, U=[{visual.U.Min():R},{visual.U.Max():R}], V=[{visual.V.Min():R},{visual.V.Max():R}]");
            }

            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(ALBEDO_PATH);
            lines.Add($"Albedo={albedo.width}x{albedo.height}, {albedo.format}, sRGB={albedo.isDataSRGB}, mips={albedo.mipmapCount}");
            var packed = AssetDatabase.LoadAssetAtPath<Texture2D>(FOLDER + "/aug_a3_bump_packed.png");
            if (packed != null)
            {
                lines.Add($"Packed={packed.width}x{packed.height}, {packed.format}, sRGB={packed.isDataSRGB}, graphics={packed.graphicsFormat}, mips={packed.mipmapCount}");
                var serialized = new SerializedObject(AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(packed)));
                SerializedProperty property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.name.IndexOf("rgb", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        lines.Add("PackedImporterProperty=" + property.propertyPath);
                    }
                }
            }
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(MODEL_PATH);
            foreach (Renderer renderer in source.GetComponentsInChildren<Renderer>(true))
            {
                lines.Add(renderer.name + ": " + string.Join(", ", renderer.sharedMaterials.Select(m => AssetDatabase.GetAssetPath(m) + " [" + m.shader.name + "]")));
            }

            return string.Join("\n", lines);
        }

        [MenuItem("Tools/X-Ray/Monolith HDRP/Create AUG A3 Comparison")]
        public static void Create()
        {
            CreateAssets();
        }

        public static string CreateAssets()
        {
            Scene comparisonScene = SceneManager.GetSceneByPath(SCENE_PATH);
            if (comparisonScene.isLoaded && comparisonScene.isDirty)
            {
                throw new InvalidOperationException("Save the AUG comparison scene before recreating its assets.");
            }

            if (!(GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset))
            {
                throw new InvalidOperationException("The active project must use HDRP.");
            }

            XRayTextureResolution resolution = Resolve();
            Directory.CreateDirectory(FOLDER);
            var options = new XRayImportOptions();
            var importer = new XRayTextureImporter(options, false, new HashSet<string>());
            XRayTextureAssetResult packed = importer.ImportPacked(resolution.Bump, FOLDER);
            XRayTextureAssetResult correction = importer.ImportPacked(resolution.BumpCorrection, FOLDER);
            importer.ImportPending();
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(ALBEDO_PATH);
            var bump = packed.AssetPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(packed.AssetPath) : null;
            var bumpCorrection = correction.AssetPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(correction.AssetPath) : null;
            if (bump != null && bump.isDataSRGB)
            {
                throw new InvalidOperationException("Packed X-Ray bump must be imported as linear data.");
            }
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MATERIAL_PATH);
            if (material == null)
            {
                material = new Material(Shader.Find(MonolithMaterialConfigurator.SHADER_NAME));
                AssetDatabase.CreateAsset(material, MATERIAL_PATH);
            }

            MonolithMaterialConfigurator.Configure(material, resolution.Description, albedo, bump, bumpCorrection, true);
            AssetDatabase.SaveAssetIfDirty(material);
            Scene original = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(MODEL_PATH);
                GameObject legacy = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                legacy.name = "AUG_A3_HDRP_Lit_Legacy";
                GameObject monolith = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                monolith.name = "AUG_A3_Monolith";
                legacy.transform.rotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
                monolith.transform.rotation = legacy.transform.rotation;
                foreach (Renderer renderer in monolith.GetComponentsInChildren<Renderer>(true))
                {
                    bool usesAugAtlas = renderer.sharedMaterials.All(m => m.GetTexture("_BaseColorMap") == albedo);
                    renderer.enabled = usesAugAtlas;
                    if (usesAugAtlas)
                    {
                        renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                    }
                }

                foreach (Renderer renderer in legacy.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = renderer.sharedMaterials.All(m => m.GetTexture("_BaseColorMap") == albedo);
                }

                Bounds bounds = GetBounds(legacy);
                float spacing = Mathf.Max(bounds.size.x, 0.5f) + 0.3f;
                legacy.transform.position += new Vector3(-spacing * 0.5f, -bounds.min.y + 0.1f, 0.0f);
                monolith.transform.position = legacy.transform.position + Vector3.right * spacing;
                PrefabUtility.SaveAsPrefabAsset(monolith, PREFAB_PATH);
                bounds = GetBounds(legacy);
                bounds.Encapsulate(GetBounds(monolith));
                CreateEnvironment(scene, bounds);
                EditorSceneManager.SaveScene(scene, SCENE_PATH);
            }
            finally
            {
                SceneManager.SetActiveScene(original);
                EditorSceneManager.CloseScene(scene, true);
            }

            return Inspect() + "\n" + Validate();
        }

        public static string Validate()
        {
            Shader shader = Shader.Find(MonolithMaterialConfigurator.SHADER_NAME);
            string[] errors = ShaderUtil.GetShaderMessages(shader).Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).Select(m => m.message + " at " + m.file + ":" + m.line).ToArray();
            if (errors.Length > 0)
            {
                throw new InvalidOperationException(string.Join("\n", errors));
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MATERIAL_PATH);
            if (prefab == null || material == null || material.shader != shader || material.GetTexture("_MonolithBump") == null || material.GetTexture("_BaseColorMap") != AssetDatabase.LoadAssetAtPath<Texture2D>(ALBEDO_PATH))
            {
                throw new InvalidOperationException("AUG A3 test assets are incomplete.");
            }

            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.enabled && renderer.sharedMaterials.Any(m => m != material))
                {
                    throw new InvalidOperationException("Test renderer material assignment is invalid: " + renderer.name);
                }
            }

            return $"Validated {prefab.GetComponentsInChildren<Renderer>(true).Length} renderers, shaderErrors={errors.Length}, shaderSupported={shader.isSupported}";
        }

        public static string Capture()
        {
            return CaptureCase("Comparison", string.Empty, 0, 1.0f, true, true, 0.0f);
        }

        public static string CaptureProbeOnly()
        {
            return CaptureCase("ProbeOnly", "None", 0, 1.0f, true, true, 0.0f);
        }

        public static string CaptureLightingMatrix()
        {
            var results = new List<string>();
            results.Add(Capture());
            results.Add(CaptureCase("Directional", "Directional Key", 0, 1.0f, true, true, 0.0f));
            results.Add(CaptureCase("Point", "Point Fill", 0, 1.0f, true, true, 0.0f));
            results.Add(CaptureCase("Spot", "Spot Rim", 0, 1.0f, true, true, 0.0f));
            results.Add(CaptureCase("Ambient", "None", 0, 1.0f, true, true, 0.0f));
            results.Add(CaptureCase("NoProbe", "None", 0, 1.0f, true, false, 0.0f));
            results.Add(CaptureCase("NoShadows", string.Empty, 0, 1.0f, false, true, 0.0f));
            results.Add(CaptureCase("RotatedKey", "Directional Key", 0, 1.0f, true, true, 70.0f));
            results.Add(CaptureCase("GlossHalf", string.Empty, 0, 0.5f, true, true, 0.0f));
            for (int debug = 1; debug <= 7; debug++)
            {
                results.Add(CaptureCase("Debug" + debug, string.Empty, debug, 1.0f, true, true, 0.0f));
            }

            return string.Join("\n", results);
        }

        private static string CaptureCase(string suffix, string lightName, int debug, float glossScale, bool shadows, bool probes, float lightYaw)
        {
            Scene original = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(SCENE_PATH);
            if (scene.isLoaded && scene.isDirty)
            {
                throw new InvalidOperationException("Save the AUG comparison scene before capturing it.");
            }

            if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Additive);
            }
            var suspended = new List<Behaviour>();
            Camera camera = null;
            RenderTexture target = null;
            Texture2D image = null;
            Material transientMaterial = null;
            string statistics = string.Empty;
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Camera>()).Single();
                camera.scene = scene;
                camera.cullingMask = 1 << 31;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    {
                        child.gameObject.layer = 31;
                    }
                }

                IEnumerable<Behaviour> external = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Cast<Behaviour>().Concat(UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None));
#if XRAY_HDRP
                external = external.Concat(UnityEngine.Object.FindObjectsByType<HDAdditionalReflectionData>(FindObjectsSortMode.None));
#endif
                foreach (Behaviour behaviour in external)
                {
                    if (behaviour.gameObject.scene != scene && behaviour.enabled)
                    {
                        suspended.Add(behaviour);
                        behaviour.enabled = false;
                    }
                }

                var reference = AssetDatabase.LoadAssetAtPath<Material>(MATERIAL_PATH);
                transientMaterial = new Material(reference);
                transientMaterial.SetFloat("_MonolithDebug", debug);
                transientMaterial.SetFloat("_MonolithGlossScale", glossScale);
                foreach (Renderer renderer in scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Renderer>(true)))
                {
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(m => m == reference ? transientMaterial : m).ToArray();
                }

                foreach (Light light in scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Light>(true)))
                {
                    light.cullingMask = 1 << 31;
                    light.enabled = string.IsNullOrEmpty(lightName) || light.name == lightName;
                    light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
                    if (light.type == LightType.Directional)
                    {
                        light.transform.Rotate(Vector3.up, lightYaw, Space.World);
                    }
                }

#if XRAY_HDRP
                foreach (HDAdditionalReflectionData probe in scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<HDAdditionalReflectionData>(true)))
                {
                    probe.enabled = probes;
                }
#endif
                foreach (ReflectionProbe probe in scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<ReflectionProbe>(true)))
                {
                    probe.enabled = probes;
                }
                target = new RenderTexture(1400, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                camera.targetTexture = target;
#if XRAY_HDRP
                ((HDRenderPipeline)RenderPipelineManager.currentPipeline).RequestSkyEnvironmentUpdate();
#endif
                camera.Render();
                camera.Render();
                RenderTexture.active = target;
                image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                Directory.CreateDirectory("scratchpad/MonolithHDRP");
                File.WriteAllBytes("scratchpad/MonolithHDRP/AUG_A3_" + suffix + ".png", image.EncodeToPNG());
                Color32[] pixels = image.GetPixels32();
                double mean = pixels.Average(p => (p.r + p.g + p.b) / 765.0);
                statistics = suffix + ": meanEncoded=" + mean.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
#if XRAY_HDRP
                HDCamera hdCamera = HDCamera.GetOrCreate(camera);
                statistics += $", cameraType={camera.cameraType}, exposure={hdCamera.volumeStack.GetComponent<Exposure>().fixedExposure.value}, sky={hdCamera.volumeStack.GetComponent<VisualEnvironment>().skyType.value}, probes={hdCamera.frameSettings.IsEnabled(FrameSettingsField.ReflectionProbe)}, skyReflection={hdCamera.frameSettings.IsEnabled(FrameSettingsField.SkyReflection)}";
#endif
            }
            finally
            {
                RenderTexture.active = previous;
                if (camera != null)
                {
                    camera.targetTexture = null;
                }

                if (target != null)
                {
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }

                if (image != null)
                {
                    UnityEngine.Object.DestroyImmediate(image);
                }

                if (transientMaterial != null)
                {
                    UnityEngine.Object.DestroyImmediate(transientMaterial);
                }

                foreach (Behaviour behaviour in suspended)
                {
                    if (behaviour != null)
                    {
                        behaviour.enabled = true;
                    }
                }

                SceneManager.SetActiveScene(original);
                EditorSceneManager.CloseScene(scene, true);
            }

            return statistics + "; " + Validate();
        }

        private static XRayTextureResolution Resolve()
        {
            XRayAssetMarker marker = XRayAssetMarker.ReadEffective(ALBEDO_PATH);
            if (marker == null || !File.Exists(marker.SourcePath))
            {
                throw new InvalidOperationException("The original AUG atlas source recorded by X-Ray Importer is unavailable.");
            }

            DirectoryInfo directory = new FileInfo(marker.SourcePath).Directory;
            while (directory != null && !string.Equals(directory.Name, "textures", StringComparison.OrdinalIgnoreCase))
            {
                directory = directory.Parent;
            }

            if (directory?.Parent == null)
            {
                throw new InvalidOperationException("The recorded AUG atlas source is outside an X-Ray textures folder.");
            }

            var configuration = new XRaySourceConfiguration { Mode = XRaySourceMode.CustomFolder, CustomFolder = directory.Parent.FullName };
            configuration.IndexScope.Clear();
            configuration.IndexScope.Add("textures/wpn/aug_a3");
            configuration.IndexScope.Add("textures.ltx");
            XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(configuration);
            if (!plan.IsValid)
            {
                throw new InvalidOperationException(string.Join("\n", plan.Errors));
            }

            XRayAssetIndex index = XRayAssetIndex.Build(plan);
            return new XRayTextureResolver(index).Resolve("wpn\\aug_a3\\aug_a3");
        }

        private static Bounds GetBounds(GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
            }

            return bounds;
        }

        private static void CreateEnvironment(Scene scene, Bounds bounds)
        {
#if XRAY_HDRP
            var cameraObject = new GameObject("Comparison Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<HDAdditionalCameraData>();
            camera.fieldOfView = 35.0f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100.0f;
            camera.transform.position = bounds.center + new Vector3(0.1f, 0.45f, -1.7f).normalized * bounds.size.magnitude * 1.25f;
            camera.transform.LookAt(bounds.center);
            cameraObject.tag = "MainCamera";
            var volumeObject = new GameObject("Neutral Lighting Volume");
            volumeObject.layer = 31;
            SceneManager.MoveGameObjectToScene(volumeObject, scene);
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1000.0f;
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(FOLDER + "/ComparisonVolume.asset");
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, FOLDER + "/ComparisonVolume.asset");
                VisualEnvironment environment = profile.Add<VisualEnvironment>(true);
                environment.skyType.Override((int)SkyType.Gradient);
                GradientSky sky = profile.Add<GradientSky>(true);
                sky.top.Override(new Color(0.35f, 0.38f, 0.42f));
                sky.middle.Override(new Color(0.5f, 0.5f, 0.5f));
                sky.bottom.Override(new Color(0.15f, 0.15f, 0.15f));
                sky.updateMode.Override(EnvironmentUpdateMode.OnChanged);
                Exposure exposure = profile.Add<Exposure>(true);
                exposure.mode.Override(ExposureMode.Fixed);
                exposure.fixedExposure.Override(10.0f);
                foreach (VolumeComponent component in profile.components)
                {
                    AssetDatabase.AddObjectToAsset(component, profile);
                }

                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
            }

            volume.sharedProfile = profile;
            if (profile.TryGet(out GradientSky gradientSky))
            {
                gradientSky.exposure.Override(9.0f);
                EditorUtility.SetDirty(gradientSky);
                AssetDatabase.SaveAssetIfDirty(gradientSky);
            }

            if (profile.TryGet(out Exposure fixedExposure))
            {
                fixedExposure.fixedExposure.Override(8.0f);
                EditorUtility.SetDirty(fixedExposure);
                AssetDatabase.SaveAssetIfDirty(fixedExposure);
            }

            cameraObject.GetComponent<HDAdditionalCameraData>().volumeLayerMask = 1 << 31;
            CreateLight(scene, "Directional Key", LightType.Directional, new Vector3(40.0f, -35.0f, 0.0f), Vector3.zero, 5000.0f);
            CreateLight(scene, "Point Fill", LightType.Point, Vector3.zero, bounds.center + new Vector3(-0.8f, 0.6f, -0.5f), 500.0f);
            CreateLight(scene, "Spot Rim", LightType.Spot, new Vector3(30.0f, 180.0f, 0.0f), bounds.center + new Vector3(0.6f, 0.6f, 0.8f), 1000.0f);
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Neutral Ground";
            SceneManager.MoveGameObjectToScene(floor, scene);
            floor.transform.position = new Vector3(bounds.center.x, -0.035f, bounds.center.z);
            floor.transform.localScale = new Vector3(8.0f, 0.05f, 8.0f);
            Material floorMaterial = AssetDatabase.LoadAssetAtPath<Material>(FOLDER + "/NeutralGround.mat");
            if (floorMaterial == null)
            {
                floorMaterial = new Material(Shader.Find("HDRP/Lit"));
                floorMaterial.SetColor("_BaseColor", new Color(0.22f, 0.22f, 0.22f));
                floorMaterial.SetFloat("_Smoothness", 0.1f);
                AssetDatabase.CreateAsset(floorMaterial, FOLDER + "/NeutralGround.mat");
            }

            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            var probeObject = new GameObject("Reflection Probe");
            SceneManager.MoveGameObjectToScene(probeObject, scene);
            probeObject.transform.position = bounds.center + Vector3.up;
            ReflectionProbe probe = probeObject.AddComponent<ReflectionProbe>();
            HDAdditionalReflectionData hdProbe = probeObject.AddComponent<HDAdditionalReflectionData>();
            hdProbe.mode = ProbeSettings.Mode.Custom;
            hdProbe.influenceVolume.boxSize = Vector3.one * 10.0f;
            Cubemap cubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(FOLDER + "/NeutralReflection.asset");
            if (cubemap == null)
            {
                cubemap = new Cubemap(32, TextureFormat.RGBAHalf, true);
                for (int face = 0; face < 6; face++)
                {
                    Color color = new Color(100.0f, 100.0f, 100.0f) * (face == 2 ? 2.0f : face == 3 ? 0.1f : 1.0f);
                    cubemap.SetPixels(Enumerable.Repeat(color, 32 * 32).ToArray(), (CubemapFace)face);
                }

                cubemap.Apply(true, false);
                AssetDatabase.CreateAsset(cubemap, FOLDER + "/NeutralReflection.asset");
            }

            hdProbe.SetTexture(ProbeSettings.Mode.Custom, cubemap);
            probe.customBakedTexture = cubemap;
            probe.mode = ReflectionProbeMode.Custom;
            probe.size = Vector3.one * 10.0f;
            probe.resolution = 128;
#endif
        }

        private static void CreateLight(Scene scene, string name, LightType type, Vector3 angles, Vector3 position, float intensity)
        {
            var lightObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            Light light = lightObject.AddComponent<Light>();
            light.type = type;
            light.color = Color.white;
            light.lightUnit = type == LightType.Directional ? LightUnit.Lux : LightUnit.Lumen;
            light.intensity = intensity;
            light.range = 5.0f;
            light.spotAngle = 80.0f;
            light.shadows = LightShadows.Soft;
            lightObject.transform.SetPositionAndRotation(position, Quaternion.Euler(angles));
#if XRAY_HDRP
            lightObject.AddComponent<HDAdditionalLightData>();
#endif
        }
    }
}

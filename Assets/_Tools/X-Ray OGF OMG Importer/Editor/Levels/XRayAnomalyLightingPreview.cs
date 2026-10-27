using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayAnomalyLightingPreview
    {
        private const string LEVEL_ROOT = "Assets/_Source/Levels/Anomaly/1v2_DX11_v_12gb_2.5_168_662acc8d/l01_escape";
        private const string SCENE_PATH = LEVEL_ROOT + "/Scenes/l01_escape.unity";
        private const string PREVIEW_ROOT = LEVEL_ROOT + "/LightingPreview";
        private const string MATERIAL_ROOT = "Assets/_Source/Levels/Anomaly/1v2_DX11_v_12gb_2.5_168_662acc8d/Materials/";
        private const string RIG_NAME = "Monolith Lighting (Phase 02)";

        [MenuItem("Tools/X-Ray/Levels/Apply Phase 02 Lighting to l01_escape")]
        public static void Apply()
        {
            Scene scene = RequireScene();
            if (scene.isDirty) throw new InvalidOperationException("Save l01_escape before applying the lighting preview.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before applying the lighting preview.");
            EnsureFolder(PREVIEW_ROOT + "/Materials");
            var materials = new Dictionary<Material, Material>();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Apply Phase 02 lighting to l01_escape");
            foreach (Renderer renderer in Components<Renderer>(scene))
            {
                Material[] replacements = renderer.sharedMaterials.Select(m => ConvertMaterial(m, materials)).ToArray();
                if (renderer.sharedMaterials.SequenceEqual(replacements)) continue;
                Undo.RecordObject(renderer, "Set Anomaly material profile");
                renderer.sharedMaterials = replacements;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            int index = 0;
            foreach (XRayLevelInstancedRenderer renderer in Components<XRayLevelInstancedRenderer>(scene))
            {
                XRayInstanceBatchSet source = renderer.BatchSet;
                if (source == null) continue;
                XRayInstanceBatchSet copy = CopyAsset(source, "Instances_" + index + ".asset");
                var batches = source.Batches.Select(b => new XRayInstanceBatch(b.Name, b.Mesh, b.Materials.Select(m => ConvertMaterial(m, materials)).ToArray(), b.Bounds, b.Matrices, b.Data, b.CastShadows, b.LodIndices)).ToList();
                XRayLevelLodSet lod = source.Lods;
                XRayLevelLodSet lodCopy = null;
                if (lod != null)
                {
                    lodCopy = CopyAsset(lod, "Lods_" + index + ".asset");
                    lodCopy.Initialize(ConvertMaterial(lod.Material, materials), lod.Spheres, lod.LodFactors, lod.Positions, lod.Uvs, lod.Hemi, lod.Sun, lod.Normals, lod.GeometryLod, lod.SsaLodA, lod.SsaLodB, lod.SsaDiscard);
                    SaveAsset(lodCopy);
                }
                copy.Initialize(source.LevelName, source.CellSize, batches, lodCopy);
                SaveAsset(copy);
                Undo.RecordObject(renderer, "Set Anomaly instance materials");
                renderer.BatchSet = copy;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                index++;
            }
            index = 0;
            foreach (XRayDetailVegetation renderer in Components<XRayDetailVegetation>(scene))
            {
                XRayDetailDatabase source = renderer.Database;
                if (source == null) continue;
                XRayDetailDatabase copy = CopyAsset(source, "Details_" + index + ".asset");
                var models = source.Models.Select(m => new XRayDetailModelEntry(m.Shader, m.Texture, m.Mesh, ConvertMaterial(m.Material, materials), m.InverseHeight, m.NoWaving, m.MinScale, m.MaxScale, m.InstanceCount)).ToList();
                copy.Initialize(source.LevelName, source.ChunkSize, source.Density, source.HeightScale, source.SlotSize, models, source.Chunks.ToList(), source.Instances, source.Slots, source.SwingNormal, source.SwingFast, source.InstanceCount, source.SlotCount);
                SaveAsset(copy);
                Undo.RecordObject(renderer, "Set Anomaly detail materials");
                renderer.Database = copy;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                index++;
            }
            GameObject rig = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RIG_NAME);
            if (rig == null)
            {
                rig = new GameObject(RIG_NAME);
                SceneManager.MoveGameObjectToScene(rig, scene);
                Undo.RegisterCreatedObjectUndo(rig, "Create Monolith lighting inputs");
            }
            MonolithWeatherInputs weather = rig.GetComponent<MonolithWeatherInputs>();
            if (weather == null) weather = Undo.AddComponent<MonolithWeatherInputs>(rig);
            MonolithSourceLighting lighting = rig.GetComponent<MonolithSourceLighting>();
            if (lighting == null) lighting = Undo.AddComponent<MonolithSourceLighting>(rig);
            Light[] lights = Components<Light>(scene).Where(l => l.isActiveAndEnabled).ToArray();
            Light sun = lights.FirstOrDefault(l => l.type == LightType.Directional);
            float[] localIntensities = lights.Where(l => l.type == LightType.Point || l.type == LightType.Spot).Select(l => l.intensity).Where(v => v > 0).OrderBy(v => v).ToArray();
            float localIntensity = localIntensities.Length > 0 ? localIntensities[localIntensities.Length / 2] : 1;
            Undo.RecordObject(lighting, "Configure native light calibration");
            var serializedLighting = new SerializedObject(lighting);
            serializedLighting.FindProperty("_calibration").vector2Value = new Vector2(1 / Mathf.Max(1, sun != null ? sun.intensity : 1), 1 / Mathf.Max(1, localIntensity));
            serializedLighting.ApplyModifiedProperties();
            lighting.Mode = MonolithSourceLighting.LightingMode.Calibrated;
            EditorUtility.SetDirty(lighting);
            Undo.RecordObject(weather, "Configure dry weather inputs");
            weather.SetInputs(0.71864f, sun != null ? new Vector3(sun.color.r, sun.color.g, sun.color.b) : Vector3.one, new Vector4(0.5f, 1, 0, 0), new Vector4(1.5f, 1, 0, 0), 0);
            EditorUtility.SetDirty(weather);
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(undoGroup);
            ConfigureSourceLighting(scene, rig);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + SCENE_PATH);
            Selection.activeGameObject = rig;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.sceneLighting = true;
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
            Debug.Log(Inspect());
        }

        [MenuItem("Tools/X-Ray/Levels/Enable Source Lighting in l01_escape")]
        public static void ApplySourceLighting()
        {
            Scene scene = RequireScene();
            if (scene.isDirty) throw new InvalidOperationException("Save l01_escape before changing the lighting setup.");
            GameObject rig = scene.GetRootGameObjects().Single(g => g.name == RIG_NAME);
            ConfigureSourceLighting(scene, rig);
            Undo.FlushUndoRecordObjects();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + SCENE_PATH);
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.sceneLighting = true;
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
            Debug.Log(Inspect());
        }

        private static void ConfigureSourceLighting(Scene scene, GameObject rig)
        {
            Light[] lights = Components<Light>(scene).Where(l => l.isActiveAndEnabled && (l.type == LightType.Directional || l.type == LightType.Point || l.type == LightType.Spot)).OrderBy(l => l.type == LightType.Directional ? 0 : 1).ThenBy(l => l.name).ToArray();
            for (int i = 0; i < lights.Length; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    bool samePosition = lights[i].type == LightType.Directional || (lights[i].transform.position - lights[j].transform.position).sqrMagnitude < 0.000001f;
                    bool sameDirection = lights[i].type == LightType.Point || (lights[i].transform.forward - lights[j].transform.forward).sqrMagnitude < 0.000001f;
                    bool sameRange = lights[i].type == LightType.Directional || Mathf.Abs(lights[i].range - lights[j].range) < 0.0001f;
                    if (lights[i].type == lights[j].type && samePosition && sameDirection && sameRange) throw new InvalidOperationException("Ambiguous source light binding: " + lights[i].name + " and " + lights[j].name);
                }
            }
            var colors = new Vector3[lights.Length];
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                float scale = light.type == LightType.Directional ? 1 : light.intensity / Mathf.Max(0.000001f, XRayLevelSceneBuilder.POINT_CANDELA_SCALE * light.range * light.range * 0.25f);
                colors[i] = new Vector3(light.color.r, light.color.g, light.color.b) * scale;
            }
            MonolithSourceLighting lighting = rig.GetComponent<MonolithSourceLighting>();
            Undo.RecordObject(lighting, "Bind source colors to all level lights");
            lighting.SetBindings(lights, colors);
            lighting.BoundRadianceScale = Mathf.Pow(2, XRayLevelSceneBuilder.FIXED_EXPOSURE);
            lighting.Mode = MonolithSourceLighting.LightingMode.Bound;
            EditorUtility.SetDirty(lighting);
            Volume volume = rig.GetComponent<Volume>();
            if (volume == null) volume = Undo.AddComponent<Volume>(rig);
            string profilePath = PREVIEW_ROOT + "/SourceLightingVolume.asset";
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
            }
            Exposure exposure = VolumeComponent<Exposure>(profile);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(XRayLevelSceneBuilder.FIXED_EXPOSURE);
            exposure.compensation.Override(0);
            VisualEnvironment environment = VolumeComponent<VisualEnvironment>(profile);
            environment.skyType.Override(SkySettings.GetUniqueID<GradientSky>());
            environment.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            GradientSky sky = VolumeComponent<GradientSky>(profile);
            sky.top.Override(new Color(0.35f, 0.4f, 0.48f));
            sky.middle.Override(new Color(0.55f, 0.55f, 0.55f));
            sky.bottom.Override(new Color(0.18f, 0.17f, 0.15f));
            sky.exposure.Override(XRayLevelSceneBuilder.FIXED_EXPOSURE);
            sky.updateMode.Override(EnvironmentUpdateMode.OnChanged);
            VolumeComponent<Tonemapping>(profile).mode.Override(TonemappingMode.None);
            VolumeComponent<Bloom>(profile).intensity.Override(0);
            VolumeComponent<ScreenSpaceReflection>(profile).enabled.Override(false);
            VolumeComponent<ScreenSpaceAmbientOcclusion>(profile).intensity.Override(0);
            VolumeComponent<GlobalIllumination>(profile).enable.Override(true);
            VolumeComponent<Fog>(profile).enabled.Override(false);
            foreach (UnityEngine.Rendering.VolumeComponent component in profile.components) SaveAsset(component);
            SaveAsset(profile);
            Undo.RecordObject(volume, "Set exposure for source lighting units");
            volume.isGlobal = true;
            volume.priority = 1000;
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(volume);
        }

        private static T VolumeComponent<T>(VolumeProfile profile) where T : UnityEngine.Rendering.VolumeComponent
        {
            if (!profile.TryGet(out T component))
            {
                component = profile.Add<T>(true);
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            return component;
        }

        [MenuItem("Tools/X-Ray/Levels/Apply Surface Lighting Fixes to l01_escape")]
        public static void ApplySurfaceLightingFixes()
        {
            Scene scene = RequireScene();
            if (scene.isDirty) throw new InvalidOperationException("Save l01_escape before applying surface lighting fixes.");
            MonolithSourceLighting lighting = Components<MonolithSourceLighting>(scene).Single();
            int opaque = 0;
            int emitters = 0;
            foreach (string path in AssetDatabase.FindAssets("t:Material", new[] { PREVIEW_ROOT + "/Materials" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material.shader.name != "HDRP/MonolithLit") continue;
                bool foliage = material.GetFloat("_MonolithLevelFlora") > 0.5f || material.GetFloat("_MonolithLevelDetail") > 0.5f || material.name.Contains("_tree_leaves");
                Undo.RecordObject(material, "Separate leaf transmission from opaque surfaces");
                material.SetFloat("_MonolithFloraSubsurface", foliage ? 1 : 0);
                SaveAsset(material);
                if (!foliage) opaque++;
                Material original = AssetDatabase.LoadAssetAtPath<Material>(MATERIAL_ROOT + path.Substring((PREVIEW_ROOT + "/Materials/").Length));
                if (original == null || !original.HasProperty("_EmissiveColor") || original.GetColor("_EmissiveColor").maxColorComponent <= 0) continue;
                Undo.RecordObject(material, "Match emission to bound lighting units");
                float scale = lighting.Mode == MonolithSourceLighting.LightingMode.Bound ? lighting.BoundRadianceScale : 1;
                material.SetColor("_EmissiveColor", original.GetColor("_EmissiveColor") * scale);
                material.SetFloat("_EmissiveIntensity", original.GetFloat("_EmissiveIntensity") * scale);
                SaveAsset(material);
                emitters++;
            }
            Volume volume = scene.GetRootGameObjects().Single(g => g.name == RIG_NAME).GetComponent<Volume>();
            GlobalIllumination gi = VolumeComponent<GlobalIllumination>(volume.sharedProfile);
            Undo.RecordObject(gi, "Enable screen space indirect lighting");
            gi.enable.Override(true);
            gi.tracing.Override(RayCastingMode.RayMarching);
            gi.ambientProbeDimmer.Override(1);
            SaveAsset(gi);
            SaveAsset(volume.sharedProfile);
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
            Debug.Log($"Surface lighting fixed: opaque={opaque}; emitters={emitters}; SSGI=RayMarching");
        }

        public static string Inspect()
        {
            Scene scene = RequireScene();
            var materials = new HashSet<Material>();
            foreach (Renderer renderer in Components<Renderer>(scene)) foreach (Material material in renderer.sharedMaterials) if (material != null && material.shader.name == "HDRP/MonolithLit") materials.Add(material);
            foreach (XRayLevelInstancedRenderer renderer in Components<XRayLevelInstancedRenderer>(scene))
            {
                if (renderer.BatchSet == null) continue;
                foreach (XRayInstanceBatch batch in renderer.BatchSet.Batches) foreach (Material material in batch.Materials) if (material != null && material.shader.name == "HDRP/MonolithLit") materials.Add(material);
                if (renderer.BatchSet.Lods != null) materials.Add(renderer.BatchSet.Lods.Material);
            }
            foreach (XRayDetailVegetation renderer in Components<XRayDetailVegetation>(scene)) if (renderer.Database != null) foreach (XRayDetailModelEntry model in renderer.Database.Models) if (model.Material != null && model.Material.shader.name == "HDRP/MonolithLit") materials.Add(model.Material);
            int profileOne = materials.Count(m => m.GetFloat("_MonolithSourceProfile") == 1);
            int local = materials.Count(m => AssetDatabase.GetAssetPath(m).StartsWith(PREVIEW_ROOT + "/", StringComparison.Ordinal));
            MonolithWeatherInputs weather = Components<MonolithWeatherInputs>(scene).SingleOrDefault();
            MonolithSourceLighting lighting = Components<MonolithSourceLighting>(scene).SingleOrDefault();
            int errors = ShaderUtil.GetShaderMessages(Shader.Find("HDRP/MonolithLit")).Count(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error);
            Camera camera = Components<Camera>(scene).First();
            Exposure exposure = VolumeManager.instance.isInitialized ? HDCamera.GetOrCreate(camera).volumeStack.GetComponent<Exposure>() : null;
            return FormattableString.Invariant($"scene={scene.path}; dirty={scene.isDirty}; materials={materials.Count}; profile1={profileOne}; sceneLocal={local}; weatherActive={Shader.GetGlobalFloat("_MonolithWeatherActive")}; gloss={(weather != null ? weather.DynamicGloss : -1)}; lighting={(lighting != null ? lighting.Mode.ToString() : "missing")}; lights={Components<Light>(scene).Count()}; sourceBindings={Shader.GetGlobalInt("_MonolithBoundLightCount")}; exposure={(exposure != null ? exposure.fixedExposure.value : -1)}; shaderErrors={errors}");
        }

        public static string Capture(string outputPath)
        {
            return CaptureInternal(outputPath);
        }

        private static string CaptureInternal(string outputPath, int renderCount = 16)
        {
            string fullPath = Path.GetFullPath(outputPath);
            if (!fullPath.StartsWith("E:\\Projects\\stalker_raid_into_darkness_Archive\\", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Capture output must be inside Archive.");
            Camera camera = Components<Camera>(RequireScene()).First();
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGBHalf);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            var image = new Texture2D(1280, 720, TextureFormat.RGBAFloat, false, true);
            var display = new Texture2D(1280, 720, TextureFormat.RGB24, false, true);
            try
            {
                camera.targetTexture = target;
                for (int i = 0; i < renderCount; i++) camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.ChangeExtension(fullPath, ".exr"), image.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat | Texture2D.EXRFlags.CompressZIP));
                Color[] colors = image.GetPixels();
                for (int i = 0; i < colors.Length; i++) colors[i] = new Color(Mathf.LinearToGammaSpace(Mathf.Clamp01(colors[i].r)), Mathf.LinearToGammaSpace(Mathf.Clamp01(colors[i].g)), Mathf.LinearToGammaSpace(Mathf.Clamp01(colors[i].b)), 1);
                display.SetPixels(colors);
                display.Apply();
                File.WriteAllBytes(fullPath, display.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(display);
            }
            return Inspect();
        }

        public static string CaptureCalibratedReference(string outputPath)
        {
            Scene scene = RequireScene();
            MonolithSourceLighting lighting = Components<MonolithSourceLighting>(scene).Single();
            MonolithSourceLighting.LightingMode previousMode = lighting.Mode;
            try
            {
                lighting.Mode = MonolithSourceLighting.LightingMode.Calibrated;
                return Capture(outputPath);
            }
            finally
            {
                lighting.Mode = previousMode;
                EditorApplication.QueuePlayerLoopUpdate();
            }
        }

        public static string CaptureSceneView(string outputPath)
        {
            Camera camera = Components<Camera>(RequireScene()).First();
            Camera view = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
            if (view == null) throw new InvalidOperationException("Open a Scene View before capturing.");
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fieldOfView = camera.fieldOfView;
            try
            {
                camera.transform.SetPositionAndRotation(view.transform.position, view.transform.rotation);
                camera.fieldOfView = view.fieldOfView;
                return Capture(outputPath);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fieldOfView;
                EditorApplication.QueuePlayerLoopUpdate();
            }
        }

        public static string InspectSurfaceState()
        {
            Scene scene = RequireScene();
            Camera camera = Components<Camera>(scene).First();
            HDCamera hd = HDCamera.GetOrCreate(camera);
            GlobalIllumination gi = hd.volumeStack.GetComponent<GlobalIllumination>();
            Camera view = SceneView.lastActiveSceneView.camera;
            var pipeline = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
            string state = FormattableString.Invariant($"supportSSGI={pipeline.currentPlatformRenderPipelineSettings.supportSSGI}; cameraSSGI={hd.frameSettings.IsEnabled(FrameSettingsField.SSGI)}; volumeSSGI={gi.enable.value}; tracing={gi.tracing.value}; viewPosition={view.transform.position.ToString("R")}; viewRotation={view.transform.eulerAngles.ToString("R")}");
            var emissive = AssetDatabase.FindAssets("t:Material", new[] { PREVIEW_ROOT }).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Material>).Where(m => m.HasProperty("_EmissiveColor") && m.GetColor("_EmissiveColor").maxColorComponent > 0);
            string objects = string.Join("\n", Components<Renderer>(scene).Where(r => r.sharedMaterials.Any(m => m != null && m.HasProperty("_EmissiveColor") && m.GetColor("_EmissiveColor").maxColorComponent > 0)).Select(r => r.name + "; center=" + r.bounds.center.ToString("R")));
            var emitters = new List<string>();
            foreach (MeshRenderer renderer in Components<MeshRenderer>(scene))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null || !material.HasProperty("_EmissiveColor") || material.GetColor("_EmissiveColor").maxColorComponent <= 0) continue;
                    Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    int[] indices = mesh.GetIndices(i);
                    Vector3[] vertices = mesh.vertices;
                    var bounds = new Bounds(vertices[indices[0]], Vector3.zero);
                    foreach (int index in indices) bounds.Encapsulate(vertices[index]);
                    emitters.Add(renderer.name + "/" + material.name + "; emissionCenter=" + renderer.transform.TransformPoint(bounds.center).ToString("R"));
                }
            }
            return state + "\n" + objects + "\n" + string.Join("\n", emitters) + "\n" + string.Join("\n", emissive.Select(m => FormattableString.Invariant($"{AssetDatabase.GetAssetPath(m)}; emission={m.GetColor("_EmissiveColor")}; exposureWeight={m.GetFloat("_EmissiveExposureWeight")}; keyword={m.IsKeywordEnabled("_EMISSIVE_COLOR_MAP")}")));
        }

        public static string CaptureSurfaceDiagnostic(string outputPath, int diagnostic, float x, float y, float z, float pitch, float yaw, float roll)
        {
            Scene scene = RequireScene();
            Camera camera = Components<Camera>(scene).First();
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fieldOfView = camera.fieldOfView;
            Material[] materials = AssetDatabase.FindAssets("t:Material", new[] { PREVIEW_ROOT + "/Materials" }).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Material>).Where(m => m.shader.name == "HDRP/MonolithLit").ToArray();
            float[] masks = materials.Select(m => m.GetFloat("_MonolithFloraSubsurface")).ToArray();
            Color[] emission = materials.Select(m => m.GetColor("_EmissiveColor")).ToArray();
            Vector4[] options = materials.Select(m => m.GetVector("_MonolithDiagnosticOptions")).ToArray();
            float[] gloss = materials.Select(m => m.GetFloat("_MonolithGlossScale")).ToArray();
            float[] debug = materials.Select(m => m.GetFloat("_MonolithDebug")).ToArray();
            Volume volume = scene.GetRootGameObjects().Single(g => g.name == RIG_NAME).GetComponent<Volume>();
            volume.sharedProfile.TryGet(out GlobalIllumination gi);
            bool indirect = gi.enable.value;
            float ambientProbe = gi.ambientProbeDimmer.value;
            RayMarchingFallbackHierarchy rayMiss = gi.rayMiss.value;
            int giQuality = gi.quality.value;
            bool giDenoise = new SerializedObject(gi).FindProperty("m_DenoiseSS.m_Value").boolValue;
            bool giFullResolution = gi.fullResolutionSS.value;
            float[] ambientHemi = materials.Select(m => m.GetFloat("_MonolithLevelAmbientHemi")).ToArray();
            volume.sharedProfile.TryGet(out Fog fog);
            volume.sharedProfile.TryGet(out ScreenSpaceAmbientOcclusion ao);
            bool fogEnabled = fog != null && fog.enabled.value;
            float aoIntensity = ao != null ? ao.intensity.value : 0;
            Light[] lights = Components<Light>(scene).ToArray();
            LightShadows[] shadows = lights.Select(l => l.shadows).ToArray();
            HDAdditionalCameraData cameraData = camera.GetComponent<HDAdditionalCameraData>();
            HDAdditionalCameraData.AntialiasingMode aa = cameraData != null ? cameraData.antialiasing : HDAdditionalCameraData.AntialiasingMode.None;
            try
            {
                camera.transform.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(pitch, yaw, roll));
                camera.fieldOfView = 60;
                if (diagnostic >= 20 && diagnostic <= 68) CaptureInternal(Path.ChangeExtension(outputPath, ".Warmup.png"), diagnostic >= 65 ? 1 : 16);
                if (diagnostic == 1) foreach (Material material in materials) material.SetFloat("_MonolithFloraSubsurface", 1);
                if (diagnostic == 2) foreach (Material material in materials) material.SetColor("_EmissiveColor", Color.black);
                if (diagnostic == 3) gi.enable.value = false;
                if (diagnostic == 5 && fog != null) fog.enabled.value = false;
                if (diagnostic == 6 && ao != null) ao.intensity.value = 0;
                if (diagnostic == 7) foreach (Light light in lights) light.shadows = LightShadows.None;
                if (diagnostic == 8) foreach (Material material in materials) material.SetVector("_MonolithDiagnosticOptions", new Vector4(0, 1, 0, 0));
                if (diagnostic == 9) foreach (Material material in materials) material.SetFloat("_MonolithGlossScale", 0);
                if (diagnostic == 10) foreach (Material material in materials) material.SetFloat("_MonolithDebug", 8);
                if (diagnostic == 11 && cameraData != null) cameraData.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
                if (diagnostic == 12 && cameraData != null) cameraData.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                if (diagnostic >= 20 && diagnostic <= 68) foreach (Material material in materials) material.SetFloat("_MonolithDebug", diagnostic);
                if (diagnostic == 70) gi.ambientProbeDimmer.value = 0;
                if (diagnostic == 71) gi.rayMiss.value = RayMarchingFallbackHierarchy.None;
                if (diagnostic == 72) foreach (Material material in materials) material.SetFloat("_MonolithLevelAmbientHemi", 0);
                if (diagnostic == 73 || diagnostic == 74)
                {
                    gi.quality.value = 3;
                    gi.denoiseSS = diagnostic == 74;
                }
                if (diagnostic == 76) HDCamera.GetOrCreate(camera).Reset();
                if (diagnostic == 77) gi.fullResolutionSS.value = false;
                return CaptureInternal(outputPath, diagnostic >= 20 && diagnostic <= 68 ? 1 : 16);
            }
            finally
            {
                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i].SetFloat("_MonolithFloraSubsurface", masks[i]);
                    materials[i].SetColor("_EmissiveColor", emission[i]);
                    materials[i].SetVector("_MonolithDiagnosticOptions", options[i]);
                    materials[i].SetFloat("_MonolithGlossScale", gloss[i]);
                    materials[i].SetFloat("_MonolithDebug", debug[i]);
                    materials[i].SetFloat("_MonolithLevelAmbientHemi", ambientHemi[i]);
                }
                gi.enable.value = indirect;
                gi.ambientProbeDimmer.value = ambientProbe;
                gi.rayMiss.value = rayMiss;
                gi.denoiseSS = giDenoise;
                gi.quality.value = giQuality;
                gi.fullResolutionSS.value = giFullResolution;
                if (fog != null) fog.enabled.value = fogEnabled;
                if (ao != null) ao.intensity.value = aoIntensity;
                for (int i = 0; i < lights.Length; i++) lights[i].shadows = shadows[i];
                if (cameraData != null) cameraData.antialiasing = aa;
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fieldOfView;
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            }
        }

        public static string InspectNoiseState()
        {
            Camera view = SceneView.lastActiveSceneView.camera;
            HDCamera hd = HDCamera.GetOrCreate(view);
            FilmGrain grain = hd.volumeStack.GetComponent<FilmGrain>();
            ScreenSpaceAmbientOcclusion ao = hd.volumeStack.GetComponent<ScreenSpaceAmbientOcclusion>();
            Fog fog = hd.volumeStack.GetComponent<Fog>();
            GlobalIllumination gi = hd.volumeStack.GetComponent<GlobalIllumination>();
            HDAdditionalCameraData data = Components<Camera>(RequireScene()).First().GetComponent<HDAdditionalCameraData>();
            var sceneAA = (HDAdditionalCameraData.AntialiasingMode)EditorPrefs.GetInt("HDRP:SceneViewCamera:Antialiasing", 0);
            return FormattableString.Invariant($"filmGrain={grain.intensity.value}; SSAO={ao.intensity.value}; fog={fog.enabled.value}; volumetricFog={fog.enableVolumetricFog.value}; SSGI={gi.enable.value}; denoiseSSGI={gi.denoiseSS}; sceneAA={sceneAA}; alwaysRefresh={SceneView.lastActiveSceneView.sceneViewState.alwaysRefresh}; cameraAA={(data != null ? data.antialiasing.ToString() : "missing")}");
        }

        public static string InspectSceneViews()
        {
            var views = new List<string>();
            foreach (SceneView view in SceneView.sceneViews)
            {
                Camera camera = view.camera;
                views.Add(FormattableString.Invariant($"id={view.GetInstanceID()}; title={view.titleContent.text}; focused={EditorWindow.focusedWindow == view}; lastActive={SceneView.lastActiveSceneView == view}; pivot={view.pivot.ToString("R")}; rotation={view.rotation.eulerAngles.ToString("R")}; cameraPosition={camera.transform.position.ToString("R")}; cameraRotation={camera.transform.eulerAngles.ToString("R")}; fov={camera.fieldOfView}; aspect={camera.aspect}; rect={view.position}"));
            }
            foreach (Camera camera in Components<Camera>(RequireScene())) views.Add(FormattableString.Invariant($"sceneCamera={camera.name}; main={Camera.main == camera}; enabled={camera.enabled}; position={camera.transform.position.ToString("R")}; rotation={camera.transform.eulerAngles.ToString("R")}"));
            return string.Join("\n", views);
        }

        [MenuItem("Tools/X-Ray/Levels/Use Reference Sun Input in l01_escape")]
        public static void ApplyReferenceSunInput()
        {
            Scene scene = RequireScene();
            if (scene.isDirty) throw new InvalidOperationException("Save l01_escape before calibrating source sunlight.");
            MonolithSourceLighting lighting = Components<MonolithSourceLighting>(scene).Single();
            Undo.RecordObject(lighting, "Use reference source sunlight");
            var serialized = new SerializedObject(lighting);
            SerializedProperty lights = serialized.FindProperty("_lights");
            SerializedProperty colors = serialized.FindProperty("_sourceColors");
            for (int i = 0; i < lights.arraySize; i++)
            {
                var light = lights.GetArrayElementAtIndex(i).objectReferenceValue as Light;
                if (light == null || light.type != LightType.Directional) continue;
                colors.GetArrayElementAtIndex(i).vector3Value = new Vector3(light.color.r, light.color.g, light.color.b);
            }
            serialized.ApplyModifiedProperties();
            lighting.Publish();
            Undo.FlushUndoRecordObjects();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + SCENE_PATH);
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        public static string ExportLightingState(string directory)
        {
            string output = Path.GetFullPath(directory);
            if (!output.StartsWith("E:\\Projects\\stalker_raid_into_darkness_Archive\\", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Lighting output must be inside Archive.");
            Directory.CreateDirectory(output);
            Scene scene = RequireScene();
            File.WriteAllLines(Path.Combine(output, "Lights.csv"), Components<Light>(scene).Select(l => FormattableString.Invariant($"{l.name},{l.type},{l.transform.position.x:R},{l.transform.position.y:R},{l.transform.position.z:R},{l.range:R},{l.intensity:R},{l.shadows},{l.shadowStrength:R},{l.renderMode}")));
            var trees = new List<string>();
            Vector3 eye = SceneView.lastActiveSceneView.camera.transform.position;
            foreach (XRayLevelInstancedRenderer renderer in Components<XRayLevelInstancedRenderer>(scene))
            {
                if (renderer.BatchSet == null) continue;
                foreach (XRayInstanceBatch batch in renderer.BatchSet.Batches)
                {
                    if (batch.Materials == null || !batch.Materials.Any(m => m != null && m.name.Contains("tree"))) continue;
                    foreach (Matrix4x4 matrix in batch.Matrices)
                    {
                        Vector3 position = matrix.MultiplyPoint3x4(Vector3.zero);
                        if ((position - eye).sqrMagnitude > 3600) continue;
                        trees.Add(FormattableString.Invariant($"{batch.Name},{position.x:R},{position.y:R},{position.z:R},{string.Join(";", batch.Materials.Select(m => m != null ? m.name : "null"))}"));
                    }
                }
            }
            File.WriteAllLines(Path.Combine(output, "Nearby_Trees.csv"), trees);
            File.WriteAllLines(Path.Combine(output, "Flora_Materials.csv"), AssetDatabase.FindAssets("t:Material", new[] { PREVIEW_ROOT + "/Materials" }).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Material>).Where(m => m.shader.name == "HDRP/MonolithLit" && m.name.Contains("tree")).Select(m => FormattableString.Invariant($"{AssetDatabase.GetAssetPath(m)},{m.GetFloat("_MonolithFloraSubsurface"):R},{m.GetFloat("_MonolithLevelFlora"):R},{m.GetFloat("_MonolithMaterialId"):R},{m.GetTexture("_BaseColorMap")?.name}")));
            return "Exported lighting state to " + output;
        }

        public static string FindMaterialLocations(string name)
        {
            var locations = new List<string>();
            foreach (MeshRenderer renderer in Components<MeshRenderer>(RequireScene()))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null || material.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    int[] indices = mesh.GetIndices(i);
                    Vector3[] vertices = mesh.vertices;
                    var bounds = new Bounds(vertices[indices[0]], Vector3.zero);
                    foreach (int index in indices) bounds.Encapsulate(vertices[index]);
                    locations.Add(renderer.name + "/" + material.name + "; center=" + renderer.transform.TransformPoint(bounds.center).ToString("R") + "; size=" + bounds.size.ToString("R"));
                }
            }
            return string.Join("\n", locations);
        }

        [MenuItem("Tools/X-Ray/Levels/Apply Stable SSAO to l01_escape")]
        public static void ApplyStableAmbientOcclusion()
        {
            Scene scene = RequireScene();
            Volume volume = scene.GetRootGameObjects().Single(g => g.name == RIG_NAME).GetComponent<Volume>();
            if (!volume.sharedProfile.TryGet(out ScreenSpaceAmbientOcclusion ao)) throw new InvalidOperationException("The level lighting profile requires an SSAO override.");
            Undo.RecordObject(ao, "Calibrate level SSAO sampling");
            ao.quality.Override((int)ScalableSettingLevelParameter.Level.High);
            ao.radius.Override(0.5f);
            ao.temporalAccumulation.Override(true);
            Undo.FlushUndoRecordObjects();
            SaveAsset(ao);
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        [MenuItem("Tools/X-Ray/Levels/Enable Preview Antialiasing in l01_escape")]
        public static void ApplyPreviewAntialiasing()
        {
            Scene scene = RequireScene();
            if (scene.isDirty) throw new InvalidOperationException("Save l01_escape before changing camera antialiasing.");
            HDAdditionalCameraData data = Components<Camera>(scene).First().GetComponent<HDAdditionalCameraData>();
            if (data == null) throw new InvalidOperationException("The level camera requires HDAdditionalCameraData.");
            Type settings = typeof(HDRenderPipeline).Assembly.GetType("UnityEngine.Rendering.HighDefinition.HDAdditionalSceneViewSettings", true);
            System.Reflection.PropertyInfo property = settings.GetProperty("sceneViewAntialiasing", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (property == null || !property.CanWrite) throw new InvalidOperationException("HDRP Scene View antialiasing preference is unavailable.");
            property.SetValue(null, HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing);
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.sceneViewState.alwaysRefresh = true;
            Undo.RecordObject(data, "Enable level camera temporal antialiasing");
            data.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            EditorUtility.SetDirty(data);
            Undo.FlushUndoRecordObjects();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + SCENE_PATH);
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        public static string CaptureWithoutBoundRadiance(string outputPath)
        {
            MonolithSourceLighting lighting = Components<MonolithSourceLighting>(RequireScene()).Single();
            float previousScale = lighting.BoundRadianceScale;
            try
            {
                lighting.BoundRadianceScale = 0;
                return Capture(outputPath);
            }
            finally
            {
                lighting.BoundRadianceScale = previousScale;
                EditorApplication.QueuePlayerLoopUpdate();
            }
        }

        public static void SetPreviewEnabled(bool enabled)
        {
            GameObject rig = RequireScene().GetRootGameObjects().Single(g => g.name == RIG_NAME);
            rig.SetActive(enabled);
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private static Scene RequireScene()
        {
            Scene scene = SceneManager.GetSceneByPath(SCENE_PATH);
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Open the Anomaly l01_escape scene first.");
            return scene;
        }

        private static IEnumerable<T> Components<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true));
        }

        private static Material ConvertMaterial(Material original, Dictionary<Material, Material> materials)
        {
            if (original == null || original.shader.name != "HDRP/MonolithLit") return original;
            if (materials.TryGetValue(original, out Material copy)) return copy;
            string originalPath = AssetDatabase.GetAssetPath(original);
            string relative = originalPath.StartsWith(MATERIAL_ROOT, StringComparison.Ordinal) ? originalPath.Substring(MATERIAL_ROOT.Length) : original.name + ".mat";
            string copyPath = originalPath.StartsWith(PREVIEW_ROOT + "/", StringComparison.Ordinal) ? originalPath : PREVIEW_ROOT + "/Materials/" + relative;
            copy = AssetDatabase.LoadAssetAtPath<Material>(copyPath);
            if (copy == null)
            {
                EnsureFolder(Path.GetDirectoryName(copyPath).Replace('\\', '/'));
                copy = new Material(original);
                AssetDatabase.CreateAsset(copy, copyPath);
            }
            copy.SetFloat("_MonolithSourceProfile", 1);
            copy.SetFloat("_MonolithTrueStalkerTerrain", 0);
            SaveAsset(copy);
            materials.Add(original, copy);
            return copy;
        }

        private static T CopyAsset<T>(T source, string name) where T : ScriptableObject
        {
            string assetPath = PREVIEW_ROOT + "/" + name;
            T copy = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (copy == null)
            {
                copy = UnityEngine.Object.Instantiate(source);
                AssetDatabase.CreateAsset(copy, assetPath);
            }
            return copy;
        }

        private static void SaveAsset(UnityEngine.Object asset)
        {
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}

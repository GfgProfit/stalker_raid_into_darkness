using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayLevelOcclusionBaking
    {
        internal const string UPDATED_METRIC = "StaticOcclusionBakeUpdated";
        private const string OWNER_LABEL = "XRayStaticOcclusion";
        private const string FINGERPRINT_PREFIX = "XRayStaticOcclusionV1_";
        private const string DATA_ASSET_NAME = "/OcclusionCullingData.asset";

        internal static readonly object WAIT_FOR_EDITOR = new object();

        public static IEnumerable<object> Prepare(string scenePath, string prefabPath, XRayLevelImportRecord record)
        {
            if (string.IsNullOrEmpty(scenePath) || string.IsNullOrEmpty(prefabPath)) yield break;
            record.SetMetric(UPDATED_METRIC, 0);
            string dataPath = FindDataPath(scenePath);
            UnityEngine.Object data = dataPath != null ? AssetDatabase.LoadMainAssetAtPath(dataPath) : null;
            if (data != null)
            {
                string[] labels = AssetDatabase.GetLabels(data);
                if (!labels.Contains(OWNER_LABEL))
                {
                    RecordAsset(record, dataPath);
                    yield break;
                }
                if (labels.Contains(Fingerprint(scenePath, prefabPath)))
                {
                    RecordAsset(record, dataPath);
                    yield break;
                }
            }

            while (!CanOpenScene())
            {
                yield return WAIT_FOR_EDITOR;
            }

            Scene previousActive = SceneManager.GetActiveScene();
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            bool keepUntitledScene = IsEmptyUntitled(previousActive);
            bool switchScene = previousSetup.Length != 1 || !previousSetup[0].isLoaded || previousSetup[0].path != scenePath;
            Scene scene = switchScene ? EditorSceneManager.OpenScene(scenePath, keepUntitledScene ? OpenSceneMode.Additive : OpenSceneMode.Single) : previousActive;
            bool ownsBake = false;
            try
            {
                if (keepUntitledScene) SceneManager.SetActiveScene(scene);
                if (data == null && StaticOcclusionCulling.umbraDataSize > 0)
                {
                    record.Warnings.Add("Unity occlusion data already exists for this scene and was preserved.");
                    yield break;
                }
                if (data != null)
                {
                    StaticOcclusionCulling.Clear();
                    EditorSceneManager.MarkSceneDirty(scene);
                }
                bool hasOccluders = PrepareGeometry(scene, prefabPath);
                if (scene.isDirty) SaveScene(scene);
                if (!hasOccluders)
                {
                    record.Warnings.Add("Unity occlusion bake skipped: no eligible opaque static geometry in " + scenePath);
                    yield break;
                }

                ownsBake = StaticOcclusionCulling.GenerateInBackground();
                if (!ownsBake)
                {
                    record.Warnings.Add("Unity could not start static occlusion baking for " + scenePath);
                    yield break;
                }

                while (StaticOcclusionCulling.isRunning)
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene() != scene)
                    {
                        record.Warnings.Add("Unity occlusion bake interrupted by a scene or Play Mode change.");
                        yield break;
                    }
                    yield return WAIT_FOR_EDITOR;
                }
                ownsBake = false;
                yield return WAIT_FOR_EDITOR;
                if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene() != scene)
                {
                    record.Warnings.Add("Unity occlusion bake interrupted before its result could be saved.");
                    yield break;
                }

                if (scene.isDirty) SaveScene(scene);
                dataPath = FindDataPath(scenePath);
                data = dataPath != null ? AssetDatabase.LoadMainAssetAtPath(dataPath) : null;
                double bakedBytes = StaticOcclusionCulling.umbraDataSize;
                if (bakedBytes <= 0 && dataPath != null && File.Exists(dataPath)) bakedBytes = new FileInfo(dataPath).Length;
                if (data == null || bakedBytes <= 0)
                {
                    record.Warnings.Add("Unity occlusion bake did not produce saved visibility data for " + scenePath);
                    yield break;
                }
                string[] bakedLabels = AssetDatabase.GetLabels(data);
                string[] updatedLabels = bakedLabels.Where(label => !label.StartsWith(FINGERPRINT_PREFIX, StringComparison.Ordinal)).Append(OWNER_LABEL).Append(Fingerprint(scenePath, prefabPath)).Distinct().ToArray();
                AssetDatabase.SetLabels(data, updatedLabels);
                RecordAsset(record, dataPath);
                record.SetMetric(UPDATED_METRIC, 1);
                record.SetMetric("StaticOcclusionDataBytes", bakedBytes);
            }
            finally
            {
                if (ownsBake && StaticOcclusionCulling.isRunning) StaticOcclusionCulling.Cancel();
                if (switchScene && scene.IsValid() && scene.isLoaded && !scene.isDirty && !StaticOcclusionCulling.isRunning && SceneManager.GetActiveScene() == scene)
                {
                    if (keepUntitledScene)
                    {
                        EditorSceneManager.CloseScene(scene, true);
                        SceneManager.SetActiveScene(previousActive);
                    }
                    else if (SceneManager.sceneCount == 1)
                    {
                        EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                    }
                }
            }
        }

        internal static StaticEditorFlags GeometryFlags(Mesh mesh, Material[] materials, ShadowCastingMode shadowMode)
        {
            if (shadowMode == ShadowCastingMode.ShadowsOnly) return 0;
            if (mesh == null || materials == null || materials.Length != mesh.subMeshCount || materials.Length == 0) return StaticEditorFlags.OccludeeStatic;
            bool hasTriangles = false;
            for (int slot = 0; slot < materials.Length; slot++)
            {
                if (mesh.GetIndexCount(slot) == 0) continue;
                if (mesh.GetTopology(slot) != MeshTopology.Triangles || !IsOpaque(materials[slot])) return StaticEditorFlags.OccludeeStatic;
                hasTriangles = true;
            }
            return hasTriangles ? StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic : StaticEditorFlags.OccludeeStatic;
        }

        internal static string FindDataPath(string scenePath)
        {
            foreach (string path in AssetDatabase.GetDependencies(scenePath, false))
            {
                if (path.EndsWith(DATA_ASSET_NAME, StringComparison.OrdinalIgnoreCase) || AssetDatabase.GetMainAssetTypeAtPath(path)?.Name == "OcclusionCullingData") return path;
            }
            return null;
        }

        private static bool IsOpaque(Material material)
        {
            if (material == null || material.shader == null || material.shader.name == XRayLevelMaterialLibrary.DECAL_SHADER_NAME || material.renderQueue >= (int)RenderQueue.AlphaTest) return false;
            if (material.IsKeywordEnabled("_ALPHATEST_ON") || material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") || material.IsKeywordEnabled("_ALPHABLEND_ON") || material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON")) return false;
            if (material.HasProperty("_AlphaCutoffEnable") && material.GetFloat("_AlphaCutoffEnable") > 0.5f) return false;
            if (material.HasProperty("_SurfaceType") && material.GetFloat("_SurfaceType") > 0.5f) return false;
            string renderType = material.GetTag("RenderType", true, string.Empty);
            return string.IsNullOrEmpty(renderType) || renderType == "Opaque" || renderType == "HDLitShader" || renderType == "HDUnlitShader";
        }

        private static bool PrepareGeometry(Scene scene, string prefabPath)
        {
            GameObject root = scene.GetRootGameObjects().FirstOrDefault(obj => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(obj) == prefabPath);
            Transform geometry = root != null ? root.transform.Find("Geometry") : null;
            if (geometry == null) return false;
            bool hasOccluders = false;
            foreach (MeshRenderer renderer in geometry.GetComponentsInChildren<MeshRenderer>(true))
            {
                Mesh mesh = renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                StaticEditorFlags flags = GeometryFlags(mesh, renderer.sharedMaterials, renderer.shadowCastingMode);
                hasOccluders |= renderer.enabled && renderer.gameObject.activeInHierarchy && (flags & StaticEditorFlags.OccluderStatic) != 0;
                GameObject obj = renderer.gameObject;
                StaticEditorFlags oldFlags = GameObjectUtility.GetStaticEditorFlags(obj);
                StaticEditorFlags updated = (oldFlags & ~(StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic)) | flags;
                if (oldFlags == updated) continue;
                GameObjectUtility.SetStaticEditorFlags(obj, updated);
                PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
                EditorSceneManager.MarkSceneDirty(scene);
            }
            return hasOccluders;
        }

        private static bool CanOpenScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || StaticOcclusionCulling.isRunning || Lightmapping.isRunning) return false;
#if XRAY_HDRP
            if (AdaptiveProbeVolumes.isRunning) return false;
#endif
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty || (string.IsNullOrEmpty(scene.path) && !IsEmptyUntitled(scene))) return false;
            }
            return true;
        }

        private static bool IsEmptyUntitled(Scene scene)
        {
            if (SceneManager.sceneCount != 1 || !string.IsNullOrEmpty(scene.path) || scene.isDirty) return false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.GetComponentInChildren<Renderer>(true) != null || root.GetComponentInChildren<Terrain>(true) != null || root.GetComponentInChildren<OcclusionArea>(true) != null || root.GetComponentInChildren<OcclusionPortal>(true) != null) return false;
            }
            return true;
        }

        private static void SaveScene(Scene scene)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Unity occlusion data to " + scene.path);
        }

        private static string Fingerprint(string scenePath, string prefabPath)
        {
            string inputs = XRayLevelSourceRefresh.HashFile(scenePath) + AssetDatabase.GetAssetDependencyHash(prefabPath) + Application.unityVersion;
            return FINGERPRINT_PREFIX + Hash128.Compute(inputs);
        }

        private static void RecordAsset(XRayLevelImportRecord record, string dataPath)
        {
            if (!record.GeneratedAssets.Contains(dataPath)) record.GeneratedAssets.Add(dataPath);
        }
    }
}

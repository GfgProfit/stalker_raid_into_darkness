#if XRAY_HDRP
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace XRay.Importer.Editor.Levels
{
    internal static class XRayLevelApvBaking
    {
        private const string FINGERPRINT_PREFIX = "XRayApvV1_";
        private const string VOLUME_NAME = "X-Ray Global APV";
        private const float INITIAL_SPACING = 2.25f;
        private const float SPACING_MULTIPLIER = 1.5f;
        private const int MAX_ATTEMPTS = 6;
        private const int BRICKS_PER_CHUNK = 128;
        private const int PROBES_PER_BRICK_SIDE = 4;

        internal static readonly object WAIT_FOR_EDITOR = new object();
        private static readonly MethodInfo SET_DEFAULTS = typeof(ProbeVolumeBakingSet).GetMethod("SetDefaults", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo PLACED_BATCH = typeof(AdaptiveProbeVolumes).GetField("m_BakingBatch", BindingFlags.Static | BindingFlags.NonPublic);

        internal static IEnumerable<object> Prepare(string scenePath, string prefabPath, bool sceneCreated, XRayLevelImportRecord record)
        {
            string levelName = Path.GetFileNameWithoutExtension(scenePath);
            string folder = Path.GetDirectoryName(scenePath).Replace('\\', '/') + "/APV";
            string setPath = folder + "/" + levelName + "_BakingSet.asset";
            string lightingPath = folder + "/" + levelName + "_Lighting.lighting";
            ProbeVolumeBakingSet set = AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(setPath);
            if (!sceneCreated && set == null) yield break;
            if (GraphicsSettings.currentRenderPipeline is not HDRenderPipelineAsset hdrp || hdrp.currentPlatformRenderPipelineSettings.lightProbeSystem != RenderPipelineSettings.LightProbeSystem.AdaptiveProbeVolumes)
            {
                record.Warnings.Add("Adaptive Probe Volumes bake skipped: the active HDRP asset does not enable APV.");
                yield break;
            }

            string sceneGuid = AssetDatabase.AssetPathToGUID(scenePath);
            set ??= CreateSet(folder, setPath, sceneGuid);
            if (set == null || set.sceneGUIDs.Count != 1 || set.sceneGUIDs[0] != sceneGuid)
            {
                record.Warnings.Add("Adaptive Probe Volumes bake skipped: the scene belongs to another baking set.");
                yield break;
            }
            AddGeneratedAsset(record, setPath);
            string fingerprint = FINGERPRINT_PREFIX + AssetDatabase.GetAssetDependencyHash(prefabPath) + "_" + (int)hdrp.currentPlatformRenderPipelineSettings.probeVolumeMemoryBudget;
            string[] labels = AssetDatabase.GetLabels(set);
            if (set.HasBakedData() && labels.Contains(fingerprint) && BakedDataTimestamp(setPath) != DateTime.MinValue && AssetDatabase.GetDependencies(scenePath, false).Contains(setPath))
            {
                if (AssetDatabase.LoadAssetAtPath<LightingSettings>(lightingPath) != null) AddGeneratedAsset(record, lightingPath);
                yield break;
            }

            while (!CanOpenScene())
            {
                yield return WAIT_FOR_EDITOR;
            }

            SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
            Scene previousActive = SceneManager.GetActiveScene();
            bool keepUntitledScene = CanKeepUntitledScene(previousActive);
            bool switchScene = previous.Length != 1 || !previous[0].isLoaded || previous[0].path != scenePath;
            Scene scene = switchScene ? EditorSceneManager.OpenScene(scenePath, keepUntitledScene ? OpenSceneMode.Additive : OpenSceneMode.Single) : previousActive;
            bool configured = false;
            try
            {
                if (keepUntitledScene) SceneManager.SetActiveScene(scene);
                GameObject root = scene.GetRootGameObjects().FirstOrDefault(obj => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(obj) == prefabPath);
                Transform geometry = root != null ? root.transform.Find("Geometry") : null;
                if (geometry == null) throw new InvalidOperationException("The imported scene has no geometry prefab.");
                if (scene.GetRootGameObjects().SelectMany(obj => obj.GetComponentsInChildren<ProbeVolume>(true)).Any(volume => volume.name != VOLUME_NAME))
                {
                    record.Warnings.Add("Adaptive Probe Volumes bake skipped: the scene contains authored probe volumes.");
                    yield break;
                }

                MeshRenderer[] contributors = geometry.GetComponentsInChildren<MeshRenderer>().Where(IsOpaqueContributor).ToArray();
                if (contributors.Length == 0) yield break;
                ConfigureScene(scene, contributors, lightingPath);
                configured = true;
                AddGeneratedAsset(record, lightingPath);

                int chunkBudget = ShChunkBudget(hdrp.currentPlatformRenderPipelineSettings.probeVolumeMemoryBudget);
                PropertyModification[] originalOverrides = PrefabUtility.GetPropertyModifications(root);
                DateTime before = BakedDataTimestamp(setPath);
                int chunks = 0;
                var replacements = new List<BakeMesh>();
                bool ownsBake = false;
                try
                {
                    foreach (MeshRenderer renderer in contributors)
                    {
                        BakeMesh replacement = BakeMesh.Create(renderer);
                        if (replacement != null) replacements.Add(replacement);
                    }

                    for (int attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
                    {
                        set.minDistanceBetweenProbes = INITIAL_SPACING * Mathf.Pow(SPACING_MULTIPLIER, attempt);
                        EditorUtility.SetDirty(set);
                        ownsBake = AdaptiveProbeVolumes.BakeAsync();
                        if (!ownsBake) break;
                        chunks = CountPlacedShChunks();
                        if (chunks > 0 && chunks <= chunkBudget)
                        {
                            while (AdaptiveProbeVolumes.isRunning)
                            {
                                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Play Mode interrupted the level APV bake.");
                                yield return WAIT_FOR_EDITOR;
                            }
                            ownsBake = false;
                            break;
                        }

                        CancelOwnedBake();
                        ownsBake = false;
                        if (chunks == 0) break;
                        yield return WAIT_FOR_EDITOR;
                    }
                }
                finally
                {
                    if (ownsBake && AdaptiveProbeVolumes.isRunning) CancelOwnedBake();
                    foreach (BakeMesh replacement in replacements)
                    {
                        replacement.Restore();
                    }
                    if (root != null && replacements.Count > 0) PrefabUtility.SetPropertyModifications(root, originalOverrides ?? Array.Empty<PropertyModification>());
                }

                bool hasSceneData = scene.GetRootGameObjects().Any(obj => obj.GetComponentInChildren<ProbeVolumePerSceneData>(true)?.bakingSet == set);
                if (!hasSceneData || !set.HasBakedData() || BakedDataTimestamp(setPath) <= before || chunks <= 0 || chunks > chunkBudget)
                {
                    record.Warnings.Add("Adaptive Probe Volumes bake did not produce a complete result within the selected HDRP probe budget.");
                    yield break;
                }

                SaveScene(scene);
                AssetDatabase.SetLabels(set, labels.Where(label => !label.StartsWith(FINGERPRINT_PREFIX, StringComparison.Ordinal)).Append(fingerprint).ToArray());
                AssetDatabase.SaveAssetIfDirty(set);
                record.SetMetric("ProbeVolumeBakeUpdated", 1);
                record.SetMetric("ProbeVolumeAtlasChunks", chunks);
                record.SetMetric("ProbeVolumeMinSpacing", set.minDistanceBetweenProbes);
            }
            finally
            {
                if (configured && scene.IsValid() && scene.isLoaded && scene.isDirty && !Application.isPlaying) SaveScene(scene);
                if (switchScene && scene.IsValid() && scene.isLoaded && !scene.isDirty)
                {
                    if (keepUntitledScene)
                    {
                        EditorSceneManager.CloseScene(scene, true);
                        SceneManager.SetActiveScene(previousActive);
                    }
                    else if (SceneManager.sceneCount == 1)
                    {
                        EditorSceneManager.RestoreSceneManagerSetup(previous);
                    }
                }
            }
        }

        private static void AddGeneratedAsset(XRayLevelImportRecord record, string path)
        {
            if (!record.GeneratedAssets.Contains(path)) record.GeneratedAssets.Add(path);
        }

        private static ProbeVolumeBakingSet CreateSet(string folder, string path, string sceneGuid)
        {
            if (SET_DEFAULTS == null || PLACED_BATCH == null) throw new InvalidOperationException("The installed HDRP APV placement API has changed.");
            var set = ScriptableObject.CreateInstance<ProbeVolumeBakingSet>();
            SET_DEFAULTS.Invoke(set, null);
            if (!set.TryAddScene(sceneGuid))
            {
                Object.DestroyImmediate(set);
                return null;
            }

            XRayLevelPaths.EnsureFolder(folder);
            set.SetSceneBaking(sceneGuid, true);
            set.minDistanceBetweenProbes = INITIAL_SPACING;
            set.simplificationLevels = 3;
            set.minRendererVolumeSize = 0f;
            set.skyOcclusion = true;
            set.skyOcclusionBakingSamples = 512;
            set.skyOcclusionBakingBounces = 2;
            set.skyOcclusionShadingDirection = true;
            AssetDatabase.CreateAsset(set, path);
            AssetDatabase.SaveAssetIfDirty(set);
            return set;
        }

        private static bool CanOpenScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning || AdaptiveProbeVolumes.isRunning) return false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty || (string.IsNullOrEmpty(scene.path) && !CanKeepUntitledScene(scene))) return false;
            }
            return true;
        }

        private static bool CanKeepUntitledScene(Scene scene)
        {
            if (SceneManager.sceneCount != 1 || !string.IsNullOrEmpty(scene.path) || scene.isDirty) return false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.GetComponentInChildren<Renderer>(true) != null || root.GetComponentInChildren<Terrain>(true) != null || root.GetComponentInChildren<ProbeVolume>(true) != null) return false;
                if (root.GetComponentsInChildren<Light>(true).Any(light => light.lightmapBakeType != LightmapBakeType.Realtime)) return false;
            }
            return true;
        }

        private static void ConfigureScene(Scene scene, MeshRenderer[] contributors, string lightingPath)
        {
            GameObject owner = scene.GetRootGameObjects().FirstOrDefault(obj => obj.name == VOLUME_NAME);
            if (owner == null)
            {
                owner = new GameObject(VOLUME_NAME);
                SceneManager.MoveGameObjectToScene(owner, scene);
                ProbeVolume volume = owner.AddComponent<ProbeVolume>();
                volume.mode = ProbeVolume.Mode.Global;
                volume.fillEmptySpaces = false;
            }

            LightingSettings lighting = AssetDatabase.LoadAssetAtPath<LightingSettings>(lightingPath);
            if (lighting == null)
            {
                lighting = Lightmapping.TryGetLightingSettings(out LightingSettings existing) ? Object.Instantiate(existing) : new LightingSettings();
                lighting.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
                lighting.bakedGI = true;
                lighting.realtimeGI = false;
                AssetDatabase.CreateAsset(lighting, lightingPath);
            }
            Lightmapping.lightingSettings = lighting;
            foreach (MeshRenderer renderer in contributors)
            {
                GameObject obj = renderer.gameObject;
                GameObjectUtility.SetStaticEditorFlags(obj, GameObjectUtility.GetStaticEditorFlags(obj) | StaticEditorFlags.ContributeGI);
                renderer.receiveGI = ReceiveGI.LightProbes;
                PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            SaveScene(scene);
        }

        private static void SaveScene(Scene scene)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save APV data to " + scene.path);
        }

        private static int ShChunkBudget(ProbeVolumeTextureMemoryBudget budget)
        {
            int atlasSide = (int)budget;
            return atlasSide / (PROBES_PER_BRICK_SIDE * BRICKS_PER_CHUNK) * (atlasSide / PROBES_PER_BRICK_SIDE);
        }

        private static int CountPlacedShChunks()
        {
            object batch = PLACED_BATCH.GetValue(null);
            if (batch == null) return 0;
            var cells = batch.GetType().GetField("cells").GetValue(batch) as IList;
            if (cells == null || cells.Count == 0) return 0;
            FieldInfo bricksField = cells[0].GetType().GetField("bricks");
            int count = 0;
            foreach (object cell in cells)
            {
                int bricks = ((Array)bricksField.GetValue(cell)).Length;
                count += (bricks + BRICKS_PER_CHUNK - 1) / BRICKS_PER_CHUNK;
            }
            return count;
        }

        private static void CancelOwnedBake()
        {
            if (!AdaptiveProbeVolumes.Cancel() || AdaptiveProbeVolumes.isRunning) throw new InvalidOperationException("Unity could not cancel the level APV bake.");
        }

        private static DateTime BakedDataTimestamp(string setPath)
        {
            string prefix = Path.ChangeExtension(setPath, null);
            var shared = new FileInfo(prefix + ".CellSharedData.bytes");
            var bricks = new FileInfo(prefix + ".CellBricksData.bytes");
            var data = new FileInfo(prefix + "-Default.CellData.bytes");
            var optional = new FileInfo(prefix + "-Default.CellOptionalData.bytes");
            if (!shared.Exists || shared.Length == 0 || !bricks.Exists || bricks.Length == 0 || !data.Exists || data.Length == 0 || !optional.Exists || optional.Length == 0) return DateTime.MinValue;
            return shared.LastWriteTimeUtc;
        }

        private static bool IsOpaqueContributor(MeshRenderer renderer)
        {
            if (!renderer.enabled || renderer.shadowCastingMode == ShadowCastingMode.Off || !renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) return false;
            Material[] materials = renderer.sharedMaterials;
            if (materials.Length != filter.sharedMesh.subMeshCount) return false;
            for (int slot = 0; slot < materials.Length; slot++)
            {
                if (IsOpaque(materials[slot]) && filter.sharedMesh.GetIndexCount(slot) > 0) return true;
            }
            return false;
        }

        private static bool IsOpaque(Material material)
        {
            return material != null && material.renderQueue < (int)RenderQueue.AlphaTest && !material.IsKeywordEnabled("_ALPHATEST_ON") && !material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") && (!material.HasProperty("_SurfaceType") || material.GetFloat("_SurfaceType") < 0.5f);
        }

        private sealed class BakeMesh
        {
            private readonly MeshRenderer _renderer;
            private readonly MeshFilter _filter;
            private readonly Mesh _source;
            private readonly Material[] _sourceMaterials;
            private readonly Mesh _temporary;

            private BakeMesh(MeshRenderer renderer, MeshFilter filter, Material[] materials, int[] slots)
            {
                _renderer = renderer;
                _filter = filter;
                _source = filter.sharedMesh;
                _sourceMaterials = materials;
                _temporary = new Mesh { name = _source.name + "_ProbeBake", indexFormat = _source.indexFormat, hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    _temporary.vertices = _source.vertices;
                    _temporary.normals = _source.normals;
                    _temporary.tangents = _source.tangents;
                    _temporary.colors32 = _source.colors32;
                    var uv = new List<Vector2>();
                    for (int channel = 0; channel < 4; channel++)
                    {
                        _source.GetUVs(channel, uv);
                        if (uv.Count == _source.vertexCount) _temporary.SetUVs(channel, uv);
                    }
                    _temporary.subMeshCount = slots.Length;
                    for (int i = 0; i < slots.Length; i++)
                    {
                        _temporary.SetIndices(_source.GetIndices(slots[i]), MeshTopology.Triangles, i, true);
                        if (_temporary.GetSubMesh(i).vertexCount == 0) throw new InvalidOperationException("Invalid APV mesh subset: " + _source.name);
                    }
                    _temporary.bounds = _source.bounds;
                    _filter.sharedMesh = _temporary;
                    _renderer.sharedMaterials = slots.Select(slot => materials[slot]).ToArray();
                }
                catch
                {
                    Restore();
                    throw;
                }
            }

            internal static BakeMesh Create(MeshRenderer renderer)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh source = filter.sharedMesh;
                Material[] materials = renderer.sharedMaterials;
                int[] slots = Enumerable.Range(0, materials.Length).Where(i => IsOpaque(materials[i]) && source.GetIndexCount(i) > 0).ToArray();
                bool supportedFormat = source.GetVertexAttributes().All(attribute => attribute.format == VertexAttributeFormat.Float32 || (attribute.attribute == VertexAttribute.Color && attribute.format == VertexAttributeFormat.UNorm8));
                if (supportedFormat && slots.Length == materials.Length && slots.All(i => source.GetSubMesh(i).vertexCount > 0)) return null;
                return new BakeMesh(renderer, filter, materials, slots);
            }

            internal void Restore()
            {
                if (_filter != null) _filter.sharedMesh = _source;
                if (_renderer != null) _renderer.sharedMaterials = _sourceMaterials;
                Object.DestroyImmediate(_temporary);
            }
        }
    }
}
#endif

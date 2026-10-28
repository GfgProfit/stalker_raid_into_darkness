using System;
using System.Collections.Generic;
using System.IO;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    public enum XRaySourceTexturePreviewMode
    {
        Existing,
        SourceOnly,
        DetailDiffuse,
        DetailBump,
        SimpleParallax,
        SteepParallax,
        Combined,
        TerrainBlend,
        TerrainAo,
        TerrainPom,
        TerrainCombined
    }

    [InitializeOnLoad]
    public static class XRayAnomalyMaterialPreview
    {
        private static readonly Dictionary<Renderer, Material[]> _renderers = new Dictionary<Renderer, Material[]>();
        private static readonly Dictionary<XRayLevelInstancedRenderer, XRayInstanceBatchSet> _instances = new Dictionary<XRayLevelInstancedRenderer, XRayInstanceBatchSet>();
        private static readonly List<UnityEngine.Object> _temporary = new List<UnityEngine.Object>();
        private static readonly Dictionary<Material, Material> _materials = new Dictionary<Material, Material>();
        private static readonly Dictionary<Material, XRayLevelSourceRefreshEntry> _entries = new Dictionary<Material, XRayLevelSourceRefreshEntry>();

        private static XRayLevelSourceRefresh _refresh;
        private static XRayTerrainDiagnosticBinding _terrain;
        private static string _root;
        private static string _level;
        private static Scene _scene;

        static XRayAnomalyMaterialPreview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorSceneManager.sceneClosing += OnSceneClosing;
        }

        public static bool IsActive => _refresh != null;
        public static int MaterialCount => _materials.Count;

        [MenuItem("Tools/X-Ray/Levels/Source Texture Preview/Modes/Existing")]
        public static void Existing()
        {
            SetMode(XRaySourceTexturePreviewMode.Existing);
        }

        [MenuItem("Tools/X-Ray/Levels/Source Texture Preview/Modes/Source Only")]
        public static void SourceOnly()
        {
            SetMode(XRaySourceTexturePreviewMode.SourceOnly);
        }

        [MenuItem("Tools/X-Ray/Levels/Source Texture Preview/Modes/Detail Diffuse")]
        public static void DetailDiffuse()
        {
            SetMode(XRaySourceTexturePreviewMode.DetailDiffuse);
        }

        [MenuItem("Tools/X-Ray/Levels/Source Texture Preview/Modes/Detail Bump")]
        public static void DetailBump()
        {
            SetMode(XRaySourceTexturePreviewMode.DetailBump);
        }

        [MenuItem("Tools/X-Ray/Levels/Source Texture Preview/Modes/Simple Parallax")]
        public static void SimpleParallax()
        {
            SetMode(XRaySourceTexturePreviewMode.SimpleParallax);
        }

        [MenuItem("Tools/X-Ray/Levels/Source Texture Preview/Modes/Steep Parallax")]
        public static void SteepParallax()
        {
            SetMode(XRaySourceTexturePreviewMode.SteepParallax);
        }

        [MenuItem("Tools/X-Ray/Levels/Source Texture Preview/Modes/Combined")]
        public static void Combined()
        {
            SetMode(XRaySourceTexturePreviewMode.Combined);
        }

        [MenuItem("Tools/X-Ray/Levels/Source Texture Preview/Modes/Terrain Blend")]
        public static void TerrainBlend()
        {
            SetMode(XRaySourceTexturePreviewMode.TerrainBlend);
        }

        [MenuItem("Tools/X-Ray/Levels/Source Texture Preview/Modes/Terrain AO")]
        public static void TerrainAo()
        {
            SetMode(XRaySourceTexturePreviewMode.TerrainAo);
        }

        [MenuItem("Tools/X-Ray/Levels/Source Texture Preview/Modes/Terrain POM")]
        public static void TerrainPom()
        {
            SetMode(XRaySourceTexturePreviewMode.TerrainPom);
        }

        [MenuItem("Tools/X-Ray/Levels/Source Texture Preview/Modes/Terrain POM And AO")]
        public static void TerrainCombined()
        {
            SetMode(XRaySourceTexturePreviewMode.TerrainCombined);
        }

        [MenuItem("Tools/X-Ray/Levels/Source Texture Preview/Terrain Active Imported Level")]
        public static void PreviewTerrainActiveLevel()
        {
            string scene = SceneManager.GetActiveScene().path;
            string folder = Path.GetDirectoryName(Path.GetDirectoryName(scene) ?? string.Empty)?.Replace('\\', '/');
            XRayLevelImportManifest manifest = AssetDatabase.LoadAssetAtPath<XRayLevelImportManifest>((folder ?? string.Empty) + "/" + XRayLevelPaths.MANIFEST_NAME);
            if (manifest == null) throw new InvalidOperationException("Open an imported level with source provenance before starting terrain preview.");
            string root = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            using (XRayTerrainDiagnosticBinding binding = XRayTerrainDiagnosticBinding.Open(root, manifest.Record.LevelName))
            {
                if (binding.Source.Profile != XRayTerrainSourceProfile.AnomalyAtmospherics) throw new NotSupportedException("The source-specific terrain path remains unchanged; this preview supports only verified Atmospherics HQ: " + binding.Source.Profile);
            }
            Start(root, manifest.Record.LevelName, XRaySourceTexturePreviewMode.TerrainCombined);
        }

        [MenuItem("Tools/X-Ray/Levels/Source Texture Preview/l01_escape")]
        public static void PreviewEscape()
        {
            StartForActiveProfile("l01_escape");
        }

        [MenuItem("Tools/X-Ray/Levels/Source Texture Preview/collaider")]
        public static void PreviewCollaider()
        {
            StartForActiveProfile("collaider");
        }

        [MenuItem("Tools/X-Ray/Levels/Source Texture Preview/Disable And Restore")]
        public static void Stop()
        {
            foreach (KeyValuePair<Renderer, Material[]> pair in _renderers)
            {
                if (pair.Key != null)
                {
                    Material[] current = pair.Key.sharedMaterials;
                    for (int i = 0; i < current.Length; i++)
                    {
                        foreach (KeyValuePair<Material, Material> material in _materials)
                        {
                            if (current[i] == material.Value)
                            {
                                current[i] = material.Key;
                                break;
                            }
                        }
                    }

                    pair.Key.sharedMaterials = current;
                }
            }

            foreach (KeyValuePair<XRayLevelInstancedRenderer, XRayInstanceBatchSet> pair in _instances)
            {
                if (pair.Key != null && _temporary.Contains(pair.Key.BatchSet))
                {
                    pair.Key.BatchSet = pair.Value;
                }
            }

            for (int i = _temporary.Count - 1; i >= 0; i--)
            {
                if (_temporary[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_temporary[i]);
                }
            }

            _renderers.Clear();
            _instances.Clear();
            _temporary.Clear();
            _materials.Clear();
            _entries.Clear();
            _terrain?.Dispose();
            _terrain = null;
            _refresh = null;
            SceneView.RepaintAll();
        }

        public static void Start(string root, string level, XRaySourceTexturePreviewMode mode)
        {
            Stop();
            XRayLevelSourceRefresh.RequireCleanEditor();
            Scene scene = SceneManager.GetActiveScene();
            if (AssetDatabase.LoadAssetAtPath<XRayLevelImportManifest>(XRayLevelPaths.ManifestPath(root, level)) == null || scene.path != XRayLevelPaths.ScenePath(root, level))
            {
                throw new InvalidOperationException("Open the explicitly selected Anomaly level before starting its source material preview.");
            }

            _root = root;
            _level = level;
            _scene = scene;
            _refresh = XRayLevelSourceRefresh.Open(root);
            try
            {
                foreach (Renderer renderer in Components<Renderer>(scene))
                {
                    Material[] original = renderer.sharedMaterials;
                    Material[] replacements = Array.ConvertAll(original, ConvertMaterial);
                    if (!original.SequenceEqual(replacements))
                    {
                        _renderers.Add(renderer, original);
                        renderer.sharedMaterials = replacements;
                    }
                }

                foreach (XRayLevelInstancedRenderer renderer in Components<XRayLevelInstancedRenderer>(scene))
                {
                    XRayInstanceBatchSet source = renderer.BatchSet;
                    if (source == null)
                    {
                        continue;
                    }

                    var copy = ScriptableObject.CreateInstance<XRayInstanceBatchSet>();
                    copy.hideFlags = HideFlags.HideAndDontSave;
                    _temporary.Add(copy);
                    var batches = source.Batches.Select(batch => new XRayInstanceBatch(batch.Name, batch.Mesh, Array.ConvertAll(batch.Materials, ConvertMaterial), batch.Bounds, batch.Matrices, batch.Data, batch.CastShadows, batch.LodIndices)).ToList();
                    copy.Initialize(source.LevelName, source.CellSize, batches, source.Lods);
                    _instances.Add(renderer, source);
                    renderer.BatchSet = copy;
                }

                if (!Components<MonolithWeatherInputs>(scene).Any(component => component.isActiveAndEnabled) || !Components<MonolithSourceLighting>(scene).Any(component => component.isActiveAndEnabled))
                {
                    var rig = new GameObject("Monolith source material preview") { hideFlags = HideFlags.HideAndDontSave };
                    SceneManager.MoveGameObjectToScene(rig, scene);
                    _temporary.Add(rig);
                    if (!Components<MonolithWeatherInputs>(scene).Any(component => component.isActiveAndEnabled))
                    {
                        rig.AddComponent<MonolithWeatherInputs>().SetInputs(0.71864f, Vector3.one, new Vector4(0.5f, 1.0f, 0.0f, 0.0f), new Vector4(1.5f, 1.0f, 0.0f, 0.0f), 0.0f);
                    }

                    if (!Components<MonolithSourceLighting>(scene).Any(component => component.isActiveAndEnabled))
                    {
                        rig.AddComponent<MonolithSourceLighting>().Mode = MonolithSourceLighting.LightingMode.Native;
                    }
                }

                SetMode(mode);
            }
            catch
            {
                Stop();
                throw;
            }
        }

        public static void SetMode(XRaySourceTexturePreviewMode mode)
        {
            foreach (KeyValuePair<Material, Material> pair in _materials)
            {
                Material copy = pair.Value;
                copy.CopyPropertiesFromMaterial(pair.Key);
                if (mode == XRaySourceTexturePreviewMode.Existing)
                {
                    continue;
                }

                if (mode >= XRaySourceTexturePreviewMode.TerrainBlend)
                {
                    if (!copy.IsKeywordEnabled("_MONOLITH_LEVEL_TERRAIN")) continue;
                    try
                    {
                        _terrain = _terrain ?? XRayTerrainDiagnosticBinding.Open(_root, _level);
                        _terrain.Bind(pair.Key, copy);
                        copy.SetFloat("_MonolithSourceProfile", 1.0f);
                        copy.SetFloat("_MonolithSourceTerrain", 1.0f);
                        copy.SetFloat("_MonolithTerrainParallax", mode == XRaySourceTexturePreviewMode.TerrainPom || mode == XRaySourceTexturePreviewMode.TerrainCombined ? 1.0f : 0.0f);
                        copy.SetFloat("_MonolithTerrainOcclusion", mode == XRaySourceTexturePreviewMode.TerrainAo || mode == XRaySourceTexturePreviewMode.TerrainCombined ? 1.0f : 0.0f);
                        copy.SetFloat("_MonolithTerrainVirtualHeight", 1.0f);
                    }
                    catch
                    {
                        Stop();
                        throw;
                    }
                    continue;
                }

                XRayLevelSourceRefreshEntry entry = _entries[pair.Key];
                _refresh.Replan(entry, copy);
                MonolithMaterialConfigurator.ConfigureSourceBindings(copy, entry.Binding);
                copy.SetFloat("_MonolithSourceProfile", 1.0f);
                int detail = Mathf.Clamp(Mathf.RoundToInt(copy.GetFloat(XRayLevelSourceBindings.RECOMMENDED_DETAIL)), 0, entry.Binding.RecommendedDetail);
                int parallax = Mathf.Clamp(Mathf.RoundToInt(copy.GetFloat(XRayLevelSourceBindings.RECOMMENDED_PARALLAX)), 0, entry.Binding.RecommendedParallax);
                copy.SetFloat("_MonolithSourceDetail", mode == XRaySourceTexturePreviewMode.Combined ? detail : mode == XRaySourceTexturePreviewMode.DetailDiffuse ? Mathf.Min(1, detail) : mode == XRaySourceTexturePreviewMode.DetailBump ? detail : 0);
                copy.SetFloat("_MonolithSourceParallax", mode == XRaySourceTexturePreviewMode.Combined ? parallax : mode == XRaySourceTexturePreviewMode.SimpleParallax ? Mathf.Min(1, parallax) : mode == XRaySourceTexturePreviewMode.SteepParallax && parallax == 2 ? 2 : 0);
                copy.SetFloat("_MonolithSourceVirtualHeight", mode == XRaySourceTexturePreviewMode.Combined && parallax > 0 ? 1.0f : 0.0f);
            }

            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        private static Material ConvertMaterial(Material source)
        {
            if (source == null || source.shader.name != MonolithMaterialConfigurator.SHADER_NAME)
            {
                return source;
            }

            if (_materials.TryGetValue(source, out Material cached))
            {
                return cached;
            }

            string path = AssetDatabase.GetAssetPath(source);
            string previewFolder = _root + "/" + _level + "/LightingPreview/Materials/";
            string original = path.StartsWith(previewFolder, StringComparison.Ordinal) ? _root + "/Materials/" + path.Substring(previewFolder.Length) : path;
            XRayLevelSourceRefreshEntry entry = _refresh.Inspect(original, _level);
            if (entry.Error != null)
            {
                return source;
            }

            var copy = new Material(source) { name = source.name + " (Source Texture Preview)", hideFlags = HideFlags.HideAndDontSave };
            _temporary.Add(copy);
            _refresh.Replan(entry, copy);
            MonolithMaterialConfigurator.ConfigureSourceBindings(copy, entry.Binding);
            _materials.Add(source, copy);
            _entries.Add(source, entry);
            return copy;
        }

        private static void StartForActiveProfile(string level)
        {
            string scene = SceneManager.GetActiveScene().path;
            string suffix = "/" + level + "/Scenes/" + level + ".unity";
            if (!scene.EndsWith(suffix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Open the selected Anomaly level; preview does not switch scenes.");
            }

            Start(scene.Substring(0, scene.Length - suffix.Length), level, XRaySourceTexturePreviewMode.Combined);
        }

        private static IEnumerable<T> Components<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
        }

        private static void OnSceneSaving(Scene scene, string path)
        {
            if (IsActive && scene == _scene)
            {
                Stop();
            }
        }

        private static void OnSceneClosing(Scene scene, bool removingScene)
        {
            if (IsActive && scene == _scene)
            {
                Stop();
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            Stop();
        }
    }
}

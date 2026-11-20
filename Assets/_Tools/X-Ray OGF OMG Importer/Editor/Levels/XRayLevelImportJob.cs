using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Core.IO;
using XRay.Importer.Core.Level;
using XRay.Importer.Levels;
using XRay.Importer.Sources;
using XRay.Importer.Sources.Levels;
using XRay.Importer.Sources.Textures;
using XRay.Importer.Sources.Vfs;
using Debug = UnityEngine.Debug;

namespace XRay.Importer.Editor.Levels
{
    public sealed class XRayLevelImportRequest
    {
        public string LevelName { get; set; }
        public string DroppedAssetPath { get; set; }
        public string Identification { get; set; }
        public XRayLevelImportSettings Settings { get; set; }
        public XRaySourcePlan SourcePlan { get; set; }
        public string OutputRoot { get; set; }
    }

    public enum XRayLevelJobState
    {
        Running,
        Completed,
        Failed,
        Cancelled
    }

    public sealed class XRayLevelImportJob
    {
        public const string IMPORTER_VERSION = "XRayLevelImporter 1.1";
        public const string FAILED_REPORT_NAME = "ImportReport_LastAttempt.json";
        public const double TICK_BUDGET_MS = 150.0;
        public const int ASSETS_PER_BATCH = 64;
        public const float LOD_GEOMETRY = 0.5f;
        public const float LOD_SSA_A = 64.0f;
        public const float LOD_SSA_B = 48.0f;
        public const float LOD_SSA_DISCARD = 3.5f;

        private static XRayLevelImportJob _current;
        private static readonly Queue<XRayLevelImportRequest> _queue = new Queue<XRayLevelImportRequest>();

        private readonly XRayLevelImportRequest _request;
        private readonly XRayLevelImportRecord _record = new XRayLevelImportRecord();
        private readonly Stopwatch _total = Stopwatch.StartNew();
        private IEnumerator _routine;
        private int _progressId;
        private bool _cancelRequested;
        private bool _locked;
        private XRayLevelGeometry _geometry;
        private XRayLevelGamedataInfo _info;
        private XRayLevelEntry _entry;
        private XRayLevelData _level;
        private XRayLevelTextureLibrary _textures;
        private XRayLevelMaterialLibrary _materials;
        private XRayShaderLibrary _shaderLibrary;
        private XRayCollisionModel _collision;
        private bool[] _passable;
        private readonly Dictionary<long, Material> _materialBySlot = new Dictionary<long, Material>();
        private readonly Dictionary<int, XRayLevelSurface> _surfaces = new Dictionary<int, XRayLevelSurface>();
        private readonly XRayLevelSceneContent _content = new XRayLevelSceneContent();
        private readonly Dictionary<int, XRayLevelTerrainTextures> _terrain = new Dictionary<int, XRayLevelTerrainTextures>();
        private XRayFloraSourceBinding _floraSource;
        private bool _windAccepted;
        private string _windWavesPath;
        private XRayLevelTextureSet _lodAlbedo;
        private string _lodNormalPath;
        private string _lodShader;
        private Material _lodMaterial;
        private bool _effective;
        private string _levelPath;
        private XRayLevelImportRecord _previous;
        private bool _resourcesOnly;
        private XRaySourcePlan _sourcePlan;
        private XRayAssetIndex _sourceIndex;
        private string _previousRoot;
        private XRayLevelAnomalyImportPlan _anomalyPlan;

        private XRayLevelImportJob(XRayLevelImportRequest request)
        {
            _request = request;
        }

        public static XRayLevelImportJob Current => _current;
        public static XRayLevelImportRecord LastRecord { get; private set; }
        public static XRayLevelJobState LastState { get; private set; } = XRayLevelJobState.Completed;
        public static event Action<XRayLevelImportRecord, XRayLevelJobState> Finished;
        public string LevelName => _request.LevelName;
        public string Stage { get; private set; } = "Queued";
        public float Progress { get; private set; }
        public XRayLevelJobState State { get; private set; } = XRayLevelJobState.Running;
        public XRayLevelImportRecord Record => _record;

        public static bool IsBusy(string level)
        {
            if (_current != null && string.Equals(_current.LevelName, level, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (XRayLevelImportRequest queued in _queue)
            {
                if (string.Equals(queued.LevelName, level, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool Enqueue(XRayLevelImportRequest request)
        {
            if (request.SourcePlan != null && string.IsNullOrEmpty(request.OutputRoot)) throw new ArgumentException("An explicit source plan requires an isolated output root.", nameof(request));
            if (request.OutputRoot != null && (!request.OutputRoot.StartsWith("Assets/", StringComparison.Ordinal) || request.OutputRoot.Split('/').Length < 3 || request.OutputRoot.Contains("..") || request.OutputRoot.Contains("\\") || Array.Exists(request.OutputRoot.Split('/'), part => string.IsNullOrEmpty(part) || part.TrimEnd(' ', '.') != part) || request.OutputRoot.StartsWith(XRayLevelPaths.ROOT, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("An output override must be a dedicated Assets subdirectory outside Levels.", nameof(request));
            if (string.IsNullOrEmpty(request.LevelName) || IsBusy(request.LevelName))
            {
                Debug.LogWarning($"[X-Ray Level] Import of '{request.LevelName}' is already running or queued.");
                return false;
            }

            _queue.Enqueue(request);
            if (_current == null)
            {
                StartNext();
            }

            return true;
        }

        public static void CancelCurrent()
        {
            if (_current != null)
            {
                _current._cancelRequested = true;
            }
        }

        private static void StartNext()
        {
            if (_queue.Count == 0)
            {
                _current = null;
                return;
            }

            _current = new XRayLevelImportJob(_queue.Dequeue());
            _current.Begin();
        }

        private void Begin()
        {
            _previousRoot = XRayLevelPaths.Root;
            _routine = Run();
            _progressId = UnityEditor.Progress.Start($"X-Ray Level Import: {LevelName}", null, UnityEditor.Progress.Options.None);
            UnityEditor.Progress.RegisterCancelCallback(_progressId, () =>
            {
                _cancelRequested = true;
                return true;
            });
            EditorApplication.LockReloadAssemblies();
            _locked = true;
            EditorApplication.update += Tick;
        }

        private void Tick()
        {
            var budget = Stopwatch.StartNew();
            try
            {
                while (budget.Elapsed.TotalMilliseconds < TICK_BUDGET_MS)
                {
                    if (_cancelRequested)
                    {
                        Finish(XRayLevelJobState.Cancelled, null);
                        return;
                    }

                    if (!_routine.MoveNext())
                    {
                        Finish(_record.Errors.Count == 0 ? XRayLevelJobState.Completed : XRayLevelJobState.Failed, null);
                        return;
                    }
#if XRAY_HDRP
                    if (ReferenceEquals(_routine.Current, XRayLevelApvBaking.WAIT_FOR_EDITOR))
                    {
                        break;
                    }
#endif
                }

                UnityEditor.Progress.Report(_progressId, Progress, Stage);
            }
            catch (Exception exception)
            {
                Finish(XRayLevelJobState.Failed, exception);
            }
        }

        private void Finish(XRayLevelJobState state, Exception exception)
        {
            EditorApplication.update -= Tick;
            State = state;
            (_routine as IDisposable)?.Dispose();
            _routine = null;
            _geometry?.Dispose();
            _geometry = null;
            _collision = null;
            if (_locked)
            {
                EditorApplication.UnlockReloadAssemblies();
                _locked = false;
            }

            if (exception != null)
            {
                _record.Errors.Add($"{Stage}: {exception.GetType().Name}: {exception.Message}");
                Debug.LogError($"[X-Ray Level] Import of '{LevelName}' failed at '{Stage}': {exception}");
            }

            if (state == XRayLevelJobState.Completed)
            {
                try
                {
                    _record.SetMetric("MatrixPreparationUpdatedSets", XRayInstancePreparation.UpdateDependencies(_record.PrefabPath, _record.ScenePath));
                }
                catch (Exception preparationException)
                {
                    state = XRayLevelJobState.Failed;
                    _record.Errors.Add("Matrix preparation: " + preparationException.Message);
                    Debug.LogError("[X-Ray Level] Matrix preparation failed for '" + LevelName + "': " + preparationException);
                }
            }

            _record.Completed = state == XRayLevelJobState.Completed;
            _record.Cancelled = state == XRayLevelJobState.Cancelled;
            _record.SetMetric("ImportSeconds", _total.Elapsed.TotalSeconds);
            try
            {
                SaveManifest();
            }
            catch (Exception saveException)
            {
                Debug.LogError($"[X-Ray Level] Cannot write the manifest of '{LevelName}': {saveException}");
            }

            UnityEditor.Progress.Finish(_progressId, state == XRayLevelJobState.Completed ? UnityEditor.Progress.Status.Succeeded : state == XRayLevelJobState.Cancelled ? UnityEditor.Progress.Status.Canceled : UnityEditor.Progress.Status.Failed);
            LastRecord = _record;
            LastState = state;
            if (state == XRayLevelJobState.Completed)
            {
                Debug.Log($"[X-Ray Level] '{LevelName}' imported in {_total.Elapsed.TotalSeconds:0.0} s: {_record.ScenePath}. Warnings: {_record.Warnings.Count}.");
            }
            else if (state == XRayLevelJobState.Cancelled)
            {
                Debug.LogWarning($"[X-Ray Level] Import of '{LevelName}' was cancelled at '{Stage}'. Generated assets are kept and will be reused by the next import.");
            }

            XRayLevelPaths.UseRoot(_previousRoot);
            Finished?.Invoke(_record, state);
            StartNext();
        }

        private void SetStage(string stage, float progress)
        {
            Stage = stage;
            Progress = Mathf.Clamp01(progress);
        }

        private IEnumerator Run()
        {
            SetStage("Preparing", 0.0f);
            Prepare();
            yield return null;
            SetStage("Copying level files", 0.02f);
            CopySourceFiles();
            yield return null;
            SetStage("Reading level", 0.05f);
            ReadLevel();
            yield return null;
            SetStage("Resolving textures", 0.08f);
            foreach (object step in ResolveTextures())
            {
                yield return step;
            }

            SetStage("Importing textures", 0.25f);
            var timer = Stopwatch.StartNew();
            _textures.ImportAll();
            _record.SetMetric("TextureSeconds", timer.Elapsed.TotalSeconds);
            yield return null;
            SetStage("Creating materials", 0.4f);
            foreach (object step in CreateMaterials())
            {
                yield return step;
            }

            if (_resourcesOnly && _materials.Created == 0)
            {
                ReuseGeometry();
                if (_anomalyPlan != null)
                {
                    RecordAnomalies(XRayLevelAnomalyImporter.UpdatePrefab(_record.PrefabPath, _anomalyPlan));
                }
                foreach (object step in PrepareAdaptiveProbeVolumes(false))
                {
                    yield return step;
                }
                SetStage("Done", 1.0f);
                yield break;
            }

            timer.Restart();
            foreach (object step in BuildStaticGeometry())
            {
                yield return step;
            }

            foreach (object step in BuildInstances())
            {
                yield return step;
            }

            _record.SetMetric("MeshSeconds", timer.Elapsed.TotalSeconds);
            timer.Restart();
            SetStage("Building detail vegetation", 0.8f);
            BuildDetails();
            _record.SetMetric("DetailSeconds", timer.Elapsed.TotalSeconds);
            yield return null;
            SetStage("Building collision", 0.86f);
            BuildCollision();
            yield return null;
            SetStage("Building scene", 0.92f);
            timer.Restart();
            bool sceneCreated = BuildScene();
            _record.SetMetric("SceneSeconds", timer.Elapsed.TotalSeconds);
            foreach (object step in PrepareAdaptiveProbeVolumes(sceneCreated))
            {
                yield return step;
            }
            OpenSceneAfterImport();
            SetStage("Done", 1.0f);
        }

        private IEnumerable<object> PrepareAdaptiveProbeVolumes(bool sceneCreated)
        {
#if XRAY_HDRP
            SetStage("Preparing adaptive probe volumes", 0.96f);
            _record.SetMetric("ProbeVolumeBakeUpdated", 0);
            foreach (object step in XRayLevelApvBaking.Prepare(_record.ScenePath, _record.PrefabPath, sceneCreated, _record))
            {
                yield return step;
            }
#else
            yield break;
#endif
        }

        private void Prepare()
        {
            _sourcePlan = _request.SourcePlan;
            _effective = _sourcePlan != null ? _sourcePlan.Installation != null : XRayLevelSourceCache.IsMo2;
            var notes = new List<string>();
            if (_sourcePlan != null)
            {
                if (!_sourcePlan.IsValid) throw new XRaySourceException(string.Join("; ", _sourcePlan.Errors));
                _sourceIndex = XRayAssetIndex.Build(_sourcePlan);
                _info = _effective ? XRayLevelEffectiveCatalog.Inspect(_sourceIndex, _sourcePlan.ProfileName) : XRayLevelGamedata.Inspect(_sourcePlan.Configuration.CustomFolder);
            }
            else if (_effective && XRayLevelSourceCache.GetMo2Index(false, notes) == null)
            {
                throw new XRaySourceException("MO2 virtual index could not be built: " + string.Join("; ", XRayLevelSourceCache.LastPlan?.Errors ?? new List<string>()));
            }

            _info = _info ?? XRayLevelSourceCache.Inspect(true);
            _sourcePlan = _sourcePlan ?? (_effective ? XRayLevelSourceCache.Mo2Plan : null);
            if (!_info.IsValid)
            {
                throw new XRaySourceException(string.Join("\n", _info.Errors));
            }

            _entry = _info.Find(_request.LevelName);
            if (_entry == null)
            {
                throw new XRaySourceException($"Level '{_request.LevelName}' was not found in '{_info.LevelsFolder}'.");
            }

            XRayLevelPaths.UseRoot(_request.OutputRoot ?? (_effective ? XRayLevelSourceCache.AssetRoot : _previousRoot));
            _previous = AssetDatabase.LoadAssetAtPath<XRayLevelImportManifest>(XRayLevelPaths.ManifestPath(_entry.Name))?.Record;
            if (_effective)
            {
                _record.Warnings.AddRange(notes);
                string profileId = XRayLevelSourceCache.CreateProfileId(_sourcePlan.Installation.InstanceFolder, _sourcePlan.ProfileName);
                XRayLevelSourceManifest.FillHeader(_record.Source, _sourcePlan, _info.Effective.Index, _entry, profileId);
                XRayLevelSourceDiff diff = XRayLevelSourceManifest.Compare(_previous, _info.Effective, _entry, _sourcePlan);
                _resourcesOnly = CanReuseGeometry(_previous, diff, JsonUtility.ToJson(_request.Settings));
                foreach (XRayLevelSourceChange change in diff.Changes)
                {
                    _record.Source.ChangesSincePrevious.Add(change.ToString());
                }

                foreach (string thm in diff.MetadataReparse)
                {
                    _record.Source.ChangesSincePrevious.Add($"MetadataReparse [THM] {thm}: THM parser v{_previous.Source.ThmParserVersion} -> v{XRayThmReader.PARSER_VERSION}");
                }
            }
            else
            {
                XRayLevelSourceManifest.FillFolderHeader(_record.Source, _info);
                _sourceIndex = _sourceIndex ?? XRayLevelSourceCache.GetIndex(false);
                _sourcePlan = _sourcePlan ?? XRayLevelSourceCache.LastPlan;
                XRayLevelSourceDiff diff = XRayLevelSourceManifest.Compare(_previous, new XRayEffectiveGamedata(_sourceIndex), _entry, _sourcePlan);
                _resourcesOnly = CanReuseGeometry(_previous, diff, JsonUtility.ToJson(_request.Settings));
                foreach (XRayLevelSourceChange change in diff.Changes) _record.Source.ChangesSincePrevious.Add(change.ToString());
                foreach (string thm in diff.MetadataReparse) _record.Source.ChangesSincePrevious.Add("MetadataReparse [THM] " + thm);
            }

            _levelPath = LevelFile(XRayLevelPaths.LEVEL_FILE);
            if (_levelPath == null)
            {
                throw new XRaySourceException($"Level file of '{_entry.Name}' cannot be read from {_entry.LevelPath}.");
            }

            _record.LevelName = _entry.Name;
            _record.SourceFolder = _entry.IsEffective ? $"{_entry.VirtualFolder} ({_entry.Origin})" : _entry.Folder;
            _record.SourceLevelPath = _entry.LevelPath;
            _record.Identification = _request.Identification ?? "explicit level selection";
            _record.ImportedUtc = DateTime.UtcNow.ToString("u");
            _record.Settings = JsonUtility.ToJson(_request.Settings);
            _record.Warnings.AddRange(_info.Warnings);
            bool automaticWeather = XRayWeatherImporter.IsAutomaticEligible(File.Exists(XRayLevelPaths.ScenePath(_entry.Name)), _previous);
            _record.SetMetric(XRayWeatherImporter.AUTOMATIC_ELIGIBILITY_METRIC, automaticWeather ? 1.0 : 0.0);
            _content.WeatherProfile = XRayWeatherImporter.ResolveLevelProfile(_request.Settings, _record, out bool inheritWeather, automaticWeather);
            _content.InheritWeather = inheritWeather;
            XRayLevelPaths.EnsureFolder(XRayLevelPaths.Textures);
            XRayLevelPaths.EnsureFolder(XRayLevelPaths.Materials);
            XRayLevelPaths.EnsureFolder(XRayLevelPaths.Meshes(_entry.Name));
            XRayLevelPaths.EnsureFolder(XRayLevelPaths.Meshes(_entry.Name) + "/Static");
            XRayLevelPaths.EnsureFolder(XRayLevelPaths.Meshes(_entry.Name) + "/Mu");
            XRayLevelPaths.EnsureFolder(XRayLevelPaths.Prefabs(_entry.Name));
            XRayLevelPaths.EnsureFolder(XRayLevelPaths.Scenes(_entry.Name));
            _content.LevelName = _entry.Name;
            _content.SourceFolder = _entry.IsEffective ? _record.SourceFolder : _entry.Folder;
            _content.ImportLights = _request.Settings.ImportLights;
            _content.LightShadows = _request.Settings.StaticLightShadows;
            _content.OpenScene = _request.Settings.OpenSceneAfterImport;
            if (_request.Settings.ImportAnomalies)
            {
                XRayAssetIndex index = _info.Effective?.Index ?? _sourceIndex;
                if (index != null && index.TryGet(XRayLevelAnomalyImporter.SPAWN_PATH, out XRayIndexedFile _))
                {
                    _anomalyPlan = XRayLevelAnomalyImporter.Prepare(index, _entry.Name, XRayLevelPaths.Root, _request.Settings.AnomalyCatalog);
                }
                else
                {
                    _record.Warnings.Add("Аномалии не импортированы: в источнике отсутствует spawns/all.spawn. Обновите виртуальный индекс с папкой spawns.");
                }
            }
        }

        private void CopySourceFiles()
        {
            string folder = XRayLevelPaths.LevelFolder(_entry.Name);
            string target = folder + "/" + XRayLevelPaths.LEVEL_FILE;
            _record.SourceLevelSha256 = XRayLevelGamedata.ComputeHash(_levelPath);
            if (!string.IsNullOrEmpty(_request.DroppedAssetPath) && File.Exists(_request.DroppedAssetPath) && !SamePath(_request.DroppedAssetPath, target))
            {
                if (!File.Exists(target))
                {
                    string error = AssetDatabase.MoveAsset(_request.DroppedAssetPath, target);
                    if (!string.IsNullOrEmpty(error))
                    {
                        File.Copy(_request.DroppedAssetPath, target, false);
                        AssetDatabase.ImportAsset(target);
                        _record.Warnings.Add($"MoveAsset failed ({error}); the dropped file was copied to '{target}' and left at '{_request.DroppedAssetPath}'.");
                    }
                }
                else if (XRayLevelGamedata.SameContent(target, _request.DroppedAssetPath))
                {
                    AssetDatabase.DeleteAsset(_request.DroppedAssetPath);
                    _record.Warnings.Add($"'{target}' already holds an identical copy; the dropped duplicate '{_request.DroppedAssetPath}' was removed from Assets (the gamedata original is untouched).");
                }
                else
                {
                    _record.Warnings.Add($"'{target}' already exists with different content; the dropped file '{_request.DroppedAssetPath}' was kept in place.");
                }
            }

            if (!File.Exists(target))
            {
                File.Copy(_levelPath, target, false);
                AssetDatabase.ImportAsset(target);
            }

            if (XRayLevelGamedata.ComputeHash(target) != _record.SourceLevelSha256)
            {
                if (_entry.IsEffective)
                {
                    File.Copy(_levelPath, target, true);
                    AssetDatabase.ImportAsset(target);
                    _record.Warnings.Add($"'{target}' was refreshed from the current winner {_entry.LevelPath}.");
                }
                else
                {
                    _record.Warnings.Add($"'{target}' differs from the gamedata level '{_entry.LevelPath}'; the import uses the gamedata version.");
                }
            }

            _record.GeneratedAssets.Add(target);
            foreach (string file in _entry.Files)
            {
                string source = _entry.IsEffective ? _info.Describe(_entry.VirtualFolder + "/" + file) : Path.Combine(_entry.Folder, file);
                DescribeLevelFile(file, source);
                if (!_request.Settings.CopySourceFiles || !IsCopiedFile(file))
                {
                    continue;
                }

                if (_entry.IsEffective)
                {
                    source = LevelFile(file);
                }

                string destination = folder + "/" + file;
                var sourceInfo = new FileInfo(source);
                var destinationInfo = new FileInfo(destination);
                if (!destinationInfo.Exists || destinationInfo.Length != sourceInfo.Length || destinationInfo.LastWriteTimeUtc != sourceInfo.LastWriteTimeUtc)
                {
                    File.Copy(source, destination, true);
                    File.SetLastWriteTimeUtc(destination, sourceInfo.LastWriteTimeUtc);
                    AssetDatabase.ImportAsset(destination);
                }

                _record.GeneratedAssets.Add(destination);
            }

            if (_entry.IsEffective)
            {
                foreach (KeyValuePair<string, int> subfolder in _entry.Subfolders)
                {
                    bool meshes = subfolder.Key.Equals("meshes", StringComparison.OrdinalIgnoreCase);
                    _record.AddDependency(meshes ? XRayLevelDependencyCategory.VisualObjects : XRayLevelDependencyCategory.Unsupported, "Level subfolder", "levels\\" + _entry.Name + "\\" + subfolder.Key, _entry.VirtualFolder + "/" + subfolder.Key, null, XRayLevelDependencyStatus.Described, meshes ? "Breakable/dynamic OGF objects spawned by gameplay (brkbl#N.ogf), not part of static geometry" : "Folder content is not referenced by the level geometry", null, $"{subfolder.Value} files");
                }

                return;
            }

            foreach (string directory in Directory.GetDirectories(_entry.Folder))
            {
                string name = Path.GetFileName(directory);
                int files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length;
                _record.AddDependency(name.Equals("meshes", StringComparison.OrdinalIgnoreCase) ? XRayLevelDependencyCategory.VisualObjects : XRayLevelDependencyCategory.Unsupported, "Level subfolder", "levels\\" + _entry.Name + "\\" + name, directory, null, files > 0 ? XRayLevelDependencyStatus.Described : XRayLevelDependencyStatus.Skipped, name.Equals("meshes", StringComparison.OrdinalIgnoreCase) ? "Breakable/dynamic OGF objects spawned by gameplay (brkbl#N.ogf), not part of static geometry" : "Folder content is not referenced by the level geometry", null, $"{files} files");
            }
        }

        public static bool CanReuseGeometry(XRayLevelImportRecord previous, XRayLevelSourceDiff diff, string settings)
        {
            if (previous == null || !previous.Completed || previous.Source.ImporterVersion != IMPORTER_VERSION || GeometrySettings(previous.Settings) != GeometrySettings(settings) || !diff.HasPrevious || !File.Exists(previous.PrefabPath) || !File.Exists(previous.ScenePath))
            {
                return false;
            }

            foreach (XRayLevelSourceChange change in diff.Changes)
            {
                if (XRayLevelHomSource.IsHomPath(change.VirtualPath)) continue;
                if (change.Kind == XRayLevelSourceChangeKind.Removed || (change.Category != XRayLevelSourceManifest.CATEGORY_TEXTURE && change.Category != XRayLevelSourceManifest.CATEGORY_BUMP))
                {
                    return false;
                }
            }

            return true;
        }

        public static string GeometrySettings(string settings)
        {
            if (string.IsNullOrEmpty(settings))
            {
                return string.Empty;
            }

            XRayLevelImportSettings parsed;
            try
            {
                parsed = JsonUtility.FromJson<XRayLevelImportSettings>(settings);
            }
            catch (ArgumentException)
            {
                return settings;
            }

            parsed.MaterialUpdateMode = XRayLevelMaterialUpdateMode.Preserve;
            parsed.OpenSceneAfterImport = false;
            parsed.ImportAnomalies = false;
            parsed.AnomalyCatalog = null;
            return JsonUtility.ToJson(parsed);
        }

        private void ReuseGeometry()
        {
            _record.PrefabPath = _previous.PrefabPath;
            _record.ScenePath = _previous.ScenePath;
            foreach (XRayLevelMetric metric in _previous.Metrics)
            {
                if (!_record.Metrics.Exists(current => current.Name == metric.Name))
                {
                    _record.SetMetric(metric.Name, metric.Value);
                }
            }

            foreach (string asset in _previous.GeneratedAssets)
            {
                if (!_record.GeneratedAssets.Contains(asset))
                {
                    _record.GeneratedAssets.Add(asset);
                }
            }

            foreach (XRayLevelDependency dependency in _previous.Dependencies)
            {
                if (dependency.Category != XRayLevelDependencyCategory.Materials.ToString() && !XRayLevelHomSource.IsHomPath(dependency.Reference?.Replace('\\', '/')))
                {
                    _record.Dependencies.Add(dependency);
                }
            }

            foreach (string limitation in _previous.Limitations)
            {
                _record.AddLimitation(limitation);
            }

            foreach (string asset in _materials.Paths)
            {
                AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<Material>(asset));
            }

            _record.SetMetric("ResourceOnlyReimport", 1);
        }

        private static bool IsCopiedFile(string file)
        {
            switch (file.ToLowerInvariant())
            {
                case "level.geom":
                case "level.geomx":
                case "level.details":
                case "level.cform":
                    return true;
                default:
                    return false;
            }
        }

        private void DescribeLevelFile(string file, string path)
        {
            string lower = file.ToLowerInvariant();
            string reference = "levels\\" + _entry.Name + "\\" + file;
            switch (lower)
            {
                case "level":
                    _record.AddDependency(XRayLevelDependencyCategory.Geometry, "Level structure", reference, path, null, XRayLevelDependencyStatus.Imported, "Header, shader table, visual hierarchy, sectors, portals, static lights, glows");
                    break;
                case "level.geom":
                    _record.AddDependency(XRayLevelDependencyCategory.Geometry, "Vertex/index buffers", reference, path, null, XRayLevelDependencyStatus.Imported, "VB/IB/SWIS of all static and MU visuals");
                    break;
                case "level.geomx":
                    _record.AddDependency(XRayLevelDependencyCategory.Geometry, "Fast-path geometry", reference, path, null, XRayLevelDependencyStatus.Described, "Position-only depth/shadow geometry (FASTPATH); Unity renders depth from the main meshes");
                    break;
                case "level.cform":
                    _record.AddDependency(XRayLevelDependencyCategory.GameplayData, "Collision form", reference, path, null, _request.Settings.ImportCollision || _request.Settings.ImportDetails ? XRayLevelDependencyStatus.Imported : XRayLevelDependencyStatus.Skipped, "Static collision (MeshCollider chunks) and ground ray tests for detail placement");
                    break;
                case "level.details":
                    _record.AddDependency(XRayLevelDependencyCategory.VisualObjects, "Detail objects", reference, path, null, _request.Settings.ImportDetails ? XRayLevelDependencyStatus.Imported : XRayLevelDependencyStatus.Skipped, "Grass/plant detail models and slots");
                    break;
                case "level.ai":
                    _record.AddDependency(XRayLevelDependencyCategory.GameplayData, "AI graph", reference, path, null, XRayLevelDependencyStatus.Described, "AI navigation nodes; not imported");
                    break;
                case "level.spawn":
                case "level.game":
                    _record.AddDependency(XRayLevelDependencyCategory.GameplayData, "Spawn/game data", reference, path, null, XRayLevelDependencyStatus.Described, "ALife spawn, way points and game objects; dynamic objects are not created");
                    break;
                case XRayLevelHomSource.FILE_NAME:
                    _record.AddDependency(XRayLevelDependencyCategory.Geometry, "Occlusion mesh", reference, path, null, XRayLevelDependencyStatus.Described, "Optional source for vegetation occlusion backed by opaque level geometry");
                    break;
                case "level.som":
                    _record.AddDependency(XRayLevelDependencyCategory.GameplayData, "Sound occlusion", reference, path, null, XRayLevelDependencyStatus.Described, "Sound occlusion mesh; not imported");
                    break;
                case "level.snd_static":
                case "level.ps_static":
                    _record.AddDependency(XRayLevelDependencyCategory.VisualObjects, lower == "level.ps_static" ? "Static particles" : "Static sounds", reference, path, null, XRayLevelDependencyStatus.Unsupported, "Static particle systems / ambient sounds; not imported");
                    break;
                case "level.wallmarks":
                    _record.AddDependency(XRayLevelDependencyCategory.VisualObjects, "Wallmarks", reference, path, null, XRayLevelDependencyStatus.Unsupported, "Static wallmark decals stored separately from geometry; not imported");
                    break;
                case "level.env_mod":
                case "level.fog_vol":
                    _record.AddDependency(XRayLevelDependencyCategory.Lighting, lower == "level.fog_vol" ? "Fog volumes" : "Environment modifiers", reference, path, null, XRayLevelDependencyStatus.Unsupported, "Weather/environment modifiers; not imported");
                    break;
                case "level.ltx":
                    _record.AddDependency(XRayLevelDependencyCategory.GameplayData, "Level config", reference, path, null, XRayLevelDependencyStatus.Described, "PDA map bounds and sub-level map");
                    break;
                case "level_lods.dds":
                case "level_lods_nm.dds":
                    _record.AddDependency(XRayLevelDependencyCategory.VisualObjects, "LOD impostor atlas", reference, path, null, XRayLevelDependencyStatus.Imported, lower == "level_lods.dds" ? "s_base of details\\lod: 8-face MT_LOD impostor colors" : "s_hemi of details\\lod: rgb = normal, a = hemisphere");
                    break;
                default:
                    if (lower.StartsWith("lmap#"))
                    {
                        bool hemi = lower.EndsWith("_2.dds");
                        _record.AddDependency(XRayLevelDependencyCategory.Lighting, hemi ? "Static lightmap (hemi/sun)" : "Static lightmap (R1 rgb)", reference, path, null, hemi ? XRayLevelDependencyStatus.Found : XRayLevelDependencyStatus.Described, hemi ? "s_hemi: A = hemisphere, G = static sun" : "R1 rgb lightmap; R2+ shaders bind only lmap#N_2 as s_hemi");
                    }
                    else
                    {
                        _record.AddDependency(XRayLevelDependencyCategory.Unsupported, "Level file", reference, path, null, XRayLevelDependencyStatus.Described, "Not used by the visual import");
                    }

                    break;
            }
        }

        private void ReadLevel()
        {
            var timer = Stopwatch.StartNew();
            _level = XRayLevelReader.Read(File.ReadAllBytes(_levelPath), _entry.LevelPath);
            string geomPath = LevelFile("level.geom");
            if (geomPath == null)
            {
                throw new XRayFormatException($"'{(_entry.IsEffective ? _entry.VirtualFolder + "/level.geom" : Path.Combine(_entry.Folder, "level.geom"))}' is missing; xrLC {_level.Version} keeps vertex and index buffers in level.geom.");
            }

            _geometry = XRayLevelGeometry.Open(geomPath);
            _content.Level = _level;
            _record.XrlcVersion = _level.Version;
            _record.XrlcQuality = _level.Quality;
            _record.Warnings.AddRange(_level.Warnings);
            foreach (uint chunk in _level.UnknownChunks)
            {
                _record.Warnings.Add($"Unknown level chunk 0x{chunk:X} was ignored.");
            }

            foreach (XRayVertexBufferInfo buffer in _geometry.VertexBuffers)
            {
                string layout = $"{buffer.Kind}: {buffer.Describe()} stride {buffer.Stride}";
                if (!_record.VertexLayouts.Contains(layout))
                {
                    _record.VertexLayouts.Add(layout);
                }
            }

            _record.SetMetric("GeometryVersion", _geometry.Version);
            _record.SetMetric("Shaders", _level.Shaders.Count);
            _record.SetMetric("Visuals", _level.Visuals.Count);
            _record.SetMetric("Sectors", _level.Sectors.Count);
            _record.SetMetric("Portals", _level.Portals.Count);
            _record.SetMetric("Lights", _level.Lights.Count);
            _record.SetMetric("Glows", _level.Glows.Count);
            _record.SetMetric("VertexBuffers", _geometry.VertexBuffers.Count);
            _record.SetMetric("IndexBuffers", _geometry.IndexBuffers.Count);
            _record.SetMetric("ParseSeconds", timer.Elapsed.TotalSeconds);
            _shaderLibrary = XRayLevelSourceCache.GetShaders(_info, _record.Warnings);
            if (_level.Glows.Count > 0)
            {
                _record.AddDependency(XRayLevelDependencyCategory.VisualObjects, "Glows", $"{_level.Glows.Count} glow sprites", _entry.LevelPath, null, XRayLevelDependencyStatus.Unsupported, "Camera-facing flare sprites (effects\\glow); not reproduced");
            }

            var bounds = new Bounds();
            bool hasBounds = false;
            foreach (XRayLevelSector sector in _level.Sectors)
            {
                if (sector.RootVisual < 0)
                {
                    continue;
                }

                Bounds sectorBounds = XRayLevelSceneBuilder.VisualBounds(_level.Visuals[sector.RootVisual]);
                if (hasBounds)
                {
                    bounds.Encapsulate(sectorBounds);
                }
                else
                {
                    bounds = sectorBounds;
                    hasBounds = true;
                }
            }

            _content.Bounds = bounds;
        }

        private IEnumerable ResolveTextures()
        {
            XRayAssetIndex index = _sourceIndex ?? _info.Effective?.Index ?? XRayLevelSourceCache.GetIndex(false);
            if (index == null)
            {
                throw new XRaySourceException("Level texture index could not be built: " + string.Join("; ", XRayLevelSourceCache.LastPlan?.Errors ?? new List<string>()));
            }

            _record.SetMetric("IndexedFiles", index.Statistics.FileCount);
            _textures = new XRayLevelTextureLibrary(index, _entry.Name, _record, XRayAssetSourceSettings.GetEffective().Import.FlipNormalGreen, _effective);
            var used = new HashSet<int>();
            var treeUsed = new HashSet<int>();
            foreach (XRayLevelVisual visual in _level.Visuals)
            {
                if (!visual.IsGeometry)
                {
                    continue;
                }

                (visual.IsTree ? treeUsed : used).Add(visual.ShaderId);
            }

            int processed = 0;
            var all = new List<int>(used);
            foreach (int id in treeUsed)
            {
                if (!all.Contains(id))
                {
                    all.Add(id);
                }
            }

            foreach (int id in all)
            {
                XRayLevelShader shader = _level.Shaders[id];
                XRayBlender blender = null;
                _shaderLibrary?.TryGet(shader.EngineShader, out blender);
                XRayLevelSurface staticSurface = XRayLevelSurfaceRules.Classify(shader, blender, false);
                string classLine = $"{shader.EngineShader} -> {staticSurface.BlenderClass} ({staticSurface.RuleName}{(staticSurface.IsFallback ? ", HDRP/Lit fallback" : string.Empty)})";
                if (!_record.ShaderClasses.Contains(classLine))
                {
                    _record.ShaderClasses.Add(classLine);
                }

                if (staticSurface.Note != null)
                {
                    _record.AddLimitation(staticSurface.Note);
                }

                if (staticSurface.Kind != XRayLevelSurfaceKind.Skipped)
                {
                    _textures.Request(shader.Texture, staticSurface.UsesMonolith, $"Base texture of shader {id} '{shader.Raw}'");
                }

                if (shader.Lightmaps.Count >= 2)
                {
                    string lightmap = shader.Lightmaps[1];
                    _textures.RequestPacked($"levels\\{_entry.Name}\\{lightmap}.dds", XRayLevelDependencyCategory.Lighting, "Hemisphere lightmap", "s_hemi (A) for USE_LM_HEMI static geometry");
                }
                else if (shader.Lightmaps.Count == 1)
                {
                    _record.AddDependency(XRayLevelDependencyCategory.Lighting, "Terrain lightmap", shader.Lightmaps[0], null, XRayVirtualPath.TextureToVirtualPath(shader.Lightmaps[0], ".dds"), XRayLevelDependencyStatus.Described, "s_lmap of the terrain impl shader (only used with R2 static sun); hemisphere comes from base alpha");
                }

                if (staticSurface.Kind == XRayLevelSurfaceKind.Terrain && blender != null)
                {
                    _terrain[id] = _textures.RequestTerrain(shader, blender, _request.Settings.TerrainImportMode == XRayTerrainImportMode.EnableVerifiedTrueStalker);
                }

                processed++;
                if (processed % 16 == 0)
                {
                    SetStage($"Resolving textures {processed}/{all.Count}", 0.08f + 0.17f * processed / all.Count);
                    yield return null;
                }
            }

            RequestLodTextures();
            if (_request.Settings.FloraWindMode != XRayFloraWindImportMode.Disabled)
            {
                RequestFloraWind();
            }
        }

        private void RequestFloraWind()
        {
            if (_floraSource == null)
            {
                ResolveFloraSource();
            }

            _record.Source.FloraWindProfile = _floraSource.WindProfile.ToString();
            _record.Source.FloraWindContractHash = _floraSource.WindProfile != XRayFloraSourceProfile.Unknown ? _floraSource.WindContractHash : "anomaly:" + _floraSource.AnomalyWindHash + ";ts:" + _floraSource.TrueStalkerWindHash;
            foreach (XRayResolvedFile file in _floraSource.WindFiles)
            {
                _record.AddDependency(XRayLevelDependencyCategory.Materials, "Flora wind contract", file.DisplayPath, file.IsResolved ? file.Winner.Location : null, file.VirtualPath, file.IsResolved ? XRayLevelDependencyStatus.Found : XRayLevelDependencyStatus.Missing, "Verified tree wind HLSL contract; tree WindFactor storage and wind material binding depend on its SHA-256", null, "sha256:" + XRayTerrainSourceBinding.Hash(file));
            }

            _windAccepted = XRayFloraSourceBinding.AcceptsWind(_request.Settings.FloraWindMode, _floraSource.WindProfile);
            if (!_windAccepted)
            {
                _record.Warnings.Add($"Source flora wind mode '{_request.Settings.FloraWindMode}' was requested, but the effective wind shader contract is '{_floraSource.WindProfile}'; trees stay static and meshes keep the legacy vertex data.");
                return;
            }

            if (_floraSource.WindProfile == XRayFloraSourceProfile.AnomalySss)
            {
                XRayTextureAssetResult waves = _textures.RequestPacked(XRayFloraSourceBinding.ANOMALY_WIND_WAVES.Replace('/', '\\'), XRayLevelDependencyCategory.Materials, "Wind waves (s_waves)", "SSFX tree branch flow map, sampled with smp_linear2 (wrap, linear) at mip 0");
                _windWavesPath = waves.AssetPath;
                if (_windWavesPath == null)
                {
                    _windAccepted = false;
                    _record.Warnings.Add("Anomaly wind flow map 'textures\\fx\\wind_wave.dds' could not be imported; trees stay static.");
                }
            }
        }

        private void ResolveFloraSource()
        {
            _floraSource = XRayFloraSourceBinding.Resolve(_textures.Resolver);
            _record.Source.FloraProfile = _floraSource.Profile.ToString();
            _record.Source.FloraContractHash = _floraSource.Profile != XRayFloraSourceProfile.Unknown ? _floraSource.ContractHash : "anomaly:" + _floraSource.AnomalyHash + ";ts:" + _floraSource.TrueStalkerHash;
            foreach (XRayResolvedFile file in _floraSource.Files)
            {
                _record.AddDependency(XRayLevelDependencyCategory.Materials, "Flora source contract", file.DisplayPath, file.IsResolved ? file.Winner.Location : null, file.VirtualPath, file.IsResolved ? XRayLevelDependencyStatus.Found : XRayLevelDependencyStatus.Missing, "Verified flora HLSL contract; source flora activation depends on its SHA-256", null, "sha256:" + XRayTerrainSourceBinding.Hash(file));
            }

            if (_request.Settings.FloraImportMode != XRayFloraImportMode.Preserve && !XRayFloraSourceBinding.Accepts(_request.Settings.FloraImportMode, _floraSource.Profile))
            {
                _record.Warnings.Add($"Source flora mode '{_request.Settings.FloraImportMode}' was requested, but the effective flora shader contract is '{_floraSource.Profile}'; flora materials keep legacy rendering.");
            }
        }

        private void RequestLodTextures()
        {
            XRayLevelVisual lod = null;
            foreach (XRayLevelVisual visual in _level.Visuals)
            {
                if (visual.ModelType == XRayLevelFormat.MT_LOD && visual.LodVertices != null)
                {
                    lod = visual;
                    break;
                }
            }

            if (lod == null)
            {
                return;
            }

            XRayLevelShader shader = _level.Shaders[lod.ShaderId];
            _lodShader = shader.EngineShader;
            _lodAlbedo = _textures.Request(shader.Texture, false, "MT_LOD impostor atlas (s_base of " + shader.Raw + ")");
            XRayResolvedFile normal = _textures.Resolver.ResolveTexture(shader.Texture + "_nm", ".dds");
            _lodNormalPath = normal.IsResolved ? _textures.RequestPacked(normal.VirtualPath, XRayLevelDependencyCategory.VisualObjects, "LOD normal/hemi atlas", "s_hemi of " + shader.Raw + ": rgb = normal, a = hemisphere").AssetPath : null;
        }

        private IEnumerable CreateMaterials()
        {
            if (_request.Settings.FloraImportMode != XRayFloraImportMode.Preserve && _floraSource == null)
            {
                ResolveFloraSource();
            }

            bool floraAccepted = _floraSource != null && XRayFloraSourceBinding.Accepts(_request.Settings.FloraImportMode, _floraSource.Profile);
            _materials = new XRayLevelMaterialLibrary(_record, _request.Settings.MaterialUpdateMode, _effective, _request.Settings.TerrainImportMode, _request.Settings.TerrainQuality, _request.Settings.FloraImportMode, _floraSource);
            if (_request.Settings.FloraWindMode != XRayFloraWindImportMode.Disabled)
            {
                _materials.ConfigureWind(_windAccepted ? _floraSource.WindProfile : XRayFloraSourceProfile.Unknown, _windWavesPath, XRayLevelSourceBindings.PackedFlipV(_windWavesPath), _floraSource?.WindContractHash);
            }
            var slots = new List<(int Shader, bool Tree)>();
            var seen = new HashSet<long>();
            foreach (XRayLevelVisual visual in _level.Visuals)
            {
                if (visual.IsGeometry && seen.Add(SlotKey(visual.ShaderId, visual.IsTree)))
                {
                    slots.Add((visual.ShaderId, visual.IsTree));
                }
            }

            int processed = 0;
            foreach ((int shaderId, bool tree) in slots)
            {
                XRayLevelShader shader = _level.Shaders[shaderId];
                XRayBlender blender = null;
                _shaderLibrary?.TryGet(shader.EngineShader, out blender);
                XRayLevelSurface surface = XRayLevelSurfaceRules.Classify(shader, blender, tree);
                _surfaces[(int)SlotKey(shaderId, tree)] = surface;
                if (surface.Kind == XRayLevelSurfaceKind.Skipped)
                {
                    continue;
                }

                XRayLevelTextureSet textures = _textures.Request(shader.Texture, surface.UsesMonolith, null);
                var request = new XRayLevelMaterialRequest { ShaderName = shader.EngineShader, Surface = surface, Textures = textures };
                if (_terrain.TryGetValue(shaderId, out XRayLevelTerrainTextures terrain))
                {
                    request.Terrain = terrain;
                }
                if (floraAccepted && surface.Flora && textures.BumpPath != null && XRayLevelMaterialLibrary.UsesFloraNormal(textures.Description))
                {
                    request.Flora = _textures.RequestFlora(textures, _floraSource);
                }
                if (surface.HemiMode == XRayLevelHemiMode.Lightmap && shader.Lightmaps.Count >= 2)
                {
                    XRayTextureAssetResult lightmap = _textures.RequestPacked($"levels\\{_entry.Name}\\{shader.Lightmaps[1]}.dds", XRayLevelDependencyCategory.Lighting, "Hemisphere lightmap", "s_hemi (A)");
                    request.LightmapPath = lightmap.AssetPath;
                    request.LightmapKey = lightmap.AssetPath != null ? XRayAssetMarker.ReadKey(lightmap.AssetPath) : "missing:" + shader.Lightmaps[1];
                    request.LightmapLabel = _entry.Name + "_" + shader.Lightmaps[1];
                    if (lightmap.AssetPath == null)
                    {
                        _record.Warnings.Add($"Lightmap '{shader.Lightmaps[1]}' of shader {shaderId} is missing; the material uses the constant Hemisphere fallback (_MonolithEnvHemisphere), which is not the game value.");
                    }
                }

                Material material = _materials.Get(request);
                _materialBySlot[SlotKey(shaderId, tree)] = material;
                processed++;
                if (processed % 24 == 0)
                {
                    SetStage($"Creating materials {processed}/{slots.Count}", 0.4f + 0.1f * processed / slots.Count);
                    yield return null;
                }
            }

            if (_lodAlbedo != null)
            {
                _lodMaterial = _materials.GetLod(_lodShader, _lodAlbedo, _lodNormalPath);
            }

            AssetDatabase.SaveAssets();
            _record.SetMetric("Materials", _materials.Paths.Count);
            _record.SetMetric("MaterialsCreated", _materials.Created);
            _record.SetMetric("MaterialsReused", _materials.Reused);
            _record.SetMetric("MaterialsRegenerated", _materials.Regenerated.Count);
            _record.SetMetric("FallbackMaterials", _materials.Fallbacks);
            _record.SetMetric("DecalMaterialsMigrated", _materials.MigratedDecals);
            _record.SetMetric("FloraMaterials", _materials.FloraMaterials);
            _record.SetMetric("FloraSourceMaterials", _materials.FloraSourceMaterials);
            _record.SetMetric("FloraUserOverrides", _materials.FloraUserOverrides);
            if (_request.Settings.FloraWindMode != XRayFloraWindImportMode.Disabled)
            {
                _record.SetMetric("FloraWindMaterials", _materials.FloraWindMaterials);
            }
            _record.GeneratedAssets.AddRange(_materials.Paths);
        }

        private static long SlotKey(int shader, bool tree)
        {
            return shader * 2L + (tree ? 1 : 0);
        }

        private IEnumerable BuildStaticGeometry()
        {
            var builder = new XRayLevelMeshBuilder(_level, _geometry);
            var chunks = new List<(int Sector, List<XRayLevelVisual> Visuals)>();
            foreach (XRayLevelSector sector in _level.Sectors)
            {
                if (sector.RootVisual >= 0)
                {
                    Partition(_level.Visuals[sector.RootVisual], sector.Index, chunks);
                }
            }

            int spatialChunks = chunks.Count;
            for (int c = 0; c < spatialChunks; c++)
            {
                (int sector, List<XRayLevelVisual> visuals) = chunks[c];
                for (int v = visuals.Count - 1; v >= 0; v--)
                {
                    XRayLevelVisual visual = visuals[v];
                    if (_surfaces[(int)SlotKey(visual.ShaderId, false)].IsDecal)
                    {
                        chunks.Add((sector, new List<XRayLevelVisual> { visual }));
                        visuals.RemoveAt(v);
                    }
                }
            }

            var submeshShaders = new List<int>();
            int built = 0;
            int pending = 0;
            int staticVisuals = 0;
            long vertices = 0;
            long triangles = 0;
            int submeshes = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                for (int c = 0; c < chunks.Count; c++)
                {
                    (int sector, List<XRayLevelVisual> visuals) = chunks[c];
                    var shadowSlices = new List<XRayLevelGeometrySlice>();
                    var noShadowSlices = new List<XRayLevelGeometrySlice>();
                    foreach (XRayLevelVisual visual in visuals)
                    {
                        XRayLevelSurface surface = _surfaces[(int)SlotKey(visual.ShaderId, false)];
                        if (surface.Kind == XRayLevelSurfaceKind.Skipped)
                        {
                            continue;
                        }

                        XRayLevelGeometrySlice slice = builder.ReadSlice(visual);
                        (surface.CastShadows ? shadowSlices : noShadowSlices).Add(slice);
                        staticVisuals++;
                        vertices += slice.VertexCount;
                        triangles += slice.Indices.Length / 3;
                    }

                    foreach (bool shadows in new[] { true, false })
                    {
                        List<XRayLevelGeometrySlice> slices = shadows ? shadowSlices : noShadowSlices;
                        if (slices.Count == 0)
                        {
                            continue;
                        }

                        bool decal = visuals.Count == 1 && _surfaces[(int)SlotKey(visuals[0].ShaderId, false)].IsDecal;
                        string name = decal ? $"S{sector:00}_V{visuals[0].Index:00000}_decal" : $"S{sector:00}_C{c:0000}{(shadows ? string.Empty : "_noshadow")}";
                        string path = XRayLevelPaths.Meshes(_entry.Name) + "/Static/" + name + ".asset";
                        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                        Mesh mesh = builder.BuildMesh(name, slices, submeshShaders, existing);
                        var materials = new Material[submeshShaders.Count];
                        for (int i = 0; i < materials.Length; i++)
                        {
                            _materialBySlot.TryGetValue(SlotKey(submeshShaders[i], false), out materials[i]);
                        }

                        if (existing == null)
                        {
                            AssetDatabase.CreateAsset(mesh, path);
                        }
                        else
                        {
                            EditorUtility.SetDirty(mesh);
                        }

                        SetNotReadable(mesh);
                        _record.GeneratedAssets.Add(path);
                        _content.Chunks.Add(new XRayLevelChunkRenderer { Sector = sector, Name = "Chunk_" + name, Mesh = mesh, Materials = materials, CastShadows = shadows });
                        submeshes += materials.Length;
                        built++;
                        pending++;
                    }

                    if (pending >= ASSETS_PER_BATCH)
                    {
                        AssetDatabase.StopAssetEditing();
                        pending = 0;
                        SetStage($"Static geometry {c + 1}/{chunks.Count}", 0.5f + 0.15f * (c + 1) / chunks.Count);
                        yield return null;
                        AssetDatabase.StartAssetEditing();
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            _record.SetMetric("StaticChunks", spatialChunks);
            _record.SetMetric("DecalVisualRenderers", chunks.Count - spatialChunks);
            _record.SetMetric("StaticRenderers", built);
            _record.SetMetric("StaticSubmeshes", submeshes);
            _record.SetMetric("StaticVisuals", staticVisuals);
            _record.SetMetric("StaticVertices", vertices);
            _record.SetMetric("StaticTriangles", triangles);
            _record.SetMetric("MeshAssets", built);
        }

        private void Partition(XRayLevelVisual node, int sector, List<(int, List<XRayLevelVisual>)> chunks)
        {
            if (node.IsGeometry)
            {
                if (!node.IsTree)
                {
                    chunks.Add((sector, new List<XRayLevelVisual> { node }));
                }

                return;
            }

            float extent = Mathf.Max(node.BoundsMax[0] - node.BoundsMin[0], node.BoundsMax[2] - node.BoundsMin[2]);
            var leaves = new List<XRayLevelVisual>();
            var groups = new List<XRayLevelVisual>();
            foreach (int child in node.Children)
            {
                XRayLevelVisual visual = _level.Visuals[child];
                if (visual.IsGeometry)
                {
                    if (!visual.IsTree)
                    {
                        leaves.Add(visual);
                    }
                }
                else if (visual.ModelType == XRayLevelFormat.MT_HIERRARHY)
                {
                    groups.Add(visual);
                }
            }

            if (extent <= _request.Settings.ChunkSize || groups.Count == 0)
            {
                var all = new List<XRayLevelVisual>();
                CollectStatic(node, all);
                if (all.Count > 0)
                {
                    chunks.Add((sector, all));
                }

                return;
            }

            if (leaves.Count > 0)
            {
                chunks.Add((sector, leaves));
            }

            foreach (XRayLevelVisual group in groups)
            {
                Partition(group, sector, chunks);
            }
        }

        private void CollectStatic(XRayLevelVisual node, List<XRayLevelVisual> result)
        {
            if (node.IsGeometry)
            {
                if (!node.IsTree)
                {
                    result.Add(node);
                }

                return;
            }

            if (node.ModelType != XRayLevelFormat.MT_HIERRARHY)
            {
                return;
            }

            foreach (int child in node.Children)
            {
                CollectStatic(_level.Visuals[child], result);
            }
        }

        private IEnumerable BuildInstances()
        {
            var builder = new XRayLevelMeshBuilder(_level, _geometry) { TreeWindFactor = _windAccepted };
            var meshes = new Dictionary<string, (Mesh Mesh, int Shader)>();
            var instances = new Dictionary<string, List<XRayLevelVisual>>();
            int lods = 0;
            int unsupported = 0;
            int hierarchy = 0;
            foreach (XRayLevelVisual visual in _level.Visuals)
            {
                if (visual.ModelType == XRayLevelFormat.MT_LOD)
                {
                    lods++;
                }
                else if (visual.ModelType == XRayLevelFormat.MT_HIERRARHY)
                {
                    hierarchy++;
                }
                else if (!visual.IsGeometry)
                {
                    unsupported++;
                }

                if (!visual.IsTree)
                {
                    continue;
                }

                string key = MuKey(visual);
                if (!instances.TryGetValue(key, out List<XRayLevelVisual> list))
                {
                    list = new List<XRayLevelVisual>();
                    instances.Add(key, list);
                }

                list.Add(visual);
            }

            var lodOwners = new Dictionary<int, int>();
            XRayLevelLodSet lodSet = BuildLodSet(lodOwners);
            _record.SetMetric("LodVisuals", lods);
            _record.SetMetric("LodImpostors", lodSet != null ? lodSet.Count : 0);
            _record.SetMetric("LodOwnedTrees", lodOwners.Count);
            _record.SetMetric("HierarchyVisuals", hierarchy);
            _record.SetMetric("UnsupportedVisuals", unsupported);
            if (lods > 0 && lodSet == null)
            {
                _record.AddLimitation($"{lods} MT_LOD visuals: their child MU/tree geometry is rendered at every distance; the LOD impostor atlas is unavailable.");
            }

            var submeshShaders = new List<int>();
            int processed = 0;
            long muVertices = 0;
            long muTriangles = 0;
            long instancedTriangles = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (KeyValuePair<string, List<XRayLevelVisual>> pair in instances)
                {
                    XRayLevelVisual first = pair.Value[0];
                    XRayLevelSurface surface = _surfaces[(int)SlotKey(first.ShaderId, true)];
                    if (surface.Kind == XRayLevelSurfaceKind.Skipped)
                    {
                        continue;
                    }

                    XRayLevelGeometrySlice slice = builder.ReadSlice(first);
                    string name = "Mu_" + pair.Key;
                    string path = XRayLevelPaths.Meshes(_entry.Name) + "/Mu/" + name + ".asset";
                    var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    Mesh mesh = builder.BuildMesh(name, new List<XRayLevelGeometrySlice> { slice }, submeshShaders, existing);
                    if (existing == null)
                    {
                        AssetDatabase.CreateAsset(mesh, path);
                    }
                    else
                    {
                        EditorUtility.SetDirty(mesh);
                    }

                    SetNotReadable(mesh);
                    _record.GeneratedAssets.Add(path);
                    meshes[pair.Key] = (mesh, first.ShaderId);
                    muVertices += slice.VertexCount;
                    muTriangles += slice.Indices.Length / 3;
                    instancedTriangles += (long)slice.Indices.Length / 3 * pair.Value.Count;
                    processed++;
                    if (processed % ASSETS_PER_BATCH == 0)
                    {
                        AssetDatabase.StopAssetEditing();
                        SetStage($"MU meshes {processed}/{instances.Count}", 0.65f + 0.1f * processed / instances.Count);
                        yield return null;
                        AssetDatabase.StartAssetEditing();
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            float cellSize = _request.Settings.InstanceCellSize;
            var batches = new List<XRayInstanceBatch>();
            int instanceCount = 0;
            int negative = 0;
            foreach (KeyValuePair<string, List<XRayLevelVisual>> pair in instances)
            {
                if (!meshes.TryGetValue(pair.Key, out (Mesh Mesh, int Shader) entry))
                {
                    continue;
                }

                XRayLevelSurface surface = _surfaces[(int)SlotKey(entry.Shader, true)];
                _materialBySlot.TryGetValue(SlotKey(entry.Shader, true), out Material material);
                var cells = new Dictionary<long, List<XRayLevelVisual>>();
                foreach (XRayLevelVisual visual in pair.Value)
                {
                    float[] m = visual.Tree.Matrix;
                    long cell = ((long)Mathf.FloorToInt(m[12] / cellSize) << 32) ^ (uint)Mathf.FloorToInt(m[14] / cellSize);
                    if (!cells.TryGetValue(cell, out List<XRayLevelVisual> list))
                    {
                        list = new List<XRayLevelVisual>();
                        cells.Add(cell, list);
                    }

                    list.Add(visual);
                }

                foreach (List<XRayLevelVisual> list in cells.Values)
                {
                    for (int start = 0; start < list.Count; start += XRayInstanceBatchSet.MAX_INSTANCES_PER_BATCH)
                    {
                        int count = Mathf.Min(XRayInstanceBatchSet.MAX_INSTANCES_PER_BATCH, list.Count - start);
                        var matrices = new Matrix4x4[count];
                        var data = new Vector4[count];
                        var lodIndices = new int[count];
                        bool owned = false;
                        Bounds bounds = default;
                        for (int i = 0; i < count; i++)
                        {
                            XRayTreeDefinition tree = list[start + i].Tree;
                            lodIndices[i] = lodOwners.TryGetValue(list[start + i].Index, out int owner) ? owner : -1;
                            owned |= lodIndices[i] >= 0;
                            Matrix4x4 matrix = XRayLevelMeshBuilder.ToUnityMatrix(tree.Matrix);
                            if (matrix.determinant < 0.0f)
                            {
                                negative++;
                            }

                            matrices[i] = matrix;
                            const float SHADER_SCALE = 1.5f * 1.3333f * 0.5f;
                            data[i] = new Vector4(SHADER_SCALE * tree.ScaleHemi, SHADER_SCALE * tree.BiasHemi, 0.0f, 0.0f);
                            Bounds transformed = Transform(entry.Mesh.bounds, matrix);
                            if (i == 0)
                            {
                                bounds = transformed;
                            }
                            else
                            {
                                bounds.Encapsulate(transformed);
                            }
                        }

                        batches.Add(new XRayInstanceBatch(entry.Mesh.name, entry.Mesh, new[] { material }, bounds, matrices, data, surface.CastShadows, owned ? lodIndices : null));
                        instanceCount += count;
                    }
                }
            }

            string setPath = XRayLevelPaths.Meshes(_entry.Name) + "/" + _entry.Name + "_StaticProps.asset";
            var set = AssetDatabase.LoadAssetAtPath<XRayInstanceBatchSet>(setPath);
            bool created = set == null;
            if (created)
            {
                set = ScriptableObject.CreateInstance<XRayInstanceBatchSet>();
            }

            set.Initialize(_entry.Name, cellSize, batches, lodSet);
            XRayInstancePreparation.Ensure(set);
            if (created)
            {
                AssetDatabase.CreateAsset(set, setPath);
            }
            else
            {
                EditorUtility.SetDirty(set);
                AssetDatabase.SaveAssetIfDirty(set);
            }

            _record.GeneratedAssets.Add(setPath);
            _content.Instances = batches.Count > 0 ? set : null;
            _record.SetMetric("MuInstances", instanceCount);
            _record.SetMetric("UniqueMuMeshes", meshes.Count);
            _record.SetMetric("InstanceBatches", batches.Count);
            _record.SetMetric("MuVertices", muVertices);
            _record.SetMetric("MuTriangles", muTriangles);
            _record.SetMetric("MuInstancedTriangles", instancedTriangles);
            _record.AddMetric("MeshAssets", meshes.Count);
            if (negative > 0)
            {
                _record.Warnings.Add($"{negative} MU instances have a negative-determinant transform (mirrored); Unity flips their winding automatically, X-Ray does not.");
            }
        }

        private XRayLevelLodSet BuildLodSet(Dictionary<int, int> owners)
        {
            if (_lodMaterial == null)
            {
                return null;
            }

            var spheres = new List<Vector4>();
            var factors = new List<float>();
            var positions = new List<Vector3>();
            var uvs = new List<Vector2>();
            var hemi = new List<float>();
            var sun = new List<float>();
            var normals = new List<Vector3>();
            var corners = new Vector3[4];
            foreach (XRayLevelVisual visual in _level.Visuals)
            {
                if (visual.ModelType != XRayLevelFormat.MT_LOD || visual.LodVertices == null)
                {
                    continue;
                }

                int lod = spheres.Count;
                spheres.Add(new Vector4(visual.SphereCenter[0], visual.SphereCenter[1], visual.SphereCenter[2], visual.SphereRadius));
                factors.Add(LodFactor(visual));
                for (int face = 0; face < XRayLevelLodSet.FACES; face++)
                {
                    for (int corner = 0; corner < 4; corner++)
                    {
                        XRayLodVertex vertex = visual.LodVertices[face * 4 + corner];
                        corners[corner] = new Vector3((float)vertex.Position.X, (float)vertex.Position.Y, (float)vertex.Position.Z);
                        positions.Add(corners[corner]);
                        uvs.Add(new Vector2(vertex.U, 1.0f - vertex.V));
                        hemi.Add(((vertex.RgbHemi >> 24) & 0xFF) / 255.0f);
                        sun.Add(vertex.Sun / 255.0f);
                    }

                    Vector3 normal = FaceNormal(corners[0], corners[1], corners[2]) + FaceNormal(corners[1], corners[2], corners[3]) + FaceNormal(corners[2], corners[3], corners[0]) + FaceNormal(corners[3], corners[0], corners[1]);
                    normals.Add(-(normal * 0.25f).normalized);
                }

                AssignLodOwner(visual, lod, owners);
            }

            if (spheres.Count == 0)
            {
                return null;
            }

            string path = XRayLevelPaths.Meshes(_entry.Name) + "/" + _entry.Name + "_Lods.asset";
            var set = AssetDatabase.LoadAssetAtPath<XRayLevelLodSet>(path);
            bool created = set == null;
            if (created)
            {
                set = ScriptableObject.CreateInstance<XRayLevelLodSet>();
            }

            set.Initialize(_lodMaterial, spheres.ToArray(), factors.ToArray(), positions.ToArray(), uvs.ToArray(), hemi.ToArray(), sun.ToArray(), normals.ToArray(), LOD_GEOMETRY, LOD_SSA_A, LOD_SSA_B, LOD_SSA_DISCARD);
            if (created)
            {
                AssetDatabase.CreateAsset(set, path);
            }
            else
            {
                EditorUtility.SetDirty(set);
                AssetDatabase.SaveAssetIfDirty(set);
            }

            _record.GeneratedAssets.Add(path);
            return set;
        }

        private void AssignLodOwner(XRayLevelVisual node, int lod, Dictionary<int, int> owners)
        {
            foreach (int child in node.Children)
            {
                XRayLevelVisual visual = _level.Visuals[child];
                if (visual.IsTree)
                {
                    owners[visual.Index] = lod;
                }
                else if (visual.ModelType == XRayLevelFormat.MT_HIERRARHY)
                {
                    AssignLodOwner(visual, lod, owners);
                }
            }
        }

        private static float LodFactor(XRayLevelVisual visual)
        {
            var extents = new float[3];
            for (int i = 0; i < 3; i++)
            {
                extents[i] = (visual.BoundsMax[i] - visual.BoundsMin[i]) * 0.5f;
            }

            Array.Sort(extents);
            float r = visual.SphereRadius;
            if (r <= 0.0f)
            {
                return 1.0f;
            }

            float a = Mathf.Min(extents[1], r);
            float sf = 4.0f * (0.5f * (r * r * Mathf.Asin(a / r) + a * Mathf.Sqrt(r * r - a * a)));
            float ss = Mathf.PI * r * r;
            return sf / ss;
        }

        private static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 c)
        {
            return Vector3.Cross(b - a, c - b).normalized;
        }

        private static string MuKey(XRayLevelVisual visual)
        {
            XRayGeometryRange range = visual.Geometry;
            return $"{visual.ShaderId}_{range.VertexBuffer}_{range.VertexOffset}_{range.IndexBuffer}_{range.IndexOffset}_{visual.SwiContainer}";
        }

        private static Bounds Transform(Bounds bounds, Matrix4x4 matrix)
        {
            Vector3 center = matrix.MultiplyPoint3x4(bounds.center);
            var result = new Bounds(center, Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                result.Encapsulate(matrix.MultiplyPoint3x4(corner));
            }

            return result;
        }

        private static void SetNotReadable(Mesh mesh)
        {
            var serialized = new SerializedObject(mesh);
            SerializedProperty readable = serialized.FindProperty("m_IsReadable");
            if (readable != null && readable.boolValue)
            {
                readable.boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private void EnsureCollision()
        {
            if (_collision != null)
            {
                return;
            }

            string path = LevelFile("level.cform");
            if (path == null)
            {
                _record.Warnings.Add($"'{(_entry.IsEffective ? _entry.VirtualFolder + "/level.cform" : Path.Combine(_entry.Folder, "level.cform"))}' is missing; collision and detail placement are skipped.");
                return;
            }

            _collision = XRayCollisionModel.Read(path);
            _collision.BuildGrid(XRayDetails.SLOT_SIZE);
            XRayGameMaterials materials = XRayLevelSourceCache.GetGameMaterials(_info, _record.Warnings);
            _passable = materials?.CreatePassableMask();
            if (materials == null)
            {
                _record.Warnings.Add("gamemtl.xr is unavailable; passable materials are not excluded from detail ground tests and collision.");
            }
        }

        private void BuildDetails()
        {
            if (!_request.Settings.ImportDetails)
            {
                return;
            }

            string path = LevelFile("level.details");
            if (path == null)
            {
                path = _entry.IsEffective ? _entry.VirtualFolder + "/level.details" : Path.Combine(_entry.Folder, "level.details");
                _record.Warnings.Add($"level.details was not found in '{(_entry.IsEffective ? _entry.VirtualFolder : _entry.Folder)}'; the level has no detail vegetation.");
                _record.AddDependency(XRayLevelDependencyCategory.VisualObjects, "Detail objects", "levels\\" + _entry.Name + "\\level.details", null, path, XRayLevelDependencyStatus.Missing, "Grass/plant detail models and slots");
                return;
            }

            EnsureCollision();
            if (_collision == null)
            {
                return;
            }

            XRayDetails details = XRayDetails.Read(File.ReadAllBytes(path), path);
            var builder = new XRayLevelDetailBuilder(_entry.Name, details, _request.Settings, _record, _info);
            builder.Place(_collision, _passable);
            builder.RequestTextures(_textures);
            _textures.ImportAll();
            XRayDetailDatabase database = builder.Build(_textures, _materials);
            builder.BindRuntimeSource(database, path, LevelFile("level.cform"), _passable, _collision);
            foreach (XRayDetailModelEntry model in database.Models)
            {
                if (model.Material != null && model.Material.GetTexture("_BaseColorMap") == null)
                {
                    _record.Warnings.Add($"Detail material '{model.Material.name}' has no diffuse texture.");
                }
            }

            _content.Details = database;
            XRayDetailPlacementStatistics statistics = builder.Statistics;
            int filled = 0;
            foreach (XRayDetailSlot slot in details.Slots)
            {
                if (!slot.IsEmpty)
                {
                    filled++;
                }
            }

            _record.SetMetric("DetailModels", details.Models.Count);
            _record.SetMetric("DetailSlots", details.Slots.Length);
            _record.SetMetric("DetailFilledSlots", filled);
            _record.SetMetric("DetailSlotsWithoutGround", statistics.SlotsWithoutGround);
            _record.SetMetric("DetailCandidates", statistics.Candidates);
            _record.SetMetric("DetailRejected", statistics.Rejected);
            _record.SetMetric("DetailInstances", statistics.Instances);
            _record.SetMetric("DetailDensity", _request.Settings.DetailDensity);
            _record.AddLimitation("Detail placement follows CDetailManager::cache_Decompress (CRandom seeds 0x12071980 ^ (sx*sz), dither 16x16, jitter density/1.7, ray down to non-passable level.cform). Unspecified C++ argument order: the z jitter is drawn before x (MSVC right-to-left); the wave variant uses a deterministic per-slot generator instead of the global ::Random; positions are quantized to 0.5 mm per 32 m chunk, yaw to 2^-16 turn.");
        }

        private void BuildCollision()
        {
            if (!_request.Settings.ImportCollision)
            {
                return;
            }

            EnsureCollision();
            if (_collision == null)
            {
                return;
            }

            XRayGameMaterials materials = XRayLevelSourceCache.GetGameMaterials(_info, _record.Warnings);
            bool[] actorPassable = materials != null ? materials.CreateActorPassableMask() : _passable;
            _content.Collision.AddRange(XRayLevelCollisionBuilder.Build(_entry.Name, _collision, actorPassable, _request.Settings.CollisionChunkSize, _record));
            if (_passable != null)
            {
                byte[] materialData = _info.Effective != null ? _info.ReadAllBytes("gamemtl.xr") : File.ReadAllBytes(_info.GameMaterialsPath);
                XRayLevelContactImporter.Prepare(_entry.Name, LevelFile("level.cform"), materialData, _content, _record);
            }
        }

        private bool BuildScene()
        {
            string prefabPath = XRayLevelPaths.PrefabPath(_entry.Name);
            string scenePath = XRayLevelPaths.ScenePath(_entry.Name);
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject prefab;
            XRayLevelAnomalyPlacementResult anomalies = null;
            try
            {
                _content.FloraWindAccepted = _windAccepted;
                _content.DetailSettingsSource = _sourcePlan;
                GameObject root = XRayLevelSceneBuilder.BuildRoot(preview, _content, _record);
                if (_anomalyPlan != null)
                {
                    anomalies = XRayLevelAnomalyImporter.Apply(root, _anomalyPlan);
                }
                prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
                if (!success || prefab == null)
                {
                    throw new InvalidOperationException($"Prefab '{prefabPath}' could not be saved.");
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }

            _record.PrefabPath = prefabPath;
            _record.ScenePath = scenePath;
            _record.GeneratedAssets.Add(prefabPath);
            if (anomalies != null)
            {
                RecordAnomalies(anomalies);
            }
            bool sceneExists = File.Exists(scenePath);
            if (!sceneExists)
            {
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = _entry.Name;
                var cameraObject = new GameObject("Level Camera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 3000.0f;
                cameraObject.tag = "MainCamera";
#if XRAY_HDRP
                var cameraData = cameraObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
                cameraData.volumeLayerMask = 1 << instance.layer;
#endif
                Bounds bounds = _content.Bounds;
                cameraObject.transform.position = bounds.center + new Vector3(0.0f, bounds.extents.y + 20.0f, -bounds.extents.z * 0.5f);
                cameraObject.transform.LookAt(bounds.center);
                EditorSceneManager.SaveScene(scene, scenePath);
                EditorSceneManager.CloseScene(scene, true);
            }

            _record.GeneratedAssets.Add(scenePath);
            return !sceneExists;
        }

        private void OpenSceneAfterImport()
        {
            if (_request.Settings.OpenSceneAfterImport)
            {
                bool dirty = false;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    dirty |= SceneManager.GetSceneAt(i).isDirty;
                }

                if (!dirty && !Application.isPlaying)
                {
                    EditorSceneManager.OpenScene(_record.ScenePath, OpenSceneMode.Single);
                    XRayLevelSceneBuilder.FrameSceneView(_content.Bounds);
                }
                else
                {
                    _record.Warnings.Add("The generated scene was not opened because loaded scenes have unsaved changes or Play Mode is active.");
                }
            }
        }

        private void RecordAnomalies(XRayLevelAnomalyPlacementResult result)
        {
            XRayLevelAnomalyImportReport report = XRayLevelAnomalyImporter.Record(_record.PrefabPath, _anomalyPlan, result);
            _record.SetMetric("AnomaliesSource", result.SourceCount);
            _record.SetMetric("AnomaliesImported", result.ImportedCount);
            _record.Warnings.AddRange(result.Warnings);
            _record.Warnings.AddRange(result.Missing);
            _record.Warnings.AddRange(result.Unsupported);
            _record.GeneratedAssets.Add(_anomalyPlan.SpawnAssetPath);
            Debug.Log("[X-Ray Anomalies] " + _entry.Name + ": " + result.ImportedCount + "/" + result.SourceCount + ". " + report.ReportPath);
        }

        private void SaveManifest()
        {
            if (string.IsNullOrEmpty(_record.LevelName))
            {
                return;
            }

            string folder = XRayLevelPaths.LevelFolder(_record.LevelName);
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            _record.SetMetric("TexturesImported", _textures?.Imported ?? 0);
            _record.SetMetric("TexturesReused", _textures?.Reused ?? 0);
            _record.SetMetric("TexturesMissing", _textures?.Missing ?? 0);
            _record.SetMetric("TexturesUnsupported", _textures?.Unsupported ?? 0);
            _record.SetMetric("TextureAssets", _textures?.RequestedAssets.Count ?? 0);
            if (_textures != null)
            {
                foreach (string asset in _textures.RequestedAssets)
                {
                    if (!_record.GeneratedAssets.Contains(asset)) _record.GeneratedAssets.Add(asset);
                }
            }

            XRayLevelSharedAssets.Count(_record);
            if (_effective && !_record.Completed)
            {
                string attemptPath = XRayLevelPaths.AttemptReportPath(_record.LevelName, _record.Source.ProfileId);
                File.WriteAllText(attemptPath, _record.ToJson());
                Debug.LogWarning($"[X-Ray Level] '{_record.LevelName}' was not completed; the previous manifest is kept and this attempt is written to {attemptPath}.");
                return;
            }

            XRayLevelSourceManifest.Collect(_record, _info.Effective ?? new XRayEffectiveGamedata(_sourceIndex), _entry);

            bool occlusionChanged = false;
            bool grassGeometryChanged = false;
            if (_record.Completed)
            {
                if (_resourcesOnly && _content.Details == null && _request.Settings.ImportDetails && _request.Settings.RuntimeDetails)
                {
                    string databasePath = XRayLevelPaths.Details(_record.LevelName) + "/" + XRayLevelDetailBuilder.DATABASE_NAME;
                    XRayDetailDatabase database = AssetDatabase.LoadAssetAtPath<XRayDetailDatabase>(databasePath);
                    grassGeometryChanged = XRayDetailSlotGeometryPreparation.PrepareForImport(database, _record);
                }

                try
                {
                    occlusionChanged = XRayVegetationOccluderPreparation.PrepareLevel(folder, _record, true);
                }
                catch (Exception preparationException)
                {
                    _record.Warnings.Add("Vegetation occlusion preparation: " + preparationException.Message);
                }
            }

            bool unchanged = _record.Completed && _resourcesOnly && _anomalyPlan == null && _previous != null && !occlusionChanged && !grassGeometryChanged && !_materials.Changed && _textures.Imported == 0 && _record.GetMetric("ProbeVolumeBakeUpdated") == 0 && _previous.Settings == _record.Settings && _record.Source.ChangesSincePrevious.Count == 0 && _record.Source.ProfileFingerprint == _previous.Source.ProfileFingerprint && _record.Source.IndexFingerprint == _previous.Source.IndexFingerprint && _previous.GetMetric("HomSourceTriangles") == _record.GetMetric("HomSourceTriangles") && _previous.GetMetric("HomOccluderTriangles") == _record.GetMetric("HomOccluderTriangles");
            string reportPath = XRayLevelPaths.AttemptReportPath(_record.LevelName, _record.Source.ProfileId);
            if (unchanged)
            {
                _record.SetMetric("PreservedManifest", 1);
                File.WriteAllText(reportPath, _record.ToJson());
                return;
            }

            string manifestPath = XRayLevelPaths.ManifestPath(_record.LevelName);
            var manifest = AssetDatabase.LoadAssetAtPath<XRayLevelImportManifest>(manifestPath);
            if (manifest == null)
            {
                manifest = ScriptableObject.CreateInstance<XRayLevelImportManifest>();
                manifest.Record = _record;
                AssetDatabase.CreateAsset(manifest, manifestPath);
            }
            else
            {
                manifest.Record = _record;
                EditorUtility.SetDirty(manifest);
                AssetDatabase.SaveAssetIfDirty(manifest);
            }

            File.WriteAllText(reportPath, _record.ToJson());
        }

        private string LevelFile(string file)
        {
            if (!_entry.IsEffective)
            {
                string path = Path.Combine(_entry.Folder, file);
                return File.Exists(path) ? path : null;
            }

            return _info.Materialize(_entry.VirtualFolder + "/" + file);
        }

        private static bool SamePath(string a, string b)
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
    }
}

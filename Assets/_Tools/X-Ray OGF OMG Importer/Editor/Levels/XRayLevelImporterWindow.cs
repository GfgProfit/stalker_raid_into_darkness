using System.IO;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Sources.Levels;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    public sealed class XRayLevelImporterWindow : EditorWindow
    {
        private Vector2 _scroll;
        private Vector2 _levelScroll;
        private string _filter = string.Empty;
        private string _status;
        private MessageType _statusType = MessageType.None;

        [MenuItem("Tools/X-Ray/Level Importer")]
        public static void Open()
        {
            GetWindow<XRayLevelImporterWindow>("X-Ray Level Importer").Show();
        }

        [MenuItem("Tools/X-Ray/Import Level...")]
        public static void ImportWithPicker()
        {
            XRayLevelGamedataInfo info = XRayLevelSourceCache.Inspect(false);
            string start = info.Effective == null ? info.LevelsFolder ?? string.Empty : string.Empty;
            string path = EditorUtility.OpenFilePanel("Select X-Ray level file", start, string.Empty);
            if (!string.IsNullOrEmpty(path))
            {
                XRayLevelDropHandler.Handle(path.Replace('/', Path.DirectorySeparatorChar), null, true);
                Open();
            }
        }

        private void OnEnable()
        {
            XRayLevelImportJob.Finished += OnFinished;
            XRayLevelSourceCache.Changed += Repaint;
        }

        private void OnDisable()
        {
            XRayLevelImportJob.Finished -= OnFinished;
            XRayLevelSourceCache.Changed -= Repaint;
        }

        private void OnFinished(XRayLevelImportRecord record, XRayLevelJobState state)
        {
            _status = $"{record.LevelName}: {state}. {record.ScenePath}";
            _statusType = state == XRayLevelJobState.Completed ? MessageType.Info : MessageType.Warning;
            Repaint();
        }

        private void Update()
        {
            if (XRayLevelImportJob.Current != null || (XRayLevelSourceCache.CurrentBuild != null && !XRayLevelSourceCache.CurrentBuild.IsDone))
            {
                Repaint();
            }
        }

        private void OnGUI()
        {
            XRayAssetSourceUserSettings user = XRayAssetSourceUserSettings.instance;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUI.BeginChangeCheck();
            DrawSource(user);
            EditorGUILayout.Space();
            DrawSettings(user.LevelImport);
            if (EditorGUI.EndChangeCheck())
            {
                user.SaveSettings();
            }

            EditorGUILayout.Space();
            DrawJob();
            DrawDropZone();
            DrawPending();
            EditorGUILayout.Space();
            DrawLevels();
            DrawLastReport();
            EditorGUILayout.EndScrollView();
        }

        public static void DrawSource(XRayAssetSourceUserSettings user)
        {
            using (new EditorGUI.DisabledScope(XRayLevelImportJob.Current != null))
            {
                DrawSourceFields(user);
            }
        }

        private static void DrawSourceFields(XRayAssetSourceUserSettings user)
        {
            EditorGUILayout.LabelField("Level Gamedata Source", EditorStyles.boldLabel);
            var sourceType = (XRayLevelSourceType)EditorGUILayout.EnumPopup(new GUIContent("Source Type", "Unpacked gamedata folder, or the effective gamedata of a Mod Organizer 2 profile (mods, Overwrite, game folder and .db archives)."), user.LevelSourceType);
            if (sourceType != user.LevelSourceType)
            {
                user.LevelSourceType = sourceType;
                user.SaveSettings();
                XRayLevelSourceCache.Inspect(true);
            }

            if (sourceType == XRayLevelSourceType.Mo2Profile)
            {
                XRayLevelMo2SourceView.Draw(user);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            string folder = EditorGUILayout.TextField(new GUIContent("Gamedata Folder", "Unpacked gamedata (levels, textures, shaders) or a folder that contains 'gamedata'."), user.LevelGamedataFolder ?? string.Empty);
            if (GUILayout.Button("Browse", GUILayout.Width(64.0f)))
            {
                string selected = EditorUtility.OpenFolderPanel("Level Gamedata Source", folder, string.Empty);
                if (!string.IsNullOrEmpty(selected))
                {
                    folder = selected.Replace('/', Path.DirectorySeparatorChar);
                    GUI.changed = true;
                }
            }

            EditorGUILayout.EndHorizontal();
            user.LevelGamedataFolder = folder;
            user.LevelAllowFallbackSources = EditorGUILayout.Toggle(new GUIContent("Allow Fallback Sources", "Resolve textures missing in the level gamedata from the MO2/Anomaly sources (lower priority, conflicts are reported)."), user.LevelAllowFallbackSources);
            XRayLevelGamedataInfo info = XRayLevelSourceCache.Inspect(false);
            if (!info.IsValid)
            {
                EditorGUILayout.HelpBox(string.Join("\n", info.Errors), MessageType.Error);
            }
            else
            {
                EditorGUILayout.LabelField("Gamedata Root", info.Root);
                EditorGUILayout.LabelField("Levels", $"{info.Levels.Count} in {info.LevelsFolder}");
                XRayAssetIndex index = XRayLevelSourceCache.CurrentIndex;
                EditorGUILayout.LabelField("Index", index == null ? "Not loaded" : $"{index.Statistics.FileCount} files, {index.Statistics.ConflictCount} conflicts, built in {index.Statistics.BuildMilliseconds} ms");
                if (info.Warnings.Count > 0)
                {
                    EditorGUILayout.HelpBox(string.Join("\n", info.Warnings), MessageType.Warning);
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Validate"))
            {
                XRayLevelSourceCache.Inspect(true);
            }

            if (GUILayout.Button("Rebuild Level Index"))
            {
                XRayAssetIndex rebuilt = XRayLevelSourceCache.GetIndex(true);
                if (rebuilt == null)
                {
                    Debug.LogError("[X-Ray Level] Level index was not built: " + string.Join("; ", XRayLevelSourceCache.LastPlan.Errors));
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private static void DrawSettings(XRayLevelImportSettings settings)
        {
            EditorGUILayout.LabelField("Import Settings", EditorStyles.boldLabel);
            settings.ChunkSize = EditorGUILayout.FloatField(new GUIContent("Static Chunk Size (m)", "Largest xrLC hierarchy node merged into one renderer."), settings.ChunkSize);
            settings.InstanceCellSize = EditorGUILayout.FloatField(new GUIContent("MU Instance Cell (m)", "Culling cell of instanced MU/tree batches."), settings.InstanceCellSize);
            settings.ImportDetails = EditorGUILayout.Toggle("Import level.details", settings.ImportDetails);
            using (new EditorGUI.DisabledScope(!settings.ImportDetails))
            {
                settings.DetailDensity = EditorGUILayout.Slider(new GUIContent("Detail Density (r__detail_density)", "Engine console value; smaller is denser. 0.6 is the default."), settings.DetailDensity, 0.04f, 1.0f);
                settings.DetailHeightScale = EditorGUILayout.Slider(new GUIContent("Detail Height (r__detail_height)"), settings.DetailHeightScale, 0.5f, 2.0f);
                settings.DetailChunkSize = EditorGUILayout.FloatField("Detail Chunk Size (m)", settings.DetailChunkSize);
            }

            settings.ImportCollision = EditorGUILayout.Toggle("Import Collision (level.cform)", settings.ImportCollision);
            settings.ImportLights = EditorGUILayout.Toggle("Import Static Lights", settings.ImportLights);
            settings.StaticLightShadows = EditorGUILayout.Toggle(new GUIContent("Static Light Shadows", "Off by default: dozens of shadowed point lights are the measured main cost."), settings.StaticLightShadows);
            settings.MaterialUpdateMode = (XRayLevelMaterialUpdateMode)EditorGUILayout.EnumPopup("Existing Materials", settings.MaterialUpdateMode);
            settings.TerrainImportMode = (XRayTerrainImportMode)EditorGUILayout.EnumPopup("Source Terrain", settings.TerrainImportMode);
            settings.TerrainQuality = (XRayTerrainQuality)EditorGUILayout.EnumPopup("Source Terrain Quality", settings.TerrainQuality);
            settings.FloraImportMode = (XRayFloraImportMode)EditorGUILayout.EnumPopup(new GUIContent("Source Flora", "Applies only when the effective flora shader contract matches the selected game; other sources keep legacy flora."), settings.FloraImportMode);
            settings.FloraWindMode = (XRayFloraWindImportMode)EditorGUILayout.EnumPopup(new GUIContent("Source Flora Wind", "Stores tree WindFactor and binds source wind only when the effective wind shader contract matches the selected game; runtime wind also needs an XRayFloraWindProvider."), settings.FloraWindMode);
            settings.CopySourceFiles = EditorGUILayout.Toggle("Copy level.geom/geomx/cform/details", settings.CopySourceFiles);
            settings.OpenSceneAfterImport = EditorGUILayout.Toggle("Open Scene After Import", settings.OpenSceneAfterImport);
            settings.AutoImportDroppedLevels = EditorGUILayout.Toggle(new GUIContent("Auto Import Dropped 'level'", "Import extensionless 'level' files dropped into the Project window."), settings.AutoImportDroppedLevels);
        }

        private static void DrawJob()
        {
            XRayLevelImportJob job = XRayLevelImportJob.Current;
            if (job == null)
            {
                return;
            }

            EditorGUILayout.LabelField("Importing " + job.LevelName, EditorStyles.boldLabel);
            Rect rect = EditorGUILayout.GetControlRect(false, 20.0f);
            EditorGUI.ProgressBar(rect, job.Progress, job.Stage);
            if (GUILayout.Button("Cancel"))
            {
                XRayLevelImportJob.CancelCurrent();
            }
        }

        private void DrawDropZone()
        {
            EditorGUILayout.LabelField("Import", EditorStyles.boldLabel);
            Rect zone = GUILayoutUtility.GetRect(0.0f, 72.0f, GUILayout.ExpandWidth(true));
            GUI.Box(zone, "Drop the extensionless 'level' file here (Windows Explorer or Project window)", EditorStyles.helpBox);
            Event current = Event.current;
            if ((current.type == EventType.DragUpdated || current.type == EventType.DragPerform) && zone.Contains(current.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (current.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (string path in DragAndDrop.paths)
                    {
                        string full = Path.GetFullPath(path);
                        string asset = path.StartsWith("Assets/") ? path : null;
                        XRayLevelDropHandler.Handle(full, asset, true);
                    }

                    _status = XRayLevelDropHandler.LastMessage;
                    _statusType = MessageType.Info;
                }

                current.Use();
            }

            if (GUILayout.Button("Import Level..."))
            {
                ImportWithPicker();
                _status = XRayLevelDropHandler.LastMessage;
            }

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.HelpBox(_status, _statusType);
            }
        }

        private static void DrawPending()
        {
            XRayLevelPendingChoice pending = XRayLevelDropHandler.Pending;
            if (pending == null)
            {
                return;
            }

            EditorGUILayout.HelpBox(XRayLevelDropHandler.LastMessage, pending.NoMatch ? MessageType.Warning : MessageType.Info);
            foreach (XRayLevelEntry entry in pending.Candidates)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(entry.Name, entry.LevelPath);
                if (GUILayout.Button("Use", GUILayout.Width(48.0f)))
                {
                    XRayLevelDropHandler.Resolve(entry);
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Dismiss"))
            {
                XRayLevelDropHandler.Dismiss();
            }
        }

        private void DrawLevels()
        {
            XRayLevelGamedataInfo info = XRayLevelSourceCache.Inspect(false);
            if (!info.IsValid)
            {
                return;
            }

            EditorGUILayout.LabelField("Levels In Gamedata", EditorStyles.boldLabel);
            _filter = EditorGUILayout.TextField("Filter", _filter);
            if (info.Effective != null)
            {
                _levelScroll = XRayLevelMo2SourceView.DrawLevels(info, _filter, _levelScroll);
                return;
            }

            _levelScroll = EditorGUILayout.BeginScrollView(_levelScroll, GUILayout.Height(340.0f));
            foreach (XRayLevelEntry entry in info.Levels)
            {
                if (_filter.Length > 0 && entry.Name.IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                bool imported = File.Exists(XRayLevelPaths.ManifestPath(entry.Name));
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(entry.Name, EditorStyles.boldLabel, GUILayout.MinWidth(0.0f));
                EditorGUILayout.LabelField($"{entry.LevelLength / 1024} KB{(imported ? ", imported" : string.Empty)}", EditorStyles.miniLabel, GUILayout.MinWidth(0.0f));
                EditorGUILayout.EndVertical();
                using (new EditorGUI.DisabledScope(XRayLevelImportJob.IsBusy(entry.Name)))
                {
                    if (GUILayout.Button(imported ? "Reimport" : "Import", GUILayout.Width(72.0f)))
                    {
                        XRayLevelDropHandler.Start(entry.Name, null, "explicit selection in Level Importer window");
                    }
                }

                using (new EditorGUI.DisabledScope(!File.Exists(XRayLevelPaths.ScenePath(entry.Name))))
                {
                    if (GUILayout.Button("Scene", GUILayout.Width(56.0f)))
                    {
                        UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
                        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(XRayLevelPaths.ScenePath(entry.Name));
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawLastReport()
        {
            XRayLevelImportRecord record = XRayLevelImportJob.LastRecord;
            if (record == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Last Import: " + record.LevelName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("State", XRayLevelImportJob.LastState + (record.Errors.Count > 0 ? $" ({record.Errors.Count} errors)" : string.Empty));
            foreach (string metric in new[] { "ImportSeconds", "StaticRenderers", "MuInstances", "InstanceBatches", "Materials", "TextureAssets", "TexturesMissing", "DetailInstances", "CollisionTriangles" })
            {
                EditorGUILayout.LabelField(metric, record.GetMetric(metric).ToString("0.##"));
            }

            if (record.Errors.Count > 0)
            {
                EditorGUILayout.HelpBox(string.Join("\n", record.Errors), MessageType.Error);
            }

            if (record.Warnings.Count > 0)
            {
                EditorGUILayout.HelpBox($"{record.Warnings.Count} warnings, {record.Limitations.Count} limitations. Reports: {Path.Combine(XRayLevelPaths.ArchiveRoot, "XRayLevelImporter_Reports")}", MessageType.Warning);
            }
        }
    }
}

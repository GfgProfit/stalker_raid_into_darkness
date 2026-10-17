using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Sources;
using XRay.Importer.Sources.Mo2;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor
{
    public sealed class XRayAssetSourcesWindow : EditorWindow
    {
        private const string ACTIVE_PROFILE_LABEL = "<Active MO2 profile>";

        private Mo2Installation _installation;
        private string _installationError;
        private List<string> _instances = new List<string>();
        private string _validation;
        private MessageType _validationType = MessageType.None;
        private List<string> _missing;
        private Vector2 _scroll;
        private Vector2 _missingScroll;

        [MenuItem("Tools/X-Ray/Asset Sources")]
        public static void Open()
        {
            GetWindow<XRayAssetSourcesWindow>("X-Ray Asset Sources").Show();
        }

        private void OnEnable()
        {
            DetectMo2();
        }

        private void OnGUI()
        {
            XRayAssetSourceProjectSettings project = XRayAssetSourceProjectSettings.instance;
            XRayAssetSourceUserSettings user = XRayAssetSourceUserSettings.instance;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUI.BeginChangeCheck();
            DrawSources(project, user);
            EditorGUILayout.Space();
            DrawImportOptions(project, user);
            if (EditorGUI.EndChangeCheck())
            {
                project.SaveSettings();
                user.SaveSettings();
            }

            EditorGUILayout.Space();
            DrawActions(user);
            EditorGUILayout.Space();
            DrawStatus();
            EditorGUILayout.EndScrollView();
        }

        private void DrawSources(XRayAssetSourceProjectSettings project, XRayAssetSourceUserSettings user)
        {
            EditorGUILayout.LabelField("Source Configuration", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Folder paths and the profile are stored per user in UserSettings/XRayAssetSources.asset (not in version control). Project defaults are stored in ProjectSettings/XRayAssetSources.asset.", MessageType.None);
            user.BaseAnomalyFolder = DrawFolder("Base Anomaly Folder", user.BaseAnomalyFolder, "Unpacked Anomaly gamedata (contains textures)");
            string mo2Folder = DrawFolder("MO2 Root Folder", user.Mo2Folder, "Mod Organizer 2 folder or instance folder");
            if (mo2Folder != user.Mo2Folder)
            {
                user.Mo2Folder = mo2Folder;
                DetectMo2();
            }

            if (_instances.Count > 0 && (_installation == null || !_installation.IsPortable))
            {
                int instanceIndex = Math.Max(0, _instances.IndexOf(user.Mo2Instance));
                int selectedInstance = EditorGUILayout.Popup("MO2 Instance", instanceIndex, _instances.ToArray());
                if (_instances[selectedInstance] != user.Mo2Instance)
                {
                    user.Mo2Instance = _instances[selectedInstance];
                    DetectMo2();
                }
            }

            DrawProfilePopup(user);
            bool overrideMode = EditorGUILayout.Toggle(new GUIContent("Override Source Mode", "Use a personal source mode instead of the project default."), user.OverrideSourceMode);
            user.OverrideSourceMode = overrideMode;
            if (overrideMode)
            {
                user.SourceMode = (XRaySourceMode)EditorGUILayout.EnumPopup("Asset Source Mode", user.SourceMode);
            }
            else
            {
                project.SourceMode = (XRaySourceMode)EditorGUILayout.EnumPopup("Asset Source Mode (Project)", project.SourceMode);
            }

            XRaySourceMode mode = overrideMode ? user.SourceMode : project.SourceMode;
            if (mode == XRaySourceMode.CustomFolder)
            {
                user.CustomFolder = DrawFolder("Custom Folder", user.CustomFolder, "Folder used as gamedata root");
            }

            project.IncludeGameGamedata = EditorGUILayout.Toggle(new GUIContent("Include Game Gamedata", "Index loose files of <game>/gamedata below all MO2 mods, as the game does."), project.IncludeGameGamedata);
            project.TreatModRootAsGamedata = EditorGUILayout.Toggle(new GUIContent("Treat Mod Root As Gamedata", "Only for MO2 game plugins that map a mod root into gamedata. The STALKER Anomaly plugin maps it to the game root."), project.TreatModRootAsGamedata);
        }

        private void DrawProfilePopup(XRayAssetSourceUserSettings user)
        {
            var options = new List<string> { ACTIVE_PROFILE_LABEL };
            if (_installation != null)
            {
                options.AddRange(_installation.Profiles);
            }

            int profileCount = options.Count;
            int index = string.IsNullOrEmpty(user.Mo2Profile) ? 0 : Math.Max(0, options.IndexOf(user.Mo2Profile));
            if (!string.IsNullOrEmpty(user.Mo2Profile) && index == 0)
            {
                options.Add(user.Mo2Profile + " (not found)");
                index = options.Count - 1;
            }

            int selected = EditorGUILayout.Popup("MO2 Profile", index, options.ToArray());
            if (selected != index && selected < profileCount)
            {
                user.Mo2Profile = selected == 0 ? string.Empty : options[selected];
            }

            if (string.IsNullOrEmpty(user.Mo2Profile) && _installation != null)
            {
                EditorGUILayout.LabelField(" ", string.IsNullOrEmpty(_installation.SelectedProfile) ? "Active profile is unknown; select one explicitly." : "Active: " + _installation.SelectedProfile, EditorStyles.miniLabel);
            }
        }

        private static void DrawImportOptions(XRayAssetSourceProjectSettings project, XRayAssetSourceUserSettings user)
        {
            EditorGUILayout.LabelField("Import Options", EditorStyles.boldLabel);
            user.OverrideImportOptions = EditorGUILayout.Toggle(new GUIContent("Override For Me", "Use personal import options instead of the project options."), user.OverrideImportOptions);
            XRayImportOptions options = user.OverrideImportOptions ? user.ImportOptions : project.ImportOptions;
            options.AutoImportTextures = EditorGUILayout.Toggle("Automatically Import Textures", options.AutoImportTextures);
            options.GenerateMaterials = EditorGUILayout.Toggle("Generate HDRP Materials", options.GenerateMaterials);
            options.CreateTexturesFolder = EditorGUILayout.Toggle("Create Textures Folder", options.CreateTexturesFolder);
            options.CreateMaterialsFolder = EditorGUILayout.Toggle("Create Materials Folder", options.CreateMaterialsFolder);
            options.PreserveExistingMaterials = EditorGUILayout.Toggle("Preserve Existing Materials", options.PreserveExistingMaterials);
            options.OverwriteExistingTextures = EditorGUILayout.Toggle("Overwrite Existing Textures", options.OverwriteExistingTextures);
            options.ResolveBumpMaps = EditorGUILayout.Toggle("Resolve Bump/Normal Maps", options.ResolveBumpMaps);
            options.FlipNormalGreen = EditorGUILayout.Toggle(new GUIContent("Flip Normal Green (Y)", "X-Ray tangent space uses Direct3D texture V; Unity flips V on import."), options.FlipNormalGreen);
            options.LogMissingTextures = EditorGUILayout.Toggle("Log Missing Textures", options.LogMissingTextures);
        }

        private void DrawActions(XRayAssetSourceUserSettings user)
        {
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Detect MO2"))
            {
                DetectMo2();
            }

            using (new EditorGUI.DisabledScope(_installation == null || string.IsNullOrEmpty(_installation.SelectedProfile)))
            {
                if (GUILayout.Button("Select Profile"))
                {
                    user.Mo2Profile = _installation.SelectedProfile;
                    user.SaveSettings();
                }
            }

            if (GUILayout.Button("Rebuild Index"))
            {
                RebuildIndex();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Validate Sources"))
            {
                Validate();
            }

            if (GUILayout.Button("Show Missing Resources"))
            {
                CollectMissing();
            }

            if (GUILayout.Button("Clear Cache"))
            {
                XRayAssetIndexCache.Clear();
                XRayResourceReportStore.Clear();
                XRayPendingGenerationStore.Clear();
                _missing = null;
                _validation = "Index cache, reports and pending generation state were cleared.";
                _validationType = MessageType.Info;
            }

            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Refresh Resources Of All Models"))
            {
                RefreshAllModels();
            }
        }

        private void DrawStatus()
        {
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            if (_installationError != null)
            {
                EditorGUILayout.HelpBox(_installationError, MessageType.Warning);
            }
            else if (_installation != null)
            {
                EditorGUILayout.LabelField("MO2 Instance", $"{_installation.InstanceFolder} ({(_installation.IsPortable ? "portable" : "global instance")})");
                EditorGUILayout.LabelField("Mods", _installation.ModsDirectory);
                EditorGUILayout.LabelField("Profiles", $"{_installation.ProfilesDirectory} ({_installation.Profiles.Count})");
                EditorGUILayout.LabelField("Overwrite", _installation.OverwriteDirectory);
            }

            XRayAssetIndex index = XRayAssetIndexCache.Current;
            if (index == null)
            {
                EditorGUILayout.LabelField("Index", "Not loaded (built on the first model import or with 'Rebuild Index').");
            }
            else
            {
                XRayAssetIndexStatistics statistics = index.Statistics;
                EditorGUILayout.LabelField("Index Profile", string.IsNullOrEmpty(index.ProfileName) ? "-" : index.ProfileName);
                EditorGUILayout.LabelField("Active Mods", statistics.ActiveModCount.ToString());
                EditorGUILayout.LabelField("Sources", $"{statistics.SourceCount} ({statistics.ContributingSourceCount} provide indexed files)");
                EditorGUILayout.LabelField("Files", statistics.FileCount.ToString());
                EditorGUILayout.LabelField("Conflicts", statistics.ConflictCount.ToString());
                EditorGUILayout.LabelField("Archive Sources", statistics.ArchiveSourceCount.ToString());
                EditorGUILayout.LabelField("Built", $"{new DateTime(statistics.BuiltAtUtcTicks, DateTimeKind.Utc).ToLocalTime():g} in {statistics.BuildMilliseconds} ms");
            }

            if (!string.IsNullOrEmpty(_validation))
            {
                EditorGUILayout.HelpBox(_validation, _validationType);
            }

            if (_missing != null)
            {
                EditorGUILayout.LabelField($"Missing Resources ({_missing.Count})", EditorStyles.boldLabel);
                _missingScroll = EditorGUILayout.BeginScrollView(_missingScroll, GUILayout.MaxHeight(240.0f));
                foreach (string line in _missing)
                {
                    EditorGUILayout.SelectableLabel(line, EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DetectMo2()
        {
            XRayAssetSourceUserSettings user = XRayAssetSourceUserSettings.instance;
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _instances = Mo2InstallationReader.FindInstances(localAppData);
            _installation = null;
            _installationError = null;
            if (string.IsNullOrEmpty(user.Mo2Folder))
            {
                _installationError = "MO2 Root Folder is not set.";
                return;
            }

            try
            {
                _installation = Mo2InstallationReader.Read(user.Mo2Folder, user.Mo2Instance, localAppData);
                if (_installation.Warnings.Count > 0)
                {
                    _installationError = string.Join("\n", _installation.Warnings);
                }
            }
            catch (XRaySourceException exception)
            {
                _installationError = exception.Message;
            }
        }

        private void RebuildIndex()
        {
            XRayAssetIndex index = XRayAssetIndexCache.Rebuild(XRayAssetSourceSettings.GetEffective());
            XRaySourcePlan plan = XRayAssetIndexCache.LastPlan;
            if (index == null)
            {
                _validation = "Index was not built:\n" + string.Join("\n", plan.Errors);
                _validationType = MessageType.Error;
                return;
            }

            _validation = $"Index rebuilt: {index.Statistics.FileCount} files from {index.Statistics.SourceCount} sources, {index.Statistics.ConflictCount} conflicts.";
            _validationType = MessageType.Info;
        }

        private void Validate()
        {
            XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(XRayAssetSourceSettings.GetEffective().Sources);
            var builder = new StringBuilder();
            builder.AppendLine(plan.IsValid ? "Sources are valid." : "Sources are invalid.");
            if (plan.ProfileName != null)
            {
                builder.AppendLine($"Profile: {plan.ProfileName}");
            }

            builder.AppendLine($"Active mods: {plan.ActiveModCount}, sources with gamedata: {plan.Sources.Count}");
            foreach (string error in plan.Errors)
            {
                builder.AppendLine("Error: " + error);
            }

            foreach (string warning in plan.Warnings)
            {
                builder.AppendLine("Warning: " + warning);
            }

            XRayAssetIndex index = XRayAssetIndexCache.Current;
            if (index != null)
            {
                builder.AppendLine(index.Fingerprint == plan.Fingerprint ? "Index is up to date." : "Index is outdated and will be rebuilt on the next import.");
                foreach (string archive in index.ArchiveSources)
                {
                    builder.AppendLine("Archives (not indexed): " + archive);
                }
            }

            _validation = builder.ToString().TrimEnd();
            _validationType = !plan.IsValid ? MessageType.Error : plan.Warnings.Count > 0 ? MessageType.Warning : MessageType.Info;
        }

        private void CollectMissing()
        {
            _missing = new List<string>();
            foreach (XRayResourceReport report in XRayResourceReportStore.LoadAll())
            {
                foreach (XRayReportMaterial material in report.Materials)
                {
                    foreach (XRayReportTexture texture in material.Textures)
                    {
                        if (texture.Status == XRayResourceStatus.Missing || texture.Status == XRayResourceStatus.Unsupported)
                        {
                            _missing.Add($"{Path.GetFileName(report.ModelPath)} [{material.Index:00}] {texture.Role}: {texture.Requested} ({texture.Status}) {texture.Note}");
                        }
                    }
                }
            }

            _missing.Sort(StringComparer.Ordinal);
        }

        private void RefreshAllModels()
        {
            List<string> models = XRayAssetGenerationCoordinator.FindModels();
            try
            {
                for (int i = 0; i < models.Count; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("X-Ray Asset Sources", models[i], (float)i / Math.Max(1, models.Count)))
                    {
                        break;
                    }

                    XRayAssetGenerationCoordinator.Refresh(models[i], out XRayResourceReport _);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            _validation = $"Resources of {models.Count} models were refreshed.";
            _validationType = MessageType.Info;
        }

        private static string DrawFolder(string label, string value, string title)
        {
            EditorGUILayout.BeginHorizontal();
            string result = EditorGUILayout.TextField(label, value ?? string.Empty);
            if (GUILayout.Button("...", GUILayout.Width(28.0f)))
            {
                string selected = EditorUtility.OpenFolderPanel(title, string.IsNullOrEmpty(result) ? string.Empty : result, string.Empty);
                if (!string.IsNullOrEmpty(selected))
                {
                    result = selected.Replace('/', Path.DirectorySeparatorChar);
                    GUI.changed = true;
                }
            }

            EditorGUILayout.EndHorizontal();
            return result;
        }
    }
}

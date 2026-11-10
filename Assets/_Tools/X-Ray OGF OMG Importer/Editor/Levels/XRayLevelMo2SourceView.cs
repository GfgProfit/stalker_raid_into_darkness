using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using XRay.Importer.Sources;
using XRay.Importer.Sources.Levels;
using XRay.Importer.Sources.Mo2;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayLevelMo2SourceView
    {
        private const string ACTIVE_PROFILE_LABEL = "<Active MO2 profile>";
        private const float BUTTON_WIDTH = 72.0f;
        private const float SCENE_WIDTH = 56.0f;

        private static string _detectMessage;
        private static MessageType _detectType = MessageType.None;
        private static bool _pathOverrides;

        public static void Draw(XRayAssetSourceUserSettings user)
        {
            string folder = DrawFolder(new GUIContent("MO2 Instance / Root", "Portable MO2 folder (with ModOrganizer.ini) or a global instance folder."), user.LevelMo2Folder, "MO2 Instance / Root");
            if (folder != user.LevelMo2Folder)
            {
                user.LevelMo2Folder = folder;
                user.SaveSettings();
                XRayLevelSourceCache.Inspect(true);
            }

            XRaySourcePlan plan = XRayLevelSourceCache.Mo2Plan ?? XRayLevelSourceCache.PlanMo2();
            Mo2Installation installation = plan.Installation;
            string gameFolder = DrawFolder(new GUIContent("Anomaly Game Folder", "Empty: gamePath from ModOrganizer.ini. Set manually only when MO2 points to another folder."), user.LevelGameFolder, "Anomaly Game Folder");
            if (string.IsNullOrEmpty(gameFolder) && installation != null)
            {
                EditorGUILayout.LabelField(" ", "auto: " + installation.GamePath, EditorStyles.miniLabel);
            }

            if (gameFolder != user.LevelGameFolder)
            {
                user.LevelGameFolder = gameFolder;
                user.SaveSettings();
                XRayLevelSourceCache.Inspect(true);
            }

            DrawProfilePopup(user, installation);
            DrawPathOverrides(user);
            DrawStatistics(plan);
            DrawButtons(user);
            DrawBuild();
            if (!string.IsNullOrEmpty(_detectMessage))
            {
                EditorGUILayout.HelpBox(_detectMessage, _detectType);
            }
        }

        public static Vector2 DrawLevels(XRayLevelGamedataInfo info, string filter, Vector2 scroll)
        {
            int modded = 0;
            int replaced = 0;
            int unavailable = 0;
            foreach (XRayLevelEntry entry in info.Levels)
            {
                modded += entry.IsModded ? 1 : 0;
                replaced += entry.IsReplaced ? 1 : 0;
                XRayLevelProbe probe = entry.Probe;
                unavailable += probe != null && probe.Status != XRayLevelProbeStatus.Ready && probe.Status != XRayLevelProbeStatus.Incomplete ? 1 : 0;
            }

            GUILayout.Label($"{info.Levels.Count} unique levels: {info.Levels.Count - modded} base ({replaced} replaced by mods), {modded} added by mods, {unavailable} unavailable{(XRayLevelSourceCache.IsProbing ? ", checking formats..." : string.Empty)}", EditorStyles.wordWrappedMiniLabel, GUILayout.MinWidth(0.0f));
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(340.0f));
            string root = XRayLevelSourceCache.AssetRoot;
            foreach (XRayLevelEntry entry in info.Levels)
            {
                if (filter.Length > 0 && entry.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && (entry.Origin == null || entry.Origin.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    continue;
                }

                DrawLevelRow(entry, root);
            }

            EditorGUILayout.EndScrollView();
            return scroll;
        }

        private static void DrawLevelRow(XRayLevelEntry entry, string root)
        {
            bool imported = File.Exists(XRayLevelPaths.ManifestPath(root, entry.Name));
            XRayLevelProbe probe = entry.Probe;
            XRayLevelSourceDiff diff = imported ? XRayLevelSourceCache.GetDiff(entry) : null;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent(entry.Name, entry.LevelPath), EditorStyles.boldLabel, GUILayout.MinWidth(0.0f));
            bool blocked = probe != null && (probe.Status == XRayLevelProbeStatus.Unsupported || probe.Status == XRayLevelProbeStatus.Unreadable || probe.Status == XRayLevelProbeStatus.Incompatible);
            using (new EditorGUI.DisabledScope(blocked || XRayLevelImportJob.IsBusy(entry.Name)))
            {
                if (GUILayout.Button(imported ? "Reimport" : "Import", GUILayout.Width(BUTTON_WIDTH)))
                {
                    XRayLevelDropHandler.Start(entry.Name, null, $"explicit selection in Level Importer window (MO2 profile, winner {entry.Origin})");
                }
            }

            string scene = XRayLevelPaths.ScenePath(root, entry.Name);
            XRayLevelAnomalyImporter.DrawLevelAction(entry, root);
            using (new EditorGUI.DisabledScope(!File.Exists(scene)))
            {
                if (GUILayout.Button("Scene", GUILayout.Width(SCENE_WIDTH)))
                {
                    if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    {
                        EditorSceneManager.OpenScene(scene);
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(new GUIContent(StatusText(entry, probe, imported, diff), StatusTooltip(entry, probe, diff)), EditorStyles.miniLabel, GUILayout.MinWidth(0.0f));
            XRayLevelAnomalyImporter.DrawLastResult(entry.Name, root);
            string origin = (entry.IsModded ? "[new] " : entry.IsReplaced ? "[replaced] " : string.Empty) + entry.Origin;
            if (GUILayout.Button(new GUIContent(origin, $"{entry.LevelPath}\n{entry.OriginCandidates} candidates. Click to inspect all files of the level."), EditorStyles.linkLabel, GUILayout.MinWidth(0.0f)))
            {
                XRayLevelMo2DiagnosticsWindow.ShowLevel(entry.Name);
            }

            EditorGUILayout.EndVertical();
        }

        private static string StatusText(XRayLevelEntry entry, XRayLevelProbe probe, bool imported, XRayLevelSourceDiff diff)
        {
            string format = probe == null ? "Checking" : probe.Status == XRayLevelProbeStatus.Ready ? $"Ready v{probe.Version}" : $"{probe.Status} v{probe.Version}";
            string registered = entry.IsRegistered ? string.Empty : ", not registered";
            string state = !imported ? string.Empty : diff != null && diff.ReimportRequired ? $", changed ({diff.Changes.Count + diff.MetadataReparse.Count})" : ", imported";
            return format + registered + state;
        }

        private static string StatusTooltip(XRayLevelEntry entry, XRayLevelProbe probe, XRayLevelSourceDiff diff)
        {
            var lines = new List<string>
            {
                "Binary Level Found: yes (" + entry.LevelPath + ")",
                "Game Registered: " + (entry.IsRegistered ? "yes (" + entry.RegisteredBy + ")" : "no (not listed in game_levels.ltx / mod_game_*.ltx)")
            };
            if (probe != null)
            {
                lines.Add($"Import Compatible: {(probe.FormatSupported && probe.Compatible ? "yes" : "no")} (xrLC {probe.Version}, {probe.Shaders} shaders, {probe.Visuals} visuals, {probe.Trees} trees, {probe.VertexBuffers} VB)");
                lines.Add("Dependencies Complete: " + (probe.DependenciesComplete ? "yes" : "no"));
                lines.AddRange(probe.Errors);
                lines.AddRange(probe.MissingRequired);
                foreach (string missing in probe.MissingOptional)
                {
                    lines.Add("optional: " + missing);
                }

                lines.AddRange(probe.Notes);
            }

            if (diff != null && diff.ReimportRequired)
            {
                lines.Add($"Changed since import: {diff.Changes.Count} files ({diff.Count(XRayLevelSourceManifest.CATEGORY_TEXTURE)} DDS, {diff.Count(XRayLevelSourceManifest.CATEGORY_BUMP)} bump, {diff.Count(XRayLevelSourceManifest.CATEGORY_LEVEL)} level files), {diff.AffectedMaterials.Count} materials affected, {diff.MetadataReparse.Count} THM read differently by the current parser.");
            }

            return string.Join("\n", lines);
        }

        private static void DrawProfilePopup(XRayAssetSourceUserSettings user, Mo2Installation installation)
        {
            var options = new List<string> { ACTIVE_PROFILE_LABEL + (installation?.SelectedProfile != null ? ": " + installation.SelectedProfile : string.Empty) };
            if (installation != null)
            {
                options.AddRange(installation.Profiles);
            }

            int index = string.IsNullOrEmpty(user.LevelMo2Profile) ? 0 : Math.Max(0, options.IndexOf(user.LevelMo2Profile));
            if (!string.IsNullOrEmpty(user.LevelMo2Profile) && index == 0)
            {
                options.Add(user.LevelMo2Profile + " (not found)");
                index = options.Count - 1;
            }

            int selected = EditorGUILayout.Popup(new GUIContent("MO2 Profile", "The active profile is read from ModOrganizer.ini (selected_profile)."), index, options.ToArray());
            if (selected == index)
            {
                return;
            }

            user.LevelMo2Profile = selected == 0 ? string.Empty : options[selected];
            user.SaveSettings();
            XRayLevelSourceCache.Inspect(true);
        }

        private static void DrawStatistics(XRaySourcePlan plan)
        {
            Mo2ModList list = plan.ModList;
            int enabled = 0;
            int disabled = 0;
            int separators = 0;
            if (list != null)
            {
                foreach (Mo2ModEntry entry in list.Entries)
                {
                    if (entry.IsSeparator)
                    {
                        separators++;
                    }
                    else if (entry.State == Mo2ModState.Enabled)
                    {
                        enabled++;
                    }
                    else
                    {
                        disabled++;
                    }
                }
            }

            XRayAssetIndex index = XRayLevelSourceCache.CurrentIndex;
            bool fresh = index != null && index.Fingerprint == plan.Fingerprint;
            EditorGUILayout.LabelField("Resolved Profile", plan.ProfileName ?? "-");
            EditorGUILayout.LabelField("Enabled Mods", list == null ? "-" : $"{enabled} enabled, {disabled} disabled, {separators} separators, {plan.MissingModFolders.Count} without folder");
            EditorGUILayout.LabelField("Indexed Files", index == null ? "Not built" : $"{index.Statistics.FileCount} ({index.Statistics.ArchiveEntryCount} from archives), built in {index.Statistics.BuildMilliseconds} ms");
            EditorGUILayout.LabelField("File Conflicts", index == null ? "-" : index.Statistics.ConflictCount.ToString());
            EditorGUILayout.LabelField("Overwrite Files", index == null ? "-" : XRayLevelSourceCache.OverwriteFiles.ToString());
            EditorGUILayout.LabelField("Archive Sources", index == null ? "-" : $"{index.Statistics.LoadedArchiveCount} loaded, {index.UnreadableArchives.Count} unreadable, {index.ShadowedArchives.Count} hidden by MO2 priority");
            string status = plan.Errors.Count > 0 ? "Error" : index == null ? "Index not built" : fresh ? "OK" : "Index outdated (profile changed)";
            EditorGUILayout.LabelField("Profile Status", status);
            if (plan.Errors.Count > 0)
            {
                EditorGUILayout.HelpBox(string.Join("\n", plan.Errors), MessageType.Error);
            }

            if (index != null && index.UnreadableArchives.Count > 0)
            {
                EditorGUILayout.HelpBox("Unreadable archives (their files are NOT in the index):\n" + string.Join("\n", index.UnreadableArchives), MessageType.Warning);
            }
        }

        private static void DrawButtons(XRayAssetSourceUserSettings user)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Detect MO2"))
            {
                Detect(user);
            }

            if (GUILayout.Button("Validate Profile"))
            {
                Validate();
            }

            using (new EditorGUI.DisabledScope(XRayLevelSourceCache.CurrentBuild != null && !XRayLevelSourceCache.CurrentBuild.IsDone))
            {
                if (GUILayout.Button("Build / Refresh Virtual Index"))
                {
                    XRayLevelSourceCache.BeginMo2Build();
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Inspect Conflicts"))
            {
                XRayLevelMo2DiagnosticsWindow.ShowConflicts();
            }

            if (GUILayout.Button("Inspect Resource"))
            {
                XRayLevelMo2DiagnosticsWindow.ShowResource(null);
            }

            if (GUILayout.Button(new GUIContent("Clear Archive Cache", "Deletes files extracted from .db archives (" + XRayLevelSourceCache.EXTRACTION_FOLDER + "). They are re-extracted on demand.")))
            {
                long size = XRayLevelSourceCache.ClearArchiveCache();
                _detectMessage = $"Archive cache cleared ({size / (1024 * 1024)} MB).";
                _detectType = MessageType.Info;
            }

            EditorGUILayout.EndHorizontal();
        }

        private static void DrawPathOverrides(XRayAssetSourceUserSettings user)
        {
            _pathOverrides = EditorGUILayout.Foldout(_pathOverrides, "Path Overrides", true);
            if (!_pathOverrides)
            {
                return;
            }

            EditorGUI.BeginChangeCheck();
            user.LevelMo2Instance = EditorGUILayout.TextField("Global Instance Name", user.LevelMo2Instance);
            user.LevelModsFolder = DrawFolder(new GUIContent("Mods Folder", "Empty: read from MO2 configuration."), user.LevelModsFolder, "Mods Folder");
            user.LevelProfilesFolder = DrawFolder(new GUIContent("Profiles Folder", "Empty: read from MO2 configuration."), user.LevelProfilesFolder, "Profiles Folder");
            user.LevelOverwriteFolder = DrawFolder(new GUIContent("Overwrite Folder", "Empty: read from MO2 configuration."), user.LevelOverwriteFolder, "Overwrite Folder");
            if (EditorGUI.EndChangeCheck())
            {
                user.SaveSettings();
                XRayLevelSourceCache.Inspect(true);
            }
        }

        private static void DrawBuild()
        {
            XRayLevelIndexJob build = XRayLevelSourceCache.CurrentBuild;
            if (build == null || build.IsDone)
            {
                if (build != null && build.Error != null)
                {
                    EditorGUILayout.HelpBox("Virtual index failed: " + build.Error, MessageType.Error);
                }

                return;
            }

            EditorGUILayout.BeginHorizontal();
            Rect rect = EditorGUILayout.GetControlRect(false, 18.0f);
            EditorGUI.ProgressBar(rect, build.Progress, "Indexing: " + build.Stage);
            if (GUILayout.Button("Cancel", GUILayout.Width(64.0f)))
            {
                XRayLevelSourceCache.CancelMo2Build();
            }

            EditorGUILayout.EndHorizontal();
        }

        private static void Detect(XRayAssetSourceUserSettings user)
        {
            string folder = string.IsNullOrEmpty(user.LevelMo2Folder) ? XRayAssetSourceUserSettings.DEFAULT_MO2_FOLDER : user.LevelMo2Folder;
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            try
            {
                Mo2Installation installation = Mo2InstallationReader.Read(folder, user.LevelMo2Instance, localAppData);
                user.LevelMo2Folder = folder;
                user.SaveSettings();
                _detectMessage = $"{(installation.IsPortable ? "Portable" : "Global instance")} MO2: {installation.InstanceFolder}\nGame: {installation.GameName} at {installation.GamePath}\nmods: {installation.ModsDirectory}\nprofiles: {installation.ProfilesDirectory} ({installation.Profiles.Count}: {string.Join(", ", installation.Profiles)})\noverwrite: {installation.OverwriteDirectory}\nactive profile: {installation.SelectedProfile ?? "<none>"}";
                if (installation.Warnings.Count > 0)
                {
                    _detectMessage += "\n" + string.Join("\n", installation.Warnings);
                }

                _detectType = installation.Warnings.Count > 0 ? MessageType.Warning : MessageType.Info;
            }
            catch (XRaySourceException exception)
            {
                List<string> instances = Mo2InstallationReader.FindInstances(localAppData);
                _detectMessage = exception.Message + (instances.Count > 0 ? "\nGlobal MO2 instances: " + string.Join(", ", instances) : string.Empty);
                _detectType = MessageType.Error;
            }

            XRayLevelSourceCache.Inspect(true);
        }

        private static void Validate()
        {
            XRayLevelGamedataInfo info = XRayLevelSourceCache.Inspect(true);
            XRaySourcePlan plan = XRayLevelSourceCache.Mo2Plan;
            var lines = new List<string>();
            lines.AddRange(plan.Errors);
            lines.AddRange(info.Errors);
            foreach (string warning in plan.Warnings)
            {
                lines.Add(warning.Length > 300 ? warning.Substring(0, 300) + "..." : warning);
            }

            if (lines.Count == 0)
            {
                lines.Add($"Profile '{plan.ProfileName}' is valid: {plan.ActiveModCount} enabled mods, {plan.Sources.Count} sources, fsgame: {plan.FsGame?.Origin}.");
            }

            _detectMessage = string.Join("\n", lines);
            _detectType = plan.Errors.Count > 0 || info.Errors.Count > 0 ? MessageType.Error : plan.Warnings.Count > 0 ? MessageType.Warning : MessageType.Info;
        }

        private static string DrawFolder(GUIContent label, string value, string title)
        {
            EditorGUILayout.BeginHorizontal();
            string folder = EditorGUILayout.DelayedTextField(label, value ?? string.Empty);
            if (GUILayout.Button("Browse", GUILayout.Width(64.0f)))
            {
                string selected = EditorUtility.OpenFolderPanel(title, folder, string.Empty);
                if (!string.IsNullOrEmpty(selected))
                {
                    folder = selected.Replace('/', Path.DirectorySeparatorChar);
                    GUI.changed = true;
                }
            }

            EditorGUILayout.EndHorizontal();
            return folder;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Sources;
using XRay.Importer.Sources.Dds;
using XRay.Importer.Sources.Levels;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    public sealed class XRayLevelMo2DiagnosticsWindow : EditorWindow
    {
        private const float ROW_HEIGHT = 18.0f;
        private const int MAX_SUGGESTIONS = 300;
        private static readonly string[] TABS = { "Conflicts", "Resource", "Level" };

        private int _tab;
        private string _conflictFilter = string.Empty;
        private bool _conflictTexturesOnly;
        private Vector2 _conflictScroll;
        private Vector2 _detailScroll;
        private string _resourcePath = string.Empty;
        private string _levelName;
        private string _cachedFingerprint;
        private string _cachedFilter;
        private bool _cachedTexturesOnly;
        private readonly List<XRayIndexedFile> _conflicts = new List<XRayIndexedFile>();
        private readonly List<string> _suggestions = new List<string>();
        private string _suggestionKey;
        private XRayResourceDetails _details;
        private string _resourceFingerprint;

        public static void ShowConflicts()
        {
            Open(0);
        }

        public static void ShowResource(string virtualPath)
        {
            XRayLevelMo2DiagnosticsWindow window = Open(1);
            if (!string.IsNullOrEmpty(virtualPath))
            {
                window._resourcePath = virtualPath;
                window._details = null;
            }
        }

        public static void ShowLevel(string level)
        {
            XRayLevelMo2DiagnosticsWindow window = Open(2);
            window._levelName = level;
        }

        private static XRayLevelMo2DiagnosticsWindow Open(int tab)
        {
            var window = GetWindow<XRayLevelMo2DiagnosticsWindow>("MO2 Effective Gamedata");
            window._tab = tab;
            window.Show();
            return window;
        }

        private void OnGUI()
        {
            XRayAssetIndex index = XRayLevelSourceCache.IsMo2 ? XRayLevelSourceCache.CurrentIndex : null;
            if (index == null)
            {
                EditorGUILayout.HelpBox("Select 'Mod Organizer 2 Profile' in the Level Importer and build the virtual index first.", MessageType.Info);
                return;
            }

            _tab = GUILayout.Toolbar(_tab, TABS);
            if (_resourceFingerprint != index.Fingerprint)
            {
                _resourceFingerprint = index.Fingerprint;
                _details = null;
                _suggestionKey = null;
            }

            var gamedata = new XRayEffectiveGamedata(index);
            switch (_tab)
            {
                case 0:
                    DrawConflicts(index);
                    break;
                case 1:
                    DrawResource(gamedata);
                    break;
                default:
                    DrawLevel(gamedata);
                    break;
            }
        }

        private void DrawConflicts(XRayAssetIndex index)
        {
            EditorGUILayout.BeginHorizontal();
            _conflictFilter = EditorGUILayout.TextField("Filter (path or mod)", _conflictFilter);
            _conflictTexturesOnly = GUILayout.Toggle(_conflictTexturesOnly, "Textures only", GUILayout.Width(100.0f));
            EditorGUILayout.EndHorizontal();
            if (_cachedFingerprint != index.Fingerprint || _cachedFilter != _conflictFilter || _cachedTexturesOnly != _conflictTexturesOnly)
            {
                RebuildConflicts(index);
            }

            EditorGUILayout.LabelField($"{_conflicts.Count} of {index.Statistics.ConflictCount} conflicting paths", EditorStyles.miniLabel);
            _conflictScroll = EditorGUILayout.BeginScrollView(_conflictScroll);
            int first = Mathf.Max(0, (int)(_conflictScroll.y / ROW_HEIGHT));
            int visible = Mathf.CeilToInt(position.height / ROW_HEIGHT) + 2;
            int last = Mathf.Min(_conflicts.Count, first + visible);
            GUILayout.Space(first * ROW_HEIGHT);
            for (int i = first; i < last; i++)
            {
                XRayIndexedFile file = _conflicts[i];
                Rect row = EditorGUILayout.GetControlRect(false, ROW_HEIGHT - 2.0f);
                string label = $"{file.VirtualPath}   <- {index.GetSource(file.Winner).Origin}   ({file.Candidates.Count} sources)";
                if (GUI.Button(row, label, EditorStyles.label))
                {
                    _resourcePath = file.VirtualPath;
                    _details = null;
                    _tab = 1;
                }
            }

            GUILayout.Space(Mathf.Max(0, _conflicts.Count - last) * ROW_HEIGHT);
            EditorGUILayout.EndScrollView();
        }

        private void RebuildConflicts(XRayAssetIndex index)
        {
            _cachedFingerprint = index.Fingerprint;
            _cachedFilter = _conflictFilter;
            _cachedTexturesOnly = _conflictTexturesOnly;
            _conflicts.Clear();
            string filter = _conflictFilter.Trim();
            foreach (XRayIndexedFile file in index.Files)
            {
                if (!file.HasConflict || (_conflictTexturesOnly && !file.VirtualPath.StartsWith("textures/", StringComparison.Ordinal)))
                {
                    continue;
                }

                if (filter.Length > 0 && file.VirtualPath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && !MatchesOrigin(index, file, filter))
                {
                    continue;
                }

                _conflicts.Add(file);
            }

            _conflicts.Sort((a, b) => string.CompareOrdinal(a.VirtualPath, b.VirtualPath));
        }

        private static bool MatchesOrigin(XRayAssetIndex index, XRayIndexedFile file, string filter)
        {
            foreach (XRayIndexCandidate candidate in file.Candidates)
            {
                if (index.GetSource(candidate).Origin.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawResource(XRayEffectiveGamedata gamedata)
        {
            EditorGUILayout.BeginHorizontal();
            string path = EditorGUILayout.TextField("Virtual Path", _resourcePath);
            if (path != _resourcePath)
            {
                _resourcePath = path;
                _details = null;
            }

            EditorGUILayout.EndHorizontal();
            if (!XRayVirtualPath.TryNormalizeSafe(_resourcePath, out string normalized))
            {
                EditorGUILayout.HelpBox($"'{_resourcePath}' leaves the gamedata root.", MessageType.Error);
                return;
            }

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
            if (normalized.Length > 0 && !gamedata.Exists(normalized))
            {
                DrawSuggestions(gamedata, normalized);
                EditorGUILayout.EndScrollView();
                return;
            }

            if (normalized.Length > 0)
            {
                if (_details == null || _details.VirtualPath != normalized)
                {
                    _details = XRayResourceDetails.Create(gamedata, normalized, XRayLevelSourceCache.AssetRoot);
                }

                DrawDetails(_details);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSuggestions(XRayEffectiveGamedata gamedata, string prefix)
        {
            if (_suggestionKey != prefix)
            {
                _suggestionKey = prefix;
                _suggestions.Clear();
                foreach (XRayIndexedFile file in gamedata.Index.Files)
                {
                    if (file.VirtualPath.IndexOf(prefix, StringComparison.Ordinal) >= 0)
                    {
                        _suggestions.Add(file.VirtualPath);
                        if (_suggestions.Count >= MAX_SUGGESTIONS)
                        {
                            break;
                        }
                    }
                }

                _suggestions.Sort(StringComparer.Ordinal);
            }

            EditorGUILayout.LabelField(_suggestions.Count == 0 ? "Not found in the effective gamedata." : $"{_suggestions.Count} matches{(_suggestions.Count >= MAX_SUGGESTIONS ? " (first " + MAX_SUGGESTIONS + ")" : string.Empty)}:", EditorStyles.miniBoldLabel);
            foreach (string suggestion in _suggestions)
            {
                if (GUILayout.Button(suggestion, EditorStyles.label))
                {
                    _resourcePath = suggestion;
                    _details = null;
                }
            }
        }

        private static void DrawDetails(XRayResourceDetails details)
        {
            EditorGUILayout.LabelField("Winner", details.Candidates[0].Origin, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Source", details.Candidates[0].SourceName);
            EditorGUILayout.LabelField("Priority", details.Candidates[0].DisplayPriority);
            EditorGUILayout.LabelField("Size", details.Candidates[0].Size.ToString("N0") + " B");
            if (details.Resolution != null)
            {
                EditorGUILayout.LabelField("Resolution", details.Resolution);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Candidates (winner first)", EditorStyles.boldLabel);
            for (int i = 0; i < details.Candidates.Count; i++)
            {
                XRayEffectiveFile candidate = details.Candidates[i];
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(new GUIContent($"{(i == 0 ? "WIN " : "lose")}  {candidate.Origin} ({candidate.DisplayPriority}), {candidate.Size:N0} B", candidate.Location));
                if (GUILayout.Button("Show in Explorer", GUILayout.Width(120.0f)))
                {
                    EditorUtility.RevealInFinder(candidate.PhysicalPath);
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.SelectableLabel(candidate.Location, EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Used By", EditorStyles.boldLabel);
            if (details.UsedBy.Count == 0)
            {
                EditorGUILayout.LabelField("No imported level of this profile references this file.", EditorStyles.miniLabel);
            }

            foreach (string usage in details.UsedBy)
            {
                EditorGUILayout.LabelField(usage, EditorStyles.miniLabel);
            }
        }

        private void DrawLevel(XRayEffectiveGamedata gamedata)
        {
            XRayLevelGamedataInfo info = XRayLevelSourceCache.Inspect(false);
            var names = new List<string>();
            foreach (XRayLevelEntry level in info.Levels)
            {
                names.Add(level.Name);
            }

            if (names.Count == 0)
            {
                EditorGUILayout.HelpBox("No levels in the effective gamedata.", MessageType.Info);
                return;
            }

            int selected = EditorGUILayout.Popup("Level", Mathf.Max(0, names.IndexOf(_levelName ?? string.Empty)), names.ToArray());
            _levelName = names[selected];
            XRayLevelEntry entry = info.Find(_levelName);
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
            EditorGUILayout.LabelField("Winning 'level'", entry.Origin + (entry.IsModded ? " (added by mod)" : entry.IsReplaced ? " (replaces base level)" : " (base)"));
            EditorGUILayout.LabelField("Location", entry.LevelPath, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("Game Registered", entry.IsRegistered ? "yes, " + entry.RegisteredBy : "no");
            XRayLevelProbe probe = entry.Probe;
            if (probe != null)
            {
                EditorGUILayout.LabelField("Format", $"xrLC {probe.Version}, {probe.Status}, {probe.Shaders} shaders, {probe.Visuals} visuals, {probe.Trees} trees, {probe.VertexBuffers} VB / {probe.IndexBuffers} IB, {probe.Lightmaps} lightmaps");
                DrawList("Errors", probe.Errors, MessageType.Error);
                DrawList("Missing required", probe.MissingRequired, MessageType.Error);
                DrawList("Missing optional", probe.MissingOptional, MessageType.Warning);
                DrawList("Notes", probe.Notes, MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Level files (effective winners)", EditorStyles.boldLabel);
            foreach (XRayEffectiveFile file in gamedata.Enumerate(entry.VirtualFolder, true))
            {
                IReadOnlyList<XRayEffectiveFile> candidates = gamedata.GetCandidates(file.VirtualPath);
                string label = $"{file.VirtualPath.Substring(entry.VirtualFolder.Length + 1)}   <- {file.Origin}{(candidates.Count > 1 ? $"   (overrides {candidates.Count - 1})" : string.Empty)}";
                if (GUILayout.Button(new GUIContent(label, file.Location), EditorStyles.label))
                {
                    _resourcePath = file.VirtualPath;
                    _details = null;
                    _tab = 1;
                }
            }

            DrawImportState(entry);
            EditorGUILayout.EndScrollView();
        }

        private static void DrawImportState(XRayLevelEntry entry)
        {
            string manifestPath = XRayLevelPaths.ManifestPath(XRayLevelSourceCache.AssetRoot, entry.Name);
            var manifest = AssetDatabase.LoadAssetAtPath<XRayLevelImportManifest>(manifestPath);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Previous Import <-> Current MO2 Profile", EditorStyles.boldLabel);
            if (manifest == null)
            {
                EditorGUILayout.LabelField("Not imported into " + XRayLevelSourceCache.AssetRoot, EditorStyles.miniLabel);
                return;
            }

            XRayLevelSourceDiff diff = XRayLevelSourceCache.GetDiff(entry);
            EditorGUILayout.LabelField("Manifest", manifestPath, EditorStyles.miniLabel);
            if (diff != null)
            {
                EditorGUILayout.LabelField("Reimport", diff.ReimportRequired ? "required" : "not required");
                EditorGUILayout.LabelField("Mod order", diff.ProfileChanged ? "changed since import" : "unchanged");
                foreach (XRayLevelSourceChange change in diff.Changes)
                {
                    EditorGUILayout.LabelField(change.ToString(), EditorStyles.miniLabel);
                }

                foreach (string material in diff.AffectedMaterials)
                {
                    EditorGUILayout.LabelField("material: " + material, EditorStyles.miniLabel);
                }
            }

            var missing = new List<string>();
            var unsupported = new List<string>();
            foreach (XRayLevelDependency dependency in manifest.Record.Dependencies)
            {
                if (dependency.Status == nameof(XRayLevelDependencyStatus.Missing))
                {
                    missing.Add($"{dependency.Role}: {dependency.Reference}");
                }
                else if (dependency.Status == nameof(XRayLevelDependencyStatus.Unsupported))
                {
                    unsupported.Add($"{dependency.Role}: {dependency.Reference} {dependency.Note}");
                }
            }

            DrawList($"Missing resources ({missing.Count})", missing, MessageType.Warning);
            DrawList($"Unsupported resources ({unsupported.Count})", unsupported, MessageType.Info);
        }

        private static void DrawList(string title, List<string> items, MessageType type)
        {
            if (items.Count == 0)
            {
                return;
            }

            EditorGUILayout.HelpBox(title + ":\n" + string.Join("\n", items.Count > 200 ? items.GetRange(0, 200) : items), type);
        }

        private sealed class XRayResourceDetails
        {
            public string VirtualPath { get; private set; }
            public IReadOnlyList<XRayEffectiveFile> Candidates { get; private set; }
            public string Resolution { get; private set; }
            public List<string> UsedBy { get; } = new List<string>();

            public static XRayResourceDetails Create(XRayEffectiveGamedata gamedata, string virtualPath, string root)
            {
                var details = new XRayResourceDetails { VirtualPath = virtualPath, Candidates = gamedata.GetCandidates(virtualPath) };
                if (virtualPath.EndsWith(".dds", StringComparison.Ordinal))
                {
                    details.Resolution = ReadResolution(gamedata, virtualPath);
                }

                foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(XRayLevelImportManifest), new[] { root }))
                {
                    var manifest = AssetDatabase.LoadAssetAtPath<XRayLevelImportManifest>(AssetDatabase.GUIDToAssetPath(guid));
                    XRayLevelSourceFile file = manifest != null ? manifest.Record.Source.Find(virtualPath) : null;
                    if (file != null)
                    {
                        details.UsedBy.Add($"{manifest.Record.LevelName} / {file.Role} -> {file.AssetPath ?? "-"} (imported from {file.Origin})");
                    }
                }

                return details;
            }

            private static string ReadResolution(XRayEffectiveGamedata gamedata, string virtualPath)
            {
                try
                {
                    var head = new byte[148];
                    using (Stream stream = gamedata.OpenRead(virtualPath))
                    {
                        int read = 0;
                        while (read < head.Length)
                        {
                            int count = stream.Read(head, read, head.Length - read);
                            if (count <= 0)
                            {
                                break;
                            }

                            read += count;
                        }
                    }

                    DdsHeader header = DdsHeader.Parse(head);
                    return $"{header.Width} x {header.Height}, {header.MipCount} mips, {header.Description}";
                }
                catch (Exception exception) when (exception is IOException || exception is FormatException || exception is XRaySourceException)
                {
                    return "unreadable: " + exception.Message;
                }
            }
        }
    }
}

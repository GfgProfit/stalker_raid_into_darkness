using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Core.Level;
using XRay.Importer.Sources.Dds;
using XRay.Importer.Sources.Textures;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    public sealed class XRayLevelBindingDependency
    {
        public string Property { get; set; }
        public XRayResolvedFile Source { get; set; }
        public string AssetPath { get; set; }
        public string SourceHash { get; set; }
        public string ExternalSourcePath { get; set; }
        public string AssetHash { get; set; }
        public string MetaHash { get; set; }
        public string Reason { get; set; }
        public bool CreatesAsset { get; set; }
        public bool BlocksRefresh { get; set; }
        public bool FlipV { get; set; }
    }

    public sealed class XRayLevelSourceRefreshEntry
    {
        public string MaterialPath { get; set; }
        public string Guid { get; set; }
        public string Hash { get; set; }
        public string MetaHash { get; set; }
        public string State { get; set; }
        public string Error { get; set; }
        public XRayTextureResolution Resolution { get; set; }
        public XRayLevelSurface Surface { get; set; }
        public XRayLevelSourceBinding Binding { get; set; }
        public XRayLevelSourceBinding ProjectedBinding { get; set; }
        public XRayLevelBindingPlan Plan { get; set; }
        public XRayLevelTerrainTextures Terrain { get; set; }
        public List<XRayLevelBindingDependency> Dependencies { get; } = new List<XRayLevelBindingDependency>();
        public bool CanApply => Error == null && !Dependencies.Exists(dependency => dependency.BlocksRefresh);
    }

    public sealed class XRayLevelSourceRefresh
    {
        private readonly XRayTextureResolver _resolver;
        private readonly XRayTextureImporter _importer;
        private readonly XRayShaderLibrary _shaders;
        private readonly string _root;
        private readonly Dictionary<string, XRayLevelBindingDependency> _dependencies = new Dictionary<string, XRayLevelBindingDependency>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _metadataHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private XRayTerrainSourceParameters _terrainParameters;

        public XRayLevelSourceRefresh(XRayAssetIndex index, string root)
        {
            if (!root.StartsWith(XRayLevelPaths.ANOMALY_ROOT + "/", StringComparison.Ordinal) || root.Contains(".."))
            {
                throw new ArgumentException("An explicit imported Anomaly profile root is required.", nameof(root));
            }

            _root = root.TrimEnd('/');
            _resolver = new XRayTextureResolver(index);
            _importer = new XRayTextureImporter(new XRayImportOptions(), false, new HashSet<string>());
            XRayResolvedFile shaders = _resolver.ResolveFile("shaders.xr");
            if (shaders.IsResolved)
            {
                _shaders = XRayShaderLibrary.Read(shaders.Winner.ReadAllBytes(), shaders.Winner.Location);
            }
        }

        public static XRayLevelSourceRefresh Open(string root, string level = null)
        {
            XRaySourceConfiguration configuration = XRayLevelSourceCache.CreateMo2Configuration();
            configuration.IndexScope.Clear();
            configuration.IndexScope.AddRange(new[] { "textures", "textures.ltx", "shaders.xr", "shaders" });
            if (!string.IsNullOrEmpty(level)) configuration.IndexScope.Add("levels/" + level);
            XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(configuration);
            if (!plan.IsValid)
            {
                throw new InvalidOperationException(string.Join("; ", plan.Errors));
            }

            string expected = XRayLevelPaths.SpaceRoot(XRayLevelSourceCache.CreateProfileId(plan.Installation.InstanceFolder, plan.ProfileName));
            if (root != expected)
            {
                throw new InvalidOperationException($"Requested root '{root}' does not match effective MO2 profile '{expected}'.");
            }

            return new XRayLevelSourceRefresh(XRayAssetIndex.Build(plan), root) { _terrainParameters = XRayTerrainSourceParameters.FromSource(new XRayLevelSourceProvenance { GameFolder = plan.GameFolder, FsGame = plan.FsGamePath }) };
        }

        public static string PreviewTargets(string root, string level, string[] targets, string outputFolder, bool apply)
        {
            if (targets == null || targets.Length == 0 || (apply && targets.Length > 32))
            {
                throw new ArgumentException("Supply explicit material targets; a saved refresh is limited to 32 materials.");
            }

            RequireArchive(outputFolder);
            var refresh = Open(root, level);
            var entries = new List<XRayLevelSourceRefreshEntry>();
            foreach (string target in targets)
            {
                entries.Add(refresh.Inspect(target, level));
            }

            string preview = Path.Combine(outputFolder, "Material_Binding_BeforeAfter.csv");
            if (File.Exists(preview))
            {
                throw new IOException("Use a unique output directory; preview already exists: " + preview);
            }

            WritePreview(entries, preview);
            WriteProvenance(entries, Path.Combine(outputFolder, "THM_Detail_Source_Provenance.csv"));
            File.WriteAllLines(Path.Combine(outputFolder, "Confirmed_Targets.txt"), targets);
            if (apply)
            {
                refresh.Apply(entries, outputFolder);
                var after = new List<XRayLevelSourceRefreshEntry>();
                foreach (string target in targets)
                {
                    after.Add(refresh.Inspect(target, level));
                }

                WritePreview(after, Path.Combine(outputFolder, "Material_Binding_After.csv"));
            }

            return $"{entries.Count} explicit targets; {entries.FindAll(entry => entry.CanApply).Count} safe plans; saved refresh {apply}; {preview}";
        }

        public static string PreviewTargetFile(string root, string level, string targetFile, string outputFolder, bool apply)
        {
            return PreviewTargets(root, level, File.ReadAllLines(targetFile), outputFolder, apply);
        }

        public static void WriteProvenance(IReadOnlyList<XRayLevelSourceRefreshEntry> entries, string path)
        {
            RequireArchive(Path.GetDirectoryName(path));
            var csv = new StringBuilder("Material,Property,VirtualPath,Winner,SourcePath,SHA256,AssetPath,AssetGUID,DetailName,DetailScale,Flags,BumpMode,Blender,Reason\n");
            foreach (XRayLevelSourceRefreshEntry entry in entries)
            {
                foreach (XRayLevelBindingDependency dependency in entry.Dependencies)
                {
                    XRayTextureDescription description = entry.Resolution?.Description;
                    Csv(csv, entry.MaterialPath, dependency.Property, dependency.Source?.VirtualPath, dependency.Source?.Winner?.SourceName, dependency.Source?.Winner?.Location ?? dependency.ExternalSourcePath, dependency.SourceHash, dependency.AssetPath, dependency.AssetPath != null ? AssetDatabase.AssetPathToGUID(dependency.AssetPath) : string.Empty, description?.DetailName, description?.DetailScale, description?.Flags, description?.BumpMode, entry.Surface?.BlenderClass, dependency.Reason);
                }
            }

            File.WriteAllText(path, csv.ToString());
        }

        public XRayLevelSourceRefreshEntry Inspect(string materialPath, string level)
        {
            var entry = new XRayLevelSourceRefreshEntry { MaterialPath = materialPath };
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            XRayAssetMarker marker = XRayAssetMarker.Read(materialPath);
            string[] key = marker?.Key.Split('|');
            if (!materialPath.StartsWith(_root + "/Materials/", StringComparison.Ordinal) || material == null || material.shader.name != MonolithMaterialConfigurator.SHADER_NAME || key == null || key.Length < XRayLevelMaterialLibrary.METADATA_KEY_PARTS || key[0] != "level" || key[2] != "static" || key[10] != "monolith")
            {
                entry.Error = "Not an imported static Anomaly Monolith material in the selected profile.";
                return entry;
            }

            string texture = key[3].Split('#')[0];
            if (texture.StartsWith("none:", StringComparison.Ordinal))
            {
                texture = texture.Substring(5);
            }

            if (texture.StartsWith("textures/", StringComparison.Ordinal))
            {
                texture = texture.Substring("textures/".Length);
            }
            else if (texture.StartsWith("levels/" + level + "/", StringComparison.Ordinal))
            {
                texture = texture.Substring(("levels/" + level + "/").Length);
            }

            _resolver.LevelFolder = "levels/" + level;
            entry.Resolution = _resolver.Resolve(texture);
            XRayBlender blender = null;
            _shaders?.TryGet(key[11], out blender);
            entry.Surface = XRayLevelSurfaceRules.Classify(new XRayLevelShader(0, key[11]), blender, key[1].StartsWith("tree_", StringComparison.Ordinal));
            if (blender == null)
            {
                entry.Error = $"Effective shaders.xr has no blender '{key[11]}'; cannot prove source path eligibility.";
                return entry;
            }

            entry.Guid = AssetDatabase.AssetPathToGUID(materialPath);
            entry.Hash = HashFile(materialPath);
            entry.MetaHash = HashFile(materialPath + ".meta");
            entry.State = EditorJsonUtility.ToJson(material);
            AddMetadata(entry, _resolver.ResolveFile(XRayVirtualPath.TextureToVirtualPath(entry.Resolution.TextureName, ".thm")));
            AddMetadata(entry, _resolver.ResolveFile("textures.ltx"));
            AddMetadata(entry, _resolver.ResolveFile("shaders.xr"));
            AddMetadata(entry, entry.Resolution.Diffuse);
            if (entry.Resolution.Bump != null)
            {
                AddMetadata(entry, entry.Resolution.Bump);
            }
            if (entry.Surface.Kind == XRayLevelSurfaceKind.Terrain)
            {
                if (!XRayTerrainSourceBinding.IsTerrainBlender(blender))
                {
                    entry.Error = "Source terrain binding requires BmmD or BmmDold metadata; a shader name alone is insufficient.";
                    return entry;
                }
                InspectTerrain(entry, blender, material);
                Replan(entry, material);
                entry.State = EditorJsonUtility.ToJson(material);
                return entry;
            }
            AddTexture(entry, XRayLevelSourceBindings.CORRECTION, entry.Resolution.BumpCorrection);
            if (XRayLevelSourceBindings.DeclaresDetail(entry.Resolution.Description))
            {
                XRayTextureResolution detail = _resolver.Resolve(entry.Resolution.Description.DetailName);
                AddMetadata(entry, _resolver.ResolveFile(XRayVirtualPath.TextureToVirtualPath(detail.TextureName, ".thm")));
                AddTexture(entry, XRayLevelSourceBindings.DETAIL, detail.Diffuse);
                AddTexture(entry, XRayLevelSourceBindings.DETAIL_BUMP, detail.Bump);
                AddTexture(entry, XRayLevelSourceBindings.DETAIL_CORRECTION, detail.BumpCorrection);
            }

            Replan(entry, material);
            entry.State = EditorJsonUtility.ToJson(material);
            return entry;
        }

        public void Replan(XRayLevelSourceRefreshEntry entry, Material material)
        {
            if (entry.Terrain != null)
            {
                entry.Terrain.MaskPath = PathFor(entry, "_MonolithTerrainMask");
                entry.Terrain.SourceBasePath = PathFor(entry, "_MonolithTerrainSourceBase");
                entry.Terrain.LodAtlasPath = PathFor(entry, "_MonolithTerrainLodAtlas");
                string[] channels = { "R", "G", "B", "A" };
                for (int i = 0; i < channels.Length; i++)
                {
                    entry.Terrain.BumpPaths[i] = PathFor(entry, "_MonolithTerrainBump" + channels[i]);
                    entry.Terrain.SourceColorPaths[i] = PathFor(entry, "_MonolithTerrainSourceColor" + channels[i]);
                    entry.Terrain.HeightPaths[i] = PathFor(entry, "_MonolithTerrainHeight" + channels[i]);
                }
                entry.Plan = XRayTerrainMaterialBindings.Plan(material, entry.Terrain, XRayTerrainImportMode.Bind);
                entry.ProjectedBinding = new XRayLevelSourceBinding();
                entry.ProjectedBinding.Reasons.Add("Terrain resource-only plan; activation and quality are preserved; presence is recomputed after importing missing resources");
                return;
            }
            entry.Binding = XRayLevelSourceBindings.Resolve(entry.Resolution.Description, entry.Surface.BlenderClass, entry.Surface.Kind, entry.Surface.Flora, false, material.GetTexture("_MonolithBump") != null && material.GetFloat("_MonolithHasBump") > 0.5f, material.GetFloat("_MonolithBumpFlipV"), PathFor(entry, XRayLevelSourceBindings.CORRECTION), PathFor(entry, XRayLevelSourceBindings.DETAIL), PathFor(entry, XRayLevelSourceBindings.DETAIL_BUMP), PathFor(entry, XRayLevelSourceBindings.DETAIL_CORRECTION));
            entry.ProjectedBinding = XRayLevelSourceBindings.Resolve(entry.Resolution.Description, entry.Surface.BlenderClass, entry.Surface.Kind, entry.Surface.Flora, false, material.GetTexture("_MonolithBump") != null && material.GetFloat("_MonolithHasBump") > 0.5f, material.GetFloat("_MonolithBumpFlipV"), ProjectedPath(entry, XRayLevelSourceBindings.CORRECTION), ProjectedPath(entry, XRayLevelSourceBindings.DETAIL), ProjectedPath(entry, XRayLevelSourceBindings.DETAIL_BUMP), ProjectedPath(entry, XRayLevelSourceBindings.DETAIL_CORRECTION), path => entry.Dependencies.Find(dependency => dependency.AssetPath == path || "planned:" + dependency.Source?.VirtualPath == path)?.FlipV ?? XRayLevelSourceBindings.PackedFlipV(path));
            entry.Plan = XRayLevelSourceBindings.Plan(material, entry.Binding);
        }

        public void ImportDependencies(IReadOnlyList<XRayLevelSourceRefreshEntry> entries)
        {
            foreach (XRayLevelSourceRefreshEntry entry in entries)
            {
                if (!entry.CanApply || !entry.MaterialPath.StartsWith(_root + "/Materials/", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Unsafe resource plan for '{entry.MaterialPath}': {entry.Error ?? "an existing shared texture needs a separate update"}");
                }
            }

            foreach (XRayLevelSourceRefreshEntry entry in entries)
            {
                foreach (XRayLevelBindingDependency dependency in entry.Dependencies)
                {
                    if (!dependency.CreatesAsset)
                    {
                        continue;
                    }

                    if (HashBytes(dependency.Source.Winner.ReadAllBytes()) != dependency.SourceHash)
                    {
                        throw new IOException($"Source changed after dry run: {dependency.Source.VirtualPath}");
                    }

                    XRayTextureAssetResult result = _importer.ImportPacked(dependency.Source, TextureFolder(dependency.Source.VirtualPath));
                    if (result.AssetPath == null)
                    {
                        throw new IOException($"Cannot import '{dependency.Source.VirtualPath}': {result.Note}");
                    }

                    dependency.AssetPath = result.AssetPath;
                    dependency.CreatesAsset = false;
                    dependency.FlipV = XRayLevelSourceBindings.PackedFlipV(result.AssetPath);
                    XRayLevelBindingDependency cached = _dependencies[dependency.Source.VirtualPath];
                    cached.AssetPath = result.AssetPath;
                    cached.CreatesAsset = false;
                    cached.FlipV = dependency.FlipV;
                }
            }

            _importer.ImportPending();
            foreach (string path in _importer.PendingImports)
            {
                if (XRayTextureImportQueue.TryPeek(path, out XRayTextureImportRequest request))
                {
                    XRayAssetMarker.Write(path, request.Marker);
                    XRayTextureImportQueue.Complete(path);
                }
            }

            foreach (XRayLevelSourceRefreshEntry entry in entries)
            {
                foreach (XRayLevelBindingDependency dependency in entry.Dependencies)
                {
                    if (dependency.AssetPath != null)
                    {
                        dependency.AssetHash = HashFile(dependency.AssetPath);
                        dependency.MetaHash = HashFile(dependency.AssetPath + ".meta");
                        XRayLevelBindingDependency cached = _dependencies[dependency.Source.VirtualPath];
                        cached.AssetHash = dependency.AssetHash;
                        cached.MetaHash = dependency.MetaHash;
                    }
                }
            }
        }

        public void Apply(IReadOnlyList<XRayLevelSourceRefreshEntry> entries, string archiveFolder)
        {
            if (entries == null || entries.Count == 0 || entries.Count > 32) throw new ArgumentException("A saved refresh requires 1 to 32 explicit materials.", nameof(entries));
            RequireCleanEditor();
            RequireArchive(archiveFolder);
            foreach (XRayLevelSourceRefreshEntry entry in entries)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(entry.MaterialPath);
                if (!entry.CanApply || !entry.MaterialPath.StartsWith(_root + "/Materials/", StringComparison.Ordinal) || material == null || EditorUtility.IsDirty(material) || entry.Guid != AssetDatabase.AssetPathToGUID(entry.MaterialPath) || entry.Hash != HashFile(entry.MaterialPath) || entry.MetaHash != HashFile(entry.MaterialPath + ".meta") || entry.State != EditorJsonUtility.ToJson(material))
                {
                    string currentState = material != null ? EditorJsonUtility.ToJson(material) : string.Empty;
                    File.WriteAllText(Path.Combine(archiveFolder, entry.Guid + "_PlannedState.json"), entry.State ?? string.Empty);
                    File.WriteAllText(Path.Combine(archiveFolder, entry.Guid + "_CurrentState.json"), currentState);
                    throw new InvalidOperationException($"Stale, dirty or blocked material plan: {entry.MaterialPath}; safe={entry.CanApply}; loaded={material != null}; dirty={material != null && EditorUtility.IsDirty(material)}; guid={entry.Guid == AssetDatabase.AssetPathToGUID(entry.MaterialPath)}; file={entry.Hash == HashFile(entry.MaterialPath)}; meta={entry.MetaHash == HashFile(entry.MaterialPath + ".meta")}; state={entry.State == currentState}");
                }

                foreach (XRayLevelBindingDependency dependency in entry.Dependencies)
                {
                    if (dependency.ExternalSourcePath != null && HashFile(dependency.ExternalSourcePath) != dependency.SourceHash) throw new IOException("Source settings changed after dry run: " + dependency.ExternalSourcePath);
                    if (dependency.Source != null && dependency.Source.IsResolved && HashBytes(dependency.Source.Winner.ReadAllBytes()) != dependency.SourceHash)
                    {
                        throw new IOException($"Source changed after dry run: {dependency.Source.VirtualPath}");
                    }

                    if (dependency.AssetPath != null && (HashFile(dependency.AssetPath) != dependency.AssetHash || HashFile(dependency.AssetPath + ".meta") != dependency.MetaHash))
                    {
                        throw new IOException($"Texture changed after dry run: {dependency.AssetPath}");
                    }
                }
            }

            var backups = new StringBuilder("Material,GUID,SHA256,MetaSHA256,Backup\n");
            foreach (XRayLevelSourceRefreshEntry entry in entries)
            {
                string backup = Path.Combine(archiveFolder, "Backups", entry.Guid + ".mat");
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                File.Copy(entry.MaterialPath, backup, false);
                File.Copy(entry.MaterialPath + ".meta", backup + ".meta", false);
                Csv(backups, entry.MaterialPath, entry.Guid, entry.Hash, entry.MetaHash, backup);
            }

            File.WriteAllText(Path.Combine(archiveFolder, "Backup_Index.csv"), backups.ToString());
            ImportDependencies(entries);
            foreach (XRayLevelSourceRefreshEntry entry in entries)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(entry.MaterialPath);
                Replan(entry, material);
                if (entry.Terrain != null) XRayTerrainMaterialBindings.Apply(material, entry.Terrain, entry.Plan);
                else MonolithMaterialConfigurator.ConfigureSourceBindings(material, entry.Binding);
                AssetDatabase.SaveAssetIfDirty(material);
                if (entry.Guid != AssetDatabase.AssetPathToGUID(entry.MaterialPath) || entry.MetaHash != HashFile(entry.MaterialPath + ".meta"))
                {
                    throw new IOException($"Material identity changed unexpectedly: {entry.MaterialPath}");
                }
            }
        }

        public static void WritePreview(IReadOnlyList<XRayLevelSourceRefreshEntry> entries, string path)
        {
            RequireArchive(Path.GetDirectoryName(path));
            var csv = new StringBuilder("Material,GUID,Property,Current,PreviousGenerated,Intended,Final,Ownership,SourcePath,MO2Winner,SHA256,CreateTexture,ReuseTexture,Modifies,Reason,CanApply,Error\n");
            foreach (XRayLevelSourceRefreshEntry entry in entries)
            {
                if (entry.Plan == null)
                {
                    Csv(csv, entry.MaterialPath, entry.Guid, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, false, false, false, entry.Error, false, entry.Error);
                    continue;
                }

                foreach (XRayLevelBindingChange change in entry.Plan.Changes)
                {
                    XRayLevelBindingDependency dependency = entry.Dependencies.Find(item => item.Property == change.Property);
                    bool creates = dependency != null && dependency.CreatesAsset;
                    string intended = creates ? dependency.Source.VirtualPath + " (import required)" : entry.Terrain != null ? change.Desired : ProjectedValue(entry, change);
                    string final = change.Ownership == XRayLevelBindingOwnership.UserOverride ? change.Current : intended;
                    Csv(csv, entry.MaterialPath, entry.Guid, change.Property, change.Current, change.Previous, intended, final, change.Ownership, dependency?.Source?.Winner?.Location ?? dependency?.ExternalSourcePath, dependency?.Source?.Winner?.SourceName, dependency?.SourceHash, creates, dependency?.AssetPath != null && !dependency.BlocksRefresh, final != change.Current, dependency?.Reason ?? string.Join("; ", entry.ProjectedBinding.Reasons), entry.CanApply, entry.Error);
                }
            }

            File.WriteAllText(path, csv.ToString());
        }

        public static void RequireCleanEditor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                throw new InvalidOperationException("Editor must be idle and outside Play Mode.");
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty)
                {
                    throw new InvalidOperationException("Unsaved scene: " + scene.path);
                }
            }

            foreach (UnityEngine.Object asset in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
            {
                if (IsUnsavedAsset(asset))
                {
                    throw new InvalidOperationException("Unsaved asset: " + AssetDatabase.GetAssetPath(asset));
                }
            }
        }

        public static string InspectEditor()
        {
            var result = new StringBuilder(Application.dataPath + "|" + SystemInfo.graphicsDeviceType);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                result.Append('\n').Append(scene.path).Append("|dirty=").Append(scene.isDirty);
            }

            foreach (UnityEngine.Object asset in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
            {
                if (EditorUtility.IsPersistent(asset) && EditorUtility.IsDirty(asset) && AssetDatabase.GetAssetPath(asset).StartsWith("Assets/", StringComparison.Ordinal))
                {
                    result.Append(IsUnsavedAsset(asset) ? "\ndirty asset=" : "\ndirty imported source cache=").Append(AssetDatabase.GetAssetPath(asset));
                }
            }

            return result.ToString();
        }

        private void AddMetadata(XRayLevelSourceRefreshEntry entry, XRayResolvedFile file)
        {
            if (file.IsResolved && !_metadataHashes.ContainsKey(file.VirtualPath))
            {
                _metadataHashes.Add(file.VirtualPath, HashBytes(file.Winner.ReadAllBytes()));
            }

            entry.Dependencies.Add(new XRayLevelBindingDependency { Source = file, SourceHash = file.IsResolved ? _metadataHashes[file.VirtualPath] : null, Reason = file.IsResolved ? "Effective source metadata" : "Optional metadata missing" });
        }

        private static bool IsUnsavedAsset(UnityEngine.Object asset)
        {
            if (!EditorUtility.IsPersistent(asset) || !EditorUtility.IsDirty(asset))
            {
                return false;
            }

            string path = AssetDatabase.GetAssetPath(asset);
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return path.StartsWith("Assets/", StringComparison.Ordinal) && (asset is AssetImporter || extension == ".mat" || extension == ".asset" || extension == ".prefab" || extension == ".unity" || extension == ".anim" || extension == ".controller" || extension == ".overridecontroller" || extension == ".playable" || extension == ".terrainlayer" || extension == ".rendertexture" || extension == ".cubemap" || extension == ".shadervariants" || extension == ".spriteatlas" || extension == ".lighting");
        }

        private void InspectTerrain(XRayLevelSourceRefreshEntry entry, XRayBlender blender, Material material)
        {
            XRayTerrainSourceBinding source = XRayTerrainSourceBinding.Resolve(_resolver);
            entry.Terrain = new XRayLevelTerrainTextures { Source = source, Parameters = _terrainParameters };
            if (source.Profile != XRayTerrainSourceProfile.AnomalyAtmospherics)
            {
                entry.Error = "Saved terrain refresh requires the verified Anomaly shader and BRDF contract.";
                return;
            }
            AddMetadata(entry, source.Shader);
            AddMetadata(entry, source.Brdf);
            AddMetadata(entry, source.MidShader);
            AddMetadata(entry, source.LowShader);
            if (_terrainParameters != null)
            {
                entry.Dependencies.Add(new XRayLevelBindingDependency { Property = "_MonolithTerrainPom", ExternalSourcePath = _terrainParameters.Path, SourceHash = _terrainParameters.Hash, Reason = "Source appdata terrain parameters; external file hash is checked before a saved refresh" });
            }
            AddTexture(entry, "_MonolithTerrainMask", _resolver.ResolveTexture(entry.Resolution.TextureName + "_mask", ".dds"));
            AddTexture(entry, "_MonolithTerrainSourceBase", entry.Resolution.Diffuse);
            if (source.VerifiedMid)
            {
                XRayResolvedFile atlas = _resolver.ResolveTexture(entry.Resolution.TextureName + "_lod_textures", ".dds");
                if (!atlas.IsResolved)
                {
                    AddMetadata(entry, atlas);
                    atlas = _resolver.ResolveTexture("terrain/default_lod_textures", ".dds");
                }
                AddTexture(entry, "_MonolithTerrainLodAtlas", atlas);
            }
            string[] channels = { "R", "G", "B", "A" };
            for (int i = 0; i < channels.Length; i++)
            {
                string detail = blender.GetString("Detail map/R2-" + channels[i]);
                entry.Terrain.DetailPaths[i] = AssetDatabase.GetAssetPath(material.GetTexture("_MonolithTerrainDetail" + channels[i]));
                if (string.IsNullOrEmpty(detail)) continue;
                AddTexture(entry, "_MonolithTerrainBump" + channels[i], _resolver.ResolveTexture(detail + "_bump", ".dds"));
                AddTexture(entry, "_MonolithTerrainSourceColor" + channels[i], _resolver.ResolveTexture(detail, ".dds"));
                AddTexture(entry, "_MonolithTerrainHeight" + channels[i], _resolver.ResolveTexture(detail + "_height", ".dds"));
                AddMetadata(entry, _resolver.ResolveFile(XRayVirtualPath.TextureToVirtualPath(detail, ".thm")));
            }
            double scale = entry.Resolution.Description?.DetailScale ?? 1.0;
            entry.Terrain.DetailScale = !double.IsNaN(scale) && !double.IsInfinity(scale) && scale > 0.0 && scale <= float.MaxValue ? (float)scale : 1.0f;
        }

        private void AddTexture(XRayLevelSourceRefreshEntry entry, string property, XRayResolvedFile file)
        {
            if (file == null || !file.IsResolved)
            {
                entry.Dependencies.Add(new XRayLevelBindingDependency { Property = property, Source = file, Reason = "Optional texture missing; unsupported operation stays disabled" });
                return;
            }

            if (!_dependencies.TryGetValue(file.VirtualPath, out XRayLevelBindingDependency dependency))
            {
                dependency = new XRayLevelBindingDependency { Source = file };
                byte[] bytes = file.Winner.ReadAllBytes();
                dependency.SourceHash = HashBytes(bytes);
                try
                {
                    DdsHeader header = DdsHeader.Parse(bytes);
                    dependency.AssetPath = _importer.FindPacked(file, TextureFolder(file.VirtualPath), out bool current);
                    if (dependency.AssetPath != null)
                    {
                        dependency.FlipV = XRayLevelSourceBindings.PackedFlipV(dependency.AssetPath);
                        dependency.AssetHash = HashFile(dependency.AssetPath);
                        dependency.MetaHash = HashFile(dependency.AssetPath + ".meta");
                        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(dependency.AssetPath);
                        dependency.BlocksRefresh = !current || texture == null || texture.isDataSRGB;
                        dependency.Reason = dependency.BlocksRefresh ? "Existing shared packed texture is stale, invalid or sRGB; separate scoped texture update required" : "Reuse current linear packed asset and GUID";
                    }
                    else if (!DdsDecoder.CanDecode(header))
                    {
                        dependency.Reason = "Unsupported packed DDS format; unsupported operation stays disabled";
                    }
                    else
                    {
                        dependency.CreatesAsset = true;
                        dependency.Reason = "Import effective packed texture as PNG";
                    }
                }
                catch (FormatException exception)
                {
                    dependency.Reason = exception.Message;
                }

                _dependencies.Add(file.VirtualPath, dependency);
            }

            entry.Dependencies.Add(new XRayLevelBindingDependency { Property = property, Source = dependency.Source, SourceHash = dependency.SourceHash, AssetPath = dependency.AssetPath, AssetHash = dependency.AssetHash, MetaHash = dependency.MetaHash, CreatesAsset = dependency.CreatesAsset, BlocksRefresh = dependency.BlocksRefresh, Reason = dependency.Reason, FlipV = dependency.FlipV });
        }

        private string TextureFolder(string virtualPath)
        {
            string normalized = XRayVirtualPath.Normalize(virtualPath);
            if (normalized.StartsWith("textures/", StringComparison.Ordinal)) normalized = normalized.Substring("textures/".Length);
            string directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
            return _root + "/Textures" + (string.IsNullOrEmpty(directory) ? string.Empty : "/" + string.Join("/", Array.ConvertAll(directory.Split('/'), XRayLevelPaths.SanitizeName)));
        }

        private static string PathFor(XRayLevelSourceRefreshEntry entry, string property)
        {
            XRayLevelBindingDependency dependency = entry.Dependencies.Find(item => item.Property == property);
            return dependency != null && !dependency.BlocksRefresh ? dependency.AssetPath : null;
        }

        private static string ProjectedPath(XRayLevelSourceRefreshEntry entry, string property)
        {
            XRayLevelBindingDependency dependency = entry.Dependencies.Find(item => item.Property == property);
            return dependency != null && !dependency.BlocksRefresh ? dependency.AssetPath ?? (dependency.CreatesAsset ? "planned:" + dependency.Source.VirtualPath : null) : null;
        }

        private static string ProjectedValue(XRayLevelSourceRefreshEntry entry, XRayLevelBindingChange change)
        {
            XRayLevelSourceBinding binding = entry.ProjectedBinding;
            bool correction = ProjectedPresent(entry, XRayLevelSourceBindings.CORRECTION);
            bool detail = ProjectedPresent(entry, XRayLevelSourceBindings.DETAIL);
            bool detailBump = ProjectedPresent(entry, XRayLevelSourceBindings.DETAIL_BUMP) && ProjectedPresent(entry, XRayLevelSourceBindings.DETAIL_CORRECTION);
            switch (change.Property)
            {
                case XRayLevelSourceBindings.DETAIL_SCALE:
                    return (detail ? binding.DetailScale : 1.0f).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                case XRayLevelSourceBindings.RECOMMENDED_DETAIL:
                    return (detail ? Mathf.Min(binding.RecommendedDetail, detailBump ? 2 : 1) : 0).ToString();
                case XRayLevelSourceBindings.RECOMMENDED_PARALLAX:
                    return (correction ? binding.RecommendedParallax : 0).ToString();
                case XRayLevelSourceBindings.HAS_CORRECTION:
                    return correction && binding.CorrectionPath != null ? "1" : "0";
                case XRayLevelSourceBindings.HAS_DETAIL:
                    return detail && binding.DetailPath != null ? "1" : "0";
                case XRayLevelSourceBindings.HAS_DETAIL_BUMP:
                    return detailBump && binding.DetailBumpPath != null ? "1" : "0";
                case XRayLevelSourceBindings.DETAIL_FLIP:
                    return entry.Plan.Changes.Find(item => item.Property == XRayLevelSourceBindings.DETAIL).Ownership == XRayLevelBindingOwnership.UserOverride ? change.Desired : entry.Dependencies.Find(item => item.Property == XRayLevelSourceBindings.DETAIL)?.FlipV == true ? "1" : "0";
                default:
                    return change.Desired;
            }
        }

        private static bool ProjectedPresent(XRayLevelSourceRefreshEntry entry, string property)
        {
            XRayLevelBindingChange change = entry.Plan.Changes.Find(item => item.Property == property);
            if (change.Ownership == XRayLevelBindingOwnership.UserOverride)
            {
                return change.Final.Length > 0;
            }

            return ProjectedPath(entry, property) != null;
        }

        public static string HashFile(string path)
        {
            return HashBytes(File.ReadAllBytes(path));
        }

        public static string HashBytes(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static void RequireArchive(string folder)
        {
            string archive = Path.GetFullPath(XRayLevelPaths.ArchiveRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(folder).StartsWith(archive, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Reports and backups must be inside " + archive);
            }

            Directory.CreateDirectory(folder);
        }

        private static void Csv(StringBuilder builder, params object[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append('"').Append((values[i]?.ToString() ?? string.Empty).Replace("\"", "\"\"")).Append('"');
            }

            builder.Append('\n');
        }
    }
}

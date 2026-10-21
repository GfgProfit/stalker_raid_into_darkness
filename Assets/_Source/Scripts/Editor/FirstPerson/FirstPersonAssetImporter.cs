using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using XRay.Importer;
using XRay.Importer.Editor;
using XRay.Importer.Sources.Vfs;

namespace RaidIntoDarkness.FirstPerson.Editor
{
    public static class FirstPersonAssetImporter
    {
        public const string ROOT = "Assets/_Source/FirstPerson";
        public const string HANDS_MODELS = ROOT + "/Hands/Models";
        public const string BODY_MODELS = ROOT + "/Body/Models";
        public const string BODY_ANIMATIONS = ROOT + "/Animations/Body";
        public const string LIBRARY_PATH = BODY_ANIMATIONS + "/BodyAnimationLibrary.asset";
        public const string CATALOG_PATH = ROOT + "/Outfits/OutfitCatalog.asset";
        public const string RIG_PREFAB_PATH = ROOT + "/FPS_Player.prefab";
        public const string PREVIEW_SCENE_PATH = ROOT + "/Preview/FirstPersonAssetsPreview.unity";
        public const string REPORTS = ROOT + "/_Reports";

        private const string MENU = "Tools/X-Ray/Import First Person Assets";
        private const int MAX_SETTLE_PASSES = 4;
        private const float SAMPLE_EPSILON_DEGREES = 0.01f;

        private static readonly string[] BODY_MOTIONS = { "meshes/sm/actor_legs/lancew_legs_krovosos.omf", "meshes/sm/actor_legs/lancew_legs_anims.omf" };
        private static readonly string[] SAMPLED_MOTIONS = { "lancew_legs_idle", "lancew_legs_sprint", "leg_crouch_walk_fwd", "leg_ladder_up" };

        [MenuItem(MENU)]
        public static void ImportFromMenu()
        {
            if (!EditorUtility.DisplayDialog("Import First Person Assets", "Copy and import first person hands, bodies and body animations from the configured X-Ray sources into " + ROOT + "?", "Import", "Cancel"))
            {
                return;
            }

            bool success = Run();
            EditorUtility.DisplayDialog("Import First Person Assets", success ? "Import completed. See " + REPORTS + "." : "Import failed. See the console and " + REPORTS + ".", "OK");
        }

        public static void RunBatch()
        {
            bool success = false;
            try
            {
                success = Run();
            }
            catch (Exception exception)
            {
                Debug.LogError("[FirstPerson] Import failed: " + exception);
            }

            EditorApplication.Exit(success ? 0 : 1);
        }

        public static bool Run()
        {
            var report = new FirstPersonImportReportData { createdUtc = DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture) };
            var log = new List<string>();
            int errors = 0;
            int warnings = 0;
            Application.LogCallback callback = (message, stackTrace, type) =>
            {
                if (type == LogType.Warning)
                {
                    warnings++;
                }
                else if (type != LogType.Log)
                {
                    errors++;
                    log.Add(message);
                }
            };

            Application.logMessageReceived += callback;
            try
            {
                return Execute(report);
            }
            finally
            {
                Application.logMessageReceived -= callback;
                report.importErrors = errors;
                report.importWarnings = warnings;
                report.errors.AddRange(log.Take(200));
                WriteReports(report);
            }
        }

        private static bool Execute(FirstPersonImportReportData report)
        {
            XRayAssetSourceOptions options = XRayAssetSourceSettings.GetEffective();
            XRaySourceConfiguration configuration = CloneWithScope(options.Sources);
            XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(configuration);
            report.warnings.AddRange(plan.Warnings);
            if (!plan.IsValid)
            {
                report.errors.AddRange(plan.Errors);
                Debug.LogError("[FirstPerson] X-Ray asset sources are invalid: " + string.Join("; ", plan.Errors));
                return false;
            }

            XRayAssetIndex index = XRayAssetIndex.Build(plan);
            report.profile = plan.ProfileName;
            report.activeMods = plan.ActiveModCount;
            report.indexedFiles = index.Count;
            FirstPersonOutfitScan scan = FirstPersonOutfitScanner.Scan(index);
            report.warnings.AddRange(scan.Warnings);
            FillScanSummary(report, scan);

            var hands = CollectModels(scan.Outfits.Select(outfit => outfit.Hands));
            var bodies = CollectModels(scan.Outfits.Select(outfit => outfit.Body));
            report.uniqueHands = hands.Count;
            report.uniqueBodies = bodies.Count;
            Dictionary<string, string> handPaths = AssignAssetPaths(hands, HANDS_MODELS);
            Dictionary<string, string> bodyPaths = AssignAssetPaths(bodies, BODY_MODELS);
            var motionPaths = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string motion in BODY_MOTIONS)
            {
                motionPaths.Add(motion, BODY_ANIMATIONS + "/" + Path.GetFileName(motion));
            }

            var rows = new Dictionary<string, FirstPersonModelRow>(StringComparer.Ordinal);
            AssetDatabase.StartAssetEditing();
            try
            {
                CopyModels(index, hands, handPaths, "Hands", scan, rows);
                CopyModels(index, bodies, bodyPaths, "Body", scan, rows);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            DisableEmbeddedMotions(handPaths.Values);

            string rigPath = FindRigPath(scan, bodyPaths);
            CopyMotions(index, motionPaths, rows, report);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (rigPath == null)
            {
                Debug.LogError("[FirstPerson] The default body (actor_legs) was not imported; OMF files have no rig.");
                return false;
            }

            AssignRig(motionPaths.Values, rigPath);
            SettleResources(handPaths.Values.Concat(bodyPaths.Values).ToList(), options);

            foreach (FirstPersonModelRow row in rows.Values)
            {
                if (row.kind != "Animation")
                {
                    ValidateModel(row);
                }
            }

            report.models.AddRange(rows.Values.OrderBy(row => row.kind, StringComparer.Ordinal).ThenBy(row => row.assetPath, StringComparer.Ordinal));
            report.handsImported = report.models.Count(row => row.kind == "Hands" && row.imported);
            report.bodiesImported = report.models.Count(row => row.kind == "Body" && row.imported);

            List<XRayMotionLibrary> libraries = LoadLibraries(rigPath, motionPaths, report);
            report.animationsImported = libraries.Count(library => library != null && library.Motions.Count > 0);
            BodyAnimationLibrary library = BuildLibrary(scan, libraries, report);
            CheckCompatibility(bodyPaths, libraries, report);
            SampleBodies(scan, bodyPaths, libraries, rigPath, report);
            FirstPersonOutfitCatalog catalog = BuildCatalog(scan, handPaths, bodyPaths, rows, library, report);
            CreateRigPrefab(catalog, scan);
            CreatePreviewScene(handPaths.Values, bodyPaths.Values);
            AssetDatabase.SaveAssets();
            return report.handsImported == hands.Count && report.bodiesImported == bodies.Count && report.animationsImported == BODY_MOTIONS.Length;
        }

        private static XRaySourceConfiguration CloneWithScope(XRaySourceConfiguration source)
        {
            var configuration = new XRaySourceConfiguration
            {
                Mode = source.Mode,
                BaseAnomalyFolder = source.BaseAnomalyFolder,
                Mo2Folder = source.Mo2Folder,
                Mo2Instance = source.Mo2Instance,
                Mo2Profile = source.Mo2Profile,
                CustomFolder = source.CustomFolder,
                LocalAppDataFolder = source.LocalAppDataFolder,
                IncludeGameGamedata = source.IncludeGameGamedata,
                TreatModRootAsGamedata = source.TreatModRootAsGamedata
            };

            configuration.IndexScope.Clear();
            configuration.IndexScope.AddRange(FirstPersonOutfitScanner.INDEX_SCOPE);
            return configuration;
        }

        private static void FillScanSummary(FirstPersonImportReportData report, FirstPersonOutfitScan scan)
        {
            report.ltxSections = scan.SectionCount;
            report.dltxFiles = scan.ModFileCount;
            report.rusStrings = scan.StringCount;
            report.useHdModels = scan.UseHdModels;
            report.swmScriptSource = scan.ScriptSource + " :: " + scan.ScriptVirtualPath;
            report.outfitRows = scan.Outfits.Count;
            report.singlePlayerOutfits = scan.Outfits.Count(outfit => outfit.Section != FirstPersonOutfitScanner.ACTOR_SECTION);
            report.multiplayerExcluded = scan.ExcludedMultiplayer.Count;
            report.multiplayerSections = string.Join(", ", scan.ExcludedMultiplayer);
            report.exoskeletonOutfits = scan.Outfits.Count(outfit => outfit.IsExoskeleton);
            report.cameraHeight = scan.CameraHeight;
            report.cameraHeightOrigin = scan.CameraHeightOrigin;
            report.legsOffset = GetFloat(scan, "legs_offset", 0.0f);
            foreach (IGrouping<FirstPersonOutfitResolution, FirstPersonOutfitRecord> group in scan.Outfits.GroupBy(outfit => outfit.Resolution).OrderBy(group => group.Key))
            {
                report.resolutionCounts.Add(group.Key + ": " + group.Count());
            }
        }

        private static List<FirstPersonModelSource> CollectModels(IEnumerable<FirstPersonModelSource> sources)
        {
            var models = new Dictionary<string, FirstPersonModelSource>(StringComparer.Ordinal);
            foreach (FirstPersonModelSource source in sources)
            {
                if (source != null && source.Exists && !models.ContainsKey(source.VirtualPath))
                {
                    models.Add(source.VirtualPath, source);
                }
            }

            return models.Values.OrderBy(model => model.VirtualPath, StringComparer.Ordinal).ToList();
        }

        private static Dictionary<string, string> AssignAssetPaths(List<FirstPersonModelSource> models, string folder)
        {
            var paths = new Dictionary<string, string>(StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FirstPersonModelSource model in models)
            {
                string name = Path.GetFileNameWithoutExtension(model.VirtualPath);
                string candidate = name;
                for (int suffix = 2; !used.Add(candidate); suffix++)
                {
                    candidate = name + "_" + suffix;
                }

                paths.Add(model.VirtualPath, folder + "/" + candidate + ".ogf");
            }

            return paths;
        }

        private static void CopyModels(XRayAssetIndex index, List<FirstPersonModelSource> models, Dictionary<string, string> paths, string kind, FirstPersonOutfitScan scan, Dictionary<string, FirstPersonModelRow> rows)
        {
            foreach (FirstPersonModelSource model in models)
            {
                string assetPath = paths[model.VirtualPath];
                var row = new FirstPersonModelRow { kind = kind, virtualPath = model.VirtualPath, assetPath = assetPath };
                row.source = index.GetSource(model.File.Winner).Name;
                row.overriddenSources = string.Join(" | ", model.File.Candidates.Skip(1).Select(candidate => index.GetSource(candidate).Name));
                row.usedByOutfits = scan.Outfits.Count(outfit => (kind == "Hands" ? outfit.Hands : outfit.Body).VirtualPath == model.VirtualPath);
                row.copyStatus = CopyFile(index.GetFullPath(model.File.Winner), assetPath);
                rows[assetPath] = row;
            }
        }

        private static void CopyMotions(XRayAssetIndex index, Dictionary<string, string> motionPaths, Dictionary<string, FirstPersonModelRow> rows, FirstPersonImportReportData report)
        {
            foreach (KeyValuePair<string, string> pair in motionPaths)
            {
                var row = new FirstPersonModelRow { kind = "Animation", virtualPath = pair.Key, assetPath = pair.Value };
                if (!index.TryGet(pair.Key, out XRayIndexedFile file))
                {
                    row.copyStatus = "SourceMissing";
                    report.errors.Add($"Body animation '{pair.Key}' is missing in the active sources.");
                    rows[pair.Value] = row;
                    continue;
                }

                row.source = index.GetSource(file.Winner).Name;
                row.overriddenSources = string.Join(" | ", file.Candidates.Skip(1).Select(candidate => index.GetSource(candidate).Name));
                row.copyStatus = CopyFile(index.GetFullPath(file.Winner), pair.Value);
                rows[pair.Value] = row;
            }
        }

        private static string CopyFile(string sourcePath, string assetPath)
        {
            string fullPath = Path.GetFullPath(assetPath);
            byte[] data = File.ReadAllBytes(sourcePath);
            if (File.Exists(fullPath))
            {
                if (File.ReadAllBytes(fullPath).SequenceEqual(data))
                {
                    return "Unchanged";
                }

                File.WriteAllBytes(fullPath, data);
                return "Updated";
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllBytes(fullPath, data);
            return "Copied";
        }

        private static void DisableEmbeddedMotions(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                bool changed = SetImporterBool(path, "_importEmbeddedMotions", false);
                changed |= SetImporterBool(path, "_updateWhenOffscreen", true);

                if (changed)
                {
                    AssetImporter.GetAtPath(path).SaveAndReimport();
                }
            }
        }

        private static string FindRigPath(FirstPersonOutfitScan scan, Dictionary<string, string> bodyPaths)
        {
            FirstPersonOutfitRecord actor = scan.Outfits.FirstOrDefault(outfit => outfit.Section == FirstPersonOutfitScanner.ACTOR_SECTION);
            if (actor == null || !actor.Body.Exists || !bodyPaths.TryGetValue(actor.Body.VirtualPath, out string path))
            {
                return null;
            }

            return path;
        }

        private static void AssignRig(IEnumerable<string> motionPaths, string rigPath)
        {
            string rigGuid = AssetDatabase.AssetPathToGUID(rigPath);
            foreach (string path in motionPaths)
            {
                var importer = AssetImporter.GetAtPath(path) as XRayOmfImporter;
                if (importer == null)
                {
                    continue;
                }

                var serialized = new SerializedObject(importer);
                SerializedProperty rig = serialized.FindProperty("_rigGuid");
                SerializedProperty autoDetect = serialized.FindProperty("_autoDetectRig");
                if (rig.stringValue == rigGuid && !autoDetect.boolValue)
                {
                    continue;
                }

                rig.stringValue = rigGuid;
                autoDetect.boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                importer.SaveAndReimport();
            }
        }

        private static bool SetImporterBool(string path, string property, bool value)
        {
            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (importer == null)
            {
                return false;
            }

            var serialized = new SerializedObject(importer);
            SerializedProperty field = serialized.FindProperty(property);
            if (field == null || field.boolValue == value)
            {
                return false;
            }

            field.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static void SettleResources(List<string> models, XRayAssetSourceOptions options)
        {
            for (int pass = 0; pass < MAX_SETTLE_PASSES; pass++)
            {
                List<string> pending = models.Where(model => XRayAssetGenerationCoordinator.NeedsGeneration(model, options)).ToList();
                if (pending.Count == 0)
                {
                    return;
                }

                foreach (string model in pending)
                {
                    XRayAssetGenerationCoordinator.Process(model, options, false, out XRayResourceReport _);
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            int remaining = models.Count(model => XRayAssetGenerationCoordinator.NeedsGeneration(model, options));
            if (remaining > 0)
            {
                Debug.LogWarning($"[FirstPerson] {remaining} models still have unassigned material slots after {MAX_SETTLE_PASSES} passes.");
            }
        }

        private static void ValidateModel(FirstPersonModelRow row)
        {
            var problems = new List<string>();
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(row.assetPath);
            XRayModel model = root != null ? root.GetComponent<XRayModel>() : null;
            if (model == null)
            {
                row.problems = "Model was not imported.";
                return;
            }

            row.imported = true;
            row.bones = model.BoneNames.Count;
            row.vertices = model.VertexCount;
            row.triangles = model.TriangleCount;
            row.missingBones = model.Bones.Count(bone => bone == null);
            row.motionReferences = string.Join(", ", model.MotionReferences);
            row.embeddedMotionsImported = model.EmbeddedMotions != null;
            var importer = AssetImporter.GetAtPath(row.assetPath) as XRayOgfImporter;
            row.embeddedMotionsPresent = HasEmbeddedMotions(row.assetPath);
            string materialsFolder = Path.GetDirectoryName(row.assetPath).Replace('\\', '/') + "/" + XRayAssetGenerationCoordinator.MATERIALS_FOLDER + "/";
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                row.skinnedRenderers++;
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null)
                {
                    problems.Add(renderer.name + ": no mesh");
                    continue;
                }

                if (mesh.bindposeCount != renderer.bones.Length || renderer.bones.Any(bone => bone == null))
                {
                    row.bindposeMismatches++;
                }

                CountMaterials(row, renderer, materialsFolder);
            }

            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                row.meshRenderers++;
                CountMaterials(row, renderer, materialsFolder);
            }

            XRayResourceReport resources = XRayResourceReportStore.Load(AssetDatabase.AssetPathToGUID(row.assetPath));
            if (resources != null)
            {
                row.texturesResolved = resources.CountResolvedTextures();
                row.texturesMissing = resources.CountTextures(XRayResourceStatus.Missing);
            }

            if (row.skinnedRenderers == 0)
            {
                problems.Add("no skinned renderers");
            }

            if (row.bindposeMismatches > 0)
            {
                problems.Add(row.bindposeMismatches + " renderers with bindpose/bone mismatch");
            }

            if (row.fallbackMaterials > 0)
            {
                problems.Add(row.fallbackMaterials + " fallback materials");
            }

            if (row.texturesMissing > 0)
            {
                problems.Add(row.texturesMissing + " missing textures");
            }

            if (importer != null && row.kind == "Hands" && row.embeddedMotionsImported)
            {
                problems.Add("embedded motions were imported");
            }

            row.problems = string.Join("; ", problems);
        }

        private static bool HasEmbeddedMotions(string assetPath)
        {
            try
            {
                return XRay.Importer.Core.Ogf.OgfReader.ReadFile(File.ReadAllBytes(assetPath), Path.GetFileName(assetPath)).EmbeddedMotions != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void CountMaterials(FirstPersonModelRow row, Renderer renderer, string materialsFolder)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                row.materials++;
                string path = material != null ? AssetDatabase.GetAssetPath(material) : null;
                if (path != null && path.StartsWith(materialsFolder, StringComparison.Ordinal))
                {
                    row.generatedMaterials++;
                }
                else
                {
                    row.fallbackMaterials++;
                }

                if (material == null || !material.HasProperty("_BaseColorMap") || material.GetTexture("_BaseColorMap") == null)
                {
                    row.materialsWithoutBaseColor++;
                }
            }
        }

        private static List<XRayMotionLibrary> LoadLibraries(string rigPath, Dictionary<string, string> motionPaths, FirstPersonImportReportData report)
        {
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(rigPath).GetComponent<XRayModel>();
            var byName = motionPaths.Values.ToDictionary(path => Path.GetFileNameWithoutExtension(path), path => AssetDatabase.LoadAssetAtPath<XRayMotionLibrary>(path), StringComparer.OrdinalIgnoreCase);
            var ordered = new List<XRayMotionLibrary>();
            foreach (string reference in rig.MotionReferences)
            {
                string name = Path.GetFileNameWithoutExtension(reference.Replace('\\', '/'));
                if (byName.TryGetValue(name, out XRayMotionLibrary library) && library != null && !ordered.Contains(library))
                {
                    ordered.Add(library);
                }
                else if (!byName.ContainsKey(name))
                {
                    report.warnings.Add($"Rig '{rigPath}' references '{reference}', which is not part of this import.");
                }
            }

            foreach (XRayMotionLibrary library in byName.Values)
            {
                if (library != null && !ordered.Contains(library))
                {
                    ordered.Add(library);
                }
            }

            report.clipsInOmf = ordered.Sum(library => library.Motions.Count(motion => motion.Clip != null));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (XRayMotionLibrary library in ordered)
            {
                foreach (XRayMotionEntry motion in library.Motions)
                {
                    if (!seen.Add(motion.Name))
                    {
                        report.duplicateMotionsAcrossOmf++;
                    }
                }
            }

            return ordered;
        }

        private static BodyAnimationBinding Bind(string table, int flags, string motion, List<XRayMotionLibrary> libraries, FirstPersonImportReportData report)
        {
            if (string.IsNullOrEmpty(motion))
            {
                return null;
            }

            foreach (XRayMotionLibrary library in libraries)
            {
                if (library.TryGetClip(motion, out AnimationClip clip))
                {
                    report.motions.Add(new FirstPersonMotionRow { table = table, flags = flags, motion = motion, clipSource = library.SourceFile, found = true });
                    return new BodyAnimationBinding(flags, motion, clip, library.SourceFile);
                }
            }

            report.motions.Add(new FirstPersonMotionRow { table = table, flags = flags, motion = motion, found = false });
            report.warnings.Add($"{table}[{flags}]: motion '{motion}' is missing in the body OMF files.");
            return new BodyAnimationBinding(flags, motion, null, null);
        }

        private static BodyAnimationLibrary BuildLibrary(FirstPersonOutfitScan scan, List<XRayMotionLibrary> libraries, FirstPersonImportReportData report)
        {
            FirstPersonSwmScript script = scan.Script;
            var tables = new Dictionary<string, List<BodyAnimationBinding>>(StringComparer.Ordinal);
            foreach (string table in new[] { FirstPersonSwmScript.NON_COMBAT_TABLE, FirstPersonSwmScript.SLOWED_DOWN_TABLE, FirstPersonSwmScript.BASIC_TABLE })
            {
                tables[table] = script.Tables[table].Select(entry => Bind(table, entry.Flags, entry.Motion, libraries, report)).ToList();
            }

            int ladder = FirstPersonSwmScript.LADDER_FLAG;
            BodyAnimationBinding ladderIdle = Bind("Ladder.Idle", ladder, script.Find(FirstPersonSwmScript.BASIC_TABLE, ladder), libraries, report);
            BodyAnimationBinding ladderUp = Bind("Ladder.Up", ladder + FirstPersonSwmScript.FORWARD_FLAG, script.Find(FirstPersonSwmScript.BASIC_TABLE, ladder + FirstPersonSwmScript.FORWARD_FLAG), libraries, report);
            BodyAnimationBinding ladderDown = Bind("Ladder.Down", ladder + FirstPersonSwmScript.BACKWARD_FLAG, script.Find(FirstPersonSwmScript.BASIC_TABLE, ladder + FirstPersonSwmScript.BACKWARD_FLAG), libraries, report);
            BodyAnimationBinding fallback = Bind("Basic.Fallback", 0, script.BasicFallback, libraries, report);
            BodyAnimationBinding turnLeft = Bind("Turn.Left", 0, script.TurnLeft, libraries, report);
            BodyAnimationBinding turnRight = Bind("Turn.Right", 0, script.TurnRight, libraries, report);
            var idles = script.Idles.Select(idle => new BodyIdleAnimation(Bind("Idle", 0, idle.Key, libraries, report), idle.Value)).ToList();
            report.tableEntries = report.motions.Count;
            report.tableEntriesMissing = report.motions.Count(motion => !motion.found);

            var library = AssetDatabase.LoadAssetAtPath<BodyAnimationLibrary>(LIBRARY_PATH);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<BodyAnimationLibrary>();
                AssetDatabase.CreateAsset(library, LIBRARY_PATH);
            }

            string source = scan.ScriptSource + " :: " + scan.ScriptVirtualPath;
            library.SetImportData(source, libraries, tables[FirstPersonSwmScript.NON_COMBAT_TABLE], tables[FirstPersonSwmScript.SLOWED_DOWN_TABLE], tables[FirstPersonSwmScript.BASIC_TABLE], fallback, ladderIdle, ladderUp, ladderDown, turnLeft, turnRight, idles, script.DisabledFlags, FirstPersonOutfitScanner.GetBool(scan, "use_combat_state", true), FirstPersonOutfitScanner.GetBool(scan, "use_slow_down_state", true), FirstPersonOutfitScanner.GetBool(scan, "enable_idle_anims", true));
            EditorUtility.SetDirty(library);
            return library;
        }

        private static void CheckCompatibility(Dictionary<string, string> bodyPaths, List<XRayMotionLibrary> libraries, FirstPersonImportReportData report)
        {
            var animatedBones = new HashSet<string>(libraries.SelectMany(library => library.BoneNames), StringComparer.Ordinal);
            var curvePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (XRayMotionLibrary library in libraries)
            {
                foreach (XRayMotionEntry motion in library.Motions)
                {
                    if (motion.Clip != null)
                    {
                        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(motion.Clip))
                        {
                            curvePaths.Add(binding.path);
                        }
                    }
                }
            }

            foreach (string path in bodyPaths.Values.OrderBy(path => path, StringComparer.Ordinal))
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                XRayModel model = root != null ? root.GetComponent<XRayModel>() : null;
                if (model == null)
                {
                    continue;
                }

                var bones = new HashSet<string>(model.BoneNames, StringComparer.Ordinal);
                List<string> missing = animatedBones.Where(bone => !bones.Contains(bone)).OrderBy(bone => bone, StringComparer.Ordinal).ToList();
                List<string> extra = bones.Where(bone => !animatedBones.Contains(bone)).OrderBy(bone => bone, StringComparer.Ordinal).ToList();
                var row = new FirstPersonCompatibilityRow { body = Path.GetFileNameWithoutExtension(path), bodyBones = bones.Count, animatedBonesMissingInBody = missing.Count, bodyBonesNotAnimated = extra.Count, missingBoneNames = string.Join(" ", missing), extraBoneNames = string.Join(" ", extra) };
                row.curvePathsMissing = curvePaths.Count(curvePath => curvePath.Length > 0 && root.transform.Find(curvePath) == null);
                report.compatibility.Add(row);
            }
        }

        private static void SampleBodies(FirstPersonOutfitScan scan, Dictionary<string, string> bodyPaths, List<XRayMotionLibrary> libraries, string rigPath, FirstPersonImportReportData report)
        {
            var targets = new List<string> { rigPath };
            FirstPersonOutfitRecord exo = scan.Outfits.FirstOrDefault(outfit => outfit.IsExoskeleton && outfit.Resolution == FirstPersonOutfitResolution.Resolved);
            FirstPersonOutfitRecord normal = scan.Outfits.FirstOrDefault(outfit => !outfit.IsExoskeleton && outfit.Section != FirstPersonOutfitScanner.ACTOR_SECTION && outfit.Resolution == FirstPersonOutfitResolution.Resolved);
            foreach (FirstPersonOutfitRecord record in new[] { normal, exo })
            {
                if (record != null && bodyPaths.TryGetValue(record.Body.VirtualPath, out string path) && !targets.Contains(path))
                {
                    targets.Add(path);
                }
            }

            var reference = new Dictionary<string, Dictionary<string, Quaternion>>(StringComparer.Ordinal);
            foreach (string target in targets)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(target);
                if (asset == null)
                {
                    continue;
                }

                GameObject instance = UnityEngine.Object.Instantiate(asset);
                instance.hideFlags = HideFlags.HideAndDontSave;
                try
                {
                    XRayModel model = instance.GetComponent<XRayModel>();
                    var restPositions = model.Bones.Select(bone => bone != null ? bone.localPosition : Vector3.zero).ToArray();
                    var restRotations = model.Bones.Select(bone => bone != null ? bone.localRotation : Quaternion.identity).ToArray();
                    foreach (string motion in SAMPLED_MOTIONS)
                    {
                        AnimationClip clip = null;
                        foreach (XRayMotionLibrary library in libraries)
                        {
                            if (library.TryGetClip(motion, out clip))
                            {
                                break;
                            }
                        }

                        if (clip == null)
                        {
                            continue;
                        }

                        float time = clip.length * 0.5f;
                        clip.SampleAnimation(instance, time);
                        var row = new FirstPersonSampleRow { body = Path.GetFileNameWithoutExtension(target), motion = motion, time = time };
                        var pose = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
                        for (int i = 0; i < model.Bones.Count; i++)
                        {
                            Transform bone = model.Bones[i];
                            if (bone == null)
                            {
                                continue;
                            }

                            Vector3 position = bone.localPosition;
                            Quaternion rotation = bone.localRotation;
                            if (!IsFinite(position) || !IsFinite(rotation))
                            {
                                row.nonFiniteTransforms++;
                                continue;
                            }

                            pose[model.BoneNames[i]] = rotation;
                            float angle = Quaternion.Angle(restRotations[i], rotation);
                            float offset = Vector3.Distance(restPositions[i], position);
                            if (angle > SAMPLE_EPSILON_DEGREES || offset > 1e-5f)
                            {
                                row.drivenTransforms++;
                            }

                            row.maxRotationFromRestDegrees = Mathf.Max(row.maxRotationFromRestDegrees, angle);
                            row.maxPositionFromRest = Mathf.Max(row.maxPositionFromRest, offset);
                        }

                        if (reference.TryGetValue(motion, out Dictionary<string, Quaternion> basePose))
                        {
                            foreach (KeyValuePair<string, Quaternion> pair in pose)
                            {
                                if (basePose.TryGetValue(pair.Key, out Quaternion other))
                                {
                                    row.maxRotationDifferenceToReferenceDegrees = Mathf.Max(row.maxRotationDifferenceToReferenceDegrees, Quaternion.Angle(other, pair.Value));
                                }
                            }
                        }
                        else
                        {
                            reference.Add(motion, pose);
                        }

                        report.samples.Add(row);
                        for (int i = 0; i < model.Bones.Count; i++)
                        {
                            if (model.Bones[i] != null)
                            {
                                model.Bones[i].localPosition = restPositions[i];
                                model.Bones[i].localRotation = restRotations[i];
                            }
                        }
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z) && float.IsFinite(value.w);
        }

        private static FirstPersonOutfitCatalog BuildCatalog(FirstPersonOutfitScan scan, Dictionary<string, string> handPaths, Dictionary<string, string> bodyPaths, Dictionary<string, FirstPersonModelRow> rows, BodyAnimationLibrary library, FirstPersonImportReportData report)
        {
            var entries = new List<FirstPersonOutfitEntry>();
            foreach (FirstPersonOutfitRecord outfit in scan.Outfits)
            {
                string handsPath = outfit.Hands.Exists ? handPaths[outfit.Hands.VirtualPath] : null;
                string bodyPath = outfit.Body.Exists ? bodyPaths[outfit.Body.VirtualPath] : null;
                GameObject handsModel = handsPath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(handsPath) : null;
                GameObject bodyModel = bodyPath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(bodyPath) : null;
                string handsSource = handsPath != null ? rows[handsPath].source : null;
                string bodySource = bodyPath != null ? rows[bodyPath].source : null;
                entries.Add(new FirstPersonOutfitEntry(outfit.Section, outfit.DisplayName, outfit.HudSection, outfit.Hands.Visual, handsModel, handsSource, outfit.LegsSection, outfit.Body.Visual, bodyModel, bodySource, library, outfit.IsExoskeleton, outfit.Resolution));
                report.outfits.Add(new FirstPersonOutfitRow { section = outfit.Section, displayName = outfit.DisplayName, hudSection = outfit.HudSection, handsVirtualPath = outfit.Hands.Visual, handsAsset = handsModel != null ? handsPath : null, handsSource = handsSource, legsSection = outfit.LegsSection, bodyVirtualPath = outfit.Body.Visual, bodyAsset = bodyModel != null ? bodyPath : null, bodySource = bodySource, isExoskeleton = outfit.IsExoskeleton, repairType = outfit.RepairType, resolution = outfit.Resolution.ToString() });
            }

            var catalog = AssetDatabase.LoadAssetAtPath<FirstPersonOutfitCatalog>(CATALOG_PATH);
            if (catalog == null)
            {
                EnsureFolder(Path.GetDirectoryName(CATALOG_PATH));
                catalog = ScriptableObject.CreateInstance<FirstPersonOutfitCatalog>();
                AssetDatabase.CreateAsset(catalog, CATALOG_PATH);
            }

            catalog.SetEntries(entries);
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        private static void CreateRigPrefab(FirstPersonOutfitCatalog catalog, FirstPersonOutfitScan scan)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(RIG_PREFAB_PATH) != null)
            {
                return;
            }

            var root = new GameObject("FPS_Player");
            try
            {
                var cameraRoot = new GameObject("CameraRoot").transform;
                cameraRoot.SetParent(root.transform, false);
                cameraRoot.localPosition = new Vector3(0.0f, scan.CameraHeight, 0.0f);
                var camera = new GameObject("Camera", typeof(Camera)).transform;
                camera.SetParent(cameraRoot, false);
                camera.GetComponent<Camera>().nearClipPlane = 0.05f;
                var handsRoot = new GameObject("HandsRoot").transform;
                handsRoot.SetParent(cameraRoot, false);
                var bodyRoot = new GameObject("BodyRoot").transform;
                bodyRoot.SetParent(root.transform, false);
                bodyRoot.localPosition = new Vector3(0.0f, 0.0f, GetFloat(scan, "legs_offset", 0.0f));
                FirstPersonRig rig = root.AddComponent<FirstPersonRig>();
                var serialized = new SerializedObject(rig);
                serialized.FindProperty("_catalog").objectReferenceValue = catalog;
                serialized.FindProperty("_handsRoot").objectReferenceValue = handsRoot;
                serialized.FindProperty("_bodyRoot").objectReferenceValue = bodyRoot;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, RIG_PREFAB_PATH);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        private static float GetFloat(FirstPersonOutfitScan scan, string key, float fallback)
        {
            return scan.SwmOptions.TryGetValue(key, out string value) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) ? result : fallback;
        }

        private static void CreatePreviewScene(IEnumerable<string> hands, IEnumerable<string> bodies)
        {
            if (File.Exists(PREVIEW_SCENE_PATH))
            {
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[FirstPerson] Preview scene was not created: modified scenes were not saved.");
                return;
            }

            EnsureFolder(Path.GetDirectoryName(PREVIEW_SCENE_PATH));
            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            try
            {
                var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RIG_PREFAB_PATH);
                if (rigPrefab != null)
                {
                    var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
                    rig.transform.position = new Vector3(0.0f, 0.0f, -3.0f);
                }

                PlaceRow(scene, "Bodies", bodies, 0.0f);
                PlaceRow(scene, "Hands", hands, 3.0f);
                EditorSceneManager.SaveScene(scene, PREVIEW_SCENE_PATH);
            }
            finally
            {
                if (setup.Length > 0 && setup.All(item => !string.IsNullOrEmpty(item.path)))
                {
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                }
            }
        }

        private static void PlaceRow(UnityEngine.SceneManagement.Scene scene, string title, IEnumerable<string> models, float z)
        {
            const int COLUMNS = 20;
            const float SPACING = 1.2f;
            var group = new GameObject(title);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(group, scene);
            int index = 0;
            foreach (string path in models.OrderBy(path => path, StringComparer.Ordinal))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null)
                {
                    continue;
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
                instance.transform.SetParent(group.transform, false);
                instance.transform.localPosition = new Vector3(index % COLUMNS * SPACING, 0.0f, z + index / COLUMNS * SPACING * 2.0f);
                index++;
            }
        }

        private static void WriteReports(FirstPersonImportReportData report)
        {
            string folder = Path.GetFullPath(REPORTS);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "FirstPersonImportReport.json"), JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "Outfits.csv"), FirstPersonReportWriter.OutfitsCsv(report), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(folder, "Models.csv"), FirstPersonReportWriter.ModelsCsv(report), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(folder, "FirstPersonImportReport.md"), FirstPersonReportWriter.Markdown(report), new UTF8Encoding(false));
            AssetDatabase.Refresh();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using RaidIntoDarkness.FirstPerson;
using RaidIntoDarkness.Integrations.Inventory;
using RaidIntoDarkness.Player;
using RaidIntoDarkness.Player.Editor;
using RaidIntoDarkness.Weapons;
using UnityEditor;
using UnityEngine;
using XRay.Importer;
using XRay.Importer.Core.Ogf;
using XRay.Importer.Core.Omf;
using XRay.Importer.Editor;
using XRay.Importer.Sources.Ltx;

namespace RaidIntoDarkness.FirstPerson.Editor
{
    public static class BoltAssetSetup
    {
        public const string ROOT = "Assets/_Source/FirstPerson/Bolt";
        public const string DEFINITION_PATH = ROOT + "/BoltDefinition.asset";
        public const string PROJECTILE_PATH = ROOT + "/BoltProjectile.prefab";

        private const string WORLD_SOURCE = "meshes/dynamics/devices/dev_bolt/dev_bolt.ogf";
        private const string HUD_SOURCE = "meshes/dynamics/devices/dev_bolt/dev_bolt_hud.ogf";
        private const string HANDS_SOURCE = "meshes/anomaly_weapons/hud_hands_animation/dev_hand_bolt_hud_animation.omf";
        private const string ITEM_SOURCE = "meshes/dynamics/devices/dev_bolt/dev_bolt_hud_animation.omf";
        private const string WORLD_PATH = ROOT + "/Models/dev_bolt.ogf";
        private const string HUD_PATH = ROOT + "/Models/dev_bolt_hud.ogf";
        private const string HANDS_PATH = ROOT + "/Animations/dev_hand_bolt_hud_animation.omf";
        private const string ITEM_PATH = ROOT + "/Animations/dev_bolt_hud_animation.omf";
        private const string COLLIDE_PATH = ROOT + "/BoltCollideSounds.asset";
        private const string REPORT_FOLDER = "E:/Projects/stalker_raid_into_darkness_Archive/Bolt_20261108_01";

        private static readonly string[] SCOPE = { "configs", "materials", "meshes/dynamics/devices/dev_bolt", "meshes/anomaly_weapons/hud_hands_animation", "sounds/weapons", "sounds/material", StalkerGameMtlLibrary.GAMEMTL };
        private static readonly string[] MOTION_FIELDS = { "_draw", "_holster", "_idle", "_moving", "_sprint", "_bore", "_throwBegin", "_throwIdle", "_throwEnd" };
        private static readonly string[] MOTION_KEYS = { "anm_show", "anm_hide", "anm_idle", "anm_idle_moving", "anm_idle_sprint", "anm_bore", "anm_throw_begin", "anm_throw_idle", "anm_throw" };

        [MenuItem("Tools/X-Ray/Setup Bolt")]
        public static void SetupFromMenu()
        {
            Setup();
        }

        public static bool Setup()
        {
            var report = new StringBuilder();
            string reportPath = Path.Combine(REPORT_FOLDER, "asset-validation-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".txt");
            try
            {
                if (Application.isPlaying || !string.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/', '\\'), "E:\\Projects\\stalker_raid_into_darkness\\Assets", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Bolt setup requires the Production project in Edit Mode.");
                }

                StalkerXRaySources sources = StalkerXRaySources.Open(SCOPE, out string error);
                if (sources == null)
                {
                    throw new InvalidOperationException(error);
                }

                report.AppendLine(sources.Describe());
                XRayLtxDatabase system = XRayLtxDatabase.LoadSystem(sources.Index);
                Copy(sources, WORLD_SOURCE, WORLD_PATH, report);
                Copy(sources, HUD_SOURCE, HUD_PATH, report);
                Copy(sources, HANDS_SOURCE, HANDS_PATH, report);
                Copy(sources, ITEM_SOURCE, ITEM_PATH, report);
                Copy(sources, "sounds/weapons/bolt_draw.ogg", ROOT + "/Sounds/bolt_draw.ogg", report);
                Copy(sources, "sounds/weapons/bolt_holster.ogg", ROOT + "/Sounds/bolt_holster.ogg", report);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                AssignRig(HANDS_PATH, FirstPersonEquipmentAnimationSetup.HANDS_RIG_PATH);
                AssignRig(ITEM_PATH, HUD_PATH);
                SettleResources();
                var definition = AssetDatabase.LoadAssetAtPath<BoltDefinition>(DEFINITION_PATH);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<BoltDefinition>();
                    AssetDatabase.CreateAsset(definition, DEFINITION_PATH);
                }

                var serialized = new SerializedObject(definition);
                Set(serialized, "_hudModel", AssetDatabase.LoadAssetAtPath<GameObject>(HUD_PATH));
                Set(serialized, "_handsMotions", AssetDatabase.LoadAssetAtPath<XRayMotionLibrary>(HANDS_PATH));
                Set(serialized, "_itemMotions", AssetDatabase.LoadAssetAtPath<XRayMotionLibrary>(ITEM_PATH));
                serialized.FindProperty("_handsPosition").vector3Value = Vector(system.GetValue("bolt_hud", "hands_position_16x9"));
                serialized.FindProperty("_hudFieldOfView").floatValue = Number(system.GetValue("bolt", "hud_fov"));
                var handsClips = new List<AnimationClip>();
                for (int i = 0; i < MOTION_FIELDS.Length; i++)
                {
                    string motion = system.GetValue("bolt_hud", MOTION_KEYS[i]).Split(',')[0].Trim();
                    AnimationClip clip = Clip(HANDS_PATH, motion);
                    Set(serialized, MOTION_FIELDS[i], clip);
                    handsClips.Add(clip);
                    report.AppendLine($"{MOTION_KEYS[i]} = {motion}: {clip.length.ToString(CultureInfo.InvariantCulture)} seconds");
                }

                AnimationClip itemIdle = Clip(ITEM_PATH, "dev_bolt_idle");
                AnimationClip drawAlternate = Clip(HANDS_PATH, "dev_bolt_draw1");
                Set(serialized, "_drawAlternate", drawAlternate);
                handsClips.Add(drawAlternate);
                AnimationClip itemFallback = Clip(ITEM_PATH, "idle");
                AnimationClip itemDraw = Clip(ITEM_PATH, "dev_bolt_draw");
                AnimationClip itemDrawAlternate = Clip(ITEM_PATH, "dev_bolt_draw1");
                AnimationClip itemBore = Clip(ITEM_PATH, "dev_bolt_idle_bore");
                Set(serialized, "_itemIdle", itemIdle);
                Set(serialized, "_itemFallback", itemFallback);
                Set(serialized, "_itemDraw", itemDraw);
                Set(serialized, "_itemDrawAlternate", itemDrawAlternate);
                Set(serialized, "_itemBore", itemBore);
                Set(serialized, "_drawSound", AssetDatabase.LoadAssetAtPath<AudioClip>(ROOT + "/Sounds/bolt_draw.ogg"));
                Set(serialized, "_holsterSound", AssetDatabase.LoadAssetAtPath<AudioClip>(ROOT + "/Sounds/bolt_holster.ogg"));
                serialized.FindProperty("_throwReleaseTime").floatValue = ReleaseTime(sources, Clip(HANDS_PATH, "dev_bolt_shoot_end").length, report);
                OgfModel sourceModel = OgfReader.ReadFile(sources.ReadBytes(WORLD_SOURCE), WORLD_SOURCE);
                float mass = 0.0f;
                string material = null;
                foreach (OgfBone bone in sourceModel.Bones)
                {
                    mass += (float)bone.Mass;
                    material = material ?? bone.GameMaterial;
                    report.AppendLine($"Physics bone {bone.Name}: mass={bone.Mass}, material={bone.GameMaterial}, shape={bone.ShapeType}");
                }

                if (mass <= 0.0f || string.IsNullOrEmpty(material))
                {
                    throw new InvalidOperationException("Bolt OGF has no source mass or material.");
                }

                StalkerCollideSoundLibrary sounds = BuildCollisionSounds(sources, system, material, report);
                Set(serialized, "_projectilePrefab", BuildProjectile(mass, material, sounds));
                serialized.ApplyModifiedPropertiesWithoutUndo();
                ValidateHands(handsClips, report);
                GameObject hud = AssetDatabase.LoadAssetAtPath<GameObject>(HUD_PATH);
                ValidateBinding(itemIdle, hud);
                ValidateBinding(itemFallback, hud);
                ValidateBinding(itemDraw, hud);
                ValidateBinding(itemDrawAlternate, hud);
                ValidateBinding(itemBore, hud);
                ConfigurePlayer(definition);
                ConfigureAnomalyProbes();
                EditorUtility.SetDirty(definition);
                AssetDatabase.SaveAssetIfDirty(definition);
                AssetDatabase.SaveAssetIfDirty(sounds);
                report.AppendLine("PASS: source models, clips, material collisions, player prefab and animation bindings.");
                Debug.Log("[Bolt] Setup completed.");
                return true;
            }
            catch (Exception exception)
            {
                report.AppendLine("FAIL: " + exception);
                Debug.LogError("[Bolt] " + exception);
                return false;
            }
            finally
            {
                Directory.CreateDirectory(REPORT_FOLDER);
                File.WriteAllText(reportPath, report.ToString());
            }
        }

        private static void Copy(StalkerXRaySources sources, string source, string destination, StringBuilder report)
        {
            string copied = sources.CopyToProject(source, Path.GetDirectoryName(destination), Path.GetFileName(destination));
            if (copied == null)
            {
                throw new FileNotFoundException("GRA source is missing: " + source);
            }

            report.AppendLine(source + " <- " + sources.GetSourceName(source) + " -> " + destination);
        }

        private static void AssignRig(string path, string rigPath)
        {
            var importer = AssetImporter.GetAtPath(path) as XRayOmfImporter;
            if (importer == null)
            {
                throw new InvalidOperationException("OMF importer is unavailable: " + path);
            }

            var serialized = new SerializedObject(importer);
            serialized.FindProperty("_rigGuid").stringValue = AssetDatabase.AssetPathToGUID(rigPath);
            serialized.FindProperty("_autoDetectRig").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            importer.SaveAndReimport();
        }

        private static void SettleResources()
        {
            XRayAssetSourceOptions options = XRayAssetSourceSettings.GetEffective();
            for (int pass = 0; pass < 4; pass++)
            {
                bool pending = false;
                foreach (string model in new[] { WORLD_PATH, HUD_PATH })
                {
                    if (XRayAssetGenerationCoordinator.NeedsGeneration(model, options))
                    {
                        pending = true;
                        XRayAssetGenerationCoordinator.Process(model, options, false, out _);
                    }
                }

                if (!pending)
                {
                    return;
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            throw new InvalidOperationException("Bolt material generation did not settle.");
        }

        private static AnimationClip Clip(string path, string name)
        {
            var library = AssetDatabase.LoadAssetAtPath<XRayMotionLibrary>(path);
            if (library == null || !library.TryGetClip(name, out AnimationClip clip))
            {
                throw new InvalidOperationException("Animation is missing: " + path + " / " + name);
            }

            return clip;
        }

        private static float ReleaseTime(StalkerXRaySources sources, float duration, StringBuilder report)
        {
            OmfData data = OmfReader.ReadFile(sources.ReadBytes(HANDS_SOURCE), HANDS_SOURCE);
            OmfMotion motion = data.FindMotion("dev_bolt_shoot_end");
            float release = duration;
            if (motion.Params != null)
            {
                foreach (OmfMotionMark mark in motion.Params.Marks)
                {
                    report.AppendLine("Throw mark " + mark.Name + ": " + string.Join(",", mark.Intervals));
                    if (mark.Intervals.Count > 0)
                    {
                        release = Mathf.Min(release, (float)mark.Intervals[0]);
                    }
                }
            }

            report.AppendLine("Throw release seconds = " + release.ToString(CultureInfo.InvariantCulture));
            return release;
        }

        private static StalkerCollideSoundLibrary BuildCollisionSounds(StalkerXRaySources sources, XRayLtxDatabase system, string material, StringBuilder report)
        {
            StalkerGameMtlLibrary materials = StalkerGameMtlLibrary.Load(sources);
            var entries = new List<StalkerCollideSounds>();
            var pending = new List<KeyValuePair<List<AudioClip>, string>>();
            for (int i = 0; i < StalkerSurfaceTypes.Count; i++)
            {
                var surface = (StalkerSurfaceType)i;
                StalkerGameMtlPair pair = materials.FindPair(material, StalkerSurfaceTypes.GetMaterialName(surface));
                var clips = new List<AudioClip>();
                if (pair != null)
                {
                    foreach (string sound in StalkerGameMtlLibrary.SplitSounds(pair.CollideSounds))
                    {
                        string relative = sound.Replace('\\', '/') + ".ogg";
                        string shared = "Assets/_Source/Audio/Collide/" + relative;
                        string path = File.Exists(shared) ? shared : sources.CopyToProject("sounds/" + relative, ROOT + "/Sounds/Impact", relative);
                        if (path == null)
                        {
                            throw new FileNotFoundException("Bolt impact sound is missing: " + relative);
                        }

                        pending.Add(new KeyValuePair<List<AudioClip>, string>(clips, path));
                    }
                }

                string origin = pair != null ? pair.Origin : "no pair";
                report.AppendLine(material + " @ " + StalkerSurfaceTypes.GetMaterialName(surface) + " <- " + origin);
                entries.Add(new StalkerCollideSounds(material, surface, origin, clips));
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (KeyValuePair<List<AudioClip>, string> item in pending)
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(item.Value);
                if (clip == null)
                {
                    throw new InvalidOperationException("Impact sound failed to import: " + item.Value);
                }

                item.Key.Add(clip);
            }

            var library = AssetDatabase.LoadAssetAtPath<StalkerCollideSoundLibrary>(COLLIDE_PATH);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<StalkerCollideSoundLibrary>();
                AssetDatabase.CreateAsset(library, COLLIDE_PATH);
            }

            library.SetImportData(entries, Number(system.GetValue("sound", "snd_collide_min_volume")), Number(system.GetValue("sound", "snd_collide_max_volume")), report.ToString());
            EditorUtility.SetDirty(library);
            return library;
        }

        private static GameObject BuildProjectile(float mass, string material, StalkerCollideSoundLibrary sounds)
        {
            var root = new GameObject("BoltProjectile");
            try
            {
                GameObject model = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(WORLD_PATH), root.transform, false);
                if (model.GetComponentsInChildren<Renderer>().Length == 0)
                {
                    throw new InvalidOperationException("Bolt world model has no rendered geometry.");
                }

                var collider = root.AddComponent<BoxCollider>();
                collider.center = new Vector3(0.0000715283677f, -0.0049078334123f, -0.0000623427331f);
                collider.size = new Vector3(0.03337807208f, 0.07814133168f, 0.03294686228f);
                Rigidbody body = root.AddComponent<Rigidbody>();
                body.mass = mass;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.useGravity = false;
                body.linearDamping = 0.0001f;
                var audio = root.AddComponent<AudioSource>();
                audio.playOnAwake = false;
                audio.spatialBlend = 1.0f;
                audio.minDistance = 0.5f;
                audio.maxDistance = sounds.MaxDistance;
                root.AddComponent<WorldItemImpactSound>().Configure(sounds, AssetDatabase.LoadAssetAtPath<StalkerSurfaceAudioLibrary>(StalkerSurfaceAudioBuilder.LIBRARY_PATH), audio, material, mass);
                root.AddComponent<BoltProjectile>();
                return PrefabUtility.SaveAsPrefabAsset(root, PROJECTILE_PATH);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void ConfigurePlayer(BoltDefinition definition)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(StalkerPlayerSetup.PREFAB_PATH);
            try
            {
                FirstPersonBoltController controller = root.GetComponent<FirstPersonBoltController>();
                if (controller == null)
                {
                    controller = root.AddComponent<FirstPersonBoltController>();
                }

                Transform audioTransform = root.transform.Find("BoltAudio");
                if (audioTransform == null)
                {
                    audioTransform = new GameObject("BoltAudio").transform;
                    audioTransform.SetParent(root.transform, false);
                }

                AudioSource audio = audioTransform.GetComponent<AudioSource>();
                if (audio == null)
                {
                    audio = audioTransform.gameObject.AddComponent<AudioSource>();
                }

                audio.playOnAwake = false;
                audio.spatialBlend = 0.0f;
                var serialized = new SerializedObject(controller);
                Set(serialized, "_player", root.GetComponent<StalkerPlayerController>());
                Set(serialized, "_weapons", root.GetComponent<FirstPersonWeaponController>());
                Set(serialized, "_input", root.GetComponent<StalkerPlayerInput>());
                Set(serialized, "_definition", definition);
                Set(serialized, "_audio", audio);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, StalkerPlayerSetup.PREFAB_PATH);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ValidateHands(List<AnimationClip> clips, StringBuilder report)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<FirstPersonOutfitCatalog>("Assets/_Source/FirstPerson/Outfits/OutfitCatalog.asset");
            var models = new HashSet<GameObject>();
            foreach (FirstPersonOutfitEntry entry in catalog.Entries)
            {
                if (entry.HandsModel == null || !models.Add(entry.HandsModel))
                {
                    continue;
                }

                foreach (AnimationClip clip in clips)
                {
                    ValidateBinding(clip, entry.HandsModel);
                }

                if (entry.HandsModel.GetComponent<XRayModel>().FindBone(FirstPersonHudAnimator.ITEM_ANCHOR_BONE) == null)
                {
                    throw new InvalidOperationException("Hands have no item anchor: " + entry.HandsModel.name);
                }
            }

            report.AppendLine($"Validated {clips.Count} hands clips against {models.Count} distinct hands models.");
        }

        private static void ConfigureAnomalyProbes()
        {
            foreach (string name in new[] { "Electra", "Zharka", "Tramplin", "Vortex" })
            {
                string path = "Assets/_Source/Anomalies/" + name + "/" + name + ".prefab";
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    AnomalyPlayerReaction reaction = root.GetComponentInChildren<AnomalyPlayerReaction>(true);
                    var serialized = new SerializedObject(reaction);
                    serialized.FindProperty("_ignoreSmallObjects").boolValue = name == "Vortex";
                    serialized.FindProperty("_smallObjectIgnoreDelay").floatValue = name == "Zharka" ? -1.0f : 0.5f;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        private static void ValidateBinding(AnimationClip clip, GameObject model)
        {
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path.Length > 0 && model.transform.Find(binding.path) == null)
                {
                    throw new InvalidOperationException(clip.name + " has missing binding in " + model.name + ": " + binding.path);
                }
            }
        }

        private static void Set(SerializedObject serialized, string property, UnityEngine.Object value)
        {
            if (value == null)
            {
                throw new InvalidOperationException("Required bolt reference is missing: " + property);
            }

            serialized.FindProperty(property).objectReferenceValue = value;
        }

        private static float Number(string value)
        {
            return float.Parse(value, CultureInfo.InvariantCulture);
        }

        private static Vector3 Vector(string value)
        {
            string[] parts = value.Split(',');
            return new Vector3(Number(parts[0]), Number(parts[1]), Number(parts[2]));
        }
    }
}

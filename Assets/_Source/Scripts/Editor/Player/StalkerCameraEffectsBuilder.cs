using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using XRay.Importer;
using XRay.Importer.Sources.Ltx;

namespace RaidIntoDarkness.Player.Editor
{
    public static class StalkerCameraEffectsBuilder
    {
        public const string PROFILE_PATH = "Assets/_Source/Settings/Player/Camera/StalkerCameraEffectsProfile.asset";
        public const string ANM_FOLDER = "Assets/_Source/Settings/Player/Camera/Anm";

        private const string BOBBING = "bobbing_effector";
        private const string ACTOR = "actor";
        private const string REANIM_SCRIPT = "scripts/camera_reanim_project.script";
        private const string OPTIONS = "configs/axr_options.ltx";
        private const string EFFECTS_FOLDER = "anims\\camera_effects\\";

        public static readonly string[] INDEX_SCOPE = { "configs", "scripts", "anims/camera_effects" };

        private static readonly Regex TIRED_POWER = new Regex("db\\.actor\\.power\\s*<\\s*([0-9.]+)", RegexOptions.Compiled);
        private static readonly Regex LANDING_SPEED = new Regex("fall_speed\\s*>\\s*([0-9.]+)", RegexOptions.Compiled);
        private static readonly Regex LANDING_DELAY = new Regex("tg_anim1_delay\\s*=\\s*([0-9.]+)", RegexOptions.Compiled);
        private static readonly Regex RANDOM_RANGE = new Regex("local\\s+rand\\s*=\\s*math\\.random\\(\\s*(\\d+)\\s*,\\s*(\\d+)\\s*\\)", RegexOptions.Compiled);

        public static StalkerCameraEffectsProfile Build(StalkerXRaySources sources, StringBuilder log)
        {
            XRayLtxDatabase system = XRayLtxDatabase.LoadSystem(sources.Index);
            var profile = AssetDatabase.LoadAssetAtPath<StalkerCameraEffectsProfile>(PROFILE_PATH);
            if (profile == null)
            {
                StalkerXRaySources.EnsureFolder(Path.GetDirectoryName(PROFILE_PATH));
                profile = ScriptableObject.CreateInstance<StalkerCameraEffectsProfile>();
                AssetDatabase.CreateAsset(profile, PROFILE_PATH);
            }

            var serialized = new SerializedObject(profile);
            log.AppendLine($"[{BOBBING}] <- {system.GetOrigin(BOBBING)}");
            Float(serialized, "_bobbing._walkAmplitude", system.GetValue(BOBBING, "walk_amplitude"), $"[{BOBBING}] walk_amplitude", log);
            Float(serialized, "_bobbing._runAmplitude", system.GetValue(BOBBING, "run_amplitude"), $"[{BOBBING}] run_amplitude", log);
            Float(serialized, "_bobbing._limpAmplitude", system.GetValue(BOBBING, "limp_amplitude"), $"[{BOBBING}] limp_amplitude", log);
            Float(serialized, "_bobbing._walkSpeed", system.GetValue(BOBBING, "walk_speed"), $"[{BOBBING}] walk_speed", log);
            Float(serialized, "_bobbing._runSpeed", system.GetValue(BOBBING, "run_speed"), $"[{BOBBING}] run_speed", log);
            Float(serialized, "_bobbing._limpSpeed", system.GetValue(BOBBING, "limp_speed"), $"[{BOBBING}] limp_speed", log);
            Float(serialized, "_bobbing._crouchFactor", "0.75", "EffectorBobbing.cpp CROUCH_FACTOR (hardcoded)", log);
            Float(serialized, "_bobbing._speedReminder", "5", "EffectorBobbing.cpp SPEED_REMINDER (hardcoded)", log);
            Float(serialized, "_bobbing._headBobFactor", sources.GetUserValue("head_bob_factor") ?? "1", "user.ltx head_bob_factor (engine default 1)", log);
            Float(serialized, "_fall._speed", "3.5", "EffectorFall.cpp FALL_SPEED (hardcoded)", log);
            Float(serialized, "_fall._maxDistance", "0.15", "EffectorFall.cpp FALL_MAXDIST (hardcoded)", log);
            Float(serialized, "_fall._crashSpeedMax", system.GetValue(ACTOR, "ph_crash_speed_max"), "[actor] ph_crash_speed_max (gcontact_Power = contact speed / max)", log);

            string script = sources.ReadScript(REANIM_SCRIPT);
            var anmFiles = new List<KeyValuePair<string, string>>();
            if (script != null)
            {
                log.AppendLine($"{REANIM_SCRIPT} <- {sources.GetSourceName(REANIM_SCRIPT)}");
                Float(serialized, "_anm._tiredPower", Match(script, TIRED_POWER), "camera_reanim_project.script db.actor.power threshold", log);
                Float(serialized, "_anm._landingSpeed", Match(script, LANDING_SPEED), "camera_reanim_project.script actor_on_land fall_speed threshold", log);
                string delay = Match(script, LANDING_DELAY);
                Float(serialized, "_anm._landingCooldown", StalkerXRaySources.TryFloat(delay, out float milliseconds) ? StalkerXRaySources.Format(milliseconds / 1000.0f) : null, "camera_reanim_project.script tg_anim1_delay (ms)", log);
                AddAnm(script, "sprint_crp", "_anm._sprintStep", anmFiles);
                AddAnm(script, "move_crp", "_anm._moveStep", anmFiles);
                AddAnm(script, "tired_crp", "_anm._tiredStep", anmFiles);
                AddAnm(script, "jump_crp", "_anm._jump", anmFiles);
                AddAnm(script, "crouch_crp", "_anm._crouch", anmFiles);
                AddAnm(script, "stand_crp", "_anm._stand", anmFiles);
                Match range = RANDOM_RANGE.Match(script);
                int landingCount = range.Success ? int.Parse(range.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
                for (int i = 1; i <= landingCount && script.IndexOf("landing_crp", StringComparison.Ordinal) >= 0; i++)
                {
                    anmFiles.Add(new KeyValuePair<string, string>("landing_crp" + i, "_anm._landing"));
                }
            }
            else
            {
                log.AppendLine($"{REANIM_SCRIPT} absent: ReanimationProject camera ANM effects are not imported.");
            }

            anmFiles.Add(new KeyValuePair<string, string>("actor_move\\strafe_left", "_anm._strafeLeft"));
            anmFiles.Add(new KeyValuePair<string, string>("actor_move\\strafe_right", "_anm._strafeRight"));
            ImportAnm(sources, serialized, anmFiles, log);
            ReadOptions(sources, serialized, log);
            log.AppendLine("Unity adaptation: bob phase follows the body locomotion cycle (BodyAnimation) instead of the global CEffectorBobbing timer; frequency then follows the Swm legs clips, amplitude stays X-Ray.");
            log.AppendLine("Unity adaptation: bobbing fades out while the actor has no ground contact; X-Ray keeps it while mcAnyMove is set in the air.");
            log.AppendLine("X-Ray: mcAnyMove is cleared when actual velocity < 0.2 m/s (Actor_Movement.cpp), so pushing into a wall stops the bob and the legs steps.");
            serialized.FindProperty("_bobPhaseSource").enumValueIndex = (int)StalkerBobPhaseSource.BodyAnimation;
            serialized.FindProperty("_bobRequiresGround").boolValue = true;
            serialized.FindProperty("_sourceReport").stringValue = log.ToString();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static void AddAnm(string script, string name, string property, List<KeyValuePair<string, string>> files)
        {
            if (script.IndexOf(name + ".anm", StringComparison.Ordinal) >= 0)
            {
                files.Add(new KeyValuePair<string, string>(name, property));
            }
        }

        private static void ImportAnm(StalkerXRaySources sources, SerializedObject serialized, List<KeyValuePair<string, string>> files, StringBuilder log)
        {
            var copied = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, string> file in files)
            {
                string virtualPath = EFFECTS_FOLDER + file.Key + ".anm";
                string assetPath = sources.CopyToProject(virtualPath, ANM_FOLDER, file.Key.Replace('\\', '/') + ".anm");
                if (assetPath == null)
                {
                    log.AppendLine($"WARNING: {virtualPath} not found in the active profile.");
                    continue;
                }

                log.AppendLine($"{file.Value} <- {virtualPath} ({sources.GetSourceName(virtualPath)})");
                copied.Add(new KeyValuePair<string, string>(assetPath, file.Value));
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            serialized.Update();
            serialized.FindProperty("_anm._landing").arraySize = 0;
            foreach (KeyValuePair<string, string> file in copied)
            {
                XRayAnmAsset asset = LoadAnm(file.Key);
                if (asset == null)
                {
                    log.AppendLine("WARNING: ANM import failed: " + file.Key);
                    continue;
                }

                SerializedProperty property = serialized.FindProperty(file.Value);
                if (property.isArray)
                {
                    property.arraySize++;
                    property.GetArrayElementAtIndex(property.arraySize - 1).objectReferenceValue = asset;
                }
                else
                {
                    property.objectReferenceValue = asset;
                }
            }
        }

        private static XRayAnmAsset LoadAnm(string assetPath)
        {
            foreach (UnityEngine.Object item in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (item is XRayAnmAsset asset)
                {
                    return asset;
                }
            }

            return null;
        }

        private static void ReadOptions(StalkerXRaySources sources, SerializedObject serialized, StringBuilder log)
        {
            if (!sources.Exists(OPTIONS))
            {
                log.AppendLine("axr_options.ltx not found; ReanimationProject MCM defaults (all enabled) are used.");
                return;
            }

            XRayLtxDatabase options = XRayLtxDatabase.LoadFile(sources.Index, OPTIONS);
            bool moving = true;
            bool jump = true;
            bool crouch = true;
            foreach (XRayLtxSection section in options.Sections.Values)
            {
                moving &= !section.Values.TryGetValue("crpmcm/moving", out string value) || value != "false";
                jump &= !section.Values.TryGetValue("crpmcm/jump_land", out value) || value != "false";
                crouch &= !section.Values.TryGetValue("crpmcm/crouch", out value) || value != "false";
            }

            log.AppendLine($"axr_options.ltx crpmcm/moving = {moving}, crpmcm/jump_land = {jump}, crpmcm/crouch = {crouch}");
            serialized.FindProperty("_stepEffectsEnabled").boolValue = moving;
            serialized.FindProperty("_jumpLandEffectsEnabled").boolValue = jump;
            serialized.FindProperty("_crouchEffectsEnabled").boolValue = crouch;
        }

        private static string Match(string text, Regex regex)
        {
            Match match = regex.Match(text);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static void Float(SerializedObject serialized, string path, string value, string origin, StringBuilder log)
        {
            SerializedProperty property = serialized.FindProperty(path);
            if (property == null)
            {
                log.AppendLine("WARNING: missing property " + path);
                return;
            }

            if (StalkerXRaySources.TryFloat(value, out float result))
            {
                property.floatValue = result;
                log.AppendLine($"{path} = {StalkerXRaySources.Format(result)}  <- {origin}");
            }
            else
            {
                log.AppendLine($"WARNING: {path}: '{origin}' not found, kept {StalkerXRaySources.Format(property.floatValue)}");
            }
        }
    }
}

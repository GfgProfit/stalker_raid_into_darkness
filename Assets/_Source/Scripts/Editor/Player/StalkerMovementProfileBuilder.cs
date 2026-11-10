using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using RaidIntoDarkness.Player;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Core.IO;
using XRay.Importer.Editor;
using XRay.Importer.Sources.Ltx;
using XRay.Importer.Sources.Vfs;

namespace RaidIntoDarkness.Player.Editor
{
    public static class StalkerMovementProfileBuilder
    {
        public const string PROFILE_PATH = "Assets/_Source/Settings/Player/StalkerMovementProfile.asset";

        private const string MENU = "Tools/X-Ray/Build Stalker Movement Profile";
        private const string ACTOR = "actor";
        private const string SWM_SCRIPT = "scripts/swm_legs.script";
        private const string ASS_SCRIPT = "scripts/advanced_stamina_system_ass.script";
        private const string SPEED_SCRIPT = "scripts/speed.script";
        private const string OPTIONS = "configs/axr_options.ltx";
        private const string GAMEMTL = "gamemtl.xr";
        private const string GROUND_MATERIAL = "materials\\earth";
        private const int GAMEMTLS_CHUNK_MATERIALS = 0x1002;
        private const int GAMEMTL_CHUNK_MAIN = 0x1000;
        private const int GAMEMTL_CHUNK_PHYSICS = 0x1002;

        private static readonly string[] INDEX_SCOPE = { "configs", "scripts", GAMEMTL };

        public static void BuildBatch()
        {
            StalkerMovementProfile profile = Build(out string report);
            Debug.Log("[Player] " + report);
            EditorApplication.Exit(profile != null ? 0 : 1);
        }

        [MenuItem(MENU)]
        public static void BuildFromMenu()
        {
            StalkerMovementProfile profile = Build(out string report);
            if (profile != null)
            {
                Selection.activeObject = profile;
            }

            Debug.Log("[Player] " + report);
        }

        public static StalkerMovementProfile Build(out string report)
        {
            var log = new StringBuilder();
            XRayAssetSourceOptions options = XRayAssetSourceSettings.GetEffective();
            var configuration = new XRaySourceConfiguration
            {
                Mode = options.Sources.Mode,
                BaseAnomalyFolder = options.Sources.BaseAnomalyFolder,
                Mo2Folder = options.Sources.Mo2Folder,
                Mo2Instance = options.Sources.Mo2Instance,
                Mo2Profile = options.Sources.Mo2Profile,
                CustomFolder = options.Sources.CustomFolder,
                LocalAppDataFolder = options.Sources.LocalAppDataFolder,
                IncludeGameGamedata = options.Sources.IncludeGameGamedata,
                TreatModRootAsGamedata = options.Sources.TreatModRootAsGamedata
            };

            configuration.IndexScope.Clear();
            configuration.IndexScope.AddRange(INDEX_SCOPE);
            XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(configuration);
            if (!plan.IsValid)
            {
                report = "X-Ray asset sources are invalid: " + string.Join("; ", plan.Errors);
                Debug.LogError("[Player] " + report);
                return null;
            }

            XRayAssetIndex index = XRayAssetIndex.Build(plan);
            XRayLtxDatabase system = XRayLtxDatabase.LoadSystem(index);
            log.AppendLine($"MO2 profile: {plan.ProfileName}, active mods: {plan.ActiveModCount}, LTX sections: {system.Sections.Count}, DLTX files: {system.ModFileCount}");
            log.AppendLine("[actor] origin: " + system.GetOrigin(ACTOR));

            var profile = AssetDatabase.LoadAssetAtPath<StalkerMovementProfile>(PROFILE_PATH);
            if (profile == null)
            {
                EnsureFolder(Path.GetDirectoryName(PROFILE_PATH));
                profile = ScriptableObject.CreateInstance<StalkerMovementProfile>();
                AssetDatabase.CreateAsset(profile, PROFILE_PATH);
            }

            var serialized = new SerializedObject(profile);
            var writer = new Writer(serialized, log);
            ReadActor(system, writer);
            ReadCondition(system, writer);
            ReadCamera(system, writer);
            ReadUserLtx(plan, writer);
            ReadMaterials(index, writer);
            ReadOptions(index, writer);
            ReadSwmScript(index, writer);
            ReadStaminaScripts(index, writer);
            serialized.FindProperty("_sourceReport").stringValue = log.ToString();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            report = log.ToString();
            return profile;
        }

        private static void ReadActor(XRayLtxDatabase system, Writer writer)
        {
            string origin = "[actor]";
            writer.Float("_actor._walkAccel", system.GetValue(ACTOR, "walk_accel"), origin + " walk_accel");
            writer.Float("_actor._jumpSpeed", system.GetValue(ACTOR, "jump_speed"), origin + " jump_speed");
            writer.Float("_actor._runCoef", system.GetValue(ACTOR, "run_coef"), origin + " run_coef");
            writer.Float("_actor._runBackCoef", system.GetValue(ACTOR, "run_back_coef"), origin + " run_back_coef");
            writer.Float("_actor._walkBackCoef", system.GetValue(ACTOR, "walk_back_coef"), origin + " walk_back_coef");
            writer.Float("_actor._sprintKoef", system.GetValue(ACTOR, "sprint_koef"), origin + " sprint_koef");
            writer.Float("_actor._crouchCoef", system.GetValue(ACTOR, "crouch_coef"), origin + " crouch_coef");
            writer.Float("_actor._climbCoef", system.GetValue(ACTOR, "climb_coef"), origin + " climb_coef");
            writer.FloatOrDefault("_actor._walkStrafeCoef", system.GetValue(ACTOR, "walk_strafe_coef"), 1.0f, origin + " walk_strafe_coef (READ_IF_EXISTS default 1.0)");
            writer.FloatOrDefault("_actor._runStrafeCoef", system.GetValue(ACTOR, "run_strafe_coef"), 1.0f, origin + " run_strafe_coef (READ_IF_EXISTS default 1.0)");
            writer.FloatOrDefault("_actor._sprintStrafeCoef", system.GetValue(ACTOR, "sprint_strafe_coef"), 1.0f, origin + " sprint_strafe_coef (READ_IF_EXISTS default 1.0)");
            writer.Float("_actor._airControlParam", system.GetValue(ACTOR, "air_control_param"), origin + " air_control_param");
            writer.Float("_actor._cameraHeightFactor", system.GetValue(ACTOR, "camera_height_factor"), origin + " camera_height_factor");
            for (int box = 0; box < 3; box++)
            {
                writer.Vector($"_actor._box{box}Center", system.GetValue(ACTOR, $"ph_box{box}_center"), $"{origin} ph_box{box}_center");
                writer.Vector($"_actor._box{box}Size", system.GetValue(ACTOR, $"ph_box{box}_size"), $"{origin} ph_box{box}_size");
            }

            writer.Float("_actor._mass", system.GetValue(ACTOR, "ph_mass"), origin + " ph_mass");
            writer.Float("_actor._crashSpeedMin", system.GetValue(ACTOR, "ph_crash_speed_min"), origin + " ph_crash_speed_min");
            writer.Float("_actor._crashSpeedMax", system.GetValue(ACTOR, "ph_crash_speed_max"), origin + " ph_crash_speed_max");
            writer.Float("_actor._maxItemMass", system.GetValue(ACTOR, "max_item_mass"), origin + " max_item_mass");
            writer.String("_actor._material", XRayLtxDatabase.Unquote(system.GetValue(ACTOR, "material")), origin + " material");
        }

        private static void ReadCondition(XRayLtxDatabase system, Writer writer)
        {
            string section = XRayLtxDatabase.Unquote(system.GetValue(ACTOR, "condition_sect")) ?? ACTOR;
            string origin = $"[{section}]";
            string[] keys = { "jump_power", "stand_power", "walk_power", "jump_weight_power", "walk_weight_power", "overweight_walk_k", "overweight_jump_k", "accel_k", "sprint_k", "limping_health_begin", "limping_health_end", "limping_power_begin", "limping_power_end", "cant_walk_power_begin", "cant_walk_power_end", "cant_sprint_power_begin", "cant_sprint_power_end", "max_power_leak_speed", "max_walk_weight", "satiety_power_v" };
            foreach (string key in keys)
            {
                writer.Float("_condition._" + ToCamel(key), system.GetValue(section, key), origin + " " + key);
            }

            writer.Float("_condition._timeFactor", system.GetValue("alife", "time_factor"), "[alife] time_factor");
        }

        private static void ReadCamera(XRayLtxDatabase system, Writer writer)
        {
            writer.Vector2("_camera._pitchLimits", system.GetValue("actor_firsteye_cam", "lim_pitch"), "[actor_firsteye_cam] lim_pitch");
            writer.Vector2("_camera._ladderYawLimits", system.GetValue("actor_ladder_cam", "lim_yaw"), "[actor_ladder_cam] lim_yaw");
        }

        private static void ReadUserLtx(XRaySourcePlan plan, Writer writer)
        {
            string gamePath = plan.Installation != null ? plan.Installation.GamePath : null;
            if (string.IsNullOrEmpty(gamePath) && !string.IsNullOrEmpty(plan.Configuration.BaseAnomalyFolder))
            {
                gamePath = Path.GetFullPath(Path.Combine(plan.Configuration.BaseAnomalyFolder, "..", ".."));
            }

            string path = string.IsNullOrEmpty(gamePath) ? null : Path.Combine(gamePath, "appdata", "user.ltx");
            if (path == null || !File.Exists(path))
            {
                writer.Warning("user.ltx not found; console values keep their engine defaults.");
                return;
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(path))
            {
                int space = line.IndexOf(' ');
                if (space > 0)
                {
                    values[line.Substring(0, space).Trim()] = line.Substring(space + 1).Trim();
                }
            }

            string origin = "user.ltx ";
            writer.Float("_camera._fieldOfView", Get(values, "fov"), origin + "fov");
            writer.FloatOrDefault("_camera._hudFieldOfView", Get(values, "hud_fov"), 0.45f, origin + "hud_fov");
            writer.Float("_camera._mouseSensitivity", Get(values, "mouse_sens"), origin + "mouse_sens");
            writer.FloatOrDefault("_camera._mouseSensitivityVertical", Get(values, "mouse_sens_vertical"), 1.0f, origin + "mouse_sens_vertical");
            writer.Bool("_camera._invertMouse", Get(values, "mouse_invert"), origin + "mouse_invert");
            writer.Note($"user.ltx g_sprint_toggle = {Get(values, "g_sprint_toggle")} is not applied: sprint is held.");
            writer.Bool("_input._crouchToggle", Get(values, "g_crouch_toggle"), origin + "g_crouch_toggle");
            writer.Bool("_input._walkToggle", Get(values, "g_walk_toggle"), origin + "g_walk_toggle");
            writer.FloatOrDefault("_condition._powerLossBias", Get(values, "power_loss_bias"), 0.25f, origin + "power_loss_bias");
            writer.FloatOrDefault("_condition._powerLossFactor", Get(values, "power_loss_factor"), 0.5f, origin + "power_loss_factor");
            writer.Bool("_condition._progressiveStaminaCost", Get(values, "g_progressive_stamina_cost"), origin + "g_progressive_stamina_cost");
            string frequency = Get(values, "ph_frequency");
            if (TryFloat(frequency, out float hz) && hz > 0.0f)
            {
                writer.Float("_engine._fixedStep", (1.0f / hz).ToString(CultureInfo.InvariantCulture), origin + "ph_frequency " + frequency);
            }

            string gravity = Get(values, "ph_gravity");
            writer.FloatOrDefault("_engine._gravity", gravity, 19.62f, gravity != null ? origin + "ph_gravity" : "default_world_gravity = 2 * 9.81 (Physics.cpp), ph_gravity not set");
        }

        private static void ReadMaterials(XRayAssetIndex index, Writer writer)
        {
            if (!index.TryGet(GAMEMTL, out XRayIndexedFile file))
            {
                writer.Warning("gamemtl.xr not found; friction keeps its defaults.");
                return;
            }

            byte[] data = File.ReadAllBytes(index.GetFullPath(file.Winner));
            var friction = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<int, ArraySegment<byte>> materials in Chunks(data, 0, data.Length))
            {
                if (materials.Key != GAMEMTLS_CHUNK_MATERIALS)
                {
                    continue;
                }

                foreach (KeyValuePair<int, ArraySegment<byte>> material in Chunks(data, materials.Value.Offset, materials.Value.Count))
                {
                    string name = null;
                    float value = float.NaN;
                    foreach (KeyValuePair<int, ArraySegment<byte>> part in Chunks(data, material.Value.Offset, material.Value.Count))
                    {
                        if (part.Key == GAMEMTL_CHUNK_MAIN)
                        {
                            int start = part.Value.Offset + 4;
                            int end = start;
                            while (end < part.Value.Offset + part.Value.Count && data[end] != 0)
                            {
                                end++;
                            }

                            name = Cp1251.Decode(data, start, end - start);
                        }
                        else if (part.Key == GAMEMTL_CHUNK_PHYSICS)
                        {
                            value = BitConverter.ToSingle(data, part.Value.Offset);
                        }
                    }

                    if (name != null && !float.IsNaN(value))
                    {
                        friction[name] = value;
                    }
                }
            }

            string actorMaterial = writer.GetString("_actor._material") ?? "creatures\\actor";
            writer.Float("_engine._actorFriction", friction.TryGetValue(actorMaterial, out float actor) ? actor.ToString(CultureInfo.InvariantCulture) : null, $"gamemtl.xr '{actorMaterial}' fPHFriction ({index.GetSource(file.Winner).Name})");
            writer.Float("_engine._groundFriction", friction.TryGetValue(GROUND_MATERIAL, out float ground) ? ground.ToString(CultureInfo.InvariantCulture) : null, $"gamemtl.xr '{GROUND_MATERIAL}' fPHFriction");
        }

        private static IEnumerable<KeyValuePair<int, ArraySegment<byte>>> Chunks(byte[] data, int offset, int count)
        {
            int position = offset;
            int end = offset + count;
            while (position + 8 <= end)
            {
                int id = BitConverter.ToInt32(data, position) & 0x7fffffff;
                int size = BitConverter.ToInt32(data, position + 4);
                if (size < 0 || position + 8 + size > end)
                {
                    yield break;
                }

                yield return new KeyValuePair<int, ArraySegment<byte>>(id, new ArraySegment<byte>(data, position + 8, size));
                position += 8 + size;
            }
        }

        private static void ReadOptions(XRayAssetIndex index, Writer writer)
        {
            if (!index.TryGet(OPTIONS, out XRayIndexedFile _))
            {
                writer.Warning("axr_options.ltx not found; MCM options keep the script defaults.");
                return;
            }

            XRayLtxDatabase options = XRayLtxDatabase.LoadFile(index, OPTIONS);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (XRayLtxSection section in options.Sections.Values)
            {
                foreach (KeyValuePair<string, string> pair in section.Values)
                {
                    values[pair.Key] = pair.Value;
                }
            }

            string origin = "axr_options.ltx ";
            writer.Float("_visibleBody._legsOffset", Get(values, "swm_legs/legs_offset"), origin + "swm_legs/legs_offset");
            writer.Float("_visibleBody._legsOffsetNonCombat", Get(values, "swm_legs/legs_offset_noncombat"), origin + "swm_legs/legs_offset_noncombat");
            writer.Float("_visibleBody._legsOffsetSide", Get(values, "swm_legs/legs_offset_side"), origin + "swm_legs/legs_offset_side");
            writer.Bool("_visibleBody._useSpecialNonCombatShift", Get(values, "swm_legs/use_special_noncombat_shift"), origin + "swm_legs/use_special_noncombat_shift");
            writer.Bool("_visibleBody._hideLegsLowCrouch", Get(values, "swm_legs/hide_legs_low_crouch"), origin + "swm_legs/hide_legs_low_crouch");
            writer.Bool("_visibleBody._enableIdleAnimations", Get(values, "swm_legs/enable_idle_anims"), origin + "swm_legs/enable_idle_anims");
            writer.Float("_visibleBody._idleCooldownMin", Get(values, "swm_legs/idle_anim_min_cooldown"), origin + "swm_legs/idle_anim_min_cooldown");
            writer.Float("_visibleBody._idleCooldownMax", Get(values, "swm_legs/idle_anim_max_cooldown"), origin + "swm_legs/idle_anim_max_cooldown");
            writer.Bool("_staminaMod._enableStaminaSpeedModifier", Get(values, "advanced_stamina_system/enable_stamina_speed_modifier"), origin + "advanced_stamina_system/enable_stamina_speed_modifier");
            writer.Float("_staminaMod._staminaSpeedModifierModifier", Get(values, "advanced_stamina_system/stamina_speed_modifier_modifier"), origin + "advanced_stamina_system/stamina_speed_modifier_modifier");
            writer.Bool("_staminaMod._enableUphillSpeedModifier", Get(values, "advanced_stamina_system/enable_uphill_speed_modifier"), origin + "advanced_stamina_system/enable_uphill_speed_modifier");
            writer.Float("_staminaMod._uphillSpeedModifierModifier", Get(values, "advanced_stamina_system/uphill_speed_modifier_modifier"), origin + "advanced_stamina_system/uphill_speed_modifier_modifier");
            writer.Bool("_staminaMod._enableUphillDrain", Get(values, "advanced_stamina_system/enable_uphill_drain"), origin + "advanced_stamina_system/enable_uphill_drain");
            writer.Float("_staminaMod._uphillDrainModifier", Get(values, "advanced_stamina_system/uphill_drain_modifier"), origin + "advanced_stamina_system/uphill_drain_modifier");
            writer.Bool("_staminaMod._enableMaterialDrain", Get(values, "advanced_stamina_system/enable_material_drain"), origin + "advanced_stamina_system/enable_material_drain");
            writer.Float("_staminaMod._materialDrainModifier", Get(values, "advanced_stamina_system/material_drain_modifier"), origin + "advanced_stamina_system/material_drain_modifier");
        }

        private static void ReadSwmScript(XRayAssetIndex index, Writer writer)
        {
            string code = ReadScript(index, SWM_SCRIPT, writer);
            if (code == null)
            {
                return;
            }

            writer.Float("_visibleBody._height", Match(code, "local\\s+HEIGHT\\s*=\\s*([-0-9.]+)"), "swm_legs.script HEIGHT");
            writer.Float("_visibleBody._heightLadder", Match(code, "local\\s+HEIGHT_ladder\\s*=\\s*([-0-9.]+)"), "swm_legs.script HEIGHT_ladder");
            writer.Float("_visibleBody._slowedRunCoef", Match(code, "jog_to_walk_thresold\\s*=\\s*([-0-9.]+)"), "swm_legs.script jog_to_walk_thresold");
            writer.Float("_visibleBody._slowedSprintKoef", Match(code, "sprint_to_jog_thresold\\s*=\\s*([-0-9.]+)"), "swm_legs.script sprint_to_jog_thresold");
            writer.Float("_visibleBody._lookDownThreshold", Match(code, "angle_down\\s*<=\\s*([-0-9.]+)\\s*then"), "swm_legs.script angle_down threshold");
            writer.Float("_visibleBody._headToBodyDistance", Match(code, "distance_H2B\\s*<\\s*([0-9.]+)"), "swm_legs.script distance_H2B");
            writer.Float("_visibleBody._turnThreshold", Match(code, "local\\s+turn_threshold\\s*=\\s*([0-9.]+)"), "swm_legs.script turn_threshold");
            writer.Float("_visibleBody._turnThresholdLookDown", Match(code, "angle_down\\s*<\\s*-?[0-9.]+\\s*then\\s*turn_threshold\\s*=\\s*([0-9.]+)"), "swm_legs.script turn_threshold while looking down");
            string turn = Match(code, "time_global\\(\\)\\s*-\\s*self\\.lastTurnTime\\s*>=\\s*([0-9.]+)");
            writer.Float("_visibleBody._turnDuration", TryFloat(turn, out float turnMilliseconds) ? (turnMilliseconds / 1000.0f).ToString(CultureInfo.InvariantCulture) : null, "swm_legs.script lastTurnTime interval (ms)");
            writer.Float("_visibleBody._fallShiftStand", Match(code, "local\\s+shift\\s*=\\s*([0-9.]+)"), "swm_legs.script shift (stand)");
            writer.Float("_visibleBody._fallShiftLowCrouch", Match(code, "actor_flags\\[6\\]\\s*then\\s*shift\\s*=\\s*([0-9.]+)"), "swm_legs.script shift (crouch + shift)");
            writer.Float("_visibleBody._fallShiftCrouch", Match(code, "else\\s*shift\\s*=\\s*([0-9.]+)"), "swm_legs.script shift (crouch)");
            float side = writer.GetFloat("_visibleBody._legsOffsetSide");
            writer.Shifts("_visibleBody._motionShifts", Table(code, "SPEC_SHIFT"), side, "swm_legs.script SPEC_SHIFT");
            writer.Shifts("_visibleBody._lowCrouchShifts", Table(code, "SPEC_SHIFT_PRONE"), side, "swm_legs.script SPEC_SHIFT_PRONE");
        }

        private static void ReadStaminaScripts(XRayAssetIndex index, Writer writer)
        {
            string code = ReadScript(index, ASS_SCRIPT, writer);
            if (code != null)
            {
                writer.Float("_staminaMod._powerSpeedMax", Match(code, "local\\s+power_speed_max\\s*=\\s*([-0-9.]+)"), "advanced_stamina_system_ass.script power_speed_max");
                writer.Float("_staminaMod._powerSpeedMin", Match(code, "normalize\\(power,\\s*([-0-9.]+),\\s*power_speed_max\\)"), "advanced_stamina_system_ass.script normalize(power, min, power_speed_max)");
                string interval = Match(code, "local\\s+tg_interval\\s*=\\s*([0-9.]+)");
                writer.Float("_staminaMod._updateInterval", TryFloat(interval, out float milliseconds) ? (milliseconds / 1000.0f).ToString(CultureInfo.InvariantCulture) : null, "advanced_stamina_system_ass.script tg_interval (ms)");
                MatchCollection pairs = Regex.Matches(code, "local\\s+coeff\\s*=\\s*([0-9.]+)\\s*\\n\\s*local\\s+coeff_downhill_reduction\\s*=\\s*([0-9.]+)");
                if (pairs.Count >= 2)
                {
                    writer.Float("_staminaMod._uphillDrainCoeff", pairs[0].Groups[1].Value, "advanced_stamina_system_ass.script climb coeff (drain)");
                    writer.Float("_staminaMod._uphillDrainDownhillReduction", pairs[0].Groups[2].Value, "advanced_stamina_system_ass.script coeff_downhill_reduction (drain)");
                    writer.Float("_staminaMod._uphillSpeedCoeff", pairs[1].Groups[1].Value, "advanced_stamina_system_ass.script climb coeff (speed)");
                    writer.Float("_staminaMod._uphillSpeedDownhillReduction", pairs[1].Groups[2].Value, "advanced_stamina_system_ass.script coeff_downhill_reduction (speed)");
                }
                else
                {
                    writer.Warning("advanced_stamina_system_ass.script: climb coefficients not found.");
                }
            }

            string speed = ReadScript(index, SPEED_SCRIPT, writer);
            if (speed != null)
            {
                Match clamp = Regex.Match(speed, "clamp\\(speeds\\[0\\]\\s*\\*\\s*run_coef,\\s*([0-9.]+),\\s*([0-9.]+)\\)");
                writer.Float("_staminaMod._speedCoefMin", clamp.Success ? clamp.Groups[1].Value : null, "speed.script clamp min");
                writer.Float("_staminaMod._speedCoefMax", clamp.Success ? clamp.Groups[2].Value : null, "speed.script clamp max");
            }
        }

        private static string ReadScript(XRayAssetIndex index, string path, Writer writer)
        {
            if (!index.TryGet(path, out XRayIndexedFile file))
            {
                writer.Warning($"'{path}' not found.");
                return null;
            }

            byte[] data = File.ReadAllBytes(index.GetFullPath(file.Winner));
            writer.Note($"{path}: {index.GetSource(file.Winner).Name}");
            return Regex.Replace(Cp1251.Decode(data, 0, data.Length), "--[^\\n]*", string.Empty);
        }

        private static List<KeyValuePair<string, string>> Table(string code, string name)
        {
            var entries = new List<KeyValuePair<string, string>>();
            Match table = Regex.Match(code, "local\\s+" + name + "\\s*=\\s*\\{(.*?)\\n\\}", RegexOptions.Singleline);
            if (!table.Success)
            {
                return entries;
            }

            foreach (Match entry in Regex.Matches(table.Groups[1].Value, "\\[\"([^\"]+)\"\\]\\s*=\\s*\\{\\s*fwd\\s*=\\s*([-0-9.]+)"))
            {
                entries.Add(new KeyValuePair<string, string>(entry.Groups[1].Value, entry.Groups[2].Value));
            }

            return entries;
        }

        private static string Match(string code, string pattern)
        {
            Match match = Regex.Match(code, pattern);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string Get(Dictionary<string, string> values, string key)
        {
            return values.TryGetValue(key, out string value) ? value : null;
        }

        private static bool TryFloat(string value, out float result)
        {
            return float.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private static string ToCamel(string key)
        {
            var builder = new StringBuilder(key.Length);
            bool upper = false;
            foreach (char ch in key)
            {
                if (ch == '_')
                {
                    upper = true;
                    continue;
                }

                builder.Append(upper ? char.ToUpperInvariant(ch) : ch);
                upper = false;
            }

            return builder.ToString();
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

        private sealed class Writer
        {
            private readonly SerializedObject _object;
            private readonly StringBuilder _log;

            public Writer(SerializedObject serializedObject, StringBuilder log)
            {
                _object = serializedObject;
                _log = log;
            }

            public void Float(string path, string value, string origin)
            {
                SerializedProperty property = Find(path);
                if (property == null)
                {
                    return;
                }

                if (TryFloat(value, out float result))
                {
                    property.floatValue = result;
                    _log.AppendLine($"{path} = {result.ToString(CultureInfo.InvariantCulture)}  <- {origin}");
                }
                else
                {
                    Warning($"{path}: '{origin}' not found, kept {property.floatValue.ToString(CultureInfo.InvariantCulture)}.");
                }
            }

            public void FloatOrDefault(string path, string value, float fallback, string origin)
            {
                SerializedProperty property = Find(path);
                if (property == null)
                {
                    return;
                }

                property.floatValue = TryFloat(value, out float result) ? result : fallback;
                _log.AppendLine($"{path} = {property.floatValue.ToString(CultureInfo.InvariantCulture)}  <- {origin}{(value == null ? " (absent)" : string.Empty)}");
            }

            public void Bool(string path, string value, string origin)
            {
                SerializedProperty property = Find(path);
                if (property == null)
                {
                    return;
                }

                if (value == null)
                {
                    Warning($"{path}: '{origin}' not found, kept {property.boolValue}.");
                    return;
                }

                string text = value.Trim().ToLowerInvariant();
                property.boolValue = text == "on" || text == "true" || text == "1";
                _log.AppendLine($"{path} = {property.boolValue}  <- {origin}");
            }

            public void String(string path, string value, string origin)
            {
                SerializedProperty property = Find(path);
                if (property != null && value != null)
                {
                    property.stringValue = value;
                    _log.AppendLine($"{path} = {value}  <- {origin}");
                }
            }

            public void Vector(string path, string value, string origin)
            {
                SerializedProperty property = Find(path);
                float[] parts = Parts(value, 3);
                if (property != null && parts != null)
                {
                    property.vector3Value = new Vector3(parts[0], parts[1], parts[2]);
                    _log.AppendLine($"{path} = {value}  <- {origin}");
                }
                else if (property != null)
                {
                    Warning($"{path}: '{origin}' not found.");
                }
            }

            public void Vector2(string path, string value, string origin)
            {
                SerializedProperty property = Find(path);
                float[] parts = Parts(value, 2);
                if (property != null && parts != null)
                {
                    property.vector2Value = new UnityEngine.Vector2(parts[0], parts[1]);
                    _log.AppendLine($"{path} = {value}  <- {origin}");
                }
                else if (property != null)
                {
                    Warning($"{path}: '{origin}' not found.");
                }
            }

            public void Shifts(string path, List<KeyValuePair<string, string>> entries, float right, string origin)
            {
                SerializedProperty property = Find(path);
                if (property == null)
                {
                    return;
                }

                property.ClearArray();
                foreach (KeyValuePair<string, string> entry in entries)
                {
                    if (!TryFloat(entry.Value, out float forward))
                    {
                        continue;
                    }

                    property.InsertArrayElementAtIndex(property.arraySize);
                    SerializedProperty element = property.GetArrayElementAtIndex(property.arraySize - 1);
                    element.FindPropertyRelative("_motion").stringValue = entry.Key;
                    element.FindPropertyRelative("_forward").floatValue = forward;
                    element.FindPropertyRelative("_right").floatValue = right;
                }

                _log.AppendLine($"{path}: {property.arraySize} entries  <- {origin}");
            }

            public float GetFloat(string path)
            {
                SerializedProperty property = Find(path);
                return property != null ? property.floatValue : 0.0f;
            }

            public string GetString(string path)
            {
                SerializedProperty property = Find(path);
                return property?.stringValue;
            }

            public void Warning(string message)
            {
                _log.AppendLine("WARNING: " + message);
            }

            public void Note(string message)
            {
                _log.AppendLine(message);
            }

            private SerializedProperty Find(string path)
            {
                SerializedProperty property = _object.FindProperty(path);
                if (property == null)
                {
                    Warning($"Profile has no property '{path}'.");
                }

                return property;
            }

            private static float[] Parts(string value, int count)
            {
                if (value == null)
                {
                    return null;
                }

                string[] items = value.Split(',');
                if (items.Length != count)
                {
                    return null;
                }

                var result = new float[count];
                for (int i = 0; i < count; i++)
                {
                    if (!TryFloat(items[i], out result[i]))
                    {
                        return null;
                    }
                }

                return result;
            }
        }
    }
}

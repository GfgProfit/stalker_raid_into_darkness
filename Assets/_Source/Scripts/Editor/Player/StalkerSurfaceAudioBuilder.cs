using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Player.Editor
{
    public static class StalkerSurfaceAudioBuilder
    {
        public const string LIBRARY_PATH = "Assets/_Source/Settings/Player/Audio/StalkerSurfaceAudioLibrary.asset";
        public const string AUDIO_FOLDER = "Assets/_Source/Audio/Player";

        private const string ACTOR_MATERIAL = "creatures\\actor";
        private const string MOVING_MODE_SCRIPT = "scripts/footstep_according_moving_mode.script";
        private const string JUMP_SCRIPT = "scripts/eft_jump_sounds.script";
        private const string GEAR_SCRIPT = "scripts/gear_rustle_sounds.script";
        private const string MOVE_EFFECTS_SCRIPT = "scripts/move_effects.script";
        private const string MOVING_MODE_FOLDER = "footstep_according_moving_mode";
        private const string JUMP_FOLDER = "eft_jump_sound";
        private const string GEAR_PREFIX = "gear-rustle\\tac_gear_";
        private const string CROUCH_PREFIX = "anim_movements\\mvt_short_";
        private const int DEFAULT_JUMP_VARIANTS = 5;

        public static readonly string[] INDEX_SCOPE = { "configs", "scripts", "materials", "sounds/material", "sounds/" + MOVING_MODE_FOLDER, "sounds/" + JUMP_FOLDER, "sounds/gear-rustle", "sounds/anim_movements", StalkerGameMtlLibrary.GAMEMTL };

        private static readonly Regex MOVING_MODE_ENTRY = new Regex("(\\w+)\\s*=\\s*\\{\\s*enable\\s*=\\s*(true|false)\\s*,\\s*sound_key\\s*=\\s*\"([^\"]*)\"\\s*,\\s*sound_total_count\\s*=\\s*(\\d+)", RegexOptions.Compiled);
        private static readonly Regex MOVE_STATE = new Regex("(walk|crouch|run)\\s*=\\s*(true|false)", RegexOptions.Compiled);
        private static readonly Regex AVAILABLE_BLOCK = new Regex("available_materials\\s*=\\s*\\{([^}]*)\\}", RegexOptions.Compiled);
        private static readonly Regex AVAILABLE_ENTRY = new Regex("(\\w+)\\s*=\\s*(true|false)", RegexOptions.Compiled);
        private static readonly Regex JUMP_VARIANTS = new Regex("\"eft_jump_sound\\\\\\\\jump_\"[^\\n]*random_number\\(\\s*(\\d+)\\s*,\\s*(\\d+)\\s*\\)", RegexOptions.Compiled);
        private static readonly Regex LAND_VARIANTS = new Regex("\"eft_jump_sound\\\\\\\\land_\"[^\\n]*random_number\\(\\s*(\\d+)\\s*,\\s*(\\d+)\\s*\\)", RegexOptions.Compiled);
        private static readonly Regex LAND_SPEED = new Regex("landing_speed\\s*>\\s*([0-9.]+)", RegexOptions.Compiled);
        private static readonly Regex LAND_DIVIDER = new Regex("landing_speed\\s*/\\s*([0-9.]+)", RegexOptions.Compiled);
        private static readonly Regex RAY_START = new Regex("actor_position\\.y\\s*\\+\\s*([0-9.]+)", RegexOptions.Compiled);
        private static readonly Regex RAY_RANGE = new Regex("range\\s*=\\s*([0-9.]+)", RegexOptions.Compiled);
        private static readonly Regex GEAR_COUNT = new Regex("tac_gear_\"\\s*\\.\\.\\s*math\\.random\\(\\s*(\\d+)\\s*\\)", RegexOptions.Compiled);
        private static readonly Regex CROUCH_COUNT = new Regex("mvt_short_\"\\s*\\.\\.\\s*math\\.random\\(\\s*(\\d+)\\s*,\\s*(\\d+)\\s*\\)", RegexOptions.Compiled);

        private sealed class MovingModeEntry
        {
            public bool Enabled;
            public string Key;
            public int Count;
        }

        public static StalkerSurfaceAudioLibrary Build(StalkerXRaySources sources, StringBuilder log)
        {
            StalkerGameMtlLibrary materials = StalkerGameMtlLibrary.Load(sources);
            if (materials == null)
            {
                log.AppendLine("ERROR: gamemtl.xr not found in the active MO2 profile.");
                return null;
            }

            foreach (string line in materials.Log)
            {
                log.AppendLine(line);
            }

            var imported = new List<KeyValuePair<List<AudioClip>, string>>();
            Dictionary<string, MovingModeEntry> movingModes = ReadMovingModes(sources, log, out HashSet<string> moveStates);
            string jumpScript = sources.ReadScript(JUMP_SCRIPT);
            HashSet<string> jumpMaterials = ReadAvailable(jumpScript);
            int jumpVariants = ReadRange(jumpScript, JUMP_VARIANTS, DEFAULT_JUMP_VARIANTS);
            int landVariants = ReadRange(jumpScript, LAND_VARIANTS, DEFAULT_JUMP_VARIANTS);
            log.AppendLine(jumpScript != null ? $"{JUMP_SCRIPT} <- {sources.GetSourceName(JUMP_SCRIPT)}: {jumpMaterials.Count} enabled materials, jump variants {jumpVariants}, land variants {landVariants}" : $"{JUMP_SCRIPT} absent: no jump or landing sounds.");

            var surfaces = new List<StalkerSurfaceSounds>();
            for (int i = 0; i < StalkerSurfaceTypes.Count; i++)
            {
                var type = (StalkerSurfaceType)i;
                string materialName = StalkerSurfaceTypes.GetMaterialName(type);
                string key = StalkerSurfaceTypes.GetShortName(type);
                var surface = new StalkerSurfaceSounds(type, materialName);
                StalkerGameMtlPair pair = materials.FindPair(ACTOR_MATERIAL, materialName);
                if (pair != null)
                {
                    foreach (string sound in StalkerGameMtlLibrary.SplitSounds(pair.StepSounds))
                    {
                        Queue(sources, "sounds\\" + sound + ".ogg", "Footsteps/" + sound + ".ogg", surface.PairStepClips, imported);
                    }

                    QueueContacts(sources, surface, pair, imported, log);
                }

                log.AppendLine($"{materialName}: pair [{ACTOR_MATERIAL}] step_sounds {(pair == null ? "pair absent" : $"{StalkerGameMtlLibrary.SplitSounds(pair.StepSounds).Count} files <- {pair.Origin}")}");
                if (movingModes.TryGetValue(key, out MovingModeEntry entry) && entry.Enabled && entry.Key.Length > 0)
                {
                    QueueMode(sources, "walk", entry, moveStates, surface.WalkClips, imported);
                    QueueMode(sources, "run", entry, moveStates, surface.RunClips, imported);
                    QueueMode(sources, "crouch", entry, moveStates, surface.CrouchClips, imported);
                    log.AppendLine($"  moving mode sound_key '{entry.Key}', sound_total_count {entry.Count}");
                }

                if (jumpMaterials.Contains(key))
                {
                    for (int variant = 1; variant <= jumpVariants; variant++)
                    {
                        Queue(sources, $"sounds\\{JUMP_FOLDER}\\jump_{key}_{variant}.ogg", $"Jump/jump_{key}_{variant}.ogg", surface.JumpClips, imported);
                    }

                    for (int variant = 1; variant <= landVariants; variant++)
                    {
                        Queue(sources, $"sounds\\{JUMP_FOLDER}\\land_{key}_{variant}.ogg", $"Landing/land_{key}_{variant}.ogg", surface.LandClips, imported);
                    }
                }

                surfaces.Add(surface);
            }

            var gear = new List<AudioClip>();
            string gearScript = sources.ReadScript(GEAR_SCRIPT);
            int gearCount = ReadCount(gearScript, GEAR_COUNT);
            for (int i = 1; i <= gearCount; i++)
            {
                Queue(sources, $"sounds\\{GEAR_PREFIX}{i}.ogg", $"Equipment/tac_gear_{i}.ogg", gear, imported);
            }

            log.AppendLine(gearScript != null ? $"{GEAR_SCRIPT} <- {sources.GetSourceName(GEAR_SCRIPT)}: tac_gear_1..{gearCount}" : $"{GEAR_SCRIPT} absent.");
            var crouch = new List<AudioClip>();
            string moveScript = sources.ReadScript(MOVE_EFFECTS_SCRIPT);
            int crouchCount = ReadRange(moveScript, CROUCH_COUNT, 0);
            for (int i = 1; i <= crouchCount; i++)
            {
                Queue(sources, $"sounds\\{CROUCH_PREFIX}{i}.ogg", $"Equipment/mvt_short_{i}.ogg", crouch, imported);
            }

            log.AppendLine(moveScript != null ? $"{MOVE_EFFECTS_SCRIPT} <- {sources.GetSourceName(MOVE_EFFECTS_SCRIPT)}: crouch key mvt_short_1..{crouchCount}" : $"{MOVE_EFFECTS_SCRIPT} absent.");

            ImportAudio(imported, log);
            foreach (StalkerSurfaceSounds surface in surfaces)
            {
                log.AppendLine($"{surface.MaterialName}: pair {surface.PairStepClips.Count}, walk {surface.WalkClips.Count}, run {surface.RunClips.Count}, crouch {surface.CrouchClips.Count}, jump {surface.JumpClips.Count}, land {surface.LandClips.Count}");
            }

            log.AppendLine($"gear rustle {gear.Count}, crouch key {crouch.Count}");
            float landSpeed = ReadFloat(jumpScript, LAND_SPEED, 5.0f);
            float landDivider = ReadFloat(jumpScript, LAND_DIVIDER, 5.0f);
            float rayStart = ReadFloat(jumpScript, RAY_START, 1.0f);
            float rayRange = ReadFloat(jumpScript, RAY_RANGE, 1.1f);
            float effects = StalkerXRaySources.TryFloat(sources.GetUserValue("snd_volume_eff"), out float volume) ? volume : 1.0f;
            log.AppendLine($"snd_volume_eff = {StalkerXRaySources.Format(effects)} <- user.ltx; land sound above {StalkerXRaySources.Format(landSpeed)} m/s, volume divider {StalkerXRaySources.Format(landDivider)}; ray {StalkerXRaySources.Format(rayStart)} m up, range {StalkerXRaySources.Format(rayRange)} m");
            log.AppendLine("Unity adaptation: fallback surface 'earth' for colliders without a surface component, PhysicsMaterial or Terrain layer mapping (X-Ray always has a game material per triangle).");
            log.AppendLine("Unity adaptation: psHUDStepSoundVolume is not stored in user.ltx; the engine default 1 is used.");

            var library = AssetDatabase.LoadAssetAtPath<StalkerSurfaceAudioLibrary>(LIBRARY_PATH);
            if (library == null)
            {
                StalkerXRaySources.EnsureFolder(System.IO.Path.GetDirectoryName(LIBRARY_PATH));
                library = ScriptableObject.CreateInstance<StalkerSurfaceAudioLibrary>();
                AssetDatabase.CreateAsset(library, LIBRARY_PATH);
            }

            library.SetImportData(surfaces, StalkerSurfaceType.Earth, gear, crouch, 1.0f, effects, landSpeed, landDivider, rayStart, rayRange, log.ToString());
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        public static bool BuildContactSounds(out string report)
        {
            var log = new StringBuilder();
            StalkerXRaySources sources = StalkerXRaySources.Open(INDEX_SCOPE, out string error);
            var library = AssetDatabase.LoadAssetAtPath<StalkerSurfaceAudioLibrary>(LIBRARY_PATH);
            if (sources == null || library == null)
            {
                report = sources == null ? error : "Missing surface audio library: " + LIBRARY_PATH;
                return false;
            }

            log.AppendLine(sources.Describe());
            StalkerGameMtlLibrary materials = StalkerGameMtlLibrary.Load(sources);
            if (materials == null)
            {
                report = "gamemtl.xr not found in the active MO2 profile.";
                return false;
            }

            foreach (string line in materials.Log)
            {
                log.AppendLine(line);
            }

            var imported = new List<KeyValuePair<List<AudioClip>, string>>();
            var surfaces = new List<StalkerSurfaceSounds>(library.Surfaces);
            for (int i = 0; i < StalkerSurfaceTypes.Count; i++)
            {
                var type = (StalkerSurfaceType)i;
                StalkerSurfaceSounds surface = surfaces.Find(item => item.Type == type);
                if (surface == null)
                {
                    surface = new StalkerSurfaceSounds(type, StalkerSurfaceTypes.GetMaterialName(type));
                    surfaces.Add(surface);
                }

                surface.CollideClips.Clear();
                StalkerGameMtlPair pair = materials.FindPair(ACTOR_MATERIAL, surface.MaterialName);
                if (pair != null)
                {
                    QueueContacts(sources, surface, pair, imported, log);
                }
            }

            ImportAudio(imported, log);
            library.SetImportedSurfaces(surfaces);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssetIfDirty(library);
            StalkerSurfaceSounds bush = library.GetSurface(StalkerSurfaceType.Bush);
            bool success = bush != null && bush.CollideClips.Count > 0;
            log.AppendLine($"Actor bush contact clips: {bush?.CollideClips.Count ?? 0}");
            report = log.ToString();
            return success;
        }

        private static void QueueContacts(StalkerXRaySources sources, StalkerSurfaceSounds surface, StalkerGameMtlPair pair, List<KeyValuePair<List<AudioClip>, string>> imported, StringBuilder log)
        {
            foreach (string sound in StalkerGameMtlLibrary.SplitSounds(pair.CollideSounds))
            {
                string virtualPath = "sounds/" + sound.Replace('\\', '/') + ".ogg";
                Queue(sources, virtualPath, "Contacts/" + sound + ".ogg", surface.CollideClips, imported);
                log.AppendLine($"{surface.MaterialName}: actor collide '{sound}' <- {sources.GetSourceName(virtualPath) ?? "MISSING"}; pair <- {pair.Origin}");
            }
        }

        private static Dictionary<string, MovingModeEntry> ReadMovingModes(StalkerXRaySources sources, StringBuilder log, out HashSet<string> moveStates)
        {
            var result = new Dictionary<string, MovingModeEntry>(StringComparer.Ordinal);
            moveStates = new HashSet<string>(StringComparer.Ordinal);
            string script = sources.ReadScript(MOVING_MODE_SCRIPT);
            if (script == null)
            {
                log.AppendLine($"{MOVING_MODE_SCRIPT} absent: no moving mode footsteps.");
                return result;
            }

            foreach (Match match in MOVING_MODE_ENTRY.Matches(script))
            {
                result[match.Groups[1].Value] = new MovingModeEntry { Enabled = match.Groups[2].Value == "true", Key = match.Groups[3].Value, Count = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) };
            }

            int states = script.IndexOf("possible_move_states", StringComparison.Ordinal);
            if (states >= 0)
            {
                foreach (Match match in MOVE_STATE.Matches(script.Substring(states, Math.Min(200, script.Length - states))))
                {
                    if (match.Groups[2].Value == "true")
                    {
                        moveStates.Add(match.Groups[1].Value);
                    }
                }
            }

            log.AppendLine($"{MOVING_MODE_SCRIPT} <- {sources.GetSourceName(MOVING_MODE_SCRIPT)}: {result.Count} materials, move states [{string.Join(", ", moveStates)}]");
            return result;
        }

        private static HashSet<string> ReadAvailable(string script)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            Match block = script != null ? AVAILABLE_BLOCK.Match(script) : Match.Empty;
            if (!block.Success)
            {
                return result;
            }

            foreach (Match match in AVAILABLE_ENTRY.Matches(block.Groups[1].Value))
            {
                if (match.Groups[2].Value == "true")
                {
                    result.Add(match.Groups[1].Value);
                }
            }

            return result;
        }

        private static void QueueMode(StalkerXRaySources sources, string mode, MovingModeEntry entry, HashSet<string> moveStates, List<AudioClip> clips, List<KeyValuePair<List<AudioClip>, string>> imported)
        {
            if (!moveStates.Contains(mode))
            {
                return;
            }

            for (int i = 1; i <= entry.Count; i++)
            {
                string name = $"{mode}_{entry.Key}_{i}.ogg";
                Queue(sources, $"sounds\\{MOVING_MODE_FOLDER}\\{name}", $"Footsteps/{MOVING_MODE_FOLDER}/{name}", clips, imported);
            }
        }

        private static void Queue(StalkerXRaySources sources, string virtualPath, string relative, List<AudioClip> clips, List<KeyValuePair<List<AudioClip>, string>> imported)
        {
            string assetPath = sources.CopyToProject(virtualPath, AUDIO_FOLDER, relative.Replace('\\', '/'));
            if (assetPath != null)
            {
                imported.Add(new KeyValuePair<List<AudioClip>, string>(clips, assetPath));
            }
        }

        private static void ImportAudio(List<KeyValuePair<List<AudioClip>, string>> imported, StringBuilder log)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            int missing = 0;
            foreach (KeyValuePair<List<AudioClip>, string> item in imported)
            {
                if (AssetImporter.GetAtPath(item.Value) is AudioImporter importer && importer.defaultSampleSettings.loadType != AudioClipLoadType.DecompressOnLoad)
                {
                    AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                    settings.loadType = AudioClipLoadType.DecompressOnLoad;
                    importer.defaultSampleSettings = settings;
                    importer.SaveAndReimport();
                }

                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(item.Value);
                if (clip == null)
                {
                    missing++;
                    log.AppendLine("WARNING: clip failed to import: " + item.Value);
                    continue;
                }

                item.Key.Add(clip);
            }

            log.AppendLine($"Imported audio clips referenced by the library: {imported.Count - missing}, failed: {missing}");
        }

        private static int ReadCount(string script, Regex regex)
        {
            Match match = script != null ? regex.Match(script) : Match.Empty;
            return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        }

        private static int ReadRange(string script, Regex regex, int fallback)
        {
            Match match = script != null ? regex.Match(script) : Match.Empty;
            return match.Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : fallback;
        }

        private static float ReadFloat(string script, Regex regex, float fallback)
        {
            Match match = script != null ? regex.Match(script) : Match.Empty;
            return match.Success && StalkerXRaySources.TryFloat(match.Groups[1].Value, out float value) ? value : fallback;
        }
    }
}

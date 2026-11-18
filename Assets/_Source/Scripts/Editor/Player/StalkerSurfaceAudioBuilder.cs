using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Player.Editor
{
    public static class StalkerSurfaceAudioBuilder
    {
        public const string LIBRARY_PATH = "Assets/_Source/Settings/Player/Audio/StalkerSurfaceAudioLibrary.asset";
        public const string AUDIO_FOLDER = "Assets/_Source/Audio/Player";

        private const string ACTOR_MATERIAL = "creatures\\actor";

        private static readonly string[] INDEX_SCOPE = { "configs", "scripts", "materials", "sounds/material", "sounds/footstep_according_moving_mode", "sounds/eft_jump_sound", "sounds/gear-rustle", "sounds/anim_movements", StalkerGameMtlLibrary.GAMEMTL };

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
    }
}

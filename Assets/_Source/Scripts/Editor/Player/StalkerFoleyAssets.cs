using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Player.Editor
{
    public static class StalkerFoleyAssets
    {
        private const string MENU = "Tools/X-Ray/Build Player Foley And Camera Effects";

        [MenuItem(MENU)]
        public static void BuildFromMenu()
        {
            bool success = Build(out string report);
            Debug.Log("[Player] " + report);
            if (success)
            {
                StalkerPlayerSetup.Setup();
            }
        }

        public static void BuildBatch()
        {
            bool success = false;
            try
            {
                success = Build(out string report);
                Debug.Log("[Player] " + report);
                success = success && StalkerPlayerSetup.Setup();
            }
            catch (Exception exception)
            {
                Debug.LogError("[Player] Foley build failed: " + exception);
            }

            EditorApplication.Exit(success ? 0 : 1);
        }

        public static bool Build(out string report)
        {
            var scope = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            scope.UnionWith(StalkerSurfaceAudioBuilder.INDEX_SCOPE);
            scope.UnionWith(StalkerFootstepTimingBuilder.INDEX_SCOPE);
            scope.UnionWith(StalkerCameraEffectsBuilder.INDEX_SCOPE);
            StalkerXRaySources sources = StalkerXRaySources.Open(scope, out string error);
            var summary = new StringBuilder();
            if (sources == null)
            {
                report = error;
                Debug.LogError("[Player] " + error);
                return false;
            }

            summary.AppendLine(sources.Describe());
            var audioLog = new StringBuilder(sources.Describe()).AppendLine();
            StalkerSurfaceAudioLibrary audio = StalkerSurfaceAudioBuilder.Build(sources, audioLog);
            var timingLog = new StringBuilder(sources.Describe()).AppendLine();
            StalkerFootstepTimingLibrary timing = StalkerFootstepTimingBuilder.Build(sources, timingLog);
            var cameraLog = new StringBuilder(sources.Describe()).AppendLine();
            StalkerCameraEffectsProfile camera = StalkerCameraEffectsBuilder.Build(sources, cameraLog);
            summary.AppendLine("=== Surface audio ===").Append(audioLog);
            summary.AppendLine("=== Footstep timing ===").Append(timingLog);
            summary.AppendLine("=== Camera effects ===").Append(cameraLog);
            report = summary.ToString();
            return audio != null && timing != null && camera != null;
        }
    }
}

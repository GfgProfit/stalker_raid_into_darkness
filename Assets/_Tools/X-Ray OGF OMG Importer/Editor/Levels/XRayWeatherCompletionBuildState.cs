using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayWeatherCompletionBuildState
    {
        private const string PROFILE_PATH = "Assets/Settings/HDRPDefaultResources/DefaultSettingsVolumeProfile.asset";
        private const string SETTINGS_PATH = "Assets/Settings/HDRPDefaultResources/HDRenderPipelineGlobalSettings.asset";
        private const string ARCHIVE = @"E:\Projects\stalker_raid_into_darkness_Archive\";

        public static void Capture(string output)
        {
            RequireOutput(output);
            if (File.Exists(Path.Combine(output, "DefaultAssetState.json"))) throw new IOException("Preserve the existing pre-build HDRP state.");
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PROFILE_PATH);
            UnityEngine.Object settings = AssetDatabase.LoadMainAssetAtPath(SETTINGS_PATH);
            SerializedProperty runtime = new SerializedObject(settings).FindProperty("m_Settings.m_RuntimeSettings.m_List");
            if (profile == null || runtime == null || !runtime.isArray) throw new InvalidOperationException("The HDRP default build state cannot be captured safely.");
            var state = new BuildState { profileHash = Hash(PROFILE_PATH), settingsHash = Hash(SETTINGS_PATH), hadWeatherSky = profile.TryGet(out XRayWeatherSkySettings sky), runtimeCount = runtime.arraySize };
            File.WriteAllBytes(Path.Combine(output, "DefaultSettingsVolumeProfile.Before.asset"), File.ReadAllBytes(PROFILE_PATH));
            File.WriteAllBytes(Path.Combine(output, "HDRenderPipelineGlobalSettings.Before.asset"), File.ReadAllBytes(SETTINGS_PATH));
            File.WriteAllText(Path.Combine(output, "DefaultAssetState.json"), JsonUtility.ToJson(state, true));
        }

        public static string Repair(string output)
        {
            return Restore(output);
        }

        public static string Restore(string output)
        {
            RequireOutput(output);
            if (BuildPipeline.isBuildingPlayer) throw new InvalidOperationException("Wait until the Player build has finished before restoring HDRP runtime settings.");
            string path = Path.Combine(output, "DefaultAssetState.json");
            if (!File.Exists(path)) throw new FileNotFoundException("The original pre-build HDRP state is required for restoration.", path);
            BuildState state = JsonUtility.FromJson<BuildState>(File.ReadAllText(path));
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PROFILE_PATH);
            UnityEngine.Object settings = AssetDatabase.LoadMainAssetAtPath(SETTINGS_PATH);
            var serialized = new SerializedObject(settings);
            SerializedProperty runtime = serialized.FindProperty("m_Settings.m_RuntimeSettings.m_List");
            string run = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            File.WriteAllBytes(Path.Combine(output, "DefaultSettingsVolumeProfile.AfterBuild_" + run + ".asset"), File.ReadAllBytes(PROFILE_PATH));
            File.WriteAllBytes(Path.Combine(output, "HDRenderPipelineGlobalSettings.AfterBuild_" + run + ".asset"), File.ReadAllBytes(SETTINGS_PATH));
            if (!state.hadWeatherSky && profile.TryGet(out XRayWeatherSkySettings sky))
            {
                if (sky.Shader != null) throw new InvalidOperationException("Preserve the newly authored default sky instead of removing it as a build side effect.");
                profile.Remove<XRayWeatherSkySettings>();
                AssetDatabase.RemoveObjectFromAsset(sky);
                UnityEngine.Object.DestroyImmediate(sky, true);
                EditorUtility.SetDirty(profile);
            }
            if (runtime.arraySize != state.runtimeCount || Hash(SETTINGS_PATH) != state.settingsHash)
            {
                if (state.runtimeCount != 0) throw new InvalidOperationException("Preserve the nonempty authored HDRP runtime settings; automatic build cleanup cannot replace them.");
                var container = settings.GetType().GetField("m_Settings", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(settings) as RenderPipelineGraphicsSettingsContainer;
                if (container == null) throw new InvalidOperationException("The authoritative HDRP graphics settings container is unavailable.");
                container.OnBeforeSerialize();
                EditorUtility.SetDirty(settings);
            }
            AssetDatabase.SaveAssetIfDirty(profile);
            AssetDatabase.SaveAssetIfDirty(settings);
            bool restored = Hash(PROFILE_PATH) == state.profileHash && Hash(SETTINGS_PATH) == state.settingsHash;
            string proof = Path.Combine(output, "DefaultAssetRestoration_" + run + ".json");
            File.WriteAllText(proof, JsonUtility.ToJson(new Restoration { passed = restored, profileHash = Hash(PROFILE_PATH), settingsHash = Hash(SETTINGS_PATH) }, true));
            if (!restored) throw new InvalidOperationException("HDRP default assets differ from their pre-build source hashes after scoped cleanup.");
            return proof;
        }

        private static void RequireOutput(string output)
        {
            if (!Path.GetFullPath(output).StartsWith(ARCHIVE, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("HDRP build restoration evidence must remain in the task Archive.");
        }

        private static string Hash(string path)
        {
            using SHA256 hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }

        [Serializable]
        private sealed class BuildState
        {
            public string profileHash;
            public string settingsHash;
            public bool hadWeatherSky;
            public int runtimeCount;
        }

        [Serializable]
        private sealed class Restoration
        {
            public bool passed;
            public string profileHash;
            public string settingsHash;
        }
    }
}

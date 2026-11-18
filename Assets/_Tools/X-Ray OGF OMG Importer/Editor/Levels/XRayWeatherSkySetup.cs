using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayWeatherSkySetup
    {
        public const float DEFAULT_NIGHT_DIFFUSE_MULTIPLIER = 19.2f;

        public static readonly Color DEFAULT_NIGHT_AMBIENT = new Color(0.003f, 0.009f, 0.025f, 1.0f);

        public static VolumeProfile GetOrCreateSkyProfile(XRayWeatherProfile weather, float hour, Shader shader, GradientSky authored, VolumeProfile authoredProfile, string assetPath)
        {
            if (!XRayWeatherImporter.HasSerializedResources(weather)) throw new InvalidDataException("Weather sky setup requires serialized sky and environment cubemaps.");
            if (authored == null || shader == null || !assetPath.StartsWith("Assets/", StringComparison.Ordinal) || !assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Weather sky setup requires an authored Gradient Sky, shader and explicit asset destination.");
            VolumeProfile existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(assetPath);
            if (existing != null)
            {
                if (existing.components.Count != 2 || !existing.TryGet(out XRayWeatherSkySettings sky) || !existing.TryGet(out VisualEnvironment visual) || sky.Shader != shader || visual.skyType.value != XRayWeatherSkySettings.SKY_ID) throw new InvalidDataException("Preserve the incompatible weather sky profile before connecting it: " + assetPath);
                return existing;
            }
            if (File.Exists(assetPath)) throw new IOException("The weather sky profile destination is already occupied: " + assetPath);
            if (!weather.TryEvaluate(hour * 3600.0, out XRayWeatherSnapshot snapshot, out string diagnostic)) throw new InvalidDataException(diagnostic);
            XRayLevelPaths.EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = Path.GetFileNameWithoutExtension(assetPath);
            XRayWeatherSkySettings settings = profile.Add<XRayWeatherSkySettings>(false);
            settings.SetWeather(snapshot, shader);
            settings.skyIntensityMode.Override(authored.skyIntensityMode.value);
            settings.exposure.Override(authored.exposure.value);
            settings.multiplier.Override(authored.multiplier.value);
            settings.upperHemisphereLuxValue.Override(authored.upperHemisphereLuxValue.value);
            settings.desiredLuxValue.Override(authored.desiredLuxValue.value);
            settings.SetHdrpIntensity(true);
            settings.SetNightAmbient(DEFAULT_NIGHT_AMBIENT);
            settings.SetNightDiffuseMultiplier(DEFAULT_NIGHT_DIFFUSE_MULTIPLIER);
            VisualEnvironment environment = profile.Add<VisualEnvironment>(false);
            environment.skyType.Override(XRayWeatherSkySettings.SKY_ID);
            if (authoredProfile != null && authoredProfile.TryGet(out VisualEnvironment sourceEnvironment))
            {
                environment.skyAmbientMode.value = sourceEnvironment.skyAmbientMode.value;
                environment.skyAmbientMode.overrideState = sourceEnvironment.skyAmbientMode.overrideState;
            }
            AssetDatabase.CreateAsset(profile, assetPath);
            foreach (VolumeComponent component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            return profile;
        }
    }
}

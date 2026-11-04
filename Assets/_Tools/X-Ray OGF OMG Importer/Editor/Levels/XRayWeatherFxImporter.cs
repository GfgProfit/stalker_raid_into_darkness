using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Levels;
using XRay.Importer.Sources.Ltx;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    public static partial class XRayWeatherImporter
    {
        private const string WEATHER_FX_ROOT = "configs/environment/weather_effects/";

        private static readonly (string Name, XRayWeatherFxKind Kind)[] WEATHER_EFFECTS =
        {
            ("fx_blowout_day", XRayWeatherFxKind.Blowout),
            ("fx_blowout_night", XRayWeatherFxKind.Blowout),
            ("fx_super_blowout_day", XRayWeatherFxKind.SuperBlowout),
            ("fx_super_blowout_night", XRayWeatherFxKind.SuperBlowout),
            ("fx_psi_storm_day", XRayWeatherFxKind.PsiStorm),
            ("fx_psi_storm_night", XRayWeatherFxKind.PsiStorm),
            ("fx_surge_day_3_stancia", XRayWeatherFxKind.Scenario)
        };
        private static readonly string[] WEATHER_FX_DEPENDENCIES =
        {
            "configs/environment/suns.ltx",
            "configs/environment/ambients.ltx",
            "configs/environment/sound_channels.ltx",
            "configs/environment/effects.ltx",
            "configs/environment/thunderbolt_collections.ltx",
            "configs/environment/thunderbolts.ltx",
            "scripts/surge_manager.script",
            "scripts/raid_super_surge_manager.script",
            "scripts/psi_storm_manager.script",
            "scripts/level_weathers.script",
            "scripts/xr_effects.script",
            "scripts/task_status_functor.script"
        };

        [MenuItem("Tools/X-Ray/Weather/Prepare Weather FX Catalog")]
        public static void PrepareWeatherFxCatalog()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || _preparation != null) throw new InvalidOperationException("Prepare the weather catalog once, outside Play Mode.");
            _preparation = ResolveFxCatalog(CreateCurrentPlan(), true).GetEnumerator();
            EditorApplication.update += AdvancePreparation;
        }

        public static XRayWeatherFxCatalog LoadPreparedFxCatalog()
        {
            foreach (XRayWeatherFxCatalog catalog in ResolveFxCatalog(CreateCurrentPlan(), false)) return catalog;
            return null;
        }

        private static IEnumerable<XRayWeatherFxCatalog> ResolveFxCatalog(XRaySourcePlan plan, bool prepare)
        {
            string assetRoot = CurrentAssetRoot(plan);
            string weatherFolder = WeatherFolder(plan, assetRoot);
            string folder = weatherFolder + "/WeatherFX";
            string catalogPath = folder + "/WeatherFxCatalog.asset";
            XRayWeatherFxCatalog catalog = AssetDatabase.LoadAssetAtPath<XRayWeatherFxCatalog>(catalogPath);
            if (!prepare && catalog == null && !File.Exists(catalogPath)) yield break;
            if (catalog == null && File.Exists(catalogPath)) throw new InvalidDataException("Weather FX catalog destination is occupied: " + catalogPath);
            XRayWeatherCatalog ordinary = LoadCatalog(plan, assetRoot);
            if (ordinary == null) throw new InvalidDataException("Prepare the ordinary weather catalog for the current source before importing effects.");
            XRayWeatherProfile sourceProfile = ordinary.Profiles[0];
            string sourcePath = AssetDatabase.GetAssetPath(sourceProfile);
            var source = new XRayEffectiveGamedata(XRayAssetIndex.Build(plan));
            source.Index.ExtractionFolder = Path.Combine(XRayLevelPaths.Mo2Cache, "WeatherArchiveCache");
            var common = new StringBuilder();
            common.AppendLine("schema=1; target=Rvm; public-engine-model=xray-monolith@313a751d; executable equivalence unproven; mode=explicit-weather-fx");
            common.AppendLine("sourceIdentity=" + sourceProfile.SourceIdentity);
            AddPhysical(common, sourcePath);
            AddEffective(common, source, SUN_POSITIONS);
            foreach (string dependency in WEATHER_FX_DEPENDENCIES) AddEffective(common, source, dependency);
            string provenance = common.ToString();
            XRayWeatherResourceImporter resources = prepare ? new XRayWeatherResourceImporter(source.Index, weatherFolder + "/Textures", assetRoot + "/WeatherProfiles") : null;
            XRayWeatherCelestialSettings celestial = AssetDatabase.LoadAssetAtPath<XRayWeatherCelestialSettings>(XRayWeatherCelestialImporter.SETTINGS_ASSET);
            if (celestial == null) throw new InvalidDataException("The prepared celestial weather definitions are missing.");
            var effects = new XRayWeatherFxProfile[WEATHER_EFFECTS.Length];
            for (int i = 0; i < effects.Length; i++)
            {
                var (name, kind) = WEATHER_EFFECTS[i];
                string path = folder + "/" + name + ".asset";
                string weather = WEATHER_FX_ROOT + name + ".ltx";
                var effectProvenance = new StringBuilder(provenance);
                ValidateInput(source, weather, new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal), effectProvenance);
                XRayWeatherFxProfile effect = AssetDatabase.LoadAssetAtPath<XRayWeatherFxProfile>(path);
                if (effect == null && (!prepare || File.Exists(path))) throw new InvalidDataException("Missing or incompatible cached weather FX profile: " + path);
                if (effect != null && (effect.EffectName != name || effect.Kind != kind || effect.SourceProfile != sourceProfile || string.IsNullOrEmpty(effect.Provenance) || !effect.Provenance.StartsWith(effectProvenance.ToString(), StringComparison.Ordinal) || !HasSerializedFxResources(effect, celestial))) throw new InvalidDataException("Preserve or replace the incompatible cached weather FX profile explicitly: " + path);
                if (prepare)
                {
                    XRayWeatherFxProfile imported = ImportFx(source, weather, name, kind, sourceProfile, effectProvenance, resources, celestial);
                    if (effect == null)
                    {
                        SaveProfile(imported, path);
                        effect = imported;
                    }
                    else
                    {
                        bool compatible = EditorJsonUtility.ToJson(effect) == EditorJsonUtility.ToJson(imported);
                        UnityEngine.Object.DestroyImmediate(imported);
                        if (!compatible) throw new InvalidDataException("Preserve or replace the incompatible cached weather FX profile explicitly: " + path);
                    }
                }
                effects[i] = effect;
                if (prepare) yield return null;
            }
            if (catalog != null)
            {
                if (catalog.OrdinaryCatalog != ordinary || catalog.Effects.Count != effects.Length) throw new InvalidDataException("Preserve or replace the incompatible cached weather FX catalog explicitly: " + catalogPath);
                for (int i = 0; i < effects.Length; i++)
                {
                    if (catalog.Effects[i] != effects[i]) throw new InvalidDataException("Preserve or replace the incompatible cached weather FX catalog explicitly: " + catalogPath);
                }
                yield return catalog;
                yield break;
            }
            catalog = ScriptableObject.CreateInstance<XRayWeatherFxCatalog>();
            try
            {
                catalog.Initialize(ordinary, effects);
                SaveProfile(catalog, catalogPath);
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(catalog);
                throw;
            }
            yield return catalog;
        }

        private static XRayWeatherFxProfile ImportFx(XRayEffectiveGamedata source, string weather, string name, XRayWeatherFxKind kind, XRayWeatherProfile sourceProfile, StringBuilder provenance, XRayWeatherResourceImporter resources, XRayWeatherCelestialSettings celestial)
        {
            XRayLtxDatabase descriptors = XRayLtxDatabase.LoadFile(source.Index, weather);
            ValidateDatabase(descriptors);
            XRayWeatherSnapshot[] frames = ReadFrames(source, descriptors, weather, provenance, resources.Bind, resources.Bind);
            var effect = ScriptableObject.CreateInstance<XRayWeatherFxProfile>();
            try
            {
                effect.name = name;
                effect.Initialize(name, kind, sourceProfile, provenance.ToString(), frames);
                if (!HasSerializedFxResources(effect, celestial)) throw new InvalidDataException("Weather FX has incomplete serialized resources: " + weather);
                return effect;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(effect);
                throw;
            }
        }

        private static bool HasSerializedFxResources(XRayWeatherFxProfile effect, XRayWeatherCelestialSettings celestial)
        {
            if (!effect.Validate(out _)) return false;
            for (int frame = 0; frame < effect.KeyframeCount; frame++)
            {
                XRayWeatherSnapshot snapshot = effect.GetKeyframe(frame);
                if (!HasSerializedSkyResources(snapshot)) return false;
                if (!string.IsNullOrEmpty(snapshot.SunDefinition) && celestial.FindDefinition(snapshot.SunDefinition) == null) return false;
                for (int resource = 0; resource < 4; resource++)
                {
                    if (snapshot.CloudsResources.Weights[resource] > 0.0f && !(snapshot.CloudsResources.Texture(resource) is Texture2D)) return false;
                }
            }
            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayWeatherCelestialValidation
    {
        private const string PRODUCTION = "E:/Projects/stalker_raid_into_darkness/Assets";
        private const string ARCHIVE = @"E:\Projects\stalker_raid_into_darkness_Archive\";
        private const string FIXTURES = "Assets/_Source/Levels/WeatherCelestialValidation/";

        public static string Prepare(string archive)
        {
            return Prepare(archive, null);
        }

        public static string PrepareCatalog(string archive)
        {
            XRayWeatherCatalog catalog = XRayWeatherImporter.LoadPreparedCatalog();
            if (catalog == null) throw new InvalidOperationException("Prepare the saved ordinary weather catalog before building its validation fixture.");
            return Prepare(archive, catalog);
        }

        public static string PrepareFx(string archive)
        {
            XRayWeatherFxCatalog catalog = XRayWeatherImporter.LoadPreparedFxCatalog();
            if (catalog == null) throw new InvalidOperationException("Prepare the saved Weather FX catalog before building its validation fixture.");
            return Prepare(archive, catalog.OrdinaryCatalog, catalog);
        }

        private static string Prepare(string archive, XRayWeatherCatalog catalog, XRayWeatherFxCatalog fxCatalog = null)
        {
            Guard(archive);
            var host = new HostScope();
            GetProfiles(host.Controller, catalog, out XRayWeatherProfile clear, out XRayWeatherProfile rain);
            XRayWeatherCelestialSettings settings = Resources.Load<XRayWeatherCelestialSettings>(XRayWeatherCelestialSettings.RESOURCE_PATH);
            if (settings == null || settings.Shader == null || !settings.Shader.isSupported) throw new InvalidOperationException("Import the current serialized celestial resources and supported shader first.");
            string folder = FIXTURES + "Run_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            string path = folder + "/WeatherCelestialValidation.unity";
            Scene fixture = default;
            try
            {
                host.Isolate();
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
                fixture = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                GameObject root = XRayWeatherFogValidation.CreateFixture(fixture, folder, new List<Object>(), out Camera camera, out _);
                camera.orthographic = false;
                camera.fieldOfView = 60.0f;
                HDAdditionalCameraData data = camera.GetComponent<HDAdditionalCameraData>();
                data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;
                data.dithering = false;
                data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.CustomPass, true);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.CustomPass] = true;
                data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.Postprocess, true);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.Postprocess] = true;
                GameObject opaque = root.transform.Find("Known Distance 0").gameObject;
                GameObject alpha = root.transform.Find("Alpha-tested surface").gameObject;
                for (int i = 0; i < 4; i++) root.transform.Find("Known Distance " + i).gameObject.SetActive(false);
                alpha.SetActive(false);
                root.transform.Find("Monolith transparent surface").gameObject.SetActive(false);
                XRayWeatherController controller = XRayLevelSceneBuilder.ConfigureWeather(root, clear);
                if (catalog != null) controller.ConfigureCatalog(catalog);
                if (fxCatalog != null) controller.ConfigureFxCatalog(fxCatalog);
                if (!XRayLevelSceneBuilder.ConfigureWeatherSky(root, controller, clear, folder + "/Sky.asset")) throw new InvalidOperationException("The fixture requires the real weather Sky.");
                var serialized = new SerializedObject(controller);
                serialized.FindProperty("_celestialSettings").objectReferenceValue = settings;
                serialized.FindProperty("_paused").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                controller.ApplySettings();
                var probeObject = new GameObject("Weather Celestial Validation Probe");
                SceneManager.MoveGameObjectToScene(probeObject, fixture);
                var probe = probeObject.AddComponent<XRayWeatherCelestialValidationProbe>();
                probe.Configure(camera, controller, clear, rain, opaque, alpha, XRayWeatherFogValidation.SourceHash());
                probe.RequireCatalog(catalog != null);
                probe.RequireFx(fxCatalog != null);
                if (fxCatalog != null)
                {
                    const string FX_SURFACE = "Assets/_Source/Levels/Anomaly/1v2_DX11_v_12gb_2.5_168_662acc8d/Materials/levels/l07_military/terrain/l07_military_terrain_terrain_military_1_terrain.mat";
                    Material surface = AssetDatabase.LoadAssetAtPath<Material>(FX_SURFACE);
                    if (surface == null || surface.shader == null || !surface.shader.isSupported) throw new InvalidOperationException("The FX lighting pixel fixture requires the serialized military terrain material.");
                    probe.ConfigureFxLightingSurface(surface);
                }
                probe.ConfigureEnvironment(serialized.FindProperty("_sun").objectReferenceValue as Light);
                if (!EditorSceneManager.SaveScene(fixture, path)) throw new IOException("Cannot save the dedicated celestial fixture.");
                File.WriteAllText(Path.Combine(archive, "Fixture_" + Path.GetFileName(folder) + ".txt"), path);
            }
            finally
            {
                try
                {
                    if (fixture.IsValid() && fixture.isLoaded) EditorSceneManager.CloseScene(fixture, true);
                }
                finally
                {
                    host.Restore();
                    WriteHost(archive, "PrepareHostAfter", host.Before);
                }
            }
            return path;
        }

        public static string Build(string scenePath, string archive)
        {
            Guard(archive);
            return XRayWeatherFogValidation.BuildCelestialFixture(scenePath, archive);
        }

        public static string RunEditor(string scenePath, string archive)
        {
            Guard(archive);
            RequireFixture(scenePath);
            var host = new HostScope();
            string output = Path.Combine(archive, "Editor_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "HostBefore.json"), JsonUtility.ToJson(host.Before, true));
            Scene fixture = default;
            try
            {
                host.Isolate();
                fixture = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                XRayWeatherController controller = Find<XRayWeatherController>(fixture);
                if (!controller.BeginPreview()) throw new InvalidOperationException(controller.Diagnostic);
                return Find<XRayWeatherCelestialValidationProbe>(fixture).RunEditorCases(output);
            }
            finally
            {
                try
                {
                    if (fixture.IsValid() && fixture.isLoaded) EditorSceneManager.CloseScene(fixture, true);
                }
                finally
                {
                    host.Restore();
                    WriteHost(output, "HostAfter", host.Before);
                }
            }
        }

        public static string SaveHost(string archive)
        {
            Guard(archive);
            string path = Path.Combine(archive, "HostBefore_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(ReadHost(), true));
            return path;
        }

        public static string RestoreHost(string snapshotPath, string archive)
        {
            Guard(archive);
            if (!Path.GetFullPath(snapshotPath).StartsWith(Path.GetFullPath(archive).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Use a snapshot from this validation directory.");
            string json = File.ReadAllText(snapshotPath);
            HostRecord before = JsonUtility.FromJson<HostRecord>(json);
            if (json.IndexOf("\"precipitationOverride\"", StringComparison.Ordinal) < 0) before.precipitationOverride = -1;
            if (json.IndexOf("\"rainVisualOverride\"", StringComparison.Ordinal) < 0) before.rainVisualOverride = -1;
            RequireSameAuthored(before, ReadHost());
            Resume(Find<XRayWeatherController>(SceneManager.GetActiveScene()), before);
            return WriteHost(archive, "HostRestored", before);
        }

        public static string InspectHost()
        {
            return JsonUtility.ToJson(ReadHost(), true);
        }

        private static void Guard(string archive)
        {
            if (!Path.GetFullPath(archive).StartsWith(ARCHIVE, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(archive)) throw new InvalidOperationException("Select an existing task folder in the project Archive.");
            if (Application.dataPath != PRODUCTION || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer) throw new InvalidOperationException("Celestial validation requires the idle Production Editor.");
            for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Preserve the dirty scene manually before validation: " + SceneManager.GetSceneAt(i).path);
            if (!SceneManager.GetActiveScene().path.EndsWith("/l07_military/Scenes/l07_military.unity", StringComparison.Ordinal)) throw new InvalidOperationException("The active Production scene must be l07_military.");
        }

        private static void RequireFixture(string scenePath)
        {
            if (!scenePath.StartsWith(FIXTURES, StringComparison.Ordinal) || !File.Exists(scenePath)) throw new InvalidOperationException("Select the saved celestial fixture.");
        }

        private static void GetProfiles(XRayWeatherController controller, XRayWeatherCatalog catalog, out XRayWeatherProfile clear, out XRayWeatherProfile rain)
        {
            if (catalog != null)
            {
                clear = null;
                rain = null;
                foreach (XRayWeatherProfile candidate in catalog.Profiles)
                {
                    if (candidate.CycleName == "w_clear1") clear = candidate;
                    if (candidate.CycleName == "w_rain1") rain = candidate;
                }
            }
            else
            {
                var profile = new SerializedObject(controller).FindProperty("_profile").objectReferenceValue as XRayWeatherProfile;
                string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(profile))?.Replace('\\', '/');
                clear = AssetDatabase.LoadAssetAtPath<XRayWeatherProfile>(folder + "/w_clear1.asset");
                rain = AssetDatabase.LoadAssetAtPath<XRayWeatherProfile>(folder + "/w_rain1.asset");
            }
            if (clear == null || rain == null || clear.SourceIdentity != rain.SourceIdentity || !XRayWeatherImporter.HasSerializedResources(clear) || !XRayWeatherImporter.HasSerializedResources(rain)) throw new InvalidOperationException("Current military clear/rain profiles and serialized resources are required.");
        }

        private static T Find<T>(Scene scene) where T : Component
        {
            T result = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (T candidate in root.GetComponentsInChildren<T>(true))
                {
                    if (result != null) throw new InvalidOperationException("The scene contains multiple " + typeof(T).Name + " components.");
                    result = candidate;
                }
            }
            if (result == null) throw new InvalidOperationException("The scene has no " + typeof(T).Name + ".");
            return result;
        }

        private static Behaviour[] Providers(XRayWeatherController controller)
        {
            var result = new List<Behaviour> { controller };
            var serialized = new SerializedObject(controller);
            foreach (string name in new[] { "_weatherInputs", "_sourceLighting", "_windProvider", "_skyRenderer", "_fogRenderer" })
            {
                if (!(serialized.FindProperty(name).objectReferenceValue is Behaviour provider)) throw new InvalidOperationException("Missing military weather reference " + name);
                result.Add(provider);
            }
            return result.ToArray();
        }

        private static string Authored(Behaviour behaviour)
        {
            string json = Regex.Replace(EditorJsonUtility.ToJson(behaviour), "\"(?:instanceID|instanceId|entityId|entityID)\":\\s*-?\\d+", "\"reference\":0");
            SerializedProperty property = new SerializedObject(behaviour).GetIterator();
            var references = new List<string>();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                Object reference = property.objectReferenceValue;
                references.Add(property.propertyPath + "=" + (reference == null ? "null" : GlobalObjectId.GetGlobalObjectIdSlow(reference).ToString()));
            }
            return json + "\n" + string.Join("\n", references);
        }

        private static HostRecord ReadHost()
        {
            Scene scene = SceneManager.GetActiveScene();
            XRayWeatherController controller = Find<XRayWeatherController>(scene);
            Behaviour[] providers = Providers(controller);
            return new HostRecord { scene = scene.path, dirty = scene.isDirty, roots = scene.rootCount, authored = Array.ConvertAll(providers, Authored), enabled = Array.ConvertAll(providers, value => value.enabled), preview = controller.PreviewActive, manager = XRayWeatherPreview.Controller == controller, profile = AssetDatabase.GetAssetPath(controller.ActiveProfile), seconds = controller.GameTimeSeconds, paused = controller.Paused, timeScale = controller.TimeScale, hasTimeScaleOverride = controller.TimeScaleOverride.HasValue, shaftsOverride = Encode(controller.SunShaftsOverride), flareOverride = Encode(controller.LensFlareOverride), precipitationOverride = Encode(controller.PrecipitationOverride), rainVisualOverride = Encode(controller.RainVisualOverride), selection = Array.ConvertAll(Selection.objects, value => GlobalObjectId.GetGlobalObjectIdSlow(value).ToString()) };
        }

        private static string WriteHost(string output, string label, HostRecord before)
        {
            HostRecord after = ReadHost();
            RequireSameAuthored(before, after);
            if (before.preview != after.preview || before.manager != after.manager || before.profile != after.profile || Math.Abs(before.seconds - after.seconds) > 0.000001 || before.paused != after.paused || before.timeScale != after.timeScale || before.hasTimeScaleOverride != after.hasTimeScaleOverride || before.shaftsOverride != after.shaftsOverride || before.flareOverride != after.flareOverride || before.precipitationOverride != after.precipitationOverride || before.rainVisualOverride != after.rainVisualOverride) throw new InvalidOperationException("Military runtime preview state changed during validation.");
            string path = Path.Combine(output, label + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(after, true));
            return path;
        }

        private static void RequireSameAuthored(HostRecord before, HostRecord after)
        {
            if (before.scene != after.scene || before.dirty != after.dirty || before.roots != after.roots || before.authored.Length != after.authored.Length) throw new InvalidOperationException("The military host scene changed during validation.");
            for (int i = 0; i < before.authored.Length; i++) if (before.authored[i] != after.authored[i] || before.enabled[i] != after.enabled[i]) throw new InvalidOperationException("The authored provider or its exact object references changed at index " + i);
        }

        private static void Resume(XRayWeatherController controller, HostRecord state)
        {
            if (XRayWeatherPreview.Controller == controller) XRayWeatherPreview.Stop();
            controller.EndPreview();
            if (state.preview && !(state.manager ? XRayWeatherPreview.Start(controller) : controller.BeginPreview())) throw new InvalidOperationException("Cannot resume military preview: " + controller.Diagnostic);
            XRayWeatherProfile profile = AssetDatabase.LoadAssetAtPath<XRayWeatherProfile>(state.profile);
            controller.SetProfile(profile);
            controller.Seek(state.seconds);
            controller.SetPaused(state.paused);
            if (state.hasTimeScaleOverride) controller.SetTimeScale(state.timeScale);
            else controller.ClearTimeScaleOverride();
            controller.SetCelestialEffectOverrides(Decode(state.shaftsOverride), Decode(state.flareOverride));
            controller.SetPrecipitationOverride(Decode(state.precipitationOverride));
            controller.SetRainVisualOverride(Decode(state.rainVisualOverride));
            var selected = new List<Object>();
            foreach (string text in state.selection)
            {
                if (!GlobalObjectId.TryParse(text, out GlobalObjectId id)) continue;
                Object value = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
                if (value != null) selected.Add(value);
            }
            Selection.objects = selected.ToArray();
        }

        private static int Encode(bool? value)
        {
            return value.HasValue ? value.Value ? 1 : 0 : -1;
        }

        private static bool? Decode(int value)
        {
            return value < 0 ? (bool?)null : value != 0;
        }

        internal sealed class HostScope
        {
            private readonly Scene _scene;
            private readonly Behaviour[] _providers;

            private bool _isolated;

            public XRayWeatherController Controller { get; }
            public HostRecord Before { get; }

            public HostScope()
            {
                _scene = SceneManager.GetActiveScene();
                Controller = Find<XRayWeatherController>(_scene);
                if (Controller.IsTransitioning || Controller.ActiveFx != null || XRayWeatherController.Active != null && XRayWeatherController.Active != Controller) throw new InvalidOperationException("Preserve the current weather transition, active WeatherFX or foreign owner before validation.");
                _providers = Providers(Controller);
                Before = ReadHost();
            }

            public void Isolate()
            {
                _isolated = true;
                if (XRayWeatherPreview.Controller == Controller) XRayWeatherPreview.Stop();
                Controller.EndPreview();
                foreach (Behaviour provider in _providers) provider.enabled = false;
                if (XRayWeatherController.Active != null) throw new InvalidOperationException("Military weather did not release ownership.");
            }

            public void BeginHostPreview()
            {
                _isolated = true;
                if (XRayWeatherPreview.Controller == Controller) XRayWeatherPreview.Stop();
                Controller.EndPreview();
                if (!Controller.BeginPreview()) throw new InvalidOperationException(Controller.Diagnostic);
            }

            public void Restore()
            {
                if (!_isolated) return;
                if (SceneManager.GetActiveScene() != _scene && !SceneManager.SetActiveScene(_scene)) throw new InvalidOperationException("Cannot restore the active military scene.");
                for (int i = 1; i < _providers.Length; i++) _providers[i].enabled = Before.enabled[i];
                Controller.enabled = Before.enabled[0];
                Resume(Controller, Before);
            }
        }

        [Serializable]
        internal sealed class HostRecord
        {
            public string scene;
            public bool dirty;
            public int roots;
            public string[] authored;
            public bool[] enabled;
            public bool preview;
            public bool manager;
            public string profile;
            public double seconds;
            public bool paused;
            public float timeScale;
            public bool hasTimeScaleOverride;
            public int shaftsOverride;
            public int flareOverride;
            public int precipitationOverride = -1;
            public int rainVisualOverride = -1;
            public string[] selection;
        }
    }
}

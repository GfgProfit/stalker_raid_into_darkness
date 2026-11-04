using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using XRay.Importer.Core.Level;
using XRay.Importer.Levels;
using XRay.Importer.Sources.Vfs;
#if XRAY_HDRP
using UnityEngine.Rendering.HighDefinition;
#endif

namespace XRay.Importer.Editor.Levels
{
    public sealed class XRayLevelChunkRenderer
    {
        public int Sector { get; set; }
        public string Name { get; set; }
        public Mesh Mesh { get; set; }
        public Material[] Materials { get; set; }
        public bool CastShadows { get; set; }
    }

    public sealed class XRayLevelSceneContent
    {
        public string LevelName { get; set; }
        public XRayLevelData Level { get; set; }
        public string SourceFolder { get; set; }
        public Bounds Bounds { get; set; }
        public List<XRayLevelChunkRenderer> Chunks { get; } = new List<XRayLevelChunkRenderer>();
        public XRayInstanceBatchSet Instances { get; set; }
        public XRayDetailDatabase Details { get; set; }
        public XRaySourcePlan DetailSettingsSource { get; set; }
        public bool FloraWindAccepted { get; set; }
        public XRayWeatherProfile WeatherProfile { get; set; }
        public XRayWeatherCatalog WeatherCatalog { get; set; }
        public List<XRayLevelCollisionChunk> Collision { get; } = new List<XRayLevelCollisionChunk>();
        public bool ImportLights { get; set; }
        public bool LightShadows { get; set; }
        public bool OpenScene { get; set; }
    }

    public static class XRayLevelSceneBuilder
    {
        public const float FIXED_EXPOSURE = 9.0f;
        public const float SKY_EXPOSURE = 10.5f;
        public const float SUN_LUX = 3000.0f;
        public const float POINT_CANDELA_SCALE = 1150.0f;

        public static GameObject BuildRoot(Scene scene, XRayLevelSceneContent content, XRayLevelImportRecord record)
        {
            var root = new GameObject(content.LevelName);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform geometry = Child(root.transform, "Geometry");
            var sectors = new Dictionary<int, Transform>();
            var decals = new List<MeshRenderer>();
            foreach (XRayLevelChunkRenderer chunk in content.Chunks)
            {
                if (!sectors.TryGetValue(chunk.Sector, out Transform sector))
                {
                    sector = Child(geometry, $"Sector_{chunk.Sector:00}");
                    sectors.Add(chunk.Sector, sector);
                }

                var go = new GameObject(chunk.Name);
                go.transform.SetParent(sector, false);
                go.AddComponent<MeshFilter>().sharedMesh = chunk.Mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = chunk.Materials;
                foreach (Material material in chunk.Materials)
                {
                    if (material != null && material.shader.name == XRayLevelMaterialLibrary.DECAL_SHADER_NAME)
                    {
                        decals.Add(renderer);
                        break;
                    }
                }
                renderer.shadowCastingMode = chunk.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            }

            if (decals.Count > 0)
            {
                geometry.gameObject.AddComponent<XRayLevelDecalOrdering>().Initialize(decals.ToArray());
            }

            if (content.Instances != null)
            {
                Transform props = Child(root.transform, "StaticProps");
                var renderer = props.gameObject.AddComponent<XRayLevelInstancedRenderer>();
                renderer.BatchSet = content.Instances;
            }

            if (content.Details != null)
            {
                Transform details = Child(root.transform, "DetailVegetation");
                var vegetation = details.gameObject.AddComponent<XRayDetailVegetation>();
                vegetation.Database = content.Details;
                if (content.Details.HasRuntimeSource)
                {
                    var controller = details.gameObject.AddComponent<XRayDetailVegetationController>();
                    controller.Initialize(vegetation);
                    if (content.Details.SourceProfile == 1)
                    {
                        if (content.DetailSettingsSource == null) record.AddLimitation("SOURCE INPUT UNCERTAIN: effective RVM import source is unavailable; frozen grass profile retained.");
                        else if (XRayRvmGrassSource.TryRead(out string json, out string status, content.DetailSettingsSource)) controller.InitializeRvmSnapshot(json);
                        else record.AddLimitation(status);
                    }
                }
            }

            Transform lighting = Child(root.transform, "Lighting");
            CreateLights(lighting, content, record);
            Transform environment = Child(root.transform, "Environment");
            CreateEnvironment(environment, content, record);
            if (content.FloraWindAccepted && (record.Source.FloraWindProfile == XRayFloraSourceProfile.AnomalySss.ToString() || record.Source.FloraWindProfile == XRayFloraSourceProfile.TrueStalker.ToString()))
            {
                var wind = Child(environment, "Source Flora Wind").gameObject.AddComponent<XRayFloraWindProvider>();
                wind.Profile = record.Source.FloraWindProfile == XRayFloraSourceProfile.AnomalySss.ToString() ? XRayFloraWindProfile.AnomalySsfx : XRayFloraWindProfile.TrueStalkerXRay;
                wind.ResetAnimation();
            }
            if (content.WeatherProfile != null)
            {
                if (content.WeatherProfile.Validate(out string diagnostic))
                {
                    XRayWeatherController controller = ConfigureWeather(root, content.WeatherProfile);
                    controller.ConfigureCatalog(content.WeatherCatalog);
#if XRAY_HDRP
                    if (XRayWeatherImporter.HasSerializedResources(content.WeatherProfile))
                    {
                        string path = XRayLevelPaths.Scenes(content.LevelName) + "/" + content.LevelName + "_WeatherSky.asset";
                        if (ConfigureWeatherSky(root, controller, content.WeatherProfile, path))
                        {
                            record.GeneratedAssets.Add(path);
                            record.AddLimitation("RVM weather controls serialized sky/environment resources and source light/material/wind inputs. Authored HDRP exposure and night ambient extension remain Unity presentation settings; original framebuffer parity and precipitation are not implemented.");
                        }
                        else
                        {
                            record.AddLimitation("Weather sky connection skipped: the authored environment has no active Gradient Sky; its existing sky is preserved while source light/material/wind inputs remain connected.");
                        }
                    }
                    else
#endif
                    {
                        record.AddLimitation("INPUT BLOCKED: the selected weather profile has no complete serialized sky/environment resources; source light/material/wind inputs remain connected and the authored HDRP sky is retained.");
                    }
                }
                else
                {
                    record.AddLimitation("Weather profile rejected: " + diagnostic);
                }
            }
            if (content.Collision.Count > 0)
            {
                Transform collision = Child(root.transform, "Collision");
                foreach (XRayLevelCollisionChunk chunk in content.Collision)
                {
                    var go = new GameObject(chunk.Name);
                    go.transform.SetParent(collision, false);
                    go.AddComponent<MeshCollider>().sharedMesh = chunk.Mesh;
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.NavigationStatic);
                }
            }

            Transform metadata = Child(root.transform, "LevelMetadata");
            var info = metadata.gameObject.AddComponent<XRayLevelMetadata>();
            var sectorInfos = new List<XRayLevelSectorInfo>();
            foreach (XRayLevelSector sector in content.Level.Sectors)
            {
                Bounds bounds = sector.RootVisual >= 0 ? VisualBounds(content.Level.Visuals[sector.RootVisual]) : new Bounds();
                sectorInfos.Add(new XRayLevelSectorInfo(sector.Index, sector.RootVisual, sector.Portals.ToArray(), bounds));
            }

            var portals = new List<XRayLevelPortalInfo>();
            foreach (XRayLevelPortal portal in content.Level.Portals)
            {
                var vertices = new Vector3[portal.Vertices.Count];
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = new Vector3((float)portal.Vertices[i].X, (float)portal.Vertices[i].Y, (float)portal.Vertices[i].Z);
                }

                portals.Add(new XRayLevelPortalInfo(portal.SectorFront, portal.SectorBack, vertices));
            }

            info.Initialize(content.LevelName, content.Level.Version, content.SourceFolder, System.DateTime.UtcNow.ToString("u"), content.Bounds, sectorInfos, portals, new List<string>(record.Limitations));
            return root;
        }

        public static XRayWeatherController ConfigureWeather(GameObject root, XRayWeatherProfile profile)
        {
            if (root == null) throw new System.ArgumentNullException(nameof(root));
            if (profile == null) throw new System.ArgumentNullException(nameof(profile));
            if (!profile.Validate(out string diagnostic)) throw new System.ArgumentException(diagnostic, nameof(profile));
            XRayWeatherController[] controllers = root.GetComponentsInChildren<XRayWeatherController>(true);
            if (controllers.Length > 1) throw new System.InvalidOperationException("The import root contains multiple weather controllers; select one owner before connecting weather.");
            XRayWeatherController controller = controllers.Length == 1 ? controllers[0] : null;
            GameObject owner;
            bool created = controller == null;
            if (created)
            {
                Transform environment = root.transform.Find("Environment");
                if (environment == null) environment = Child(root.transform, "Environment");
                owner = new GameObject("Source Weather");
                owner.SetActive(false);
                owner.transform.SetParent(environment, false);
            }
            else
            {
                owner = controller.gameObject;
            }

            MonolithWeatherInputs weather = root.GetComponentInChildren<MonolithWeatherInputs>(true);
            if (weather == null) weather = owner.AddComponent<MonolithWeatherInputs>();
            MonolithSourceLighting lighting = root.GetComponentInChildren<MonolithSourceLighting>(true);
            if (lighting == null)
            {
                lighting = owner.AddComponent<MonolithSourceLighting>();
                lighting.BoundRadianceScale = MonolithSourceLighting.DEFAULT_WEATHER_RADIANCE_SCALE;
                lighting.Mode = MonolithSourceLighting.LightingMode.Bound;
            }
            XRayFloraWindProvider wind = root.GetComponentInChildren<XRayFloraWindProvider>(true);
            if (wind == null) wind = owner.AddComponent<XRayFloraWindProvider>();
            Light sun = null;
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
            {
                if (light.type != LightType.Directional) continue;
                sun = light;
                break;
            }
            if (sun == null)
            {
                var sunObject = new GameObject("Weather Sun (HDRP diagnostic intensity)");
                sunObject.transform.SetParent(owner.transform, false);
                sun = sunObject.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.lightUnit = LightUnit.Lux;
                sun.intensity = SUN_LUX;
#if XRAY_HDRP
                sunObject.AddComponent<HDAdditionalLightData>();
#endif
            }
            if (created) controller = owner.AddComponent<XRayWeatherController>();
            controller.Configure(profile, weather, lighting, wind, sun);
#if XRAY_HDRP
            ConfigureWeatherFog(controller);
#endif
            if (created) owner.SetActive(true);
            return controller;
        }

#if XRAY_HDRP
        public static XRayWeatherFogRenderer ConfigureWeatherFog(XRayWeatherController controller)
        {
            return XRayWeatherFogSetup.ConfigureController(controller);
        }

        public static bool ConfigureWeatherSky(GameObject root, XRayWeatherController controller, XRayWeatherProfile profile, string assetPath)
        {
            if (root == null || controller == null) throw new System.ArgumentNullException(nameof(root));
            Volume authoredVolume = null;
            foreach (Volume candidate in root.GetComponentsInChildren<Volume>(true))
            {
                if (!candidate.isGlobal || candidate.sharedProfile == null || !candidate.sharedProfile.TryGet(out GradientSky gradient) || !gradient.active) continue;
                if (authoredVolume == null || candidate.priority > authoredVolume.priority) authoredVolume = candidate;
            }
            if (authoredVolume == null) return false;
            authoredVolume.sharedProfile.TryGet(out GradientSky authored);
            Shader shader = Shader.Find("XRay/Monolith Weather Sky");
            if (shader == null || !shader.isSupported) throw new System.InvalidOperationException("The X-Ray weather sky shader must compile before importing the level environment.");
            VolumeProfile skyProfile = XRayWeatherSkySetup.GetOrCreateSkyProfile(profile, (float)(controller.GameTimeSeconds / 3600.0), shader, authored, authoredVolume.sharedProfile, assetPath);
            Transform child = controller.transform.Find("Source Weather Sky");
            if (child == null) child = Child(controller.transform, "Source Weather Sky");
            child.gameObject.layer = XRayWeatherFogRenderer.GetVolumeLayer(controller.gameObject);
            Volume volume = child.GetComponent<Volume>();
            if (volume == null) volume = child.gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = authoredVolume.priority + 1.0f;
            volume.sharedProfile = skyProfile;
            XRayWeatherSkyRenderer renderer = controller.GetComponent<XRayWeatherSkyRenderer>();
            if (renderer == null) renderer = controller.gameObject.AddComponent<XRayWeatherSkyRenderer>();
            renderer.Configure(volume, shader);
            controller.ConfigureSky(renderer);
            return true;
        }
#endif

        public static Bounds VisualBounds(XRayLevelVisual visual)
        {
            var min = new Vector3(visual.BoundsMin[0], visual.BoundsMin[1], visual.BoundsMin[2]);
            var max = new Vector3(visual.BoundsMax[0], visual.BoundsMax[1], visual.BoundsMax[2]);
            return new Bounds((min + max) * 0.5f, max - min);
        }

        private static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void CreateLights(Transform parent, XRayLevelSceneContent content, XRayLevelImportRecord record)
        {
            int created = 0;
            bool sun = false;
            Transform staticLights = null;
            foreach (XRayLevelLight source in content.Level.Lights)
            {
                if (source.Type == XRayLevelFormat.D3D_LIGHT_DIRECTIONAL)
                {
                    if (sun)
                    {
                        continue;
                    }

                    sun = true;
                    var direction = new Vector3(source.Direction[0], source.Direction[1], source.Direction[2]);
                    if (direction.sqrMagnitude < 1e-6f)
                    {
                        direction = new Vector3(0.3f, -1.0f, 0.4f);
                    }

                    var sunObject = new GameObject("Sun (xrLC build direction, neutral intensity)");
                    sunObject.transform.SetParent(parent, false);
                    sunObject.transform.rotation = Quaternion.LookRotation(direction.normalized);
                    Light light = sunObject.AddComponent<Light>();
                    light.type = LightType.Directional;
                    light.color = Color.white;
                    light.lightUnit = LightUnit.Lux;
                    light.intensity = SUN_LUX;
                    light.shadows = LightShadows.Soft;
#if XRAY_HDRP
                    sunObject.AddComponent<HDAdditionalLightData>();
#endif
                    continue;
                }

                if (!content.ImportLights)
                {
                    continue;
                }

                if (staticLights == null)
                {
                    staticLights = Child(parent, "StaticLights");
                }

                var go = new GameObject($"Light_{source.Index:000}_{(source.Type == XRayLevelFormat.D3D_LIGHT_SPOT ? "spot" : "point")}");
                go.transform.SetParent(staticLights, false);
                go.transform.position = new Vector3(source.Position[0], source.Position[1], source.Position[2]);
                Light point = go.AddComponent<Light>();
                bool spot = source.Type == XRayLevelFormat.D3D_LIGHT_SPOT;
                point.type = spot ? LightType.Spot : LightType.Point;
                float brightness = Mathf.Max(source.Diffuse[0], Mathf.Max(source.Diffuse[1], source.Diffuse[2]));
                point.color = brightness > 1e-5f ? new Color(source.Diffuse[0] / brightness, source.Diffuse[1] / brightness, source.Diffuse[2] / brightness) : Color.black;
                float range = Mathf.Max(0.1f, source.Range);
                point.range = range;
                point.lightUnit = LightUnit.Candela;
                point.intensity = POINT_CANDELA_SCALE * (range * 0.5f) * (range * 0.5f) * brightness;
                point.shadows = content.LightShadows ? LightShadows.Soft : LightShadows.None;
                if (spot)
                {
                    var direction = new Vector3(source.Direction[0], source.Direction[1], source.Direction[2]);
                    if (direction.sqrMagnitude > 1e-6f)
                    {
                        go.transform.rotation = Quaternion.LookRotation(direction.normalized);
                    }

                    point.spotAngle = Mathf.Clamp(source.Phi * Mathf.Rad2Deg, 1.0f, 179.0f);
                    point.innerSpotAngle = Mathf.Clamp(source.Theta * Mathf.Rad2Deg, 0.0f, point.spotAngle);
                }

#if XRAY_HDRP
                go.AddComponent<HDAdditionalLightData>();
#endif
                created++;
            }

            record.SetMetric("LightsCreated", created);
            if (!sun)
            {
                record.AddLimitation("The level has no build-time sun (LIGHT_DYNAMIC controller 1); no directional light was created.");
            }
        }

        private static void CreateEnvironment(Transform parent, XRayLevelSceneContent content, XRayLevelImportRecord record)
        {
#if XRAY_HDRP
            string path = XRayLevelPaths.Scenes(content.LevelName) + "/" + content.LevelName + "_Environment.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
                VisualEnvironment visual = profile.Add<VisualEnvironment>(true);
                visual.skyType.Override((int)SkyType.Gradient);
                GradientSky sky = profile.Add<GradientSky>(true);
                sky.top.Override(new Color(0.35f, 0.4f, 0.48f));
                sky.middle.Override(new Color(0.55f, 0.55f, 0.55f));
                sky.bottom.Override(new Color(0.18f, 0.17f, 0.15f));
                sky.exposure.Override(SKY_EXPOSURE);
                sky.updateMode.Override(EnvironmentUpdateMode.OnChanged);
                Exposure exposure = profile.Add<Exposure>(true);
                exposure.mode.Override(ExposureMode.Fixed);
                exposure.fixedExposure.Override(FIXED_EXPOSURE);
                foreach (VolumeComponent component in profile.components)
                {
                    AssetDatabase.AddObjectToAsset(component, profile);
                }

                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
            }

            var volumeObject = new GameObject("Neutral Test Environment");
            volumeObject.transform.SetParent(parent, false);
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10.0f;
            volume.sharedProfile = profile;
            record.GeneratedAssets.Add(path);
#endif
            if (content.WeatherProfile == null || !XRayWeatherImporter.HasSerializedResources(content.WeatherProfile)) record.AddLimitation("Environment: neutral test lighting (gradient sky, fixed exposure EV100 9, gradient sky exposure 10.5, 3000 lux sun). True Stalker weather (configs/environment) is not imported and this is not the original game weather.");
        }

        public static void FrameSceneView(Bounds bounds)
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null)
            {
                return;
            }

            view.Frame(bounds, false);
        }
    }
}

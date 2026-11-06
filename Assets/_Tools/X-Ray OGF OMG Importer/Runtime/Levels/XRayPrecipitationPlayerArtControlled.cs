using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Levels
{
    public sealed partial class XRayPrecipitationPlayerSafetyProbe
    {
        private GameObject _artControlledRoot;
        private HDAdditionalReflectionData _artControlledProbe;
        private PlanarReflectionProbe _artControlledPlanar;
        private int _artControlledRequests;

        private readonly List<UnityEngine.Object> _artControlledResources = new List<UnityEngine.Object>();
        private readonly List<RenderTexture> _artControlledNativeTextures = new List<RenderTexture>();

        private ArtStudy CreateControlledArtStudy()
        {
            _artControlledNativeTextures.Clear();
            var selected = new Material[3];
            foreach (GameObject root in _controller.gameObject.scene.GetRootGameObjects())
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false))
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null || !material.HasProperty("_MonolithWetResponse")) continue;
                        XRayWetSurfaceType surface = XRayWetMaterialBindings.Classify(material);
                        int index = surface == XRayWetSurfaceType.Soil ? 0 : surface == XRayWetSurfaceType.Asphalt ? 1 : surface == XRayWetSurfaceType.Concrete ? 2 : -1;
                        if (index >= 0 && selected[index] == null) selected[index] = material;
                    }
                }
            }
            Require(selected[0] != null && selected[2] != null, "Controlled native material studies require real Military soil and concrete source materials.");
            if (selected[1] == null) selected[1] = selected[0];
            _artControlledRoot = new GameObject("Owned Controlled Puddle Study");
            SceneManager.MoveGameObjectToScene(_artControlledRoot, _controller.gameObject.scene);
            _artControlledRoot.transform.position = new Vector3(-16.0f, 30.0f, 272.0f);
            string[] names = { "Soil", "Asphalt", "Concrete" };
            for (int index = 0; index < selected.Length; index++)
            {
                Material material = ControlledMaterial(selected[index], names[index], index);
                Mesh mesh = ControlledDepression();
                _artControlledResources.Add(mesh);
                var panel = new GameObject(names[index] + " Depression");
                panel.transform.SetParent(_artControlledRoot.transform, false);
                panel.transform.localPosition = new Vector3((index - 1) * 8.0f, 0.0f, 0.0f);
                panel.transform.localScale = new Vector3(1.0f, 1.0f, 3.0f);
                panel.AddComponent<MeshFilter>().sharedMesh = mesh;
                panel.AddComponent<MeshRenderer>().sharedMaterial = material;
            }
            string white = ControlledSource("WhiteSphere", LightType.Point, new Vector3(-3.0f, 1.4f, 1.0f), Color.white, 0.18f, new Vector3(0.36f, 0.36f, 0.36f), selected[2]);
            string orange = ControlledSource("WarmCapsule", LightType.Point, new Vector3(0.0f, 1.0f, 1.0f), new Color(1.0f, 0.32f, 0.05f), 0.25f, new Vector3(0.5f, 0.9f, 0.5f), selected[2]);
            string spot = ControlledSource("SpotLamp", LightType.Spot, new Vector3(3.0f, 2.0f, 1.0f), new Color(0.7f, 0.8f, 1.0f), 0.06f, new Vector3(0.12f, 0.12f, 0.12f), selected[2]);
            string rectangle = ControlledSource("RectangleEmitter", LightType.Rectangle, new Vector3(5.0f, 2.2f, -1.0f), new Color(1.0f, 0.85f, 0.6f), 0.0f, new Vector3(1.2f, 0.4f, 0.03f), selected[2]);
            var probeObject = new GameObject("Controlled Native Probe");
            probeObject.transform.SetParent(_artControlledRoot.transform, false);
            probeObject.transform.localPosition = new Vector3(0.0f, 1.5f, 0.0f);
            probeObject.layer = XRayWeatherFogRenderer.GetVolumeLayer(_controller.gameObject);
            probeObject.AddComponent<ReflectionProbe>();
            _artControlledProbe = probeObject.AddComponent<HDAdditionalReflectionData>();
            probeObject.SetActive(false);
            _artControlledProbe.mode = ProbeSettings.Mode.Realtime;
            _artControlledProbe.realtimeMode = ProbeSettings.RealtimeMode.OnDemand;
            _artControlledProbe.timeSlicing = true;
            _artControlledProbe.influenceVolume.shape = InfluenceShape.Box;
            _artControlledProbe.influenceVolume.boxSize = new Vector3(32.0f, 12.0f, 32.0f);
            _artControlledProbe.settingsRaw.proxySettings.useInfluenceVolumeAsProxyVolume = true;
            _artControlledProbe.settingsRaw.cubeResolution.useOverride = true;
            _artControlledProbe.settingsRaw.cubeResolution.@override = (CubeReflectionResolution)128;
            _artControlledProbe.settingsRaw.cameraSettings.frustum.nearClipPlaneRaw = 0.1f;
            _artControlledProbe.settingsRaw.cameraSettings.frustum.farClipPlaneRaw = 50.0f;
            _artControlledProbe.settingsRaw.cameraSettings.volumes.layerMask = 1 << probeObject.layer;
            _artControlledProbe.settingsRaw.cameraSettings.volumes.anchorOverride = probeObject.transform;
            _artControlledProbe.settingsRaw.cameraSettings.culling.useOcclusionCulling = false;
            _artControlledProbe.importance = 100;
            _artControlledProbe.weight = 1.0f;
            probeObject.SetActive(true);
            _artControlledProbe.RequestRenderNextUpdate();
            _artControlledRequests = 1;
            CreateControlledPlanar();
            Vector3 position = _artControlledRoot.transform.position + new Vector3(0.0f, 1.65f, -17.0f);
            Vector3 low = _artControlledRoot.transform.position + new Vector3(0.0f, 0.45f, -12.0f);
            Vector3 target = _artControlledRoot.transform.position + new Vector3(0.0f, 0.0f, 1.0f);
            WriteControlledGeometry(position, low);
            var directional = new List<string>();
            foreach (GameObject root in _controller.gameObject.scene.GetRootGameObjects()) foreach (Light light in root.GetComponentsInChildren<Light>(false)) if (light.type == LightType.Directional) directional.Add(ControlledPath(light.transform));
            return new ArtStudy { name = "ControlledSyntheticDepressions", context = "Runtime_only_calibrated_fixture_not_real_campfire; three8x24m_depressions; source_texture_frequency_preserved; no_saved_scene_assets; owned_native_cube_and_horizontal_planar;16_bracketing_mirror_hits_inside_panels", surface = "Soil_Asphalt_Concrete_8x24m_depressions_depth0.1m", fov = 75.0f, position = position, rotation = Quaternion.LookRotation(target - position), lowPosition = low, lowRotation = Quaternion.LookRotation(target - low), lights = new[] { white, orange, spot, rectangle }, directionalLights = directional.ToArray(), emitters = new[] { white + "/VisibleEmitter", orange + "/VisibleEmitter", spot + "/VisibleEmitter", rectangle + "/VisibleEmitter" } };
        }

        private void CreateControlledPlanar()
        {
            var root = new GameObject("Controlled Native Planar Probe");
            root.transform.SetParent(_artControlledRoot.transform, false);
            root.transform.localPosition = new Vector3(0.0f, -0.045f, 0.0f);
            root.layer = XRayWeatherFogRenderer.GetVolumeLayer(_controller.gameObject);
            _artControlledPlanar = root.AddComponent<PlanarReflectionProbe>();
            root.SetActive(false);
            _artControlledPlanar.mode = ProbeSettings.Mode.Realtime;
            _artControlledPlanar.realtimeMode = ProbeSettings.RealtimeMode.EveryFrame;
            _artControlledPlanar.timeSlicing = false;
            _artControlledPlanar.influenceVolume.shape = InfluenceShape.Box;
            _artControlledPlanar.influenceVolume.boxSize = new Vector3(32.0f, 2.0f, 32.0f);
            _artControlledPlanar.influenceVolume.boxBlendDistancePositive = new Vector3(2.0f, 0.1f, 2.0f);
            _artControlledPlanar.influenceVolume.boxBlendDistanceNegative = new Vector3(2.0f, 0.1f, 2.0f);
            _artControlledPlanar.settingsRaw.proxySettings.useInfluenceVolumeAsProxyVolume = true;
            _artControlledPlanar.settingsRaw.proxySettings.mirrorPositionProxySpace = Vector3.zero;
            _artControlledPlanar.settingsRaw.proxySettings.mirrorRotationProxySpace = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            _artControlledPlanar.settingsRaw.resolutionScalable.useOverride = true;
            _artControlledPlanar.settingsRaw.resolutionScalable.@override = (PlanarReflectionAtlasResolution)256;
            _artControlledPlanar.settingsRaw.roughReflections = true;
            _artControlledPlanar.settingsRaw.frustum.fieldOfViewMode = ProbeSettings.Frustum.FOVMode.Viewer;
            _artControlledPlanar.settingsRaw.frustum.viewerScale = 1.0f;
            _artControlledPlanar.settingsRaw.cameraSettings.frustum.nearClipPlaneRaw = 0.05f;
            _artControlledPlanar.settingsRaw.cameraSettings.frustum.farClipPlaneRaw = 50.0f;
            _artControlledPlanar.settingsRaw.cameraSettings.volumes.layerMask = 1 << root.layer;
            _artControlledPlanar.settingsRaw.cameraSettings.volumes.anchorOverride = _camera.transform;
            _artControlledPlanar.settingsRaw.cameraSettings.culling.useOcclusionCulling = false;
            _artControlledPlanar.importance = 128;
            _artControlledPlanar.multiplier = 1.0f;
            _artControlledPlanar.weight = 1.0f;
            _artControlledPlanar.fadeDistance = 64.0f;
            root.SetActive(true);
            _artControlledPlanar.PrepareCulling();
        }

        private void WriteControlledGeometry(Vector3 eyePosition, Vector3 lowPosition)
        {
            var evidence = new ControlledMirrorGeometry { coordinateSpace = "fixture_local", rootPosition = _artControlledRoot.transform.position, panelWidth = 8.0f, panelLength = 24.0f, nominalDepth = 0.1f, scale = new Vector3(1.0f, 1.0f, 3.0f), uvMultiplier = new Vector2(1.0f, 3.0f), textureCyclesPerMeter = new Vector2(0.25f, 0.25f), horizontalMinimum = new Vector2(-12.0f, -12.0f), horizontalMaximum = new Vector2(12.0f, 12.0f), planarHeight = -0.045f, allInside = true };
            Vector3[] viewers = { eyePosition - evidence.rootPosition, lowPosition - evidence.rootPosition };
            string[] sourceNames = { "WhiteSphere", "WarmCapsule", "SpotLamp", "RectangleEmitter" };
            Vector3[] sources = { new Vector3(-3.0f, 1.4f, 1.0f), new Vector3(0.0f, 1.0f, 1.0f), new Vector3(3.0f, 2.0f, 1.0f), new Vector3(5.0f, 2.2f, -1.0f) };
            float[] planes = { 0.0f, -0.1f };
            for (int view = 0; view < viewers.Length; view++)
            {
                for (int source = 0; source < sources.Length; source++)
                {
                    foreach (float plane in planes)
                    {
                        Vector3 mirrored = sources[source];
                        mirrored.y = 2.0f * plane - mirrored.y;
                        float ratio = (viewers[view].y - plane) / (viewers[view].y - mirrored.y);
                        Vector3 hit = Vector3.LerpUnclamped(viewers[view], mirrored, ratio);
                        bool inside = Mathf.Abs(hit.x) <= 12.0f && Mathf.Abs(hit.z) <= 12.0f && ratio > 0.0f && ratio < 1.0f;
                        evidence.allInside &= inside;
                        evidence.hits.Add(new ControlledMirrorHit { viewer = view == 0 ? "Eye" : "Low", source = sourceNames[source], planeHeight = plane, viewerPosition = viewers[view], sourcePosition = sources[source], intersection = hit, insidePanelBounds = inside });
                    }
                }
            }
            Write(_artOutput, "ControlledGeometry", evidence);
            Require(evidence.allInside && evidence.hits.Count == 16, "All four emitter mirror rays must hit the controlled panels from eye and low cameras throughout the depression depth.");
        }

        private Material ControlledMaterial(Material source, string surface, int index)
        {
            Material material = new Material(source) { name = "Controlled " + surface + " from " + source.name, hideFlags = HideFlags.HideAndDontSave };
            _artControlledResources.Add(material);
            if (source.IsKeywordEnabled("_MONOLITH_LEVEL_TERRAIN"))
            {
                string channel = index == 1 ? "G" : "R";
                Texture color = source.GetTexture("_MonolithTerrainSourceColor" + channel);
                if (color == null) color = source.GetTexture("_MonolithTerrainDetail" + channel);
                if (color != null) material.SetTexture("_BaseColorMap", color);
                Texture bump = source.GetTexture("_MonolithTerrainBump" + channel);
                if (bump != null) material.SetTexture("_MonolithBump", bump);
            }
            foreach (string keyword in new[] { "_MONOLITH_LEVEL", "_MONOLITH_LEVEL_TERRAIN", "_MONOLITH_ORIGINAL_ENVIRONMENT" }) material.DisableKeyword(keyword);
            foreach (string property in new[] { "_MonolithLevel", "_MonolithLevelTerrain", "_MonolithEnvironmentMode", "_MonolithLevelHemiMode", "_MonolithSourceTerrain", "_MonolithTrueStalkerSourceTerrain" }) if (material.HasProperty(property)) material.SetFloat(property, 0.0f);
            material.SetColor("_EmissiveColor", Color.black);
            return material;
        }

        private string ControlledSource(string name, LightType type, Vector3 position, Color color, float radius, Vector3 emitterSize, Material source)
        {
            var root = new GameObject(name);
            root.transform.SetParent(_artControlledRoot.transform, false);
            root.transform.localPosition = position;
            if (type == LightType.Spot || type == LightType.Rectangle) root.transform.localRotation = Quaternion.LookRotation(Vector3.down);
            Light light = root.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.range = 12.0f;
            light.shapeRadius = radius;
            light.areaSize = new Vector2(emitterSize.x, emitterSize.y);
            light.lightUnit = type == LightType.Rectangle ? LightUnit.Lumen : LightUnit.Candela;
            light.intensity = 500.0f;
            light.spotAngle = 45.0f;
            light.innerSpotAngle = 30.0f;
            light.shadows = LightShadows.None;
            root.AddComponent<HDAdditionalLightData>();
            GameObject emitter = GameObject.CreatePrimitive(type == LightType.Rectangle ? PrimitiveType.Cube : PrimitiveType.Sphere);
            emitter.name = "VisibleEmitter";
            emitter.transform.SetParent(root.transform, false);
            emitter.transform.localScale = emitterSize;
            Collider collider = emitter.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            Material material = ControlledMaterial(source, "glass emitter", 2);
            material.SetColor("_BaseColor", Color.black);
            material.SetTexture("_BaseColorMap", Texture2D.blackTexture);
            material.SetColor("_EmissiveColor", color.linear * 40.0f);
            emitter.GetComponent<Renderer>().sharedMaterial = material;
            return ControlledPath(root.transform);
        }

        private static Mesh ControlledDepression()
        {
            const int SEGMENTS = 24;
            int count = SEGMENTS + 1;
            var vertices = new Vector3[count * count];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[SEGMENTS * SEGMENTS * 6];
            for (int z = 0; z < count; z++)
            {
                for (int x = 0; x < count; x++)
                {
                    int index = z * count + x;
                    float px = x * 8.0f / SEGMENTS - 4.0f;
                    float pz = z * 8.0f / SEGMENTS - 4.0f;
                    float basin = Mathf.Clamp01(1.0f - (px * px + pz * pz) / 16.0f);
                    vertices[index] = new Vector3(px, -0.1f * basin + 0.006f * Mathf.Sin(px * 2.0f) * Mathf.Sin(pz * 2.0f), pz);
                    uv[index] = new Vector2(px * 0.25f, pz * 0.75f);
                    if (x == SEGMENTS || z == SEGMENTS) continue;
                    int triangle = (z * SEGMENTS + x) * 6;
                    triangles[triangle] = index;
                    triangles[triangle + 1] = index + count;
                    triangles[triangle + 2] = index + 1;
                    triangles[triangle + 3] = index + 1;
                    triangles[triangle + 4] = index + count;
                    triangles[triangle + 5] = index + count + 1;
                }
            }
            var mesh = new Mesh { name = "Controlled 0.1m Depression", vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static string ControlledPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }

        private void ReleaseControlledArtStudy()
        {
            ReleaseControlledReflection(_artControlledProbe);
            ReleaseControlledReflection(_artControlledPlanar);
            if (_artControlledRoot != null)
            {
                _artControlledRoot.SetActive(false);
                Destroy(_artControlledRoot);
            }
            foreach (UnityEngine.Object resource in _artControlledResources) if (resource != null) Destroy(resource);
            _artControlledResources.Clear();
            _artControlledRoot = null;
            _artControlledProbe = null;
            _artControlledPlanar = null;
        }

        private void ReleaseControlledReflection(HDProbe probe)
        {
            if (probe == null) return;
            if (probe.realtimeTexture != null && !_artControlledNativeTextures.Contains(probe.realtimeTexture)) _artControlledNativeTextures.Add(probe.realtimeTexture);
            RenderTexture depth = probe.realtimeDepthTextureRTH?.rt;
            if (depth != null && !_artControlledNativeTextures.Contains(depth)) _artControlledNativeTextures.Add(depth);
            probe.enabled = false;
            if (!Application.isEditor)
            {
                probe.gameObject.AddComponent<XRayWetnessReflectionRelease>().Initialize(probe.realtimeTextureRTH, probe.realtimeDepthTextureRTH);
            }
        }

        [Serializable]
        private sealed class ControlledMirrorGeometry
        {
            public string coordinateSpace;
            public Vector3 rootPosition;
            public float panelWidth;
            public float panelLength;
            public float nominalDepth;
            public Vector3 scale;
            public Vector2 uvMultiplier;
            public Vector2 textureCyclesPerMeter;
            public Vector2 horizontalMinimum;
            public Vector2 horizontalMaximum;
            public float planarHeight;
            public bool allInside;
            public List<ControlledMirrorHit> hits = new List<ControlledMirrorHit>();
        }

        [Serializable]
        private sealed class ControlledMirrorHit
        {
            public string viewer;
            public string source;
            public float planeHeight;
            public Vector3 viewerPosition;
            public Vector3 sourcePosition;
            public Vector3 intersection;
            public bool insidePanelBounds;
        }
    }
}

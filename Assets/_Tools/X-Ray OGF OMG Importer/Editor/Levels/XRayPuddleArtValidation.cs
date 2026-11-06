using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Editor.Levels
{
    public static class XRayPuddleArtValidation
    {
        private const string PRODUCTION = "E:/Projects/stalker_raid_into_darkness/Assets";
        private const string ARCHIVE = @"E:\Projects\stalker_raid_into_darkness_Archive\";

        public static string InspectScene(string archive)
        {
            if (!string.Equals(Application.dataPath, PRODUCTION, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Inspect the Production project only.");
            string output = Path.GetFullPath(archive);
            if (!output.StartsWith(ARCHIVE, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(output)) throw new InvalidOperationException("Use the existing task Archive.");
            var report = new Inspection { unity = Application.unityVersion, project = Application.dataPath, playing = Application.isPlaying };
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                var record = new SceneInspection { path = scene.path, dirty = scene.isDirty, loaded = scene.isLoaded };
                report.scenes.Add(record);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Camera camera in root.GetComponentsInChildren<Camera>(true)) record.cameras.Add(new CameraInspection { path = HierarchyPath(camera.transform), enabled = camera.isActiveAndEnabled, position = camera.transform.position, rotation = camera.transform.rotation, fov = camera.fieldOfView });
                    foreach (Light light in root.GetComponentsInChildren<Light>(true))
                    {
                        HDAdditionalLightData data = light.GetComponent<HDAdditionalLightData>();
                        record.lights.Add(new LightInspection { path = HierarchyPath(light.transform), type = light.type.ToString(), enabled = light.isActiveAndEnabled, position = light.transform.position, rotation = light.transform.rotation, color = light.color, intensity = light.intensity, unit = light.lightUnit.ToString(), range = light.range, radius = light.shapeRadius, spotAngle = light.spotAngle, innerSpotAngle = light.innerSpotAngle, affectDiffuse = data == null || data.affectDiffuse, affectSpecular = data == null || data.affectSpecular, shadows = light.shadows.ToString() });
                    }
                    foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        Material[] materials = renderer.sharedMaterials;
                        MeshFilter filter = renderer.GetComponent<MeshFilter>();
                        for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                        {
                            Material material = materials[materialIndex];
                            if (material == null) continue;
                            Color emission = Color.black;
                            if (material.HasProperty("_EmissiveColor")) emission = material.GetColor("_EmissiveColor");
                            else if (material.HasProperty("_EmissionColor")) emission = material.GetColor("_EmissionColor");
                            string key = (renderer.name + " " + material.name + " " + material.shader.name).ToLowerInvariant();
                            bool named = key.Contains("fire") || key.Contains("flame") || key.Contains("camp") || key.Contains("burn") || key.Contains("lamp");
                            if (!named && emission.maxColorComponent <= 0.0f) continue;
                            Bounds materialBounds = renderer.bounds;
                            bool submeshBounds = filter != null && filter.sharedMesh != null && materialIndex < filter.sharedMesh.subMeshCount;
                            if (submeshBounds) materialBounds = WorldBounds(filter.sharedMesh.GetSubMesh(materialIndex).bounds, renderer.localToWorldMatrix);
                            record.emitters.Add(new EmitterInspection { path = HierarchyPath(renderer.transform), enabled = renderer.enabled && renderer.gameObject.activeInHierarchy, rendererType = renderer.GetType().Name, material = material.name, materialIndex = materialIndex, shader = material.shader.name, materialPath = AssetDatabase.GetAssetPath(material), emission = emission, bounds = materialBounds, submeshBounds = submeshBounds, namedCandidate = named });
                        }
                    }
                }
            }
            string path = Path.Combine(output, "SceneSources_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            return path;
        }

        private static Bounds WorldBounds(Bounds local, Matrix4x4 matrix)
        {
            Vector3 x = matrix.MultiplyVector(new Vector3(local.extents.x, 0.0f, 0.0f));
            Vector3 y = matrix.MultiplyVector(new Vector3(0.0f, local.extents.y, 0.0f));
            Vector3 z = matrix.MultiplyVector(new Vector3(0.0f, 0.0f, local.extents.z));
            Vector3 extents = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x), Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(matrix.MultiplyPoint3x4(local.center), extents * 2.0f);
        }

        private static string HierarchyPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }

        [Serializable]
        private sealed class Inspection
        {
            public string unity;
            public string project;
            public bool playing;
            public List<SceneInspection> scenes = new List<SceneInspection>();
        }

        [Serializable]
        private sealed class SceneInspection
        {
            public string path;
            public bool dirty;
            public bool loaded;
            public List<CameraInspection> cameras = new List<CameraInspection>();
            public List<LightInspection> lights = new List<LightInspection>();
            public List<EmitterInspection> emitters = new List<EmitterInspection>();
        }

        [Serializable]
        private sealed class CameraInspection
        {
            public string path;
            public bool enabled;
            public Vector3 position;
            public Quaternion rotation;
            public float fov;
        }

        [Serializable]
        private sealed class LightInspection
        {
            public string path;
            public string type;
            public bool enabled;
            public Vector3 position;
            public Quaternion rotation;
            public Color color;
            public float intensity;
            public string unit;
            public float range;
            public float radius;
            public float spotAngle;
            public float innerSpotAngle;
            public bool affectDiffuse;
            public bool affectSpecular;
            public string shadows;
        }

        [Serializable]
        private sealed class EmitterInspection
        {
            public string path;
            public bool enabled;
            public string rendererType;
            public string material;
            public int materialIndex;
            public string shader;
            public string materialPath;
            public Color emission;
            public Bounds bounds;
            public bool submeshBounds;
            public bool namedCandidate;
        }
    }
}

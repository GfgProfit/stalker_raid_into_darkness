using System;
using UnityEditor;
using UnityEngine;

namespace Rvm.Benchmark.Editor
{
    public static class BenchmarkRouteAuthoring
    {
        public static BenchmarkScenario CreateRoute(string path, string scenePath, Camera camera, Transform authoring)
        {
            string authoringPath = authoring != null ? AssetDatabase.GetAssetPath(authoring.root.gameObject) : "";
            if (!path.StartsWith("Assets/_BENCHMARK/", StringComparison.Ordinal) || !authoringPath.StartsWith("Assets/_BENCHMARK/", StringComparison.Ordinal) || !authoringPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) || camera == null) throw new ArgumentException("Choose a module prefab root and a camera; save the profile inside Assets/_BENCHMARK.");
            var profile = ScriptableObject.CreateInstance<BenchmarkScenario>();
            try
            {
                var poses = OrderedChildren(profile, authoring);
                var positions = new Vector3[4];
                var rotations = new Quaternion[4];
                for (int i = 0; i < 4; i++)
                {
                    positions[i] = poses[i].position;
                    rotations[i] = poses[i].rotation;
                }
                string id = System.IO.Path.GetFileNameWithoutExtension(path);
                profile.ConfigureRoute(id, id, scenePath, AssetDatabase.AssetPathToGUID(scenePath), AssetDatabase.AssetPathToGUID(authoringPath), positions, rotations, BenchmarkCameraSettings.Capture(camera), "Captured from selected camera: " + camera.name);
                AssetDatabase.CreateAsset(profile, path);
                return profile;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(profile);
                throw;
            }
        }

        public static Transform[] OrderedChildren(BenchmarkScenario profile, Transform root)
        {
            if (root == null) throw new ArgumentException("Select the module authoring prefab root.");
            string[] keys = profile.PositionKeys;
            var ordered = new Transform[4];
            foreach (Transform child in root)
            {
                int index = Array.IndexOf(keys, child.name);
                if (index < 0) continue;
                if (ordered[index] != null) throw new ArgumentException("Duplicate pose: " + child.name);
                ordered[index] = child;
            }
            for (int i = 0; i < 4; i++)
            {
                if (ordered[i] == null) throw new ArgumentException("Missing pose: " + keys[i]);
            }
            return ordered;
        }

        public static void UpdateRoute(BenchmarkScenario profile, Transform[] poses)
        {
            string scenePath = AssetDatabase.GUIDToAssetPath(profile.SceneGuid);
            if (string.IsNullOrEmpty(scenePath) || !scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The bound scene no longer exists.");
            if (poses == null || poses.Length != 4) throw new ArgumentException("Select exactly four poses in Pos 1–4 order.");
            var positions = new Vector3[4];
            var rotations = new Quaternion[4];
            string authoringPath = AssetDatabase.GUIDToAssetPath(profile.AuthoringGuid);
            for (int i = 0; i < 4; i++)
            {
                Transform pose = poses[i];
                if (pose == null || pose.name != profile.PositionKeys[i]) throw new ArgumentException("Missing or incorrectly ordered pose: " + profile.PositionKeys[i]);
                for (int j = 0; j < i; j++)
                {
                    if (pose == poses[j]) throw new ArgumentException("Duplicate Transform.");
                }
                string assetPath = AssetDatabase.GetAssetPath(pose.root.gameObject);
                var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(pose.gameObject);
                if (stage != null) assetPath = stage.assetPath;
                bool inScene = pose.gameObject.scene.path == scenePath;
                bool inAuthoring = !string.IsNullOrEmpty(authoringPath) && assetPath == authoringPath && authoringPath.StartsWith("Assets/_BENCHMARK/", StringComparison.Ordinal);
                if (!inScene && !inAuthoring) throw new ArgumentException("Pose is not in the bound scene or its module authoring prefab.");
                positions[i] = pose.position;
                rotations[i] = pose.rotation;
            }
            string before = EditorJsonUtility.ToJson(profile);
            profile.ConfigureRoute(profile.ScenarioId, profile.DisplayName, scenePath, profile.SceneGuid, profile.AuthoringGuid, positions, rotations, profile.CameraSettings, profile.CameraProvenance);
            if (EditorJsonUtility.ToJson(profile) == before) return;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
        }

        public static Transform[] AuthoringPoses(BenchmarkScenario profile)
        {
            string path = AssetDatabase.GUIDToAssetPath(profile.AuthoringGuid);
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return OrderedChildren(profile, root != null ? root.transform : null);
        }
    }
}

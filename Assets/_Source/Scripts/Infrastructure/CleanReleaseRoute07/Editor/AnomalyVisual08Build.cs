using System;
using System.Collections.Generic;
using System.IO;
using RaidIntoDarkness.Effects;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rvm.CleanReleaseRoute07.Editor
{
    public static class AnomalyVisual08Build
    {
        private static readonly Dictionary<string, TargetRecord> SOURCE_TARGETS = new Dictionary<string, TargetRecord>();

        public static void CaptureSource(Scene scene)
        {
            SOURCE_TARGETS.Clear();
            AnomalyVisual09Audit.Reset();
            var catalog = new SerializedObject(AssetDatabase.LoadMainAssetAtPath("Assets/_Tools/X-Ray OGF OMG Importer/Editor/Levels/XRayLevelAnomalyCatalog.asset"));
            var entries = catalog.FindProperty("_entries");
            var profiles = new Dictionary<UnityEngine.Object, TargetRecord>();
            for (int i = 0; i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                var prefab = entry.FindPropertyRelative("_prefab").objectReferenceValue as GameObject;
                string family = prefab != null ? Family(prefab, out _) : null;
                if (family == null)
                {
                    continue;
                }
                UnityEngine.Object profile = Profile(prefab);
                if (profile == null)
                {
                    throw new InvalidOperationException("Catalog target has no influence profile.");
                }
                if (!profiles.TryGetValue(profile, out var record))
                {
                    record = new TargetRecord { family = family, prefab = AssetDatabase.GetAssetPath(prefab), prefabGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)), profile = AssetDatabase.GetAssetPath(profile), sections = "" };
                    profiles.Add(profile, record);
                }
                if (record.family != family)
                {
                    throw new InvalidOperationException("Ambiguous target profile ownership.");
                }
                record.sections += entry.FindPropertyRelative("_section").stringValue + ";";
            }
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var visual in root.GetComponentsInChildren<AnomalyVisual>(true))
                {
                    UnityEngine.Object profile = Profile(visual.gameObject);
                    if (profile != null && profiles.TryGetValue(profile, out var identity))
                    {
                        SOURCE_TARGETS.Add(Hierarchy(visual.transform), identity);
                        AnomalyVisual09Audit.Inspect(visual.gameObject);
                    }
                }
            }
            if (SOURCE_TARGETS.Count == 0)
            {
                throw new InvalidOperationException("No catalog/profile identities found in source scene.");
            }
            AnomalyVisual09Audit.Save(Environment.GetEnvironmentVariable("CLEAN_RELEASE_ROUTE07_OUTPUT"));
        }

        private static UnityEngine.Object Profile(GameObject value)
        {
            foreach (var component in value.GetComponents<MonoBehaviour>())
            {
                if (component != null && component.GetType().Name == "AnomalyPlayerReaction")
                {
                    return new SerializedObject(component).FindProperty("_influenceProfile").objectReferenceValue;
                }
            }
            return null;
        }

        private static string Hierarchy(Transform value)
        {
            string path = value.name;
            while (value.parent != null)
            {
                value = value.parent;
                path = value.name + "/" + path;
            }
            return path;
        }

        public static void Prepare(Scene scene, string output)
        {
            var records = new List<object>();
            var text = new System.Text.StringBuilder();
            text.Append("{\"targets\":[");
            int count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var visual in root.GetComponentsInChildren<AnomalyVisual>(true))
                {
                    if (!SOURCE_TARGETS.TryGetValue(Hierarchy(visual.transform), out var identity))
                    {
                        continue;
                    }
                    visual.gameObject.AddComponent<AnomalyVisual08Target>().Configure(identity.family, identity.prefab);
                    var record = new TargetRecord { family = identity.family, instanceName = visual.name, prefab = identity.prefab, prefabGuid = identity.prefabGuid, profile = identity.profile, sections = identity.sections, components = Components(visual.gameObject) };
                    if (count++ > 0)
                    {
                        text.Append(',');
                    }
                    text.Append(JsonUtility.ToJson(record, true));
                }
            }
            text.Append("]}");
            if (count == 0)
            {
                throw new InvalidOperationException("No exact Gazirovka/Kisel prefab instances found.");
            }
            File.WriteAllText(Path.Combine(output, "ANOMALY_TARGET_INVENTORY.json"), text.ToString());
            File.Copy("Assets/_Tools/X-Ray OGF OMG Importer/Editor/Levels/XRayLevelAnomalyCatalog.asset", Path.Combine(output, "ANOMALY_CATALOG_INPUT.asset"));
        }

        private static string Family(GameObject value, out string prefab)
        {
            prefab = null;
            for (int i = 0; value != null && i < 12; i++)
            {
                string path = AssetDatabase.GetAssetPath(value);
                if (!string.IsNullOrEmpty(path))
                {
                    prefab = prefab ?? path;
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    if (guid == "edd76411fe76bcb4f9128d7dcf32e3b8")
                    {
                        return "Gazirovka";
                    }
                    if (guid == "f55d417a9b75a5f469d4d95e71cd516d")
                    {
                        return "Kisel";
                    }
                }
                value = PrefabUtility.GetCorrespondingObjectFromSource(value);
            }
            return null;
        }

        private static string[] Components(GameObject root)
        {
            var output = new List<string>();
            var visited = new HashSet<GameObject>();
            Inspect(root, visited, output);
            return output.ToArray();
        }

        private static void Inspect(GameObject root, HashSet<GameObject> visited, List<string> output)
        {
            if (root == null || !visited.Add(root))
            {
                return;
            }
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                {
                    continue;
                }
                string path = AssetDatabase.GetAssetPath(root) + "|" + component.gameObject.name;
                if (component is Renderer || component is Light || component is Collider || component is AudioSource)
                {
                    output.Add(path + "|" + component.GetType().FullName);
                }
                if (component is ParticleSystem system)
                {
                    output.Add(path + "|ParticleSystem|culling=" + system.main.cullingMode + "|lights=" + system.lights.enabled + "|lightTemplate=" + AssetDatabase.GetAssetPath(system.lights.light));
                    var sub = system.subEmitters;
                    for (int i = 0; i < sub.subEmittersCount; i++)
                    {
                        var child = sub.GetSubEmitterSystem(i);
                        if (child != null && !child.transform.IsChildOf(root.transform))
                        {
                            Inspect(child.gameObject, visited, output);
                        }
                    }
                }
                if (component is MonoBehaviour)
                {
                    output.Add(path + "|" + component.GetType().FullName);
                    var data = new SerializedObject(component);
                    var property = data.GetIterator();
                    while (property.NextVisible(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference)
                        {
                            continue;
                        }
                        UnityEngine.Object value = property.objectReferenceValue;
                        if (value != null)
                        {
                            output.Add(path + "|" + property.propertyPath + "|" + AssetDatabase.GetAssetPath(value));
                        }
                        GameObject linked = value as GameObject;
                        if (value is ParticleSystem particle)
                        {
                            linked = particle.gameObject;
                        }
                        if (linked != null && !linked.transform.IsChildOf(root.transform))
                        {
                            Inspect(linked, visited, output);
                        }
                    }
                }
            }
        }

        [Serializable]
        private sealed class TargetRecord
        {
            public string family;
            public string instanceName;
            public string prefab;
            public string prefabGuid;
            public string profile;
            public string sections;
            public string[] components;
        }
    }
}

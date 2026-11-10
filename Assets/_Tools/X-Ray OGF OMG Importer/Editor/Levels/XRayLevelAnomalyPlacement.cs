using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Core;
using XRay.Importer.Core.Anm;
using XRay.Importer.Core.Spawn;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    public sealed class XRayLevelAnomalyPlacementResult
    {
        public int SourceCount { get; internal set; }
        public int ImportedCount { get; internal set; }
        public XRayLevelAnomalyGroup Group { get; internal set; }
        public List<string> Missing { get; } = new List<string>();
        public List<string> Unsupported { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class XRayLevelAnomalyPlacement
    {
        public const string GROUP_NAME = "Imported Anomalies";

        private const float AXIS_EPSILON = 0.00001f;
        private const float ORTHOGONAL_EPSILON = 0.001f;

        public static XRayLevelAnomalyPlacementResult Apply(GameObject levelRoot, string levelName, string spawnAssetPath, string origin, string hash, XRayLevelSpawnData data, XRayLevelAnomalyCatalog catalog)
        {
            if (levelRoot == null || !levelRoot.scene.IsValid())
            {
                throw new ArgumentException("Требуется корневой объект локации в загруженной сцене.", nameof(levelRoot));
            }

            if (data == null || !data.Levels.Exists(level => string.Equals(level.Name, levelName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Локация «" + levelName + "» отсутствует во встроенном graph файла all.spawn.");
            }

            if (catalog == null)
            {
                throw new InvalidOperationException("Не назначен конфиг префабов аномалий.");
            }

            if (!catalog.Validate(out string catalogError))
            {
                throw new InvalidOperationException(catalogError);
            }

            var previous = new List<GameObject>();
            foreach (Transform child in levelRoot.transform)
            {
                XRayLevelAnomalyGroup group = child.GetComponent<XRayLevelAnomalyGroup>();
                if (group != null && string.Equals(group.LevelName, levelName, StringComparison.OrdinalIgnoreCase))
                {
                    previous.Add(child.gameObject);
                }
            }

            var result = new XRayLevelAnomalyPlacementResult();
            var missing = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var unsupported = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int periodic = 0;
            var staging = new GameObject(GROUP_NAME);
            SceneManager.MoveGameObjectToScene(staging, levelRoot.scene);
            staging.SetActive(false);
            staging.transform.SetParent(levelRoot.transform, false);
            try
            {
                foreach (XRaySpawnAnomaly anomaly in data.Anomalies)
                {
                    if (!string.Equals(anomaly.LevelName, levelName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    result.SourceCount++;
                    bool mapped = catalog.TryGet(anomaly.Section, out XRayLevelAnomalyEntry entry);
                    string description = (mapped ? entry.DisplayName : anomaly.Section) + " [" + anomaly.Section + "]";
                    if (!mapped || entry.Prefab == null)
                    {
                        Count(missing, description);
                        continue;
                    }

                    if (anomaly.Shapes.Count != 1)
                    {
                        Count(unsupported, description + ": составной объём (форм: " + anomaly.Shapes.Count + ")");
                        continue;
                    }

                    XRaySpawnShape shape = anomaly.Shapes[0];
                    string reason = ValidateShape(shape);
                    if (reason != null)
                    {
                        Count(unsupported, description + ": " + reason);
                        continue;
                    }

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(entry.Prefab, levelRoot.scene);
                    instance.transform.SetParent(staging.transform, false);
                    instance.name = anomaly.Name + " [" + anomaly.SpawnId + "] — " + entry.DisplayName;
                    instance.transform.localPosition = XRayUnitySpace.Position(anomaly.Position, 1f);
                    instance.transform.localRotation = SourceRotation(anomaly.Angles);
                    instance.transform.localScale = Vector3.one;
                    ConfigureVolume(instance, shape, description);
                    DisableRandomRadius(instance);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
                    result.ImportedCount++;
                    if (anomaly.EnabledTime != 0 || anomaly.DisabledTime != 0 || anomaly.StartTimeShift != 0)
                    {
                        periodic++;
                    }
                }

                foreach (KeyValuePair<string, int> item in missing)
                {
                    result.Missing.Add("Нет префаба аномалии «" + item.Key + "»: " + item.Value + ".");
                }

                foreach (KeyValuePair<string, int> item in unsupported)
                {
                    result.Unsupported.Add("Не импортировано «" + item.Key + "»: " + item.Value + ".");
                }

                if (periodic > 0)
                {
                    result.Warnings.Add("Аномалии с расписанием активности all.spawn: " + periodic + ". Используется игровой цикл назначенного префаба; расписание X-Ray не переносится.");
                }

                result.Group = staging.AddComponent<XRayLevelAnomalyGroup>();
                result.Group.Initialize(levelName, spawnAssetPath, origin, hash, result.ImportedCount, result.Missing, result.Unsupported);
                foreach (GameObject old in previous)
                {
                    UnityEngine.Object.DestroyImmediate(old);
                }

                staging.SetActive(true);
                return result;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(staging);
                throw;
            }
        }

        public static Quaternion SourceRotation(XVector3 angles)
        {
            XMatrix4 matrix = XRayAnmTransform.EngineHpbMatrix(angles.Y, angles.X, angles.Z);
            return Quaternion.LookRotation(Column(matrix, 2), Column(matrix, 1));
        }

        private static string ValidateShape(XRaySpawnShape shape)
        {
            if (shape.Type == XRaySpawnShapeType.Sphere)
            {
                return shape.Radius <= 0.0 ? "нулевой радиус сферы" : null;
            }

            if (shape.Type != XRaySpawnShapeType.Box)
            {
                return "неподдерживаемая форма " + shape.Type;
            }

            Vector3 x = Column(shape.Box, 0);
            Vector3 y = Column(shape.Box, 1);
            Vector3 z = Column(shape.Box, 2);
            if (x.magnitude <= AXIS_EPSILON || y.magnitude <= AXIS_EPSILON || z.magnitude <= AXIS_EPSILON)
            {
                return "вырожденная матрица бокса";
            }

            if (Mathf.Abs(Vector3.Dot(x.normalized, y.normalized)) > ORTHOGONAL_EPSILON || Mathf.Abs(Vector3.Dot(x.normalized, z.normalized)) > ORTHOGONAL_EPSILON || Mathf.Abs(Vector3.Dot(y.normalized, z.normalized)) > ORTHOGONAL_EPSILON)
            {
                return "матрица бокса содержит сдвиг осей (shear)";
            }

            return null;
        }

        private static void ConfigureVolume(GameObject instance, XRaySpawnShape shape, string description)
        {
            SphereCollider volume = instance.GetComponent<SphereCollider>();
            if (volume == null)
            {
                throw new InvalidOperationException("Префаб «" + description + "» должен иметь корневой SphereCollider для системы влияния аномалий.");
            }

            volume.enabled = true;
            volume.isTrigger = true;
            volume.center = XRayUnitySpace.Position(shape.Center, 1f);
            if (shape.Type == XRaySpawnShapeType.Sphere)
            {
                volume.radius = (float)shape.Radius;
            }
            else
            {
                Vector3 x = Column(shape.Box, 0);
                Vector3 y = Column(shape.Box, 1);
                Vector3 z = Column(shape.Box, 2);
                Vector3 halfBounds = (Absolute(x) + Absolute(y) + Absolute(z)) * 0.5f;
                volume.radius = halfBounds.magnitude;
                volume.includeLayers = 0;
                volume.excludeLayers = ~0;
                volume.layerOverridePriority = int.MaxValue;
                Quaternion rotation = Quaternion.LookRotation(z.normalized, y.normalized);
                Quaternion inverse = Quaternion.Inverse(rotation);
                foreach (Transform child in instance.transform)
                {
                    child.localPosition = inverse * child.localPosition;
                    child.localRotation = inverse * child.localRotation;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(child);
                }

                instance.transform.localRotation *= rotation;
                volume.center = inverse * volume.center;
                var box = instance.AddComponent<BoxCollider>();
                box.center = volume.center;
                box.size = new Vector3(x.magnitude, y.magnitude, z.magnitude);
                box.isTrigger = true;
            }

            PrefabUtility.RecordPrefabInstancePropertyModifications(volume);
        }

        private static void DisableRandomRadius(GameObject instance)
        {
            foreach (MonoBehaviour component in instance.GetComponents<MonoBehaviour>())
            {
                if (component == null)
                {
                    continue;
                }

                var serialized = new SerializedObject(component);
                SerializedProperty randomize = serialized.FindProperty("_randomizeRadius");
                if (randomize != null && randomize.propertyType == SerializedPropertyType.Boolean)
                {
                    randomize.boolValue = false;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }
            }
        }

        private static void Count(IDictionary<string, int> counts, string key)
        {
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }

        private static Vector3 Column(XMatrix4 matrix, int column)
        {
            return new Vector3((float)matrix[0, column], (float)matrix[1, column], (float)matrix[2, column]);
        }

        private static Vector3 Absolute(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }
    }
}

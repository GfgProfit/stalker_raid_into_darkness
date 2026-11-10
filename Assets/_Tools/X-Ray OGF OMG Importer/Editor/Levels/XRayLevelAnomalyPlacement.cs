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

                    string reason = ValidateShapes(anomaly.Shapes);
                    if (reason != null)
                    {
                        Count(unsupported, description + ": " + reason);
                        continue;
                    }

                    string motionError = ValidateMotion(entry.Prefab, anomaly.MotionName, catalog, out XRayAnmAsset motionAsset);
                    if (motionError != null)
                    {
                        Count(unsupported, description + ": " + motionError);
                        continue;
                    }

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(entry.Prefab, levelRoot.scene);
                    instance.transform.SetParent(staging.transform, false);
                    instance.name = anomaly.Name + " [" + anomaly.SpawnId + "] — " + entry.DisplayName;
                    instance.transform.localPosition = XRayUnitySpace.Position(anomaly.Position, 1f);
                    instance.transform.localRotation = SourceRotation(anomaly.Angles);
                    instance.transform.localScale = Vector3.one;
                    ConfigureVolumes(instance, anomaly.Shapes, description);
                    DisableRandomRadius(instance);
                    ConfigureMotion(instance, motionAsset);
                    ConfigureSchedule(instance, anomaly);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
                    result.ImportedCount++;
                }

                foreach (KeyValuePair<string, int> item in missing)
                {
                    result.Missing.Add("Нет префаба аномалии «" + item.Key + "»: " + item.Value + ".");
                }

                foreach (KeyValuePair<string, int> item in unsupported)
                {
                    result.Unsupported.Add("Не импортировано «" + item.Key + "»: " + item.Value + ".");
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
            if (shape == null)
            {
                return "пустая форма";
            }
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

        private static string ValidateShapes(IReadOnlyList<XRaySpawnShape> shapes)
        {
            if (shapes == null || shapes.Count == 0)
            {
                return "пустой объём (форм: 0)";
            }
            for (int index = 0; index < shapes.Count; index++)
            {
                string error = ValidateShape(shapes[index]);
                if (error != null)
                {
                    return "форма " + index + ": " + error;
                }
            }
            return null;
        }

        private static string ValidateMotion(GameObject prefab, string motionName, XRayLevelAnomalyCatalog catalog, out XRayAnmAsset motionAsset)
        {
            motionAsset = null;
            bool hasAdapter = FindMotionSource(prefab) != null;
            if (string.IsNullOrWhiteSpace(motionName))
            {
                return hasAdapter ? "в all.spawn отсутствует исходный motion" : null;
            }
            if (!hasAdapter)
            {
                return "префаб не поддерживает исходный motion «" + motionName + "»";
            }
            if (!catalog.TryGetMotion(motionName, out motionAsset) || !motionAsset.HasCurves)
            {
                return "не назначен XRayAnmAsset с исходными кривыми для motion «" + motionName + "»";
            }
            return null;
        }

        private static IXRayAnmSource FindMotionSource(GameObject root)
        {
            foreach (MonoBehaviour component in root.GetComponents<MonoBehaviour>())
            {
                if (component != null && component is IXRayAnmSource source)
                {
                    return source;
                }
            }
            return null;
        }

        private static void ConfigureMotion(GameObject instance, XRayAnmAsset asset)
        {
            if (asset == null)
            {
                return;
            }
            IXRayAnmSource source = FindMotionSource(instance);
            source.Configure(asset, false);
            var component = (MonoBehaviour)source;
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            XRayAnmPlayer player = instance.GetComponent<XRayAnmPlayer>();
            if (player != null)
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(player);
            }
        }

        private static void ConfigureSchedule(GameObject instance, XRaySpawnAnomaly anomaly)
        {
            if (anomaly.EnabledTime == 0 && anomaly.DisabledTime == 0 && anomaly.StartTimeShift == 0)
            {
                return;
            }
            XRayAnomalySchedule schedule = instance.GetComponent<XRayAnomalySchedule>();
            if (schedule == null)
            {
                schedule = instance.AddComponent<XRayAnomalySchedule>();
            }
            schedule.Configure(anomaly.EnabledTime, anomaly.DisabledTime, anomaly.StartTimeShift);
            PrefabUtility.RecordPrefabInstancePropertyModifications(schedule);
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
                ConfigureActivation(instance, volume, new Collider[] { box });
            }

            PrefabUtility.RecordPrefabInstancePropertyModifications(volume);
        }

        private static void ConfigureVolumes(GameObject instance, IReadOnlyList<XRaySpawnShape> shapes, string description)
        {
            if (shapes.Count == 1)
            {
                ConfigureVolume(instance, shapes[0], description);
                return;
            }
            SphereCollider proxy = instance.GetComponent<SphereCollider>();
            if (proxy == null)
            {
                throw new InvalidOperationException("Префаб «" + description + "» должен иметь корневой SphereCollider для системы влияния аномалий.");
            }
            var actual = new Collider[shapes.Count];
            Bounds bounds = default;
            for (int index = 0; index < shapes.Count; index++)
            {
                XRaySpawnShape shape = shapes[index];
                var child = new GameObject("ActivationShape_" + index.ToString("D4"));
                child.layer = instance.layer;
                child.transform.SetParent(instance.transform, false);
                Vector3 center = XRayUnitySpace.Position(shape.Center, 1f);
                child.transform.localPosition = center;
                Vector3 extents;
                if (shape.Type == XRaySpawnShapeType.Sphere)
                {
                    var sphere = child.AddComponent<SphereCollider>();
                    sphere.radius = (float)shape.Radius;
                    sphere.isTrigger = true;
                    extents = Vector3.one * sphere.radius;
                    actual[index] = sphere;
                }
                else
                {
                    Vector3 x = Column(shape.Box, 0);
                    Vector3 y = Column(shape.Box, 1);
                    Vector3 z = Column(shape.Box, 2);
                    child.transform.localRotation = Quaternion.LookRotation(z.normalized, y.normalized);
                    var box = child.AddComponent<BoxCollider>();
                    box.size = new Vector3(x.magnitude, y.magnitude, z.magnitude);
                    box.isTrigger = true;
                    extents = (Absolute(x) + Absolute(y) + Absolute(z)) * 0.5f;
                    actual[index] = box;
                }
                var shapeBounds = new Bounds(center, extents * 2f);
                if (index == 0)
                {
                    bounds = shapeBounds;
                }
                else
                {
                    bounds.Encapsulate(shapeBounds);
                }
            }
            proxy.enabled = true;
            proxy.isTrigger = true;
            proxy.center = bounds.center;
            proxy.radius = bounds.extents.magnitude;
            proxy.includeLayers = 0;
            proxy.excludeLayers = ~0;
            proxy.layerOverridePriority = int.MaxValue;
            Rigidbody body = instance.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = instance.AddComponent<Rigidbody>();
            }
            body.isKinematic = true;
            body.useGravity = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(proxy);
            PrefabUtility.RecordPrefabInstancePropertyModifications(body);
            ConfigureActivation(instance, proxy, actual);
        }

        private static void ConfigureActivation(GameObject instance, SphereCollider proxy, Collider[] actualShapes)
        {
            XRayAnomalyActivationVolume activation = instance.GetComponent<XRayAnomalyActivationVolume>();
            if (activation == null)
            {
                activation = instance.AddComponent<XRayAnomalyActivationVolume>();
            }
            activation.Configure(proxy, actualShapes);
            PrefabUtility.RecordPrefabInstancePropertyModifications(activation);
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

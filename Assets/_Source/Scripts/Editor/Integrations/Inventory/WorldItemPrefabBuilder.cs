using System.Collections.Generic;
using System.IO;
using Inventory;
using RaidIntoDarkness.Player;
using RaidIntoDarkness.Player.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Integrations.Inventory.Editor
{
    public static class WorldItemPrefabBuilder
    {
        public const string WORLD_ITEMS_ROOT = "WorldItems";
        public const string PREFABS_FOLDER = "Prefabs";

        private const int COLUMNS = 16;
        private const float SPACING = 1.25f;
        private const float FORWARD_OFFSET = 4f;
        private const float DROP_HEIGHT = 0.3f;
        private const float GROUND_PROBE_HEIGHT = 50f;
        private const float MIN_COLLIDER_SIZE = 0.05f;
        private const float MIN_MASS = 0.5f;
        private const float OBSTACLE_MARGIN = 1f;
        private const float MAX_SEARCH_DISTANCE = 120f;
        private const float SEARCH_STEP = 1f;
        private const float FLAT_TOLERANCE = 0.25f;

        public static List<WorldItem> Build(IEnumerable<ItemDefinition> definitions)
        {
            var sorted = new List<ItemDefinition>();

            foreach (ItemDefinition definition in definitions)
            {
                if (definition != null && definition.VisualPrefab != null)
                {
                    sorted.Add(definition);
                }
            }

            sorted.Sort((left, right) => left.Category != right.Category ? string.CompareOrdinal(left.Category, right.Category) : string.CompareOrdinal(left.name, right.name));
            var prefabs = new List<WorldItem>();

            foreach (ItemDefinition definition in sorted)
            {
                string modelFolder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(definition.VisualPrefab)).Replace('\\', '/');
                string folder = Path.GetDirectoryName(modelFolder).Replace('\\', '/') + "/" + PREFABS_FOLDER;
                InventoryDemoContentBuilder.EnsureFolder(folder);
                WorldItem prefab = CreatePrefab(definition, $"{folder}/{definition.name}.prefab");
                SetWorldComponent(definition, prefab);
                prefabs.Add(prefab);
            }

            AssetDatabase.SaveAssets();
            return prefabs;
        }

        public static void PlaceInScene(Scene scene, IReadOnlyList<WorldItem> prefabs, Transform anchor)
        {
            foreach (GameObject existing in scene.GetRootGameObjects())
            {
                if (existing.name == WORLD_ITEMS_ROOT)
                {
                    UnityEngine.Object.DestroyImmediate(existing);
                }
            }

            var root = new GameObject(WORLD_ITEMS_ROOT);
            SceneManager.MoveGameObjectToScene(root, scene);

            if (prefabs.Count == 0)
            {
                return;
            }

            Place(scene, prefabs, anchor, root.transform, new List<Bounds>());
        }

        public static void AppendToScene(Scene scene, IReadOnlyList<WorldItem> prefabs, Transform anchor, string groupName)
        {
            GameObject root = null;

            foreach (GameObject existing in scene.GetRootGameObjects())
            {
                if (existing.name == WORLD_ITEMS_ROOT)
                {
                    root = existing;
                }
            }

            if (root == null)
            {
                root = new GameObject(WORLD_ITEMS_ROOT);
                SceneManager.MoveGameObjectToScene(root, scene);
            }

            Transform previous = root.transform.Find(groupName);

            if (previous != null)
            {
                UnityEngine.Object.DestroyImmediate(previous.gameObject);
            }

            var occupied = new List<Bounds>();

            foreach (Collider collider in root.GetComponentsInChildren<Collider>())
            {
                occupied.Add(collider.bounds);
            }

            var group = new GameObject(groupName);
            group.transform.SetParent(root.transform, false);

            if (prefabs.Count > 0)
            {
                Place(scene, prefabs, anchor, group.transform, occupied);
            }
        }

        private static void Place(Scene scene, IReadOnlyList<WorldItem> prefabs, Transform anchor, Transform parent, List<Bounds> occupied)
        {
            Vector3 origin = anchor != null ? anchor.position : Vector3.zero;
            Vector3 forward = anchor != null ? Vector3.ProjectOnPlane(anchor.forward, Vector3.up).normalized : Vector3.forward;
            forward = forward.sqrMagnitude > 0f ? forward : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            int rows = (prefabs.Count + COLUMNS - 1) / COLUMNS;
            Physics.SyncTransforms();
            Collider ground = CollectObstacles(scene, anchor, out List<Bounds> obstacles);
            obstacles.AddRange(occupied);
            Vector3 center = FindFreeCenter(origin, forward, right, rows, prefabs.Count, ground, obstacles);

            var heights = new float[prefabs.Count];

            for (int i = 0; i < prefabs.Count; i++)
            {
                Vector3 cell = GetCell(center, forward, right, rows, i);
                heights[i] = Physics.Raycast(cell + Vector3.up * GROUND_PROBE_HEIGHT, Vector3.down, out RaycastHit hit, GROUND_PROBE_HEIGHT * 2f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : origin.y;
            }

            for (int i = 0; i < prefabs.Count; i++)
            {
                Vector3 position = GetCell(center, forward, right, rows, i);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i].gameObject, parent);
                var collider = instance.GetComponent<BoxCollider>();
                float bottom = collider != null ? collider.center.y - collider.size.y * 0.5f : 0f;
                instance.transform.SetPositionAndRotation(new Vector3(position.x, heights[i] + DROP_HEIGHT - bottom, position.z), Quaternion.LookRotation(-forward, Vector3.up));
            }
        }

        private static Vector3 GetCell(Vector3 center, Vector3 forward, Vector3 right, int rows, int index)
        {
            int row = index / COLUMNS;
            int column = index % COLUMNS;
            return center + forward * ((row - (rows - 1) * 0.5f) * SPACING) + right * ((column - (COLUMNS - 1) * 0.5f) * SPACING);
        }

        private static Collider CollectObstacles(Scene scene, Transform anchor, out List<Bounds> obstacles)
        {
            obstacles = new List<Bounds>();
            Collider ground = FindGround(anchor);
            float groundTop = GetGroundTop(ground, anchor);

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == WORLD_ITEMS_ROOT)
                {
                    continue;
                }

                foreach (Collider collider in root.GetComponentsInChildren<Collider>())
                {
                    if (collider != ground && collider.enabled && collider.isTrigger == false && collider.bounds.max.y > groundTop + FLAT_TOLERANCE && (anchor == null || collider.transform.IsChildOf(anchor.root) == false))
                    {
                        obstacles.Add(collider.bounds);
                    }
                }
            }

            return ground;
        }

        private static float GetGroundTop(Collider ground, Transform anchor)
        {
            if (ground != null)
            {
                return ground.bounds.max.y;
            }

            return anchor != null ? anchor.position.y : 0f;
        }

        private static Collider FindGround(Transform anchor)
        {
            if (anchor == null)
            {
                return null;
            }

            RaycastHit[] hits = Physics.RaycastAll(anchor.position + Vector3.up, Vector3.down, GROUND_PROBE_HEIGHT, ~0, QueryTriggerInteraction.Ignore);
            Collider ground = null;
            float nearest = float.MaxValue;

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.IsChildOf(anchor.root) == false && hit.distance < nearest)
                {
                    ground = hit.collider;
                    nearest = hit.distance;
                }
            }

            return ground;
        }

        private static Vector3 FindFreeCenter(Vector3 origin, Vector3 forward, Vector3 right, int rows, int count, Collider ground, List<Bounds> obstacles)
        {
            Vector3[] directions = { forward, right, -right, -forward };
            float[] extents = { (rows - 1) * 0.5f * SPACING, (COLUMNS - 1) * 0.5f * SPACING, (COLUMNS - 1) * 0.5f * SPACING, (rows - 1) * 0.5f * SPACING };

            for (float offset = FORWARD_OFFSET; offset <= MAX_SEARCH_DISTANCE; offset += SEARCH_STEP)
            {
                for (int d = 0; d < directions.Length; d++)
                {
                    Vector3 center = origin + directions[d] * (extents[d] + offset);

                    if (IsFree(center, forward, right, rows, count, ground, obstacles))
                    {
                        return center;
                    }
                }
            }

            Debug.LogWarning("[Inventory] No free ground area was found for world items; they are placed in front of the player.");
            return origin + forward * (extents[0] + FORWARD_OFFSET);
        }

        private static bool IsFree(Vector3 center, Vector3 forward, Vector3 right, int rows, int count, Collider ground, List<Bounds> obstacles)
        {
            float groundTop = GetGroundTop(ground, null);

            for (int i = 0; i < count; i++)
            {
                Vector3 cell = GetCell(center, forward, right, rows, i);

                foreach (Bounds bounds in obstacles)
                {
                    if (cell.x > bounds.min.x - OBSTACLE_MARGIN && cell.x < bounds.max.x + OBSTACLE_MARGIN && cell.z > bounds.min.z - OBSTACLE_MARGIN && cell.z < bounds.max.z + OBSTACLE_MARGIN)
                    {
                        return false;
                    }
                }

                if (ground != null && (Physics.Raycast(cell + Vector3.up * GROUND_PROBE_HEIGHT, Vector3.down, out RaycastHit hit, GROUND_PROBE_HEIGHT * 2f, ~0, QueryTriggerInteraction.Ignore) == false || Mathf.Abs(hit.point.y - groundTop) > FLAT_TOLERANCE))
                {
                    return false;
                }
            }

            return true;
        }

        private static WorldItem CreatePrefab(ItemDefinition definition, string path)
        {
            var root = new GameObject(definition.name);

            try
            {
                PrefabUtility.InstantiatePrefab(definition.VisualPrefab, root.transform);
                Bounds bounds = CalculateBounds(root);
                var collider = root.AddComponent<BoxCollider>();
                collider.center = bounds.center;
                collider.size = Vector3.Max(bounds.size, Vector3.one * MIN_COLLIDER_SIZE);
                var body = root.AddComponent<Rigidbody>();
                body.mass = Mathf.Max(MIN_MASS, definition.Weight);
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.Continuous;
                root.AddComponent<WorldItem>().Configure(definition, 1);
                GraInventorySoundSetup.ConfigureImpact(root, definition, AssetDatabase.LoadAssetAtPath<StalkerCollideSoundLibrary>(GraInventorySoundSetup.COLLIDE_LIBRARY_PATH), AssetDatabase.LoadAssetAtPath<StalkerSurfaceAudioLibrary>(StalkerSurfaceAudioBuilder.LIBRARY_PATH));
                return PrefabUtility.SaveAsPrefabAsset(root, path).GetComponent<WorldItem>();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);

            if (renderers.Length == 0)
            {
                return new Bounds(Vector3.zero, Vector3.one * 0.25f);
            }

            Bounds bounds = renderers[0].bounds;

            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static void SetWorldComponent(ItemDefinition definition, WorldItem prefab)
        {
            var serialized = new SerializedObject(definition);
            SerializedProperty list = serialized.FindProperty("_components");
            int index = -1;

            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).managedReferenceValue is WorldItemComponent)
                {
                    index = i;
                }
            }

            if (index < 0)
            {
                index = list.arraySize;
                list.arraySize++;
                list.GetArrayElementAtIndex(index).managedReferenceValue = new WorldItemComponent();
            }

            list.GetArrayElementAtIndex(index).FindPropertyRelative("_prefab").objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
        }
    }
}

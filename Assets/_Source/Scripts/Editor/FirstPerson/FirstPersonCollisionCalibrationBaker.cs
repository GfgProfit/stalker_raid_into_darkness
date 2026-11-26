using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using XRay.Importer;

namespace RaidIntoDarkness.FirstPerson.Editor
{
    public static class FirstPersonCollisionCalibrationBaker
    {
        private const string PLAYER_PREFAB = "Assets/_Source/FirstPerson/FPS_Player.prefab";
        private const string BOLT_DEFINITION = "Assets/_Source/FirstPerson/Bolt/BoltDefinition.asset";
        private const string DATA_PATH = "Assets/_Source/FirstPerson/Resources/" + FirstPersonContactCatalog.RESOURCE_PATH + ".bytes";
        private const float MIN_EXTENT = 0.006f;
        private const float SLICE_LENGTH = 0.22f;

        private static readonly string[] ARM_BONES = { "l_upperarm", "l_forearm", "l_hand", "r_upperarm", "r_forearm", "r_hand" };

        [MenuItem("Tools/Raid Into Darkness/Calibrate First Person Collision")]
        public static void Bake()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                throw new InvalidOperationException("First-person collision calibration requires Edit Mode outside Prefab Mode.");
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PLAYER_PREFAB);
            FirstPersonRig rig = prefab.GetComponent<FirstPersonRig>();
            var data = new SortedDictionary<string, List<KeyValuePair<int, Bounds>>>(StringComparer.Ordinal);
            int fallbackSegments = 0;
            int handCount = 0;
            foreach (GameObject hand in rig.Catalog.Entries.Select(entry => entry.HandsModel).Where(model => model != null).Distinct())
            {
                BakeHands(hand, data, ref fallbackSegments);
                handCount++;
            }

            var weapons = new HashSet<GameObject>();
            AddReferencedModels("t:FirstPersonWeaponAsset", "Assets/_Source", "_hudModel", weapons);
            GameObject bolt = AssetDatabase.LoadAssetAtPath<BoltDefinition>(BOLT_DEFINITION).HudModel;
            weapons.Add(bolt);
            var addons = new HashSet<GameObject>();
            AddReferencedModels("t:ItemDefinition", "Assets/_Source/Resources/Items/Addons", "_visualPrefab", addons);
            foreach (GameObject held in weapons.Concat(addons).Distinct())
            {
                BakeItem(held, data);
            }

            byte[] bytes = Encode(data);
            if (!File.Exists(DATA_PATH) || !File.ReadAllBytes(DATA_PATH).SequenceEqual(bytes))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DATA_PATH));
                File.WriteAllBytes(DATA_PATH, bytes);
                AssetDatabase.ImportAsset(DATA_PATH, ImportAssetOptions.ForceSynchronousImport);
            }

            int shapeCount = data.Values.Sum(shapes => shapes.Count);
            Debug.Log($"First-person collision calibrated: {handCount} hand rigs, {weapons.Count - 1} held item models plus bolt, {addons.Count} addon models, {data.Count} entries, {shapeCount} boxes, {fallbackSegments} bone-fitted arm segments. {bytes.Length} bytes.");
        }

        private static void AddReferencedModels(string filter, string directory, string property, HashSet<GameObject> models)
        {
            foreach (string guid in AssetDatabase.FindAssets(filter, new[] { directory }))
            {
                UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && new SerializedObject(asset).FindProperty(property)?.objectReferenceValue is GameObject visual)
                {
                    models.Add(visual);
                }
            }
        }

        private static void BakeHands(GameObject hands, SortedDictionary<string, List<KeyValuePair<int, Bounds>>> data, ref int fallbacks)
        {
            XRayModel model = hands.GetComponent<XRayModel>();
            if (model == null || model.FindBone(FirstPersonHudAnimator.ITEM_ANCHOR_BONE) == null)
            {
                throw new InvalidOperationException($"The hands model '{hands.name}' is missing its item anchor.");
            }

            Transform[] armBones = Array.ConvertAll(ARM_BONES, model.FindBone);
            if (armBones.Any(bone => bone == null))
            {
                throw new InvalidOperationException($"The hands model '{hands.name}' is missing an arm bone.");
            }

            var points = new List<Vector3>[ARM_BONES.Length];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = new List<Vector3>();
            }

            foreach (SkinnedMeshRenderer renderer in hands.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                Vector3[] vertices = Vertices(mesh);
                BoneWeight[] weights = mesh.boneWeights;
                Matrix4x4[] bindposes = mesh.bindposes;
                Transform[] bones = renderer.bones;
                int[] segment = Array.ConvertAll(bones, bone => FindArmSegment(bone, armBones));
                for (int vertex = 0; vertex < vertices.Length && vertex < weights.Length; vertex++)
                {
                    int bone = weights[vertex].boneIndex0;
                    if (bone < 0 || bone >= segment.Length || bone >= bindposes.Length || segment[bone] < 0)
                    {
                        continue;
                    }

                    int arm = segment[bone];
                    Vector3 atBind = bindposes[bone].MultiplyPoint3x4(vertices[vertex]);
                    points[arm].Add(armBones[arm].InverseTransformPoint(bones[bone].TransformPoint(atBind)));
                }
            }

            var shapes = new List<KeyValuePair<int, Bounds>>(armBones.Length);
            for (int i = 0; i < armBones.Length; i++)
            {
                Bounds bounds;
                if (points[i].Count > 0)
                {
                    bounds = Fit(points[i]);
                }
                else
                {
                    Vector3 tip = i % 3 < 2 ? armBones[i].InverseTransformPoint(armBones[i + 1].position) : new Vector3(0.0f, 0.0f, 0.08f);
                    bounds = new Bounds(tip * 0.5f, new Vector3(Mathf.Abs(tip.x), Mathf.Abs(tip.y), Mathf.Abs(tip.z)) + Vector3.one * 0.065f);
                    fallbacks++;
                }

                shapes.Add(new KeyValuePair<int, Bounds>(i, bounds));
            }

            AddEntry(data, FirstPersonContactCatalog.HandsKey(model), shapes);
        }

        private static int FindArmSegment(Transform bone, Transform[] armBones)
        {
            if (bone != null)
            {
                for (int i = armBones.Length - 1; i >= 0; i--)
                {
                    if (bone.IsChildOf(armBones[i]))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static void BakeItem(GameObject held, SortedDictionary<string, List<KeyValuePair<int, Bounds>>> data)
        {
            foreach (Renderer renderer in held.GetComponentsInChildren<Renderer>(true))
            {
                var skin = renderer as SkinnedMeshRenderer;
                Mesh mesh = skin != null ? skin.sharedMesh : renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                if (mesh == null || mesh.vertexCount == 0)
                {
                    continue;
                }

                XRayModel owner = renderer.GetComponentInParent<XRayModel>(true);
                string key = FirstPersonContactCatalog.MeshKey(owner, mesh);
                Vector3[] vertices = Vertices(mesh);
                BoneWeight[] weights = skin != null ? mesh.boneWeights : Array.Empty<BoneWeight>();
                Matrix4x4[] bindposes = skin != null ? mesh.bindposes : Array.Empty<Matrix4x4>();
                var groups = new SortedDictionary<int, List<Vector3>>();
                for (int i = 0; i < vertices.Length; i++)
                {
                    int bone = i < weights.Length && weights[i].weight0 > 0.0f ? weights[i].boneIndex0 : -1;
                    if (bone >= bindposes.Length)
                    {
                        bone = -1;
                    }

                    if (!groups.TryGetValue(bone, out List<Vector3> points))
                    {
                        points = new List<Vector3>();
                        groups.Add(bone, points);
                    }

                    points.Add(bone >= 0 ? bindposes[bone].MultiplyPoint3x4(vertices[i]) : vertices[i]);
                }

                var shapes = new List<KeyValuePair<int, Bounds>>();
                foreach (KeyValuePair<int, List<Vector3>> group in groups)
                {
                    AddItemBounds(shapes, group.Key, group.Value);
                }

                AddEntry(data, key, shapes);
            }
        }

        private static void AddItemBounds(List<KeyValuePair<int, Bounds>> shapes, int bone, List<Vector3> points)
        {
            Bounds bounds = Fit(points);
            Vector3 size = bounds.size;
            int axis = size.x > size.y ? 0 : 1;
            axis = size[axis] > size.z ? axis : 2;
            int count = Mathf.Clamp(Mathf.CeilToInt(size[axis] / SLICE_LENGTH), 1, 3);
            if (count == 1 || points.Count < 24)
            {
                shapes.Add(new KeyValuePair<int, Bounds>(bone, bounds));
                return;
            }

            float minimum = bounds.min[axis];
            float width = size[axis] / count;
            var bins = new List<Vector3>[count];
            for (int i = 0; i < count; i++)
            {
                bins[i] = new List<Vector3>();
            }

            foreach (Vector3 point in points)
            {
                int bin = Mathf.Clamp((int)((point[axis] - minimum) / width), 0, count - 1);
                bins[bin].Add(point);
            }

            for (int i = 0; i < count; i++)
            {
                if (bins[i].Count == 0)
                {
                    continue;
                }

                Bounds section = Fit(bins[i]);
                Vector3 min = section.min;
                Vector3 max = section.max;
                min[axis] = minimum + width * i;
                max[axis] = minimum + width * (i + 1);
                section.SetMinMax(min, max);
                shapes.Add(new KeyValuePair<int, Bounds>(bone, section));
            }
        }

        private static void AddEntry(SortedDictionary<string, List<KeyValuePair<int, Bounds>>> data, string key, List<KeyValuePair<int, Bounds>> shapes)
        {
            if (!data.TryGetValue(key, out List<KeyValuePair<int, Bounds>> existing))
            {
                data.Add(key, shapes);
                return;
            }

            if (!existing.SequenceEqual(shapes))
            {
                throw new InvalidOperationException($"First-person collision calibration found conflicting geometry for '{key}'.");
            }
        }

        private static Bounds Fit(List<Vector3> points)
        {
            var bounds = new Bounds(points[0], Vector3.zero);
            for (int i = 1; i < points.Count; i++)
            {
                bounds.Encapsulate(points[i]);
            }

            bounds.size = Vector3.Max(bounds.size, Vector3.one * MIN_EXTENT * 2.0f);
            return bounds;
        }

        private static Vector3[] Vertices(Mesh mesh)
        {
            using Mesh.MeshDataArray readable = MeshUtility.AcquireReadOnlyMeshData(mesh);
            using var vertices = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            readable[0].GetVertices(vertices);
            return vertices.ToArray();
        }

        private static byte[] Encode(SortedDictionary<string, List<KeyValuePair<int, Bounds>>> data)
        {
            using var memory = new MemoryStream();
            using var writer = new BinaryWriter(memory);
            writer.Write(FirstPersonContactCatalog.VERSION);
            writer.Write(data.Count);
            foreach (KeyValuePair<string, List<KeyValuePair<int, Bounds>>> entry in data)
            {
                writer.Write(entry.Key);
                writer.Write(entry.Value.Count);
                foreach (KeyValuePair<int, Bounds> shape in entry.Value)
                {
                    writer.Write(shape.Key);
                    WriteVector(writer, shape.Value.center);
                    WriteVector(writer, shape.Value.size);
                }
            }

            writer.Flush();
            return memory.ToArray();
        }

        private static void WriteVector(BinaryWriter writer, Vector3 vector)
        {
            writer.Write(vector.x);
            writer.Write(vector.y);
            writer.Write(vector.z);
        }
    }
}

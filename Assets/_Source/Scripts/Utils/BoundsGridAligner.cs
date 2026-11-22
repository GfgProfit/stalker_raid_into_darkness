using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public sealed class BoundsGridAligner : MonoBehaviour
{
    [Header("Objects to arrange (in order)")]
    [FormerlySerializedAs("objects")]
    [SerializeField] private Transform[] _objects = new Transform[0];

    [Header("Layout")]
    [Min(0f)]
    [FormerlySerializedAs("spacing")]
    [SerializeField] private float _spacing = 0.5f;

    [Tooltip("0 = automatically create an approximately square grid.")]
    [Min(0)]
    [FormerlySerializedAs("columns")]
    [SerializeField] private int _columns;

    [Tooltip("Leave empty to use the parent of the first object. Positions are relative to this root.")]
    [FormerlySerializedAs("layoutRoot")]
    [SerializeField] private Transform _layoutRoot;

    [Tooltip("Align the lowest points of the models along the Y axis.")]
    [FormerlySerializedAs("alignBottoms")]
    [SerializeField] private bool _alignBottoms = true;

    private readonly struct Item
    {
        public Transform Transform { get; }
        public Bounds BoundsFromPivot { get; }

        public Item(Transform transform, Bounds boundsFromPivot)
        {
            Transform = transform;
            BoundsFromPivot = boundsFromPivot;
        }
    }

    [ContextMenu("Align")]
    public void Align()
    {
        if (_objects == null || _objects.Length == 0 || _objects[0] == null)
        {
            Debug.LogWarning("BoundsGridAligner: The first array element must contain a Transform.", this);
            return;
        }

        Transform root = _layoutRoot != null ? _layoutRoot : _objects[0].parent;
        if (root == null)
        {
            Debug.LogWarning("BoundsGridAligner: Specify Layout Root or parent the first object.", this);
            return;
        }

        var items = new List<Item>(_objects.Length);
        var unique = new HashSet<Transform>();

        foreach (Transform target in _objects)
        {
            if (target == null)
            {
                continue;
            }

            if (!unique.Add(target))
            {
                Debug.LogWarning("BoundsGridAligner: Duplicate object skipped: " + target.name, this);
                continue;
            }

            if (target == root || !target.IsChildOf(root))
            {
                Debug.LogError("BoundsGridAligner: Every object must be a descendant of Layout Root: " + target.name, this);
                return;
            }

            if (!TryGetModelBounds(target, root, out Bounds rootBounds))
            {
                Debug.LogError("BoundsGridAligner: No MeshRenderer/SkinnedMeshRenderer found on " + target.name, this);
                return;
            }

            Vector3 originalPivot = root.InverseTransformPoint(target.position);
            rootBounds.center -= originalPivot;

            items.Add(new Item(target, rootBounds));
        }

        if (items.Count == 0)
        {
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            for (int j = i + 1; j < items.Count; j++)
            {
                if (items[i].Transform.IsChildOf(items[j].Transform) || items[j].Transform.IsChildOf(items[i].Transform))
                {
                    Debug.LogError("BoundsGridAligner: Do not list a parent and its child as separate items.", this);
                    return;
                }
            }
        }

        int columnCount = _columns > 0 ? Mathf.Min(_columns, items.Count) : Mathf.CeilToInt(Mathf.Sqrt(items.Count));
        float leftEdge = items[0].BoundsFromPivot.min.x;
        float rowNearEdge = items[0].BoundsFromPivot.min.z;
        float bottomEdge = items[0].BoundsFromPivot.min.y;
        float gap = Mathf.Max(0f, _spacing);

        var positions = new Vector3[items.Count];

        for (int start = 0; start < items.Count; start += columnCount)
        {
            int end = Mathf.Min(start + columnCount, items.Count);
            float nextLeftEdge = leftEdge;
            float rowDepth = 0f;

            for (int i = start; i < end; i++)
            {
                Bounds b = items[i].BoundsFromPivot;
                positions[i] = new Vector3(nextLeftEdge - b.min.x, _alignBottoms ? bottomEdge - b.min.y : 0f, rowNearEdge - b.min.z);
                nextLeftEdge += b.size.x + gap;
                rowDepth = Mathf.Max(rowDepth, b.size.z);
            }

            rowNearEdge += rowDepth + gap;
        }

#if UNITY_EDITOR
        var undoTargets = new Object[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            undoTargets[i] = items[i].Transform;
        }

        UnityEditor.Undo.RecordObjects(undoTargets, "Align Objects By Bounds");
#endif

        for (int i = 0; i < items.Count; i++)
        {
            Transform target = items[i].Transform;
            target.position = root.TransformPoint(positions[i]);

#if UNITY_EDITOR
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(target);
#endif
        }

        Debug.Log($"BoundsGridAligner: Arranged {items.Count} objects into {columnCount} columns.", this);
    }

    private static bool TryGetModelBounds(Transform modelRoot, Transform layoutRoot, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;

        Renderer[] renderers = modelRoot.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            if (renderer is MeshRenderer)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    continue;
                }
            }
            else if (renderer is SkinnedMeshRenderer skinned)
            {
                if (skinned.sharedMesh == null)
                {
                    continue;
                }
            }
            else
            {
                continue;
            }

            Bounds localBounds = renderer.localBounds;
            Vector3 min = localBounds.min;
            Vector3 max = localBounds.max;
            Matrix4x4 toRoot = layoutRoot.worldToLocalMatrix * renderer.transform.localToWorldMatrix;

            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 localCorner = new Vector3((corner & 1) == 0 ? min.x : max.x, (corner & 2) == 0 ? min.y : max.y, (corner & 4) == 0 ? min.z : max.z);

                Vector3 point = toRoot.MultiplyPoint3x4(localCorner);

                if (!hasBounds)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(point);
                }
            }
        }

        return hasBounds;
    }
}

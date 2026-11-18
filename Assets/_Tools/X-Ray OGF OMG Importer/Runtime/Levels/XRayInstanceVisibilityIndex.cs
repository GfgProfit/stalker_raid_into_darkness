using System;
using System.Collections.Generic;
using UnityEngine;

namespace XRay.Importer.Levels
{
    public sealed class XRayInstanceVisibilityIndex
    {
        private const int NODE_DATA_BYTES = 48;
        private const float PLANE_ROUNDING_MARGIN = 0.00001f;
        private const float BOUNDS_ROUNDING_MARGIN = 0.000002f;

        private readonly SourceComparer[] _comparers = { new SourceComparer(0), new SourceComparer(1), new SourceComparer(2) };
        private XRayInstanceBatchSet _owner;
        private int _revision = -1;
        private Bounds[] _conservativeBounds;
        private int _boundsRevision;
        private Node[] _nodes = Array.Empty<Node>();
        private Bounds[] _windBounds = Array.Empty<Bounds>();
        private int[] _windBoundsStamps = Array.Empty<int>();
        private bool[] _windBoundsInvalid = Array.Empty<bool>();
        private int[] _order = Array.Empty<int>();
        private byte[] _selected = Array.Empty<byte>();
        private int[] _selectedSources = Array.Empty<int>();
        private int _nodeCount;
        private Plane[] _planes;
        private int _planeCount;
        private XRayFloraWindState _wind;
        private XRayFloraWindBounds.SnapshotKey _windBoundsKey;
        private int _windBoundsStamp;
        private bool _validPlanes;
        private bool _selectionValid;

        public int VisitedNodes { get; private set; }
        public int SelectedCount { get; private set; }
        public long MemoryBytes => (long)_nodes.Length * NODE_DATA_BYTES + (long)_windBounds.Length * 24L + (long)_windBoundsStamps.Length * sizeof(int) + _windBoundsInvalid.Length + (long)_order.Length * sizeof(int) + _selected.Length + (long)_selectedSources.Length * sizeof(int);

        public void Ensure(XRayInstanceBatchSet set)
        {
            Ensure(set, null, 0);
        }

        public void Ensure(XRayInstanceBatchSet set, Bounds[] conservativeBounds, int boundsRevision)
        {
            int count = set != null ? set.Batches.Count : 0;
            if (conservativeBounds != null && conservativeBounds.Length != count) throw new ArgumentException("Conservative bounds must match the number of source batches.", nameof(conservativeBounds));
            if (set == null)
            {
                _owner = null;
                _revision = -1;
                _conservativeBounds = null;
                _boundsRevision = 0;
                _nodes = Array.Empty<Node>();
                _windBounds = Array.Empty<Bounds>();
                _windBoundsStamps = Array.Empty<int>();
                _windBoundsInvalid = Array.Empty<bool>();
                _windBoundsStamp = 0;
                _windBoundsKey = default;
                _order = Array.Empty<int>();
                _selected = Array.Empty<byte>();
                _selectedSources = Array.Empty<int>();
                _nodeCount = 0;
                _planes = null;
                _planeCount = 0;
                _wind = null;
                _selectionValid = false;
                VisitedNodes = 0;
                SelectedCount = 0;
                foreach (SourceComparer comparer in _comparers)
                {
                    comparer.Sources = null;
                    comparer.ConservativeBounds = null;
                }
                return;
            }
            if (_owner == set && _revision == set.MatrixRevision && _selected.Length == count && ReferenceEquals(_conservativeBounds, conservativeBounds) && _boundsRevision == boundsRevision) return;
            _owner = set;
            _revision = set.MatrixRevision;
            _conservativeBounds = conservativeBounds;
            _boundsRevision = boundsRevision;
            _nodes = count > 0 ? new Node[checked(count * 2 - 1)] : Array.Empty<Node>();
            _windBounds = new Bounds[_nodes.Length];
            _windBoundsStamps = new int[_nodes.Length];
            _windBoundsInvalid = new bool[_nodes.Length];
            _windBoundsStamp = 0;
            _windBoundsKey = default;
            _order = new int[count];
            _selected = new byte[count];
            _selectedSources = new int[count];
            _nodeCount = 0;
            _selectionValid = false;
            VisitedNodes = 0;
            SelectedCount = 0;
            for (int index = 0; index < count; index++) _order[index] = index;
            foreach (SourceComparer comparer in _comparers)
            {
                comparer.Sources = set.Batches;
                comparer.ConservativeBounds = conservativeBounds;
            }
            if (count > 0) Build(0, count);
        }

        public void Select(Plane[] planes, int planeCount, XRayFloraWindState wind)
        {
            _planes = planes;
            _planeCount = planeCount;
            _wind = wind;
            var key = new XRayFloraWindBounds.SnapshotKey(wind);
            if (_windBoundsStamp == 0 || !_windBoundsKey.Equals(key))
            {
                if (_windBoundsStamp == int.MaxValue)
                {
                    Array.Clear(_windBoundsStamps, 0, _windBoundsStamps.Length);
                    _windBoundsStamp = 1;
                }
                else _windBoundsStamp++;
                _windBoundsKey = key;
            }
            _validPlanes = ValidPlanes(planes, planeCount);
            _selectionValid = true;
            VisitedNodes = 0;
            SelectedCount = 0;
            Array.Clear(_selected, 0, _selected.Length);
            if (_nodeCount > 0) Visit(0);
        }

        public bool IsCandidate(int sourceIndex)
        {
            return !_selectionValid || (uint)sourceIndex >= (uint)_selected.Length || _selected[sourceIndex] != 0;
        }

        public int SelectedSource(int ordinal)
        {
            if ((uint)ordinal >= (uint)SelectedCount) throw new ArgumentOutOfRangeException(nameof(ordinal));
            return _selectedSources[ordinal];
        }

        private int Build(int start, int count)
        {
            int index = _nodeCount++;
            var node = new Node { Left = -1, Right = -1, Start = start, Count = count };
            bool hasBounds = false;
            for (int offset = start; offset < start + count; offset++)
            {
                XRayInstanceBatch source = _owner.Batches[_order[offset]];
                if (source == null)
                {
                    node.Invalid = true;
                    continue;
                }
                Bounds bounds = source.Bounds;
                float height = bounds.size.y;
                if (!FiniteBounds(bounds) || !float.IsFinite(height))
                {
                    node.Invalid = true;
                    continue;
                }
                if (_conservativeBounds != null)
                {
                    Bounds conservative = _conservativeBounds[_order[offset]];
                    float conservativeHeight = conservative.size.y;
                    if (!FiniteBounds(conservative) || !float.IsFinite(conservativeHeight))
                    {
                        node.Invalid = true;
                        continue;
                    }
                    bounds.Encapsulate(conservative);
                    height = Mathf.Max(height, conservativeHeight);
                }
                if (hasBounds)
                {
                    Bounds combined = node.Bounds;
                    combined.Encapsulate(bounds);
                    node.Bounds = combined;
                }
                else node.Bounds = bounds;
                hasBounds = true;
                node.SourceHeight = Mathf.Max(node.SourceHeight, height);
            }
            node.Invalid |= !hasBounds || !FiniteBounds(node.Bounds);
            if (!node.Invalid)
            {
                Bounds expanded = node.Bounds;
                Vector3 center = expanded.center;
                Vector3 magnitude = new Vector3(Mathf.Abs(center.x), Mathf.Abs(center.y), Mathf.Abs(center.z)) + expanded.extents;
                expanded.Expand(Vector3.Max(Vector3.one, magnitude) * BOUNDS_ROUNDING_MARGIN);
                node.Bounds = expanded;
                node.Invalid = !FiniteBounds(expanded);
            }
            if (count > 1)
            {
                Vector3 size = node.Bounds.size;
                int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                Array.Sort(_order, start, count, _comparers[axis]);
                int leftCount = count / 2;
                node.Left = Build(start, leftCount);
                node.Right = Build(start + leftCount, count - leftCount);
            }
            _nodes[index] = node;
            return index;
        }

        private void Visit(int index)
        {
            VisitedNodes++;
            Node node = _nodes[index];
            if (_windBoundsStamps[index] != _windBoundsStamp)
            {
                Bounds expanded = XRayFloraWindBounds.Expand(node.Bounds, _wind, node.SourceHeight);
                _windBounds[index] = expanded;
                _windBoundsInvalid[index] = node.Invalid || !FiniteBounds(expanded);
                _windBoundsStamps[index] = _windBoundsStamp;
            }
            Bounds bounds = _windBounds[index];
            if (_windBoundsInvalid[index])
            {
                Mark(node);
                return;
            }
            int visibility = CameraVisibility(bounds);
            if (visibility > 0)
            {
                Mark(node);
                return;
            }
            if (visibility < 0) return;
            if (node.Count == 1)
            {
                SelectSource(_order[node.Start]);
                return;
            }
            Visit(node.Left);
            Visit(node.Right);
        }

        private void Mark(Node node)
        {
            for (int offset = node.Start; offset < node.Start + node.Count; offset++) SelectSource(_order[offset]);
        }

        private void SelectSource(int sourceIndex)
        {
            if (_selected[sourceIndex] != 0) return;
            _selected[sourceIndex] = 1;
            _selectedSources[SelectedCount++] = sourceIndex;
        }

        private int CameraVisibility(Bounds bounds)
        {
            if (!_validPlanes) return 1;
            bool inside = true;
            Vector3 center = bounds.center;
            Vector3 extent = bounds.extents;
            for (int index = 0; index < _planeCount; index++)
            {
                Plane plane = _planes[index];
                Vector3 normal = plane.normal;
                float distance = Vector3.Dot(normal, center) + plane.distance;
                float radius = Mathf.Abs(normal.x) * extent.x + Mathf.Abs(normal.y) * extent.y + Mathf.Abs(normal.z) * extent.z;
                if (!float.IsFinite(distance) || !float.IsFinite(radius)) return 0;
                float margin = PLANE_ROUNDING_MARGIN * (1f + Mathf.Abs(distance) + radius);
                if (distance < -radius - margin) return -1;
                if (distance < radius + margin) inside = false;
            }
            return inside ? 1 : 0;
        }

        private static bool ValidPlanes(Plane[] planes, int count)
        {
            if (planes == null || count < 0 || count > planes.Length) return false;
            for (int index = 0; index < count; index++)
            {
                Plane plane = planes[index];
                Vector3 normal = plane.normal;
                float magnitude = normal.sqrMagnitude;
                if (!float.IsFinite(normal.x) || !float.IsFinite(normal.y) || !float.IsFinite(normal.z) || !float.IsFinite(plane.distance) || !float.IsFinite(magnitude) || magnitude <= 0f) return false;
            }
            return true;
        }

        private static bool FiniteBounds(Bounds bounds)
        {
            Vector3 center = bounds.center;
            Vector3 extent = bounds.extents;
            Vector3 minimum = center - extent;
            Vector3 maximum = center + extent;
            return float.IsFinite(center.x) && float.IsFinite(center.y) && float.IsFinite(center.z) && float.IsFinite(extent.x) && float.IsFinite(extent.y) && float.IsFinite(extent.z) && extent.x >= 0f && extent.y >= 0f && extent.z >= 0f && float.IsFinite(minimum.x) && float.IsFinite(minimum.y) && float.IsFinite(minimum.z) && float.IsFinite(maximum.x) && float.IsFinite(maximum.y) && float.IsFinite(maximum.z);
        }

        private struct Node
        {
            public Bounds Bounds { get; set; }
            public float SourceHeight { get; set; }
            public int Left { get; set; }
            public int Right { get; set; }
            public int Start { get; set; }
            public int Count { get; set; }
            public bool Invalid { get; set; }
        }

        private sealed class SourceComparer : IComparer<int>
        {
            private readonly int _axis;

            public IReadOnlyList<XRayInstanceBatch> Sources { get; set; }
            public Bounds[] ConservativeBounds { get; set; }

            public SourceComparer(int axis)
            {
                _axis = axis;
            }

            public int Compare(int first, int second)
            {
                XRayInstanceBatch firstSource = Sources[first];
                XRayInstanceBatch secondSource = Sources[second];
                float firstCenter = firstSource != null ? SourceBounds(firstSource, first).center[_axis] : 0f;
                float secondCenter = secondSource != null ? SourceBounds(secondSource, second).center[_axis] : 0f;
                int result = firstCenter.CompareTo(secondCenter);
                return result != 0 ? result : first.CompareTo(second);
            }

            private Bounds SourceBounds(XRayInstanceBatch source, int index)
            {
                Bounds bounds = source.Bounds;
                if (ConservativeBounds != null) bounds.Encapsulate(ConservativeBounds[index]);
                return bounds;
            }
        }
    }
}

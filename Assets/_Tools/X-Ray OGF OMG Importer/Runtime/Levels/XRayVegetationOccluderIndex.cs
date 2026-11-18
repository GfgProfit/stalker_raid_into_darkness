using System;
using System.Collections.Generic;
using UnityEngine;

namespace XRay.Importer.Levels
{
    internal sealed class XRayVegetationOccluderIndex
    {
        private const int LEAF_SIZE = 8;
        private const int NODE_BUDGET_PER_RENDERER = 2;
        private const float BOUNDS_ROUNDING_MARGIN = 0.000002f;

        private readonly Node[] _nodes;
        private readonly int[] _order;

        internal XRayVegetationOccluderIndex(Bounds[] bounds)
        {
            _order = new int[bounds.Length];
            int count = 0;
            for (int index = 0; index < bounds.Length; index++)
            {
                if (Valid(bounds[index])) _order[count++] = index;
            }

            if (count == 0)
            {
                _nodes = Array.Empty<Node>();
                return;
            }

            int maximumLeaves = Mathf.Max(1, count / (LEAF_SIZE / 2));
            _nodes = new Node[maximumLeaves * 2 - 1];
            var comparers = new[] { new CenterComparer(bounds, 0), new CenterComparer(bounds, 1), new CenterComparer(bounds, 2) };
            int nextNode = 0;
            Build(0, count);

            int Build(int start, int itemCount)
            {
                int index = nextNode++;
                Bounds first = bounds[_order[start]];
                Vector3 minimum = first.min;
                Vector3 maximum = first.max;
                Vector3 centerMinimum = first.center;
                Vector3 centerMaximum = first.center;
                for (int offset = start + 1; offset < start + itemCount; offset++)
                {
                    Bounds item = bounds[_order[offset]];
                    minimum = Vector3.Min(minimum, item.min);
                    maximum = Vector3.Max(maximum, item.max);
                    centerMinimum = Vector3.Min(centerMinimum, item.center);
                    centerMaximum = Vector3.Max(centerMaximum, item.center);
                }

                Bounds combined = default;
                combined.SetMinMax(minimum, maximum);
                combined.Expand(Vector3.Max(Vector3.one, Vector3.Max(-minimum, maximum)) * BOUNDS_ROUNDING_MARGIN);
                if (itemCount <= LEAF_SIZE)
                {
                    _nodes[index] = new Node(combined, start, itemCount, -1, -1);
                    return index;
                }

                Vector3 size = centerMaximum - centerMinimum;
                int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                Array.Sort(_order, start, itemCount, comparers[axis]);
                int leftCount = itemCount / 2;
                int left = Build(start, leftCount);
                int right = Build(start + leftCount, itemCount - leftCount);
                _nodes[index] = new Node(combined, 0, 0, left, right);
                return index;
            }
        }

        internal int Select(Plane[] planes, Vector3 eye, float maximumDistanceSquared, int[] results)
        {
            if (_nodes.Length == 0 || results.Length == 0) return 0;
            int count = 0;
            int remainingNodes = results.Length * NODE_BUDGET_PER_RENDERER;
            Visit(0, _nodes[0].Bounds.SqrDistance(eye));
            return count;

            void Visit(int index, float distance)
            {
                if (remainingNodes == 0 || count == results.Length) return;
                remainingNodes--;
                Node node = _nodes[index];
                if (distance > maximumDistanceSquared || !GeometryUtility.TestPlanesAABB(planes, node.Bounds)) return;
                if (node.Count > 0)
                {
                    int length = Mathf.Min(node.Count, results.Length - count);
                    Array.Copy(_order, node.Start, results, count, length);
                    count += length;
                    return;
                }

                float leftDistance = _nodes[node.Left].Bounds.SqrDistance(eye);
                float rightDistance = _nodes[node.Right].Bounds.SqrDistance(eye);
                if (leftDistance <= rightDistance)
                {
                    Visit(node.Left, leftDistance);
                    Visit(node.Right, rightDistance);
                }
                else
                {
                    Visit(node.Right, rightDistance);
                    Visit(node.Left, leftDistance);
                }
            }
        }

        private static bool Valid(Bounds bounds)
        {
            Vector3 extents = bounds.extents;
            Vector3 minimum = bounds.min;
            Vector3 maximum = bounds.max;
            return extents.x >= 0f && extents.y >= 0f && extents.z >= 0f && extents.sqrMagnitude > 0f && float.IsFinite(minimum.x) && float.IsFinite(minimum.y) && float.IsFinite(minimum.z) && float.IsFinite(maximum.x) && float.IsFinite(maximum.y) && float.IsFinite(maximum.z);
        }

        private readonly struct Node
        {
            public Bounds Bounds { get; }
            public int Start { get; }
            public int Count { get; }
            public int Left { get; }
            public int Right { get; }

            public Node(Bounds bounds, int start, int count, int left, int right)
            {
                Bounds = bounds;
                Start = start;
                Count = count;
                Left = left;
                Right = right;
            }
        }

        private sealed class CenterComparer : IComparer<int>
        {
            private readonly Bounds[] _bounds;
            private readonly int _axis;

            public CenterComparer(Bounds[] bounds, int axis)
            {
                _bounds = bounds;
                _axis = axis;
            }

            public int Compare(int first, int second)
            {
                int result = _bounds[first].center[_axis].CompareTo(_bounds[second].center[_axis]);
                return result != 0 ? result : first.CompareTo(second);
            }
        }
    }
}

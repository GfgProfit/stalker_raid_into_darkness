using System;
using System.Collections.Generic;
using UnityEngine;
using XRay.Importer.Core.Level;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    internal sealed class XRayHomOccluderValidation
    {
        private const int MAX_PATCHES_PER_MESH = 40;
        private const int MAX_POLYGON_VERTICES = 8;
        private const int MAX_FACES_PER_LEAF = 8;
        private const float MAX_PLANE_OFFSET = 0.2f;
        private const float MIN_NORMAL_AGREEMENT = 0.985f;
        private const float MIN_DOUBLE_AREA_SQUARED = 0.04f;
        private const float INSET_FRACTION = 0.00025f;

        private readonly List<HomFace> _source = new List<HomFace>();
        private readonly List<Node> _nodes = new List<Node>();
        private readonly int[] _sourceOrder;
        private readonly List<int> _candidates = new List<int>();
        private readonly XRayVegetationOccluderData.Triangle[] _patches = new XRayVegetationOccluderData.Triangle[MAX_PATCHES_PER_MESH];
        private readonly float[] _priorities = new float[MAX_PATCHES_PER_MESH];
        private readonly PolygonPoint[] _input = new PolygonPoint[MAX_POLYGON_VERTICES];
        private readonly PolygonPoint[] _output = new PolygonPoint[MAX_POLYGON_VERTICES];
        private readonly PolygonPoint[] _best = new PolygonPoint[MAX_POLYGON_VERTICES];

        private Matrix4x4 _meshToLevel;
        private bool _identity;
        private int _count;

        public XRayHomOccluderValidation(IReadOnlyList<XRayHomTriangle> triangles)
        {
            foreach (XRayHomTriangle triangle in triangles)
            {
                Vector3 a = XRayUnitySpace.Position(triangle.A, 1f);
                Vector3 b = XRayUnitySpace.Position(triangle.B, 1f);
                Vector3 c = XRayUnitySpace.Position(triangle.C, 1f);
                Vector3 cross = Vector3.Cross(b - a, c - a);
                float area = cross.sqrMagnitude;
                if (!float.IsFinite(area) || area < MIN_DOUBLE_AREA_SQUARED) continue;
                var face = new HomFace(a, b, c, cross / Mathf.Sqrt(area), triangle.DoubleSided);
                if (face.Bounds.Intersects(face.Bounds)) _source.Add(face);
            }

            _sourceOrder = new int[_source.Count];
            for (int index = 0; index < _sourceOrder.Length; index++) _sourceOrder[index] = index;
            if (_sourceOrder.Length != 0) BuildIndex(0, _sourceOrder.Length, new SourceComparer(_source));
        }

        public bool BeginMesh(Bounds bounds)
        {
            return BeginMesh(bounds, Matrix4x4.identity);
        }

        public bool BeginMesh(Bounds bounds, Matrix4x4 meshToLevel)
        {
            _candidates.Clear();
            _count = 0;
            _meshToLevel = meshToLevel;
            _identity = meshToLevel.Equals(Matrix4x4.identity);
            if (!_identity)
            {
                if (!(meshToLevel.determinant > 0f)) return false;
                Vector3 e = bounds.extents;
                var x = new Vector3(Mathf.Abs(meshToLevel.m00), Mathf.Abs(meshToLevel.m01), Mathf.Abs(meshToLevel.m02));
                var y = new Vector3(Mathf.Abs(meshToLevel.m10), Mathf.Abs(meshToLevel.m11), Mathf.Abs(meshToLevel.m12));
                var z = new Vector3(Mathf.Abs(meshToLevel.m20), Mathf.Abs(meshToLevel.m21), Mathf.Abs(meshToLevel.m22));
                bounds = new Bounds(meshToLevel.MultiplyPoint3x4(bounds.center), 2f * new Vector3(Vector3.Dot(x, e), Vector3.Dot(y, e), Vector3.Dot(z, e)));
            }

            if (_nodes.Count == 0) return false;
            Vector3 minimum = bounds.min;
            Vector3 maximum = bounds.max;
            if (_nodes[0].IsWithin(minimum, maximum))
            {
                for (int index = 0; index < _source.Count; index++) _candidates.Add(index);
            }
            else
            {
                CollectCandidates(0, bounds, minimum, maximum);
                _candidates.Sort();
            }

            return _candidates.Count != 0;
        }

        public void AddBackingTriangle(int subMesh, Vector3 a, Vector3 b, Vector3 c)
        {
            if (_candidates.Count == 0) return;
            float localArea = Vector3.Cross(b - a, c - a).sqrMagnitude;
            float minimum = _count == MAX_PATCHES_PER_MESH ? _priorities[_count - 1] : MIN_DOUBLE_AREA_SQUARED;
            if (!float.IsFinite(localArea) || localArea <= minimum) return;

            Vector3 levelA = _identity ? a : _meshToLevel.MultiplyPoint3x4(a);
            Vector3 levelB = _identity ? b : _meshToLevel.MultiplyPoint3x4(b);
            Vector3 levelC = _identity ? c : _meshToLevel.MultiplyPoint3x4(c);
            Bounds bounds = TriangleBounds(levelA, levelB, levelC);
            Vector3 normal = Vector3.Cross(levelB - levelA, levelC - levelA).normalized;
            var pointA = new PolygonPoint(a, levelA);
            var pointB = new PolygonPoint(b, levelB);
            var pointC = new PolygonPoint(c, levelC);
            var center = new PolygonPoint((a + b + c) / 3f, (levelA + levelB + levelC) / 3f);
            pointA = PolygonPoint.Lerp(pointA, center, INSET_FRACTION);
            pointB = PolygonPoint.Lerp(pointB, center, INSET_FRACTION);
            pointC = PolygonPoint.Lerp(pointC, center, INSET_FRACTION);

            int bestCount = 0;
            float bestArea = 0f;
            bool doubleSided = false;
            foreach (int index in _candidates)
            {
                HomFace face = _source[index];
                if (!face.Bounds.Intersects(bounds) || !face.Supports(levelA, levelB, levelC, normal)) continue;
                if (face.Contains(levelA) && face.Contains(levelB) && face.Contains(levelC))
                {
                    Insert(new XRayVegetationOccluderData.Triangle(subMesh, a, b, c, face.DoubleSided), localArea);
                    return;
                }

                _input[0] = pointA;
                _input[1] = pointB;
                _input[2] = pointC;
                int count = Clip(_input, 3, _output, face.A, face.EdgeAB);
                if (count < 3) continue;
                count = Clip(_output, count, _input, face.B, face.EdgeBC);
                if (count < 3) continue;
                count = Clip(_input, count, _output, face.C, face.EdgeCA);
                if (count < 3) continue;

                float area = 0f;
                Vector3 pivot = _output[0].Local;
                for (int i = 1; i + 1 < count; i++)
                {
                    area += Vector3.Cross(_output[i].Local - pivot, _output[i + 1].Local - pivot).magnitude;
                }
                if (!(area > bestArea)) continue;
                Array.Copy(_output, _best, count);
                bestCount = count;
                bestArea = area;
                doubleSided = face.DoubleSided;
            }

            for (int i = 1; i + 1 < bestCount; i++)
            {
                Vector3 first = _best[0].Local;
                Vector3 second = _best[i].Local;
                Vector3 third = _best[i + 1].Local;
                float priority = Vector3.Cross(second - first, third - first).sqrMagnitude;
                if (!float.IsFinite(priority) || priority < MIN_DOUBLE_AREA_SQUARED) continue;
                Insert(new XRayVegetationOccluderData.Triangle(subMesh, first, second, third, doubleSided), priority);
            }
        }

        public XRayVegetationOccluderData.Triangle[] FinishMesh()
        {
            if (_count == 0) return Array.Empty<XRayVegetationOccluderData.Triangle>();
            var result = new XRayVegetationOccluderData.Triangle[_count];
            Array.Copy(_patches, result, _count);
            return result;
        }

        private int BuildIndex(int start, int count, SourceComparer comparer)
        {
            int index = _nodes.Count;
            _nodes.Add(default);

            Bounds first = _source[_sourceOrder[start]].Bounds;
            Vector3 minimum = first.min;
            Vector3 maximum = first.max;
            for (int offset = start + 1; offset < start + count; offset++)
            {
                Bounds bounds = _source[_sourceOrder[offset]].Bounds;
                minimum = Vector3.Min(minimum, bounds.min);
                maximum = Vector3.Max(maximum, bounds.max);
            }

            int left = -1;
            int right = -1;
            if (count > MAX_FACES_PER_LEAF)
            {
                Vector3 size = maximum - minimum;
                comparer.Axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                Array.Sort(_sourceOrder, start, count, comparer);
                int leftCount = count / 2;
                left = BuildIndex(start, leftCount, comparer);
                right = BuildIndex(start + leftCount, count - leftCount, comparer);
            }
            _nodes[index] = new Node(minimum, maximum, start, count, left, right);
            return index;
        }

        private void CollectCandidates(int index, Bounds bounds, Vector3 minimum, Vector3 maximum)
        {
            Node node = _nodes[index];
            if (!node.Intersects(minimum, maximum)) return;
            if (node.Left >= 0)
            {
                CollectCandidates(node.Left, bounds, minimum, maximum);
                CollectCandidates(node.Right, bounds, minimum, maximum);
                return;
            }

            for (int offset = node.Start; offset < node.Start + node.Count; offset++)
            {
                int sourceIndex = _sourceOrder[offset];
                if (_source[sourceIndex].Bounds.Intersects(bounds)) _candidates.Add(sourceIndex);
            }
        }

        private void Insert(XRayVegetationOccluderData.Triangle triangle, float priority)
        {
            int index = Mathf.Min(_count, MAX_PATCHES_PER_MESH - 1);
            if (_count == MAX_PATCHES_PER_MESH && priority <= _priorities[index]) return;
            while (index > 0 && priority > _priorities[index - 1])
            {
                _priorities[index] = _priorities[index - 1];
                _patches[index] = _patches[index - 1];
                index--;
            }

            _priorities[index] = priority;
            _patches[index] = triangle;
            _count = Mathf.Min(_count + 1, MAX_PATCHES_PER_MESH);
        }

        private static int Clip(PolygonPoint[] input, int count, PolygonPoint[] output, Vector3 origin, Vector3 edge)
        {
            int written = 0;
            PolygonPoint previous = input[count - 1];
            float previousDistance = Vector3.Dot(edge, previous.Level - origin);
            for (int i = 0; i < count; i++)
            {
                PolygonPoint current = input[i];
                float distance = Vector3.Dot(edge, current.Level - origin);
                if ((previousDistance < 0f && distance > 0f) || (previousDistance > 0f && distance < 0f)) output[written++] = PolygonPoint.Lerp(previous, current, previousDistance / (previousDistance - distance));
                if (distance >= 0f) output[written++] = current;
                previous = current;
                previousDistance = distance;
            }

            return written;
        }

        private static Bounds TriangleBounds(Vector3 a, Vector3 b, Vector3 c)
        {
            var bounds = new Bounds();
            bounds.SetMinMax(Vector3.Min(a, Vector3.Min(b, c)), Vector3.Max(a, Vector3.Max(b, c)));
            return bounds;
        }

        private readonly struct Node
        {
            private readonly Vector3 _minimum;
            private readonly Vector3 _maximum;

            public int Start { get; }
            public int Count { get; }
            public int Left { get; }
            public int Right { get; }

            public Node(Vector3 minimum, Vector3 maximum, int start, int count, int left, int right)
            {
                _minimum = minimum;
                _maximum = maximum;
                Start = start;
                Count = count;
                Left = left;
                Right = right;
            }

            public bool IsWithin(Vector3 minimum, Vector3 maximum)
            {
                return _minimum.x >= minimum.x && _maximum.x <= maximum.x && _minimum.y >= minimum.y && _maximum.y <= maximum.y && _minimum.z >= minimum.z && _maximum.z <= maximum.z;
            }

            public bool Intersects(Vector3 minimum, Vector3 maximum)
            {
                return _minimum.x <= maximum.x && _maximum.x >= minimum.x && _minimum.y <= maximum.y && _maximum.y >= minimum.y && _minimum.z <= maximum.z && _maximum.z >= minimum.z;
            }
        }

        private sealed class SourceComparer : IComparer<int>
        {
            private readonly List<HomFace> _source;

            public int Axis { get; set; }

            public SourceComparer(List<HomFace> source)
            {
                _source = source;
            }

            public int Compare(int first, int second)
            {
                int result = _source[first].Bounds.center[Axis].CompareTo(_source[second].Bounds.center[Axis]);
                return result != 0 ? result : first.CompareTo(second);
            }
        }

        private readonly struct HomFace
        {
            public Vector3 A { get; }
            public Vector3 B { get; }
            public Vector3 C { get; }
            public Vector3 Normal { get; }
            public Vector3 EdgeAB { get; }
            public Vector3 EdgeBC { get; }
            public Vector3 EdgeCA { get; }
            public Bounds Bounds { get; }
            public bool DoubleSided { get; }

            public HomFace(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, bool doubleSided)
            {
                A = a;
                B = b;
                C = c;
                Normal = normal;
                EdgeAB = Vector3.Cross(normal, b - a);
                EdgeBC = Vector3.Cross(normal, c - b);
                EdgeCA = Vector3.Cross(normal, a - c);
                Bounds bounds = TriangleBounds(a, b, c);
                bounds.Expand(2f * MAX_PLANE_OFFSET);
                Bounds = bounds;
                DoubleSided = doubleSided;
            }

            public bool Supports(Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
            {
                float agreement = Vector3.Dot(normal, Normal);
                if (DoubleSided) agreement = Mathf.Abs(agreement);
                return agreement >= MIN_NORMAL_AGREEMENT && NearPlane(a) && NearPlane(b) && NearPlane(c);
            }

            public bool Contains(Vector3 point)
            {
                return Vector3.Dot(EdgeAB, point - A) >= 0f && Vector3.Dot(EdgeBC, point - B) >= 0f && Vector3.Dot(EdgeCA, point - C) >= 0f;
            }

            private bool NearPlane(Vector3 point)
            {
                return Mathf.Abs(Vector3.Dot(point - A, Normal)) <= MAX_PLANE_OFFSET;
            }
        }

        private readonly struct PolygonPoint
        {
            public Vector3 Local { get; }
            public Vector3 Level { get; }

            public PolygonPoint(Vector3 local, Vector3 level)
            {
                Local = local;
                Level = level;
            }

            public static PolygonPoint Lerp(PolygonPoint a, PolygonPoint b, float t)
            {
                return new PolygonPoint(Vector3.Lerp(a.Local, b.Local, t), Vector3.Lerp(a.Level, b.Level, t));
            }
        }
    }
}

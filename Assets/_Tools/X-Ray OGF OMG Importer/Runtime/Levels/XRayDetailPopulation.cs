using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using XRay.Importer.Core.Level;

namespace XRay.Importer.Levels
{
    public sealed class XRayDetailCell
    {
        public int X { get; internal set; }
        public int Z { get; internal set; }
        public Bounds Bounds { get; internal set; }
        public Vector3 FadeCenter { get; internal set; }
        public float Alpha { get; internal set; }
        public int Generation { get; internal set; }
        public List<XRayDetailInstance> Instances { get; } = new List<XRayDetailInstance>();
    }

    public sealed class XRayDetailPopulation
    {
        public const int MAX_SLOT_BUILDS_PER_FRAME = 4;
        public const int MAX_SLOT_PROBES_PER_FRAME = 512;
        public const double BUILD_BUDGET_MILLISECONDS = 2.0;

        private readonly XRayDetails _details;
        private readonly XRayDetailPlacement _placement;
        private readonly XRayDetailPlacementSettings _placementSettings;
        private readonly Dictionary<int, XRayDetailCell> _cells = new Dictionary<int, XRayDetailCell>();
        private readonly Stack<XRayDetailCell> _pool = new Stack<XRayDetailCell>();
        private readonly List<int> _remove = new List<int>();
        private readonly List<Vector2Int> _offsets = new List<Vector2Int>();
        private readonly Stopwatch _timer = new Stopwatch();
        private readonly bool _alignment;
        private readonly float _modelExtent;
        private readonly Vector3 _windExtent;
        private Vector2Int _eyeSlot = new Vector2Int(int.MinValue, int.MinValue);
        private int _cursor;
        private int _generation;
        private XRayGrassValues _values;

        public XRayDetailPopulation(XRayDetailDatabase database, XRayGrassDefaults defaults)
        {
            _details = XRayDetails.Read(database.SourceDetails.bytes, database.LevelName + " level.details");
            XRayCollisionModel collision = XRayCollisionModel.Read(database.SourceCollision.bytes, database.LevelName + " level.cform");
            collision.BuildGrid(XRayDetails.SLOT_SIZE);
            _alignment = database.SourceProfile == 1 && defaults.TerrainAlignment;
            float horizontal = Mathf.Abs(defaults.WindGrass.y) + Mathf.Abs(defaults.WindGrass.z);
            _windExtent = new Vector3(horizontal, Mathf.Abs(defaults.WindGrass.w), horizontal);
            _placementSettings = new XRayDetailPlacementSettings { MonolithRelease = database.SourceProfile == 1, TerrainSlope = defaults.TerrainSlope, HeightScale = 1.0f };
            _placement = new XRayDetailPlacement(_details, collision, database.Passable, _placementSettings);
            foreach (XRayDetailModel model in _details.Models)
            {
                var min = new Vector3(model.BoundsMin[0], model.BoundsMin[1], model.BoundsMin[2]);
                var max = new Vector3(model.BoundsMax[0], model.BoundsMax[1], model.BoundsMax[2]);
                _modelExtent = Mathf.Max(_modelExtent, Mathf.Max(min.magnitude, max.magnitude) * model.MaxScale * 2.0f);
            }

            _offsets.Add(Vector2Int.zero);
            int rings = Mathf.CeilToInt(250.0f / XRayDetails.SLOT_SIZE) + 2;
            for (int ring = 1; ring <= rings; ring++)
            {
                for (int x = -ring; x <= ring; x++)
                {
                    _offsets.Add(new Vector2Int(x, -ring));
                    _offsets.Add(new Vector2Int(x, ring));
                }

                for (int z = -ring + 1; z < ring; z++)
                {
                    _offsets.Add(new Vector2Int(-ring, z));
                    _offsets.Add(new Vector2Int(ring, z));
                }
            }
        }

        public Dictionary<int, XRayDetailCell>.ValueCollection Cells => _cells.Values;
        public int ResidentCells => _cells.Count;
        public int ResidentInstances { get; private set; }
        public double LastBuildMilliseconds { get; private set; }
        public float BoundsPadding => _modelExtent * 1.5f + _windExtent.magnitude;
        public float MaximumModelHeight => _modelExtent;
        public bool NeedsStreaming => _cursor < _offsets.Count;
        public bool HasFadingCells { get; private set; }

        public bool IsCurrent(XRayDetailCell cell)
        {
            return cell.Generation == _generation;
        }

        public void Apply(XRayGrassValues values)
        {
            if (!values.Valid) throw new ArgumentOutOfRangeException(nameof(values));
            if (_values.Density != values.Density)
            {
                _generation++;
                _placementSettings.Density = values.Density;
                _cursor = 0;
            }

            if (_values.Radius != values.Radius) _cursor = 0;
            _values = values;
        }

        public void Stream(Vector3 eye, float deltaTime = 0.0f)
        {
            _timer.Restart();
            var slot = new Vector2Int(Mathf.FloorToInt(eye.x / XRayDetails.SLOT_SIZE), Mathf.FloorToInt(eye.z / XRayDetails.SLOT_SIZE));
            if (slot != _eyeSlot)
            {
                _eyeSlot = slot;
                _cursor = 0;
            }

            float radius = Mathf.Max(0.0f, _values.FadeDistance);
            float exitRadius = radius + XRayDetails.SLOT_SIZE * 2.0f;
            _remove.Clear();
            HasFadingCells = false;
            foreach (KeyValuePair<int, XRayDetailCell> pair in _cells)
            {
                pair.Value.Alpha = Mathf.MoveTowards(pair.Value.Alpha, 1.0f, Mathf.Max(0.0f, deltaTime));
                if (IsCurrent(pair.Value) && pair.Value.Alpha < 1.0f) HasFadingCells = true;
                if (pair.Value.Bounds.SqrDistance(eye) > exitRadius * exitRadius) _remove.Add(pair.Key);
                if (_remove.Count >= MAX_SLOT_PROBES_PER_FRAME) break;
            }

            foreach (int key in _remove)
            {
                Recycle(_cells[key]);
                _cells.Remove(key);
            }

            int builds = 0;
            for (int probes = 0; probes < MAX_SLOT_PROBES_PER_FRAME && _cursor < _offsets.Count; probes++)
            {
                Vector2Int offset = _offsets[_cursor++];
                int x = slot.x + offset.x + _details.OffsetX;
                int z = slot.y + offset.y + _details.OffsetZ;
                if (x < 0 || x >= _details.SizeX || z < 0 || z >= _details.SizeZ) continue;
                int key = z * _details.SizeX + x;
                _cells.TryGetValue(key, out XRayDetailCell cached);
                if (cached != null && IsCurrent(cached) || _details.GetSlot(x, z).IsEmpty) continue;
                XRayDetailSlot source = _details.GetSlot(x, z);
                var center = new Vector3((x - _details.OffsetX + 0.5f) * XRayDetails.SLOT_SIZE, source.YBase + source.YHeight * 0.5f, (z - _details.OffsetZ + 0.5f) * XRayDetails.SLOT_SIZE);
                var bounds = new Bounds(center, new Vector3(XRayDetails.SLOT_SIZE, source.YHeight, XRayDetails.SLOT_SIZE));
                if (bounds.SqrDistance(eye) > radius * radius) continue;
                XRayDetailCell cell = cached ?? (_pool.Count > 0 ? _pool.Pop() : new XRayDetailCell());
                if (cached != null) ResidentInstances -= cached.Instances.Count;
                cell.Instances.Clear();
                int maximum = (_placement.DitherSize + 1) * (_placement.DitherSize + 1);
                if (cell.Instances.Capacity > Mathf.Max(64, maximum * 2)) cell.Instances.Capacity = maximum;
                cell.X = x;
                cell.Z = z;
                cell.Generation = _generation;
                cell.Alpha = 0.0f;
                HasFadingCells = true;
                _placement.PlaceSlot(x, z, cell.Instances);
                bool first = true;
                foreach (XRayDetailInstance instance in cell.Instances)
                {
                    XRayDetailModel model = _details.Models[instance.Model];
                    var min = new Vector3(model.BoundsMin[0], model.BoundsMin[1], model.BoundsMin[2]);
                    var max = new Vector3(model.BoundsMax[0], model.BoundsMax[1], model.BoundsMax[2]);
                    Matrix4x4 matrix = Matrix(instance, instance.Scale);
                    Vector3 extent = (max - min) * 0.5f;
                    Vector3 worldExtent = new Vector3(Mathf.Abs(matrix.m00) * extent.x + Mathf.Abs(matrix.m01) * extent.y + Mathf.Abs(matrix.m02) * extent.z, Mathf.Abs(matrix.m10) * extent.x + Mathf.Abs(matrix.m11) * extent.y + Mathf.Abs(matrix.m12) * extent.z, Mathf.Abs(matrix.m20) * extent.x + Mathf.Abs(matrix.m21) * extent.y + Mathf.Abs(matrix.m22) * extent.z);
                    var itemBounds = new Bounds(matrix.MultiplyPoint3x4((min + max) * 0.5f), worldExtent * 2.0f);
                    if (first) bounds = itemBounds;
                    else bounds.Encapsulate(itemBounds);
                    first = false;
                }

                cell.FadeCenter = bounds.center;
                bounds.Expand(Vector3.one * (_modelExtent * 3.0f) + _windExtent * 2.0f);
                cell.Bounds = bounds;
                _cells[key] = cell;
                ResidentInstances += cell.Instances.Count;
                if (++builds >= MAX_SLOT_BUILDS_PER_FRAME || _timer.Elapsed.TotalMilliseconds >= BUILD_BUDGET_MILLISECONDS) break;
            }

            _timer.Stop();
            LastBuildMilliseconds = _timer.Elapsed.TotalMilliseconds;
        }

        public Matrix4x4 Matrix(XRayDetailInstance instance, float scale)
        {
            return MatrixFor(instance, scale, _alignment);
        }

        public static Matrix4x4 MatrixFor(XRayDetailInstance instance, float scale, bool alignment)
        {
            var rotation = Matrix4x4.Rotate(Quaternion.Euler(0.0f, instance.Yaw * Mathf.Rad2Deg, 0.0f));
            if (alignment)
            {
                var normal = new Vector3(instance.NormalX, instance.NormalY, instance.NormalZ);
                Vector3 axis;
                if (Mathf.Abs(normal.x) >= Mathf.Abs(normal.y)) axis = new Vector3(-normal.z, 0.0f, normal.x).normalized;
                else axis = new Vector3(0.0f, normal.z, -normal.y).normalized;
                Vector3 right = Vector3.Cross(axis, normal);
                Matrix4x4 basis = Matrix4x4.identity;
                basis.SetColumn(0, axis);
                basis.SetColumn(1, normal);
                basis.SetColumn(2, right);
                rotation = basis * rotation;
            }

            Matrix4x4 matrix = rotation * Matrix4x4.Scale(Vector3.one * scale);
            matrix.SetColumn(3, new Vector4(instance.X, instance.Y, instance.Z, 1.0f));
            return matrix;
        }

        private void Recycle(XRayDetailCell cell)
        {
            ResidentInstances -= cell.Instances.Count;
            cell.Instances.Clear();
            if (_pool.Count < 256 && cell.Instances.Capacity <= 4096) _pool.Push(cell);
        }
    }
}

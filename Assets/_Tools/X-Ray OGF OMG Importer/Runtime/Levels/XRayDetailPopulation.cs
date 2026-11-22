using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;
using XRay.Importer.Core.IO;
using XRay.Importer.Core.Level;

namespace XRay.Importer.Levels
{
    public sealed class XRayDetailCell
    {
        private readonly List<XRayDetailInstance> _instances = new List<XRayDetailInstance>();

        public int X { get; internal set; }
        public int Z { get; internal set; }
        public Bounds Bounds { get; internal set; }
        public Vector3 FadeCenter { get; internal set; }
        public float Alpha { get; internal set; }
        public int Generation { get; internal set; }
        public IReadOnlyList<XRayDetailInstance> Instances { get; }

        internal List<XRayDetailInstance> MutableInstances => _instances;
        internal XRayPreparedDetail[] Prepared { get; set; }

        public XRayDetailCell()
        {
            Instances = _instances.AsReadOnly();
        }
    }

    internal readonly struct XRayPreparedDetail
    {
        public Vector3 Right { get; }
        public Vector3 Forward { get; }
        public float Rank { get; }

        public XRayPreparedDetail(Matrix4x4 matrix, float rank)
        {
            Right = new Vector3(matrix.m00, matrix.m10, matrix.m20);
            Forward = new Vector3(matrix.m02, matrix.m12, matrix.m22);
            Rank = rank;
        }

        internal void WriteMatrix(in XRayDetailInstance instance, float scale, bool alignment, ref Matrix4x4 matrix)
        {
            Vector3 right = Right;
            Vector3 forward = Forward;
            Vector3 normal = alignment ? new Vector3(instance.NormalX, instance.NormalY, instance.NormalZ) : Vector3.up;
            matrix.m00 = right.x * scale;
            matrix.m10 = right.y * scale;
            matrix.m20 = right.z * scale;
            matrix.m30 = 0.0f;
            matrix.m01 = normal.x * scale;
            matrix.m11 = normal.y * scale;
            matrix.m21 = normal.z * scale;
            matrix.m31 = 0.0f;
            matrix.m02 = forward.x * scale;
            matrix.m12 = forward.y * scale;
            matrix.m22 = forward.z * scale;
            matrix.m32 = 0.0f;
            matrix.m03 = instance.X;
            matrix.m13 = instance.Y;
            matrix.m23 = instance.Z;
            matrix.m33 = 1.0f;
        }

        public Matrix4x4 Matrix(XRayDetailInstance instance, float scale, bool alignment)
        {
            Matrix4x4 matrix = default;
            WriteMatrix(in instance, scale, alignment, ref matrix);
            return matrix;
        }
    }

    public sealed class XRayDetailPopulation
    {
        public const int MAX_SLOT_BUILDS_PER_FRAME = 4;
        public const int MAX_SLOT_PROBES_PER_FRAME = 512;
        public const double BUILD_BUDGET_MILLISECONDS = 2.0;

        private const int MAX_PREPARED_CAPACITY = 262144;
        private const int BACKGROUND_BATCH_SIZE = 48;
        private const int MAX_SLOT_PUBLICATIONS_PER_FRAME = 24;
        private const int MAX_INSTANCE_PUBLICATIONS_PER_FRAME = 1000;
        private const double PUBLICATION_BUDGET_MILLISECONDS = 0.75;
        private const float MAX_GRASS_HEIGHT = 2.0f;
        private const float STARTUP_RADIUS = 25.0f;

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
        private readonly Bounds[] _modelBounds;
        private readonly SlotWork[] _batch = new SlotWork[BACKGROUND_BATCH_SIZE];
        private Vector2Int _eyeSlot = new Vector2Int(int.MinValue, int.MinValue);
        private int _cursor;
        private int _generation;
        private int _preparedCapacity;
        private int _probeLimit;
        private int _batchCount;
        private int _publishCursor;
        private int _batchGeneration;
        private bool _building;
        private bool _useSynchronousFallback;
        private XRayGrassValues _values;

        public XRayDetailPopulation(XRayDetailDatabase database, XRayGrassDefaults defaults) : this(Source.Capture(database, defaults, true), default)
        {
        }

        internal XRayDetailPopulation(Source source, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _details = XRayDetails.Read(source.Details, source.Name + " level.details");
            cancellationToken.ThrowIfCancellationRequested();
            XRayCollisionModel collision = source.SharedCollision ?? XRayCollisionModel.Read(source.Collision, source.Name + " level.cform");
            cancellationToken.ThrowIfCancellationRequested();
            XRayDetailSlotGeometry geometry = source.ReadGeometry(_details, collision);
            RejectedSlotGeometry = source.Geometry != null && geometry == null;
            if (source.SharedCollision == null && geometry == null)
            {
                collision.BuildGrid(XRayDetails.SLOT_SIZE);
                PreparedCollision = collision;
            }
            cancellationToken.ThrowIfCancellationRequested();
            _alignment = source.Alignment;
            _placementSettings = new XRayDetailPlacementSettings { MonolithRelease = source.Monolith, TerrainSlope = source.TerrainSlope, HeightScale = 1.0f };
            _placement = new XRayDetailPlacement(_details, collision, source.Passable, _placementSettings, geometry);
            _modelBounds = new Bounds[_details.Models.Count];
            for (int index = 0; index < _modelBounds.Length; index++)
            {
                XRayDetailModel model = _details.Models[index];
                var min = new Vector3(model.BoundsMin[0], model.BoundsMin[1], model.BoundsMin[2]);
                var max = new Vector3(model.BoundsMax[0], model.BoundsMax[1], model.BoundsMax[2]);
                _modelBounds[index].SetMinMax(min, max);
                if (index < source.MeshBounds.Length && source.MeshBounds[index] is Bounds meshBounds)
                {
                    _modelBounds[index].Encapsulate(meshBounds);
                }
                _modelExtent = Mathf.Max(_modelExtent, Vector3.Max(-_modelBounds[index].min, _modelBounds[index].max).magnitude * model.MaxScale * MAX_GRASS_HEIGHT);
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
        public float BoundsPadding => XRayDetails.SLOT_SIZE * 2.0f + _modelExtent * 1.5f;
        public float MaximumModelHeight => _modelExtent;
        public bool NeedsStreaming => _cursor < _probeLimit || _batchCount > 0;
        public bool HasFadingCells { get; private set; }

        internal bool RejectedSlotGeometry { get; }
        internal XRayCollisionModel PreparedCollision { get; }

        public bool IsCurrent(XRayDetailCell cell)
        {
            return cell.Generation == _generation;
        }

        public void Apply(XRayGrassValues values)
        {
            if (!values.Valid) throw new ArgumentOutOfRangeException(nameof(values));
            if (_values.Density != values.Density)
            {
                Interlocked.Increment(ref _generation);
                _cursor = 0;
            }

            if (_values.Radius != values.Radius) _cursor = 0;
            _values = values;
            int side = 2 * (Mathf.CeilToInt(Mathf.Max(0f, values.FadeDistance) / XRayDetails.SLOT_SIZE) + 1) + 1;
            _probeLimit = Mathf.Min(_offsets.Count, side * side);
        }

        public void Stream(Vector3 eye, float deltaTime = 0.0f)
        {
            if (_batchCount != 0) return;
            float radius = BeginStream(eye, deltaTime);
            _placementSettings.Density = _values.Density;
            int builds = 0;
            int probes = 0;
            while (TryNextSlot(eye, radius, ref probes, out SlotWork work))
            {
                _cells.TryGetValue(work.Key, out XRayDetailCell cached);
                XRayDetailCell cell = cached ?? RentCell();
                if (cached != null) ResidentInstances -= cached.Instances.Count;
                BuildCell(cell, work, _generation, false);
                _cells[work.Key] = cell;
                ResidentInstances += cell.Instances.Count;
                HasFadingCells = true;
                if (++builds >= MAX_SLOT_BUILDS_PER_FRAME || _timer.Elapsed.TotalMilliseconds >= BUILD_BUDGET_MILLISECONDS) break;
            }
            EndStream();
        }

        internal void StreamBackground(Vector3 eye, float deltaTime, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return;
            if (_useSynchronousFallback)
            {
                Stream(eye, deltaTime);
                return;
            }

            float radius = BeginStream(eye, deltaTime);
            if (!_building)
            {
                double deadline = _timer.Elapsed.TotalMilliseconds + PUBLICATION_BUDGET_MILLISECONDS;
                PublishBatch(eye, radius + XRayDetails.SLOT_SIZE * 2.0f, deadline);
                if (_batchCount == 0) ScheduleBatch(eye, radius, deadline, cancellationToken);
            }
            EndStream();
        }

        internal void PrepareNear(Vector3 eye, XRayGrassValues values, CancellationToken cancellationToken)
        {
            Apply(values);
            float radius = BeginStream(eye, 0.0f);
            CollectBatch(eye, Mathf.Min(radius, STARTUP_RADIUS), double.PositiveInfinity);
            _batchGeneration = _generation;
            BuildBatch(_batchCount, _batchGeneration, values.Density, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _cursor = 0;
            EndStream();
        }

        private float BeginStream(Vector3 eye, float deltaTime)
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

            return radius;
        }

        private bool TryNextSlot(Vector3 eye, float radius, ref int probes, out SlotWork work)
        {
            while (probes < MAX_SLOT_PROBES_PER_FRAME && _cursor < _probeLimit)
            {
                probes++;
                Vector2Int offset = _offsets[_cursor++];
                int x = _eyeSlot.x + offset.x + _details.OffsetX;
                int z = _eyeSlot.y + offset.y + _details.OffsetZ;
                if (x < 0 || x >= _details.SizeX || z < 0 || z >= _details.SizeZ) continue;
                int key = z * _details.SizeX + x;
                if (_cells.TryGetValue(key, out XRayDetailCell cached) && IsCurrent(cached)) continue;
                XRayDetailSlot source = _details.GetSlot(x, z);
                if (source.IsEmpty) continue;
                var center = new Vector3((x - _details.OffsetX + 0.5f) * XRayDetails.SLOT_SIZE, source.YBase + source.YHeight * 0.5f, (z - _details.OffsetZ + 0.5f) * XRayDetails.SLOT_SIZE);
                var bounds = new Bounds(center, new Vector3(XRayDetails.SLOT_SIZE, source.YHeight, XRayDetails.SLOT_SIZE));
                if (bounds.SqrDistance(eye) > radius * radius) continue;
                work = new SlotWork(x, z, key, bounds);
                return true;
            }

            work = default;
            return false;
        }

        private void ScheduleBatch(Vector3 eye, float radius, double deadline, CancellationToken cancellationToken)
        {
            CollectBatch(eye, radius, deadline);
            if (_batchCount == 0) return;
            _batchGeneration = _generation;
            _building = true;
            _ = BuildBatchAsync(_batchCount, _batchGeneration, _values.Density, cancellationToken);
        }

        private void CollectBatch(Vector3 eye, float radius, double deadline)
        {
            int probes = 0;
            while (_batchCount < _batch.Length && _timer.Elapsed.TotalMilliseconds < deadline && TryNextSlot(eye, radius, ref probes, out SlotWork work))
            {
                XRayDetailCell cell = _pool.Count != 0 ? _pool.Pop() : null;
                _preparedCapacity -= cell?.Prepared?.Length ?? 0;
                _batch[_batchCount++] = new SlotWork(work.X, work.Z, work.Key, work.Bounds, cell);
            }

        }

        private int BuildBatch(int count, int generation, float density, CancellationToken cancellationToken)
        {
            int completed = 0;
            _placementSettings.Density = density;
            while (completed < count && !cancellationToken.IsCancellationRequested && generation == Volatile.Read(ref _generation))
            {
                SlotWork work = _batch[completed];
                XRayDetailCell cell = work.Cell ?? new XRayDetailCell();
                BuildCell(cell, work, generation, true);
                _batch[completed++] = new SlotWork(work.X, work.Z, work.Key, work.Bounds, cell);
            }
            return completed;
        }

        private async Awaitable BuildBatchAsync(int count, int generation, float density, CancellationToken cancellationToken)
        {
            Exception failure = null;
            int completed = 0;
            await Awaitable.BackgroundThreadAsync();
            try
            {
                completed = BuildBatch(count, generation, density, cancellationToken);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            await Awaitable.MainThreadAsync();
            _building = false;
            if (failure != null || cancellationToken.IsCancellationRequested || completed != count || generation != _generation)
            {
                DiscardBatch();
            }
            if (failure != null && !cancellationToken.IsCancellationRequested)
            {
                UnityEngine.Debug.LogWarning("Grass placement fell back to synchronous streaming: " + failure.Message);
                _cursor = 0;
                _useSynchronousFallback = true;
            }
        }

        private void PublishBatch(Vector3 eye, float exitRadius, double deadline)
        {
            if (_batchGeneration != _generation)
            {
                DiscardBatch();
                return;
            }

            int instances = 0;
            int published = 0;
            while (_publishCursor < _batchCount && published < MAX_SLOT_PUBLICATIONS_PER_FRAME && _timer.Elapsed.TotalMilliseconds < deadline)
            {
                SlotWork work = _batch[_publishCursor];
                XRayDetailCell cell = work.Cell;
                if (published != 0 && instances + cell.Instances.Count > MAX_INSTANCE_PUBLICATIONS_PER_FRAME) break;
                _batch[_publishCursor++] = default;
                AccountPrepared(cell);
                if (work.Bounds.SqrDistance(eye) > exitRadius * exitRadius)
                {
                    PoolCell(cell);
                    continue;
                }
                if (_cells.TryGetValue(work.Key, out XRayDetailCell existing)) Recycle(existing);
                _cells[work.Key] = cell;
                ResidentInstances += cell.Instances.Count;
                HasFadingCells = true;
                instances += cell.Instances.Count;
                published++;
            }

            if (_publishCursor == _batchCount)
            {
                _publishCursor = 0;
                _batchCount = 0;
            }
        }

        private void DiscardBatch()
        {
            while (_publishCursor < _batchCount)
            {
                XRayDetailCell cell = _batch[_publishCursor].Cell;
                _batch[_publishCursor++] = default;
                if (cell == null) continue;
                AccountPrepared(cell);
                PoolCell(cell);
            }
            _publishCursor = 0;
            _batchCount = 0;
        }

        private void AccountPrepared(XRayDetailCell cell)
        {
            int capacity = cell.Prepared?.Length ?? 0;
            if (_preparedCapacity + capacity > MAX_PREPARED_CAPACITY) cell.Prepared = null;
            else _preparedCapacity += capacity;
        }

        private void BuildCell(XRayDetailCell cell, SlotWork work, int generation, bool background)
        {
            List<XRayDetailInstance> instances = cell.MutableInstances;
            instances.Clear();
            int maximum = (_placement.DitherSize + 1) * (_placement.DitherSize + 1);
            if (instances.Capacity > Mathf.Max(64, maximum * 2)) instances.Capacity = maximum;
            cell.X = work.X;
            cell.Z = work.Z;
            cell.Generation = generation;
            cell.Alpha = 0.0f;
            _placement.PlaceSlot(work.X, work.Z, instances);
            if (background)
            {
                int capacity = cell.Prepared?.Length ?? 0;
                if (capacity < instances.Count || capacity > Mathf.Max(32, instances.Count * 4)) cell.Prepared = instances.Count > 0 ? new XRayPreparedDetail[instances.Count] : null;
            }
            else ReservePrepared(cell, instances.Count);
            Bounds bounds = work.Bounds;
            Bounds renderBounds = bounds;
            for (int index = 0; index < instances.Count; index++)
            {
                XRayDetailInstance instance = instances[index];
                Matrix4x4 matrix = Matrix(instance, 1.0f);
                XRayPreparedDetail prepared = new XRayPreparedDetail(matrix, StableRank(instance.X, instance.Y, instance.Z));
                if (cell.Prepared != null) cell.Prepared[index] = prepared;
                matrix = PreparedMatrix(instance, prepared, instance.Scale);
                Bounds modelBounds = _modelBounds[instance.Model];
                Vector3 extent = modelBounds.extents;
                Vector3 worldExtent = new Vector3(Mathf.Abs(matrix.m00) * extent.x + Mathf.Abs(matrix.m01) * extent.y + Mathf.Abs(matrix.m02) * extent.z, Mathf.Abs(matrix.m10) * extent.x + Mathf.Abs(matrix.m11) * extent.y + Mathf.Abs(matrix.m12) * extent.z, Mathf.Abs(matrix.m20) * extent.x + Mathf.Abs(matrix.m21) * extent.y + Mathf.Abs(matrix.m22) * extent.z);
                var itemBounds = new Bounds(matrix.MultiplyPoint3x4(modelBounds.center), worldExtent * 2.0f);
                Bounds grownBounds = DeformationBounds(instance, itemBounds);
                if (index == 0) bounds = itemBounds;
                else bounds.Encapsulate(itemBounds);
                if (index == 0) renderBounds = grownBounds;
                else renderBounds.Encapsulate(grownBounds);
            }

            cell.FadeCenter = bounds.center;
            cell.Bounds = renderBounds;
        }

        private void EndStream()
        {
            _timer.Stop();
            LastBuildMilliseconds = _timer.Elapsed.TotalMilliseconds;
        }

        public Matrix4x4 Matrix(XRayDetailInstance instance, float scale)
        {
            return MatrixFor(instance, scale, _alignment);
        }

        internal void WritePreparedMatrix(in XRayDetailInstance instance, in XRayPreparedDetail prepared, float scale, ref Matrix4x4 matrix)
        {
            prepared.WriteMatrix(in instance, scale, _alignment, ref matrix);
        }

        internal Matrix4x4 PreparedMatrix(XRayDetailInstance instance, XRayPreparedDetail prepared, float scale)
        {
            return prepared.Matrix(instance, scale, _alignment);
        }

        internal static float StableRank(float x, float y, float z)
        {
            unchecked
            {
                uint value = (uint)BitConverter.SingleToInt32Bits(x) * 73856093u ^ (uint)BitConverter.SingleToInt32Bits(y) * 19349663u ^ (uint)BitConverter.SingleToInt32Bits(z) * 83492791u;
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                value ^= value >> 16;
                return (value >> 8) * (1.0f / 16777216.0f);
            }
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
            PoolCell(cell);
        }

        private XRayDetailCell RentCell()
        {
            return _pool.Count > 0 ? _pool.Pop() : new XRayDetailCell();
        }

        private void PoolCell(XRayDetailCell cell)
        {
            cell.MutableInstances.Clear();
            if (_pool.Count < 256 && cell.MutableInstances.Capacity <= 4096)
            {
                _pool.Push(cell);
                return;
            }

            _preparedCapacity -= cell.Prepared?.Length ?? 0;
        }

        private void ReservePrepared(XRayDetailCell cell, int count)
        {
            int capacity = cell.Prepared?.Length ?? 0;
            if (capacity >= count && capacity <= Mathf.Max(32, count * 4)) return;
            _preparedCapacity -= capacity;
            int requested = Mathf.NextPowerOfTwo(count);
            cell.Prepared = requested > 0 && _preparedCapacity + requested <= MAX_PREPARED_CAPACITY ? new XRayPreparedDetail[requested] : null;
            _preparedCapacity += cell.Prepared?.Length ?? 0;
        }

        private Bounds DeformationBounds(XRayDetailInstance instance, Bounds bounds)
        {
            Vector3 origin = new Vector3(instance.X, instance.Y, instance.Z);
            Vector3 min = Vector3.Min(Vector3.zero, (bounds.min - origin) * MAX_GRASS_HEIGHT);
            Vector3 max = Vector3.Max(Vector3.zero, (bounds.max - origin) * MAX_GRASS_HEIGHT);
            if (_alignment)
            {
                float height = Mathf.Max(-min.y, max.y) * 0.5f;
                var shear = new Vector3(Mathf.Abs(instance.NormalX) * height, 0.0f, Mathf.Abs(instance.NormalZ) * height);
                min -= shear;
                max += shear;
            }

            bounds.SetMinMax(origin + min, origin + max);
            return bounds;
        }

        private readonly struct SlotWork
        {
            public int X { get; }
            public int Z { get; }
            public int Key { get; }
            public Bounds Bounds { get; }
            public XRayDetailCell Cell { get; }

            public SlotWork(int x, int z, int key, Bounds bounds, XRayDetailCell cell = null)
            {
                X = x;
                Z = z;
                Key = key;
                Bounds = bounds;
                Cell = cell;
            }
        }

        internal sealed class Source
        {
            private readonly string _detailsHash;
            private readonly string _collisionHash;

            public byte[] Details { get; }
            public byte[] Collision { get; }
            public byte[] Geometry { get; }
            public XRayCollisionModel SharedCollision { get; }
            public string Name { get; }
            public bool[] Passable { get; }
            public Bounds?[] MeshBounds { get; }
            public bool Monolith { get; }
            public bool Alignment { get; }
            public float TerrainSlope { get; }

            internal Source(byte[] details, byte[] collision, byte[] geometry, XRayCollisionModel sharedCollision, string name, bool[] passable, Bounds?[] meshBounds, bool monolith, bool alignment, float terrainSlope) : this(details, collision, geometry, sharedCollision, name, passable, meshBounds, monolith, alignment, terrainSlope, null, null)
            {
            }

            private Source(byte[] details, byte[] collision, byte[] geometry, XRayCollisionModel sharedCollision, string name, bool[] passable, Bounds?[] meshBounds, bool monolith, bool alignment, float terrainSlope, string detailsHash, string collisionHash)
            {
                Details = details;
                Collision = collision;
                Geometry = geometry;
                SharedCollision = sharedCollision;
                Name = name;
                Passable = passable;
                MeshBounds = meshBounds;
                Monolith = monolith;
                Alignment = alignment;
                TerrainSlope = terrainSlope;
                _detailsHash = detailsHash;
                _collisionHash = collisionHash;
            }

            internal static Source Capture(XRayDetailDatabase database, XRayGrassDefaults defaults, bool synchronous)
            {
                var meshBounds = new Bounds?[database.Models.Count];
                for (int index = 0; index < meshBounds.Length; index++)
                {
                    Mesh mesh = database.Models[index].Mesh;
                    if (mesh != null) meshBounds[index] = mesh.bounds;
                }

                TextAsset collision = database.SourceCollision;
                TextAsset geometry = database.SourceSlotGeometry;
                XRayCollisionModel shared = synchronous ? XRayRuntimeCollisionSource.Load(collision) : XRayRuntimeCollisionSource.GetCached(collision);
                byte[] collisionBytes = shared == null || geometry != null ? collision.bytes : null;
                bool monolith = database.SourceProfile == 1;
                return new Source(database.SourceDetails.bytes, collisionBytes, geometry != null ? geometry.bytes : null, shared, database.LevelName, (bool[])database.Passable?.Clone(), meshBounds, monolith, monolith && defaults.TerrainAlignment, defaults.TerrainSlope, database.SourceSlotGeometryDetailsHash, database.SourceSlotGeometryCollisionHash);
            }

            internal XRayDetailSlotGeometry ReadGeometry(XRayDetails details, XRayCollisionModel collision)
            {
                if (Geometry == null || !MatchesHash(Details, _detailsHash) || !MatchesHash(Collision, _collisionHash)) return null;
                try
                {
                    return XRayDetailSlotGeometry.Read(Geometry, details, collision);
                }
                catch (XRayFormatException)
                {
                    return null;
                }
            }

            private static bool MatchesHash(byte[] bytes, string expected)
            {
                if (bytes == null || string.IsNullOrEmpty(expected)) return false;
                using var stream = new MemoryStream(bytes, false);
                return XRayDetailDatabase.ComputeSlotGeometrySourceHash(stream) == expected;
            }
        }
    }
}

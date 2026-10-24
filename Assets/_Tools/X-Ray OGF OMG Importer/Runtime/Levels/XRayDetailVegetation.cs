using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRay.Importer.Levels
{
    [ExecuteAlways]
    public sealed class XRayDetailVegetation : MonoBehaviour
    {
        public const string WAVE1_PROPERTY = "_MonolithDetailWave1";
        public const string WIND1_PROPERTY = "_MonolithDetailWind1";
        public const string WAVE2_PROPERTY = "_MonolithDetailWave2";
        public const string WIND2_PROPERTY = "_MonolithDetailWind2";
        public const float ENGINE_FADE_DISTANCE = 47.5f;
        public const int MAX_CACHED_CHUNKS = 512;

        private const float TWO_PI = Mathf.PI * 2.0f;

        [SerializeField] private XRayDetailDatabase _database;
        [SerializeField] private float _drawDistance = ENGINE_FADE_DISTANCE;
        [SerializeField] private bool _wind = true;
        [SerializeField, Range(0.0f, 1.0f)] private float _windStrength;
        [SerializeField] private bool _castShadows;
        [SerializeField] private bool _debugSlots;
        [SerializeField] private float _debugSlotRadius = 30.0f;

        private readonly Dictionary<int, DecodedChunk> _cache = new Dictionary<int, DecodedChunk>();
        private readonly LinkedList<int> _usage = new LinkedList<int>();
        private readonly Plane[] _planes = new Plane[6];
        private Matrix4x4[][] _matrixBuffers;
        private Vector4[][] _dataBuffers;
        private int[] _counts;
        private MaterialPropertyBlock _block;
        private byte[] _instanceBytes;
        private int _dataId;
        private double _lastTime;
        private float _timeRot1;
        private float _timeRot2;
        private float _timePos;

        public XRayDetailDatabase Database
        {
            get => _database;
            set
            {
                _database = value;
                ResetCache();
            }
        }

        public bool DebugSlots
        {
            get => _debugSlots;
            set => _debugSlots = value;
        }

        public float DrawDistance
        {
            get => _drawDistance;
            set => _drawDistance = value;
        }

        public int LastVisibleInstances { get; private set; }
        public int LastDrawCalls { get; private set; }

        private void OnEnable()
        {
            _dataId = Shader.PropertyToID(XRayLevelInstancedRenderer.INSTANCE_DATA_PROPERTY);
            _block = new MaterialPropertyBlock();
            _lastTime = Time.realtimeSinceStartupAsDouble;
            ResetCache();
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            ResetCache();
        }

        private void ResetCache()
        {
            _cache.Clear();
            _usage.Clear();
            _instanceBytes = null;
            _matrixBuffers = null;
        }

        private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (_database == null)
            {
                return;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            float delta = (float)(now - _lastTime);
            _lastTime = now;
            if (delta < 0.0f || delta > 1.0f)
            {
                delta = 0.03f;
            }

            XRayDetailSwing swing = XRayDetailSwing.Lerp(_database.SwingNormal, _database.SwingFast, _windStrength);
            if (swing.Rotation1 > 0.0f)
            {
                _timeRot1 += TWO_PI * delta / swing.Rotation1;
            }

            if (swing.Rotation2 > 0.0f)
            {
                _timeRot2 += TWO_PI * delta / swing.Rotation2;
            }

            _timePos += delta * swing.Speed;
            Vector3 direction1 = new Vector3(Mathf.Sin(_timeRot1), 0.0f, Mathf.Cos(_timeRot1)).normalized * swing.Amplitude1;
            Vector3 direction2 = new Vector3(Mathf.Sin(_timeRot2), 0.0f, Mathf.Cos(_timeRot2)).normalized * swing.Amplitude2;
            Shader.SetGlobalVector(WAVE1_PROPERTY, new Vector4(1.0f / 5.0f, 1.0f / 7.0f, 1.0f / 3.0f, _timePos) / TWO_PI);
            Shader.SetGlobalVector(WIND1_PROPERTY, _wind ? new Vector4(direction1.x, 0.0f, direction1.z, 0.0f) : Vector4.zero);
            Shader.SetGlobalVector(WAVE2_PROPERTY, new Vector4(1.0f / 3.0f, 1.0f / 7.0f, 1.0f / 5.0f, _timePos) / TWO_PI);
            Shader.SetGlobalVector(WIND2_PROPERTY, _wind ? new Vector4(direction2.x, 0.0f, direction2.z, 0.0f) : Vector4.zero);
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (_database == null || _database.Instances == null || camera == null || camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection)
            {
                return;
            }

            EnsureBuffers();
            GeometryUtility.CalculateFrustumPlanes(camera, _planes);
            Vector3 eye = camera.transform.position;
            float fade = Mathf.Max(1.0f, _drawDistance);
            float fadeLimit = fade * fade;
            float fadeRange = fadeLimit - 1.0f;
            Array.Clear(_counts, 0, _counts.Length);
            int visible = 0;
            int draws = 0;
            IReadOnlyList<XRayDetailChunk> chunks = _database.Chunks;
            for (int c = 0; c < chunks.Count; c++)
            {
                XRayDetailChunk chunk = chunks[c];
                if (chunk.InstanceCount == 0 || chunk.Bounds.SqrDistance(eye) > fadeLimit || !GeometryUtility.TestPlanesAABB(_planes, chunk.Bounds))
                {
                    continue;
                }

                DecodedChunk decoded = GetChunk(c, chunk);
                for (int i = 0; i < decoded.Count; i++)
                {
                    float dx = decoded.X[i] - eye.x;
                    float dy = decoded.Y[i] - eye.y;
                    float dz = decoded.Z[i] - eye.z;
                    float distance = dx * dx + dy * dy + dz * dz;
                    if (distance > fadeLimit)
                    {
                        continue;
                    }

                    float alpha = distance < 1.0f ? 0.0f : (distance - 1.0f) / fadeRange;
                    float scale = decoded.Scale[i] * (1.0f - alpha);
                    int model = decoded.Model[i];
                    if (model >= _counts.Length || scale <= 0.0f)
                    {
                        continue;
                    }

                    int slot = _counts[model];
                    float sin = decoded.Sin[i] * scale;
                    float cos = decoded.Cos[i] * scale;
                    Matrix4x4 matrix = Matrix4x4.identity;
                    matrix.m00 = cos;
                    matrix.m02 = sin;
                    matrix.m11 = scale;
                    matrix.m20 = -sin;
                    matrix.m22 = cos;
                    matrix.m03 = decoded.X[i];
                    matrix.m13 = decoded.Y[i];
                    matrix.m23 = decoded.Z[i];
                    _matrixBuffers[model][slot] = matrix;
                    _dataBuffers[model][slot] = new Vector4(decoded.Hemi[i], _database.Models[model].InverseHeight, decoded.Wave[i], decoded.Sun[i]);
                    visible++;
                    if (++_counts[model] == XRayInstanceBatchSet.MAX_INSTANCES_PER_BATCH)
                    {
                        draws += Flush(camera, model);
                    }
                }
            }

            for (int model = 0; model < _counts.Length; model++)
            {
                draws += Flush(camera, model);
            }

            LastVisibleInstances = visible;
            LastDrawCalls = draws;
        }

        private int Flush(Camera camera, int model)
        {
            int count = _counts[model];
            _counts[model] = 0;
            XRayDetailModelEntry entry = _database.Models[model];
            if (count == 0 || entry.Mesh == null || entry.Material == null)
            {
                return 0;
            }

            _block.Clear();
            _block.SetVectorArray(_dataId, _dataBuffers[model]);
            var parameters = new RenderParams(entry.Material)
            {
                camera = camera,
                worldBounds = new Bounds(camera.transform.position, Vector3.one * (_drawDistance * 2.0f + 10.0f)),
                layer = gameObject.layer,
                shadowCastingMode = _castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = true,
                matProps = _block
            };
            Graphics.RenderMeshInstanced(parameters, entry.Mesh, 0, _matrixBuffers[model], count);
            return 1;
        }

        private void EnsureBuffers()
        {
            int models = _database.Models.Count;
            if (_matrixBuffers != null && _matrixBuffers.Length == models)
            {
                return;
            }

            _matrixBuffers = new Matrix4x4[models][];
            _dataBuffers = new Vector4[models][];
            _counts = new int[models];
            for (int i = 0; i < models; i++)
            {
                _matrixBuffers[i] = new Matrix4x4[XRayInstanceBatchSet.MAX_INSTANCES_PER_BATCH];
                _dataBuffers[i] = new Vector4[XRayInstanceBatchSet.MAX_INSTANCES_PER_BATCH];
            }
        }

        private DecodedChunk GetChunk(int index, XRayDetailChunk chunk)
        {
            if (_cache.TryGetValue(index, out DecodedChunk cached))
            {
                _usage.Remove(cached.Node);
                _usage.AddFirst(cached.Node);
                return cached;
            }

            if (_instanceBytes == null)
            {
                _instanceBytes = _database.Instances.bytes;
            }

            DecodedChunk decoded = Decode(chunk);
            decoded.Node = _usage.AddFirst(index);
            _cache[index] = decoded;
            while (_cache.Count > MAX_CACHED_CHUNKS)
            {
                int last = _usage.Last.Value;
                _usage.RemoveLast();
                _cache.Remove(last);
            }

            return decoded;
        }

        private DecodedChunk Decode(XRayDetailChunk chunk)
        {
            var decoded = new DecodedChunk(chunk.InstanceCount);
            Vector3 origin = chunk.PositionOrigin;
            float size = _database.ChunkSize;
            for (int i = 0; i < chunk.InstanceCount; i++)
            {
                int p = (chunk.InstanceStart + i) * XRayDetailDatabase.INSTANCE_STRIDE;
                decoded.X[i] = origin.x + BitConverter.ToUInt16(_instanceBytes, p) / XRayDetailDatabase.POSITION_QUANT * size;
                decoded.Z[i] = origin.z + BitConverter.ToUInt16(_instanceBytes, p + 2) / XRayDetailDatabase.POSITION_QUANT * size;
                decoded.Y[i] = BitConverter.ToSingle(_instanceBytes, p + 4);
                float yaw = BitConverter.ToUInt16(_instanceBytes, p + 8) / XRayDetailDatabase.POSITION_QUANT * TWO_PI;
                decoded.Sin[i] = Mathf.Sin(yaw);
                decoded.Cos[i] = Mathf.Cos(yaw);
                decoded.Scale[i] = BitConverter.ToUInt16(_instanceBytes, p + 10) / 4096.0f;
                decoded.Model[i] = _instanceBytes[p + 12];
                decoded.Hemi[i] = _instanceBytes[p + 13] / 15.0f;
                decoded.Sun[i] = _instanceBytes[p + 14] / 15.0f;
                decoded.Wave[i] = _instanceBytes[p + 15];
            }

            decoded.Count = chunk.InstanceCount;
            return decoded;
        }

        private void OnDrawGizmosSelected()
        {
            if (!_debugSlots || _database == null || _database.Slots == null)
            {
                return;
            }

            Camera camera = Camera.current;
            Vector3 eye = camera != null ? camera.transform.position : transform.position;
            byte[] slots = _database.Slots.bytes;
            float radiusSqr = _debugSlotRadius * _debugSlotRadius;
            float size = _database.SlotSize;
            Gizmos.color = new Color(0.2f, 1.0f, 0.3f, 0.6f);
            foreach (XRayDetailChunk chunk in _database.Chunks)
            {
                if (chunk.Bounds.SqrDistance(eye) > radiusSqr)
                {
                    continue;
                }

                for (int i = 0; i < chunk.SlotCount; i++)
                {
                    int p = (chunk.SlotStart + i) * XRayDetailDatabase.SLOT_STRIDE;
                    float x = BitConverter.ToInt16(slots, p) * size;
                    float z = BitConverter.ToInt16(slots, p + 2) * size;
                    float y = BitConverter.ToSingle(slots, p + 4);
                    float height = BitConverter.ToSingle(slots, p + 8);
                    var center = new Vector3(x + size * 0.5f, y + height * 0.5f, z + size * 0.5f);
                    if ((center - eye).sqrMagnitude <= radiusSqr)
                    {
                        Gizmos.DrawWireCube(center, new Vector3(size, Mathf.Max(0.05f, height), size));
                    }
                }
            }
        }

        private sealed class DecodedChunk
        {
            public DecodedChunk(int count)
            {
                X = new float[count];
                Y = new float[count];
                Z = new float[count];
                Sin = new float[count];
                Cos = new float[count];
                Scale = new float[count];
                Model = new byte[count];
                Hemi = new float[count];
                Sun = new float[count];
                Wave = new float[count];
            }

            public int Count { get; set; }
            public LinkedListNode<int> Node { get; set; }
            public float[] X { get; }
            public float[] Y { get; }
            public float[] Z { get; }
            public float[] Sin { get; }
            public float[] Cos { get; }
            public float[] Scale { get; }
            public byte[] Model { get; }
            public float[] Hemi { get; }
            public float[] Sun { get; }
            public float[] Wave { get; }
        }
    }
}

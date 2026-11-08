using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Core.Level;

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
        private const int MAX_SOURCE_INSTANCES_PER_BATCH = 511;

        [SerializeField] private XRayDetailDatabase _database;
        [SerializeField, HideInInspector] private float _drawDistance = ENGINE_FADE_DISTANCE;
        [SerializeField] private bool _wind = true;
        [SerializeField, Range(0.0f, 1.0f)] private float _windStrength;
        [SerializeField, HideInInspector] private bool _castShadows;
        [SerializeField] private bool _debugSlots;
        [SerializeField] private float _debugSlotRadius = 30.0f;
        [SerializeField] private Camera _streamingCamera;

        private readonly Dictionary<int, DecodedChunk> _cache = new Dictionary<int, DecodedChunk>();
        private readonly LinkedList<int> _usage = new LinkedList<int>();
        private readonly Plane[] _planes = new Plane[6];
        private Matrix4x4[][] _matrixBuffers;
        private SourceInstance[][] _sourceBuffers;
        private Vector4[][] _dataBuffers;
        private int[] _counts;
        private MaterialPropertyBlock _block;
        private byte[] _instanceBytes;
        private int _dataId;
        private double _lastTime;
        private float _timeRot1;
        private float _timeRot2;
        private float _timePos;
        private XRayGrassSettings _settings;
        private XRayDetailPopulation _population;
        private Vector4[][] _extraBuffers;
        private int _streamFrame = -1;
        private float _heightPrevious;
        private float _heightRendered;
        private double _lastStreamTime;
        private Camera _sceneStreamingCamera;
        private int _heightFrame = -1;

        public XRayDetailDatabase Database
        {
            get => _database;
            set
            {
                _database = value;
                ResetCache();
                if (_settings != null && _database != null && _database.HasRuntimeSource)
                {
                    _settings.SetLevel(_database.LevelName);
                    ApplySettings();
                }
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
        public Camera StreamingCamera
        {
            get => _streamingCamera;
            set
            {
                _streamingCamera = value;
                _streamFrame = -1;
                _sceneStreamingCamera = null;
            }
        }

        public Func<Material, Material> MaterialResolver { get; set; }
        public int LastDrawCalls { get; private set; }
        public int LastShadowVisibleInstances { get; private set; }
        public int ResidentCells => _population?.ResidentCells ?? _cache.Count;
        public int ResidentInstances => _population?.ResidentInstances ?? 0;
        public XRayDetailPopulation Population => _population;
        public bool CastShadows => _castShadows;
        public XRayGrassValues GrassValues => _settings != null ? _settings.Values : default;
        public bool NeedsEditorUpdate => isActiveAndEnabled && !Application.isPlaying && _database != null && _database.HasRuntimeSource && (_population == null || _population.NeedsStreaming || _population.HasFadingCells);

        private void OnEnable()
        {
            _dataId = Shader.PropertyToID(XRayLevelInstancedRenderer.INSTANCE_DATA_PROPERTY);
            _block = new MaterialPropertyBlock();
            _lastTime = Time.realtimeSinceStartupAsDouble;
            ResetCache();
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            BindSettings();
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            ResetCache();
            if (_settings != null) _settings.Changed -= ApplySettings;
            _settings = null;
            _block = null;
        }

        private void ResetCache()
        {
            _cache.Clear();
            _usage.Clear();
            _instanceBytes = null;
            _matrixBuffers = null;
            _sourceBuffers = null;
            _dataBuffers = null;
            _extraBuffers = null;
            _counts = null;
            _population = null;
            _streamFrame = -1;
            _lastStreamTime = 0.0;
            _sceneStreamingCamera = null;
            _heightFrame = -1;
        }

        private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (_database == null)
            {
                return;
            }

            if (_database.HasRuntimeSource)
            {
                BindSettings();
                if (!Application.isPlaying || _heightFrame != Time.frameCount) _heightPrevious = _heightRendered;
                _heightRendered = _settings != null ? _settings.Values.Height : 1.0f;
                if (_heightPrevious <= 0.0f) _heightPrevious = _heightRendered;
                _heightFrame = Time.frameCount;
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
            if (_database == null || _database.Instances == null && !_database.HasRuntimeSource || camera == null || camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection)
            {
                return;
            }

            if (Application.isPlaying && (!Application.IsPlaying(gameObject) || camera.cameraType == CameraType.SceneView) || _database.HasRuntimeSource && !Application.isPlaying && camera.cameraType != CameraType.SceneView) return;

            EnsureBuffers();
            if (_database.HasRuntimeSource)
            {
                RenderSource(camera);
                return;
            }
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

            Material material = MaterialResolver != null ? MaterialResolver(entry.Material) : entry.Material;
            if (material == null) return 0;
            _block.Clear();
            _block.SetVectorArray(_dataId, _dataBuffers[model]);
            var parameters = new RenderParams(material)
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
            _sourceBuffers = _database.HasRuntimeSource ? new SourceInstance[models][] : null;
            _dataBuffers = new Vector4[models][];
            _extraBuffers = new Vector4[models][];
            _counts = new int[models];
            int capacity = _sourceBuffers != null ? MAX_SOURCE_INSTANCES_PER_BATCH : XRayInstanceBatchSet.MAX_INSTANCES_PER_BATCH;
            for (int i = 0; i < models; i++)
            {
                _extraBuffers[i] = new Vector4[capacity];
                if (_sourceBuffers == null) _matrixBuffers[i] = new Matrix4x4[XRayInstanceBatchSet.MAX_INSTANCES_PER_BATCH];
                else _sourceBuffers[i] = new SourceInstance[capacity];
                _dataBuffers[i] = new Vector4[capacity];
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

        private void BindSettings()
        {
            if (_database == null || !_database.HasRuntimeSource || _settings != null) return;
            var controller = GetComponent<XRayDetailVegetationController>();
            if (controller != null && controller.isActiveAndEnabled)
            {
                controller.Initialize(this);
                if (_settings != null) return;
            }

            if (Application.IsPlaying(gameObject))
            {
                XRayGrassSettingsProvider provider = XRayGrassSettingsProvider.Ensure();
                if (provider.Settings != null) SetSettings(provider.Settings);
                return;
            }

            TextAsset profile = Resources.Load<TextAsset>("XRayRvmGrassDefaults");
            if (profile != null) SetSettings(new XRayGrassSettings(XRayGrassDefaults.Load(profile.text), null));
        }

        private void ApplySettings()
        {
            if (_settings == null || _database == null) return;
            _drawDistance = _settings.Values.FadeDistance;
            _castShadows = _settings.Values.Shadows;
            _population?.Apply(_settings.Values);
        }

        public void SetSettings(XRayGrassSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (ReferenceEquals(_settings, settings))
            {
                ApplySettings();
                return;
            }

            if (_settings != null) _settings.Changed -= ApplySettings;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settings.SetLevel(_database.LevelName);
            _settings.Changed += ApplySettings;
            _heightRendered = _settings.Values.Height;
            _heightPrevious = _heightRendered;
            ApplySettings();
        }

        public void PrepareSource(XRayGrassSettings settings)
        {
            SetSettings(settings);
            _population = new XRayDetailPopulation(_database, _settings.Defaults);
            _population.Apply(_settings.Values);
        }

        public void StreamSource(Vector3 eye)
        {
            if (_settings == null) return;
            if (_population == null)
            {
                _population = new XRayDetailPopulation(_database, _settings.Defaults);
                _population.Apply(_settings.Values);
            }

            double now = Time.realtimeSinceStartupAsDouble;
            float delta = Application.isPlaying ? Time.deltaTime : _lastStreamTime > 0.0 ? Mathf.Clamp((float)(now - _lastStreamTime), 0.0f, 0.1f) : 0.0f;
            _lastStreamTime = now;
            _population.Stream(eye, delta);
        }

        private void RenderSource(Camera camera)
        {
            BindSettings();
            if (_settings == null) return;
            Vector3 eye = camera.transform.position;
            if ((!Application.isPlaying || _streamFrame != Time.frameCount) && CanStreamFrom(camera))
            {
                StreamSource(eye);
                _streamFrame = Time.frameCount;
            }

            if (_population == null) return;
            XRayFloraWindProvider provider = XRayFloraWindProvider.Active;
            XRayGrassBenderState benders = _database.SourceProfile == 1 && provider != null && provider.isActiveAndEnabled ? provider.GrassBenders : null;
            float maximumHeight = _population.MaximumModelHeight * Mathf.Max(_settings.Values.Height, _heightPrevious);
            GeometryUtility.CalculateFrustumPlanes(camera, _planes);
            LastVisibleInstances = 0;
            LastShadowVisibleInstances = 0;
            LastDrawCalls = 0;
            for (int pass = 0; pass < (_castShadows && _settings.Defaults.GrassShadows.x > 0.0f ? 2 : 1); pass++)
            {
                Array.Clear(_counts, 0, _counts.Length);
                foreach (XRayDetailCell cell in _population.Cells)
                {
                    if (!_population.IsCurrent(cell)) continue;
                    bool shadow = pass == 1;
                    float distance = (cell.FadeCenter - eye).sqrMagnitude;
                    float fade = SourceFade(distance, _drawDistance);
                    if (shadow)
                    {
                        float start = _drawDistance * _drawDistance * _settings.Defaults.GrassShadows.y;
                        if (distance > start) fade *= Mathf.Max(0.0f, 1.0f - Mathf.Abs(distance - start) * 0.005f);
                    }
                    Bounds bounds = benders != null ? benders.ExpandBounds(cell.Bounds, maximumHeight) : cell.Bounds;
                    if (fade <= 0.0f || !shadow && !GeometryUtility.TestPlanesAABB(_planes, bounds)) continue;
                    foreach (XRayDetailInstance instance in cell.Instances)
                    {
                        int model = instance.Model;
                        int index = _counts[model];
                        Matrix4x4 matrix = _population.Matrix(instance, instance.Scale * fade);
                        _sourceBuffers[model][index] = new SourceInstance(matrix, matrix);
                        _dataBuffers[model][index] = new Vector4(instance.Hemi, _database.Models[model].InverseHeight, _wind ? instance.Wave : 0.0f, instance.Sun);
                        _extraBuffers[model][index] = new Vector4(instance.NormalX, instance.NormalY, instance.NormalZ, cell.Alpha);
                        if (shadow) LastShadowVisibleInstances++;
                        else LastVisibleInstances++;
                        if (++_counts[model] == MAX_SOURCE_INSTANCES_PER_BATCH) FlushSource(camera, model, shadow, benders, maximumHeight);
                    }
                }

                for (int model = 0; model < _counts.Length; model++) FlushSource(camera, model, pass == 1, benders, maximumHeight);
            }
        }

        public static float SourceFade(float distanceSquared, float fadeDistance)
        {
            if (fadeDistance <= 1.0f || distanceSquared >= fadeDistance * fadeDistance) return 0.0f;
            return distanceSquared <= 1.0f ? 1.0f : 1.0f - (distanceSquared - 1.0f) / (fadeDistance * fadeDistance - 1.0f);
        }

        public bool CanStreamFrom(Camera camera)
        {
            if (camera == null) return false;
            if (!Application.isPlaying)
            {
                if (camera.cameraType != CameraType.SceneView) return false;
                if (_streamingCamera != null && _streamingCamera.cameraType == CameraType.SceneView) return camera == _streamingCamera;
                if (_sceneStreamingCamera == null) _sceneStreamingCamera = camera;
                return camera == _sceneStreamingCamera;
            }
            if (camera.cameraType != CameraType.Game) return false;
            Camera owner = _streamingCamera != null ? _streamingCamera : Camera.main;
            return owner == null || camera == owner;
        }

        private void FlushSource(Camera camera, int model, bool shadow, XRayGrassBenderState benders, float maximumHeight)
        {
            int count = _counts[model];
            _counts[model] = 0;
            XRayDetailModelEntry entry = _database.Models[model];
            if (count == 0 || entry.Mesh == null || entry.Material == null) return;
            Material material = MaterialResolver != null ? MaterialResolver(entry.Material) : entry.Material;
            if (material == null) return;
            _block.Clear();
            _block.SetVectorArray(_dataId, _dataBuffers[model]);
            _block.SetVectorArray("_MonolithInstanceExtra", _extraBuffers[model]);
            _block.SetFloat("_MonolithGrassRuntime", 1.0f);
            _block.SetFloat("_MonolithGrassAlign", _database.SourceProfile == 1 && _settings.Defaults.TerrainAlignment ? 1.0f : 0.0f);
            _block.SetFloat("_MonolithGrassHeight", _settings.Values.Height);
            _block.SetFloat("_MonolithGrassHeightPrevious", _heightPrevious > 0.0f ? _heightPrevious : _settings.Values.Height);
            _block.SetVector("_MonolithWindGrassSetup", _settings.Defaults.WindGrass);
            Bounds bounds = new Bounds(camera.transform.position, Vector3.one * ((Mathf.Max(0.0f, _drawDistance) + _population.BoundsPadding) * 2.0f));
            if (benders != null)
            {
                bounds.Expand(benders.MaximumDisplacement(maximumHeight) * 2f);
            }
            var parameters = new RenderParams(material)
            {
                camera = camera,
                worldBounds = bounds,
                layer = gameObject.layer,
                shadowCastingMode = shadow ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.Off,
                motionVectorMode = shadow ? MotionVectorGenerationMode.Camera : MotionVectorGenerationMode.Object,
                receiveShadows = true,
                matProps = _block
            };
            Graphics.RenderMeshInstanced(parameters, entry.Mesh, 0, _sourceBuffers[model], count);
            LastDrawCalls++;
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

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private readonly struct SourceInstance
        {
            public readonly Matrix4x4 objectToWorld;
            public readonly Matrix4x4 prevObjectToWorld;

            public SourceInstance(Matrix4x4 current, Matrix4x4 previous)
            {
                objectToWorld = current;
                prevObjectToWorld = previous;
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

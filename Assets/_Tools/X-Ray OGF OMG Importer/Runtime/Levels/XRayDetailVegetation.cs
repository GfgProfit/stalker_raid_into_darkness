using System;
using System.Collections.Generic;
using System.Threading;
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
        private const float THINNING_TRANSITION = 0.08f;

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
        private readonly List<XRayDetailCell> _shadowCells = new List<XRayDetailCell>();
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
        private GameConfig _configuration;
        private Vector3 _bakedBoundsPadding;
        private XRayDetailPopulation _population;
        private CancellationTokenSource _sourceLifetime;
        private Vector4[][] _extraBuffers;
        private int _streamFrame = -1;
        private float _heightPrevious;
        private float _heightRendered;
        private double _lastStreamTime;
        private Camera _sceneStreamingCamera;
        private Camera _fallbackRuntimeCamera;
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
                _fallbackRuntimeCamera = null;
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
            _configuration = GameConfig.Load();
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

        private void Start()
        {
            if (Application.IsPlaying(gameObject)) BeginRuntimePreparation();
        }

        private void LateUpdate()
        {
            if (!Application.IsPlaying(gameObject) || _database == null || !_database.HasRuntimeSource) return;
            BeginRuntimePreparation();
            Camera owner = _streamingCamera != null ? _streamingCamera : Camera.main;
            if (owner == null) owner = _fallbackRuntimeCamera;
            if (owner == null || !owner.isActiveAndEnabled || owner.cameraType != CameraType.Game || _streamFrame == Time.frameCount) return;
            StreamSource(owner.transform.position);
            _streamFrame = Time.frameCount;
        }

        private void ResetCache()
        {
            StopRuntimePreparation();
            _cache.Clear();
            _usage.Clear();
            _shadowCells.Clear();
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
            _fallbackRuntimeCamera = null;
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

            RenderBaked(camera);
        }

        private void RenderBaked(Camera camera)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, _planes);
            Vector3 eye = camera.transform.position;
            float drawDistance = Mathf.Max(0.0f, _drawDistance);
            float shadowDistance = ResolveShadowDistance();
            float thinningStart = _configuration != null ? _configuration.GrassThinningStartDistance : GameConfig.DEFAULT_GRASS_THINNING_START_DISTANCE;
            XRayVegetationOcclusion occlusion = XRayVegetationOcclusion.Begin(camera, transform.parent);
            LastVisibleInstances = 0;
            LastShadowVisibleInstances = 0;
            LastDrawCalls = 0;
            RenderBakedPass(camera, eye, drawDistance, thinningStart, 0.0f, false, occlusion);
            if (shadowDistance > 0.0f)
            {
                RenderBakedPass(camera, eye, shadowDistance, thinningStart, ResolveShadowFade(), true, null);
            }
        }

        private void RenderBakedPass(Camera camera, Vector3 eye, float distanceLimit, float thinningStart, float shadowFade, bool shadow, XRayVegetationOcclusion occlusion)
        {
            float distanceLimitSquared = distanceLimit * distanceLimit;
            Array.Clear(_counts, 0, _counts.Length);
            int visible = 0;
            int draws = 0;
            IReadOnlyList<XRayDetailChunk> chunks = _database.Chunks;
            for (int c = 0; c < chunks.Count; c++)
            {
                XRayDetailChunk chunk = chunks[c];
                if (chunk.InstanceCount == 0)
                {
                    continue;
                }
                Bounds bounds = chunk.Bounds;
                bounds.Expand(_bakedBoundsPadding * 2.0f);
                if (bounds.SqrDistance(eye) >= distanceLimitSquared || !shadow && (!GeometryUtility.TestPlanesAABB(_planes, bounds) || occlusion != null && occlusion.IsOccluded(bounds)))
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
                    if (distance >= distanceLimitSquared)
                    {
                        continue;
                    }

                    float fade = SourceFade(distance, _drawDistance);
                    fade *= ThinningScale(decoded.Rank[i], DistanceRetention(distance, thinningStart, _drawDistance));
                    if (shadow) fade *= ShadowFade(distance, distanceLimit, shadowFade);
                    float scale = decoded.Scale[i] * fade;
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
                    _dataBuffers[model][slot] = new Vector4(decoded.Hemi[i], _database.Models[model].InverseHeight, _wind ? decoded.Wave[i] : 0.0f, decoded.Sun[i]);
                    visible++;
                    if (++_counts[model] == XRayInstanceBatchSet.MAX_INSTANCES_PER_BATCH)
                    {
                        draws += Flush(camera, model, shadow, distanceLimit);
                    }
                }
            }

            for (int model = 0; model < _counts.Length; model++)
            {
                draws += Flush(camera, model, shadow, distanceLimit);
            }

            if (shadow) LastShadowVisibleInstances = visible;
            else LastVisibleInstances = visible;
            LastDrawCalls += draws;
        }

        private int Flush(Camera camera, int model, bool shadow, float distanceLimit)
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
                worldBounds = new Bounds(camera.transform.position, (Vector3.one * distanceLimit + _bakedBoundsPadding) * 2.0f),
                layer = gameObject.layer,
                shadowCastingMode = shadow ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.Off,
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
            if (_sourceBuffers == null)
            {
                _bakedBoundsPadding = CalculateBakedBoundsPadding();
            }
            for (int i = 0; i < models; i++)
            {
                _extraBuffers[i] = new Vector4[capacity];
                if (_sourceBuffers == null) _matrixBuffers[i] = new Matrix4x4[XRayInstanceBatchSet.MAX_INSTANCES_PER_BATCH];
                else _sourceBuffers[i] = new SourceInstance[capacity];
                _dataBuffers[i] = new Vector4[capacity];
            }
        }

        private Vector3 CalculateBakedBoundsPadding()
        {
            float radius = 0.0f;
            XRayDetailSwing normal = _database.SwingNormal;
            XRayDetailSwing fast = _database.SwingFast;
            float amplitude = Mathf.Max(Mathf.Max(Mathf.Abs(normal.Amplitude1), Mathf.Abs(normal.Amplitude2)), Mathf.Max(Mathf.Abs(fast.Amplitude1), Mathf.Abs(fast.Amplitude2)));
            foreach (XRayDetailModelEntry model in _database.Models)
            {
                if (model.Mesh == null) continue;
                Bounds bounds = model.Mesh.bounds;
                Vector3 extent = Vector3.Max(-bounds.min, bounds.max);
                float windReach = extent.y * extent.y * Mathf.Abs(model.InverseHeight) * amplitude;
                radius = Mathf.Max(radius, (extent.magnitude + windReach) * Mathf.Max(0.0f, model.MaxScale));
            }

            return Vector3.one * (radius + 0.1f);
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
                decoded.Rank[i] = XRayDetailPopulation.StableRank(decoded.X[i], decoded.Y[i], decoded.Z[i]);
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
            StopRuntimePreparation();
            SetSettings(settings);
            _population = new XRayDetailPopulation(_database, _settings.Defaults);
            _population.Apply(_settings.Values);
        }

        private void BeginRuntimePreparation()
        {
            if (_population != null || _sourceLifetime != null || _database == null || !_database.HasRuntimeSource || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            BindSettings();
            if (_settings == null) return;
            _sourceLifetime = CancellationTokenSource.CreateLinkedTokenSource(Application.exitCancellationToken);
            _ = PrepareRuntimeSourceAsync(_sourceLifetime.Token);
        }

        private void StopRuntimePreparation()
        {
            if (_sourceLifetime == null) return;
            _sourceLifetime.Cancel();
            _sourceLifetime.Dispose();
            _sourceLifetime = null;
        }

        private async Awaitable PrepareRuntimeSourceAsync(CancellationToken cancellationToken)
        {
            XRayDetailPopulation population = null;
            Exception failure = null;
            try
            {
                XRayDetailPopulation.Source source = XRayDetailPopulation.Source.Capture(_database, _settings.Defaults, false);
                Camera owner = _streamingCamera != null ? _streamingCamera : Camera.main;
                Vector3? initialEye = owner != null && owner.isActiveAndEnabled && owner.cameraType == CameraType.Game ? owner.transform.position : null;
                XRayGrassValues initialValues = _settings.Values;
                await Awaitable.BackgroundThreadAsync();
                population = new XRayDetailPopulation(source, cancellationToken);
                if (initialEye is Vector3 eye) population.PrepareNear(eye, initialValues, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            await Awaitable.MainThreadAsync();
            if (cancellationToken.IsCancellationRequested) return;
            if (failure != null)
            {
                Debug.LogError("Could not prepare grass for " + _database.LevelName + ": " + failure.Message, this);
                return;
            }

            if (population.PreparedCollision != null) XRayRuntimeCollisionSource.Cache(_database.SourceCollision, population.PreparedCollision);
            if (population.RejectedSlotGeometry) Debug.LogWarning("Grass slot geometry is outdated or damaged for " + _database.LevelName + "; using level collision.", this);
            population.Apply(_settings.Values);
            _population = population;
        }

        public void StreamSource(Vector3 eye)
        {
            if (_settings == null) return;
            if (_population == null)
            {
                if (Application.IsPlaying(gameObject))
                {
                    BeginRuntimePreparation();
                    return;
                }

                _population = new XRayDetailPopulation(_database, _settings.Defaults);
                _population.Apply(_settings.Values);
            }

            double now = Time.realtimeSinceStartupAsDouble;
            float delta = Application.isPlaying ? Time.deltaTime : _lastStreamTime > 0.0 ? Mathf.Clamp((float)(now - _lastStreamTime), 0.0f, 0.1f) : 0.0f;
            _lastStreamTime = now;
            if (_sourceLifetime != null) _population.StreamBackground(eye, delta, _sourceLifetime.Token);
            else _population.Stream(eye, delta);
        }

        private void RenderSource(Camera camera)
        {
            BindSettings();
            if (_settings == null) return;
            Vector3 eye = camera.transform.position;
            if (!Application.isPlaying && CanStreamFrom(camera))
            {
                StreamSource(eye);
            }
            else if (Application.isPlaying && _streamingCamera == null && (_fallbackRuntimeCamera == null || !_fallbackRuntimeCamera.isActiveAndEnabled) && CanStreamFrom(camera)) _fallbackRuntimeCamera = camera;

            if (_population == null) return;
            XRayFloraWindProvider provider = XRayFloraWindProvider.Active;
            bool sourceWind = _database.SourceProfile == 1 && provider != null && provider.isActiveAndEnabled;
            XRayGrassBenderState benders = sourceWind ? provider.GrassBenders : null;
            Vector3 windExtent = sourceWind && _wind ? ResolveWindExtent(provider.State) : Vector3.zero;
            float maximumHeight = _population.MaximumModelHeight * Mathf.Max(_settings.Values.Height, _heightPrevious);
            GeometryUtility.CalculateFrustumPlanes(camera, _planes);
            XRayVegetationOcclusion occlusion = XRayVegetationOcclusion.Begin(camera, transform.parent);
            LastVisibleInstances = 0;
            LastShadowVisibleInstances = 0;
            LastDrawCalls = 0;
            float shadowDistance = _settings.Defaults.GrassShadows.x > 0.0f ? ResolveShadowDistance() : 0.0f;
            float shadowLimit = shadowDistance * shadowDistance;
            float thinningStart = _configuration != null ? _configuration.GrassThinningStartDistance : GameConfig.DEFAULT_GRASS_THINNING_START_DISTANCE;
            Vector3 boundsPadding = Vector3.one * _population.BoundsPadding + windExtent;
            if (benders != null) boundsPadding += benders.MaximumDisplacement(maximumHeight);
            Bounds colorBatchBounds = new Bounds(eye, (Vector3.one * Mathf.Max(0.0f, _drawDistance) + boundsPadding) * 2.0f);
            _shadowCells.Clear();
            Array.Clear(_counts, 0, _counts.Length);
            foreach (XRayDetailCell cell in _population.Cells)
            {
                if (!_population.IsCurrent(cell) || cell.Instances.Count == 0) continue;
                float distance = (cell.FadeCenter - eye).sqrMagnitude;
                if (distance < shadowLimit)
                {
                    _shadowCells.Add(cell);
                }

                float fade = SourceFade(distance, _drawDistance);
                if (fade <= 0.0f) continue;
                Bounds bounds = cell.Bounds;
                bounds.Expand(windExtent * 2.0f);
                if (benders != null) bounds = benders.ExpandBounds(bounds, maximumHeight);
                if (!GeometryUtility.TestPlanesAABB(_planes, bounds) || occlusion != null && occlusion.IsOccluded(bounds)) continue;
                float retention = DistanceRetention(distance, thinningStart, _drawDistance);
                RenderSourceCell(cell, fade, retention, camera, false, colorBatchBounds);
            }

            for (int model = 0; model < _counts.Length; model++) FlushSource(camera, model, false, colorBatchBounds);

            if (_shadowCells.Count > 0)
            {
                float shadowFade = ResolveShadowFade();
                Bounds shadowBatchBounds = new Bounds(eye, (Vector3.one * shadowDistance + boundsPadding) * 2.0f);
                foreach (XRayDetailCell cell in _shadowCells)
                {
                    float distance = (cell.FadeCenter - eye).sqrMagnitude;
                    float fade = SourceFade(distance, _drawDistance) * ShadowFade(distance, shadowDistance, shadowFade);
                    if (fade > 0.0f) RenderSourceCell(cell, fade, DistanceRetention(distance, thinningStart, _drawDistance), camera, true, shadowBatchBounds);
                }
                for (int model = 0; model < _counts.Length; model++) FlushSource(camera, model, true, shadowBatchBounds);
                _shadowCells.Clear();
            }
        }

        private void RenderSourceCell(XRayDetailCell cell, float fade, float retention, Camera camera, bool shadow, Bounds batchBounds)
        {
            List<XRayDetailInstance> instances = cell.MutableInstances;
            XRayPreparedDetail[] prepared = cell.Prepared;
            int visible = 0;
            for (int instanceIndex = 0; instanceIndex < instances.Count; instanceIndex++)
            {
                XRayDetailInstance instance = instances[instanceIndex];
                int model = instance.Model;
                if (model >= _counts.Length) continue;
                float scale = instance.Scale * fade;
                if (retention < 1.0f)
                {
                    float rank = prepared != null ? prepared[instanceIndex].Rank : XRayDetailPopulation.StableRank(instance.X, instance.Y, instance.Z);
                    scale *= ThinningScale(rank, retention);
                }
                if (scale <= 0.0f) continue;
                int index = _counts[model];
                if (prepared != null)
                {
                    ref SourceInstance source = ref _sourceBuffers[model][index];
                    _population.WritePreparedMatrix(in instance, in prepared[instanceIndex], scale, ref source.objectToWorld);
                    source.prevObjectToWorld = source.objectToWorld;
                }
                else
                {
                    Matrix4x4 matrix = _population.Matrix(instance, scale);
                    _sourceBuffers[model][index] = new SourceInstance(matrix, matrix);
                }
                _dataBuffers[model][index] = new Vector4(instance.Hemi, _database.Models[model].InverseHeight, _wind ? instance.Wave : 0.0f, instance.Sun);
                _extraBuffers[model][index] = new Vector4(instance.NormalX, instance.NormalY, instance.NormalZ, cell.Alpha);
                visible++;
                if (++_counts[model] == MAX_SOURCE_INSTANCES_PER_BATCH) FlushSource(camera, model, shadow, batchBounds);
            }

            if (shadow) LastShadowVisibleInstances += visible;
            else LastVisibleInstances += visible;
        }

        public static float SourceFade(float distanceSquared, float fadeDistance)
        {
            if (fadeDistance <= 1.0f || distanceSquared >= fadeDistance * fadeDistance) return 0.0f;
            return distanceSquared <= 1.0f ? 1.0f : 1.0f - (distanceSquared - 1.0f) / (fadeDistance * fadeDistance - 1.0f);
        }

        private float ResolveShadowDistance()
        {
            if (!_castShadows) return 0.0f;
            float settingsDistance = _configuration != null ? _configuration.GrassShadowDistance : GameConfig.DEFAULT_GRASS_SHADOW_DISTANCE;
            return Mathf.Max(0.0f, Mathf.Min(settingsDistance, _drawDistance));
        }

        private float ResolveShadowFade()
        {
            return _configuration != null ? _configuration.GrassShadowFadeDistance : GameConfig.DEFAULT_GRASS_SHADOW_FADE_DISTANCE;
        }

        private Vector3 ResolveWindExtent(XRayFloraWindState wind)
        {
            if (!wind.Initialized || wind.Profile != XRayFloraWindProfile.AnomalySsfx) return Vector3.zero;
            Vector4 settings = _settings.Defaults.WindGrass;
            float speed = Mathf.Max(wind.TreesSetup.w, Mathf.Clamp01(wind.Weather.Velocity * 0.001f));
            float horizontal = Mathf.Abs(settings.y) + Mathf.Abs(settings.z);
            return new Vector3(horizontal, Mathf.Abs(settings.w), horizontal) * speed;
        }

        internal static float ShadowFade(float distanceSquared, float shadowDistance, float fadeLength)
        {
            if (distanceSquared >= shadowDistance * shadowDistance) return 0.0f;
            float start = Mathf.Max(0.0f, shadowDistance - fadeLength);
            if (distanceSquared <= start * start) return 1.0f;
            return Mathf.SmoothStep(0.0f, 1.0f, (shadowDistance - Mathf.Sqrt(distanceSquared)) / (shadowDistance - start));
        }

        internal static float DistanceRetention(float distanceSquared, float thinningStart, float distanceLimit)
        {
            if (distanceSquared <= thinningStart * thinningStart || distanceLimit <= thinningStart) return 1.0f;
            return Mathf.Clamp01((distanceLimit - Mathf.Sqrt(distanceSquared)) / (distanceLimit - thinningStart));
        }

        internal static float ThinningScale(float rank, float retention)
        {
            if (retention >= 1.0f) return 1.0f;
            if (retention <= rank) return 0.0f;
            return Mathf.SmoothStep(0.0f, 1.0f, (retention - rank) / Mathf.Min(THINNING_TRANSITION, 1.0f - rank));
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

        private void FlushSource(Camera camera, int model, bool shadow, Bounds batchBounds)
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
            var parameters = new RenderParams(material)
            {
                camera = camera,
                worldBounds = batchBounds,
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
        private struct SourceInstance
        {
            public Matrix4x4 objectToWorld;
            public Matrix4x4 prevObjectToWorld;

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
                Rank = new float[count];
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
            public float[] Rank { get; }
        }
    }
}

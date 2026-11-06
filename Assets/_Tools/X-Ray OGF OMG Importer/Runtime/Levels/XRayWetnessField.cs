using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRay.Importer.Levels
{
    [StructLayout(LayoutKind.Sequential)]
    public struct XRayWetnessCell
    {
        [SerializeField] private Vector4 _moisture;
        [SerializeField] private Vector2 _heights;

        public Vector4 Moisture => _moisture;
        public Vector2 Heights => _heights;
    }

    public sealed class XRayWetnessField : IDisposable
    {
        private const int CELL_STRIDE = 24;

        private static readonly int _activeId = Shader.PropertyToID("_MonolithWetnessActive");
        private static readonly int _ownerId = Shader.PropertyToID("_MonolithWetnessOwner");
        private static readonly int _fieldId = Shader.PropertyToID("_MonolithWetnessCells");
        private static readonly int _patternsId = Shader.PropertyToID("_MonolithWetSurfaceTextures");
        private static readonly int _mappingId = Shader.PropertyToID("_MonolithWetnessMapping");
        private static readonly int _cellId = Shader.PropertyToID("_MonolithWetnessCellSize");
        private static readonly int _settingsId = Shader.PropertyToID("_MonolithWetnessSettings");
        private static readonly int _puddleId = Shader.PropertyToID("_MonolithPuddleSettings");
        private static readonly int _reflectionId = Shader.PropertyToID("_MonolithWetReflectionIntensity");

        private readonly int _ownerToken;
        private readonly int[] _size = new int[2];
        private readonly int[] _allocationSize = new int[2];
        private readonly XRayWetnessCell[] _readback = new XRayWetnessCell[1];

        private ComputeShader _sourceCompute;
        private ComputeShader _compute;
        private GraphicsBuffer _current;
        private GraphicsBuffer _next;
        private Texture _savedPatterns;
        private Vector4 _mapping;
        private Vector4 _savedMapping;
        private Vector4 _savedCell;
        private Vector4 _savedSettings;
        private Vector4 _savedPuddle;
        private float _savedActive;
        private float _savedReflection;
        private int _savedOwner;
        private int _updateKernel;
        private int _resampleKernel;
        private int _clearKernel;
        private bool _published;

        public GraphicsBuffer Buffer => _current;
        public int Width => _size[0];
        public int Height => _size[1];
        public long EstimatedBytes => _current != null ? (long)_current.count * CELL_STRIDE * 2 : 0;

        public XRayWetnessField(GameObject owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            _ownerToken = owner.GetEntityId().GetHashCode();
        }

        public void Tick(PrecipitationConfig config, Texture heightTexture, Vector4 worldMapping, float rainDensity, float realDelta)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (heightTexture == null) throw new ArgumentNullException(nameof(heightTexture));
            if (!float.IsFinite(realDelta) || realDelta < 0.0f) throw new ArgumentOutOfRangeException(nameof(realDelta));
            if (!float.IsFinite(rainDensity)) throw new ArgumentOutOfRangeException(nameof(rainDensity));
            if (!float.IsFinite(worldMapping.x) || !float.IsFinite(worldMapping.y) || !float.IsFinite(worldMapping.z) || !float.IsFinite(worldMapping.w) || worldMapping.z <= 0.0f || worldMapping.w <= 0.0f) throw new ArgumentOutOfRangeException(nameof(worldMapping));
            EnsureResources(config.WetnessCompute, heightTexture.width, heightTexture.height, worldMapping);
            _compute.SetBuffer(_updateKernel, "_Previous", _current);
            _compute.SetBuffer(_updateKernel, "_Result", _next);
            _compute.SetTexture(_updateKernel, "_Height", heightTexture);
            _compute.SetInts("_Size", _size);
            _compute.SetInts("_PreviousSize", _size);
            _compute.SetVector("_PreviousMapping", _mapping);
            _compute.SetVector("_Mapping", worldMapping);
            _compute.SetVector("_WetRates", new Vector4(config.WettingRate, config.DryingRate, config.MaxWetness, Mathf.Clamp01(rainDensity)));
            _compute.SetVector("_PuddleRates", new Vector4(config.PuddleFormationRate, config.PuddleDryingRate, config.MaxPuddles, config.PuddleWetnessThreshold));
            _compute.SetFloat("_Delta", realDelta);
            _compute.SetFloat("_ShelterTolerance", config.ShelterTolerance);
            Dispatch(_updateKernel, Width, Height);
            GraphicsBuffer previous = _current;
            _current = _next;
            _next = previous;
            _mapping = worldMapping;
        }

        public void Publish(PrecipitationConfig config, float realTime, float rainDensity)
        {
            if (config == null || _current == null) return;
            if (config.WetSurfaceTextures == null) throw new InvalidOperationException("PrecipitationConfig requires prepared wet surface texture slices.");
            if (!_published)
            {
                _savedOwner = Shader.GetGlobalInteger(_ownerId);
                _savedActive = Shader.GetGlobalFloat(_activeId);
                if (_savedOwner != 0 || !float.IsFinite(_savedActive) || _savedActive > 0.5f) throw new InvalidOperationException("Another weather owner already publishes the precipitation wetness field.");
                _savedPatterns = Shader.GetGlobalTexture(_patternsId);
                _savedMapping = Shader.GetGlobalVector(_mappingId);
                _savedCell = Shader.GetGlobalVector(_cellId);
                _savedSettings = Shader.GetGlobalVector(_settingsId);
                _savedPuddle = Shader.GetGlobalVector(_puddleId);
                _savedReflection = Shader.GetGlobalFloat(_reflectionId);
                _published = true;
            }
            else if (Shader.GetGlobalInteger(_ownerId) != _ownerToken) throw new InvalidOperationException("The precipitation wetness field no longer owns its shader bindings.");
            Shader.SetGlobalBuffer(_fieldId, _current);
            Shader.SetGlobalTexture(_patternsId, config.WetSurfaceTextures);
            Shader.SetGlobalVector(_mappingId, _mapping);
            Shader.SetGlobalVector(_cellId, new Vector4(1.0f / (Width * _mapping.z), 1.0f / (Height * _mapping.w), Width, Height));
            Shader.SetGlobalVector(_settingsId, new Vector4(config.WetnessEnabled ? config.WetnessStrength : 0.0f, config.PuddlesEnabled ? config.PuddleStrength : 0.0f, config.ShelterTolerance, Mathf.Clamp01(rainDensity)));
            Shader.SetGlobalVector(_puddleId, new Vector4(config.RippleStrength, config.PuddleMaskScale, realTime, config.WetSurfaceTextures.depth));
            Shader.SetGlobalFloat(_reflectionId, config.ReflectionIntensity);
            Shader.SetGlobalInteger(_ownerId, _ownerToken);
            Shader.SetGlobalFloat(_activeId, config.WetnessEnabled || config.PuddlesEnabled ? 1.0f : 0.0f);
        }

        public void Unpublish()
        {
            if (!_published) return;
            if (Shader.GetGlobalInteger(_ownerId) == _ownerToken)
            {
                Shader.SetGlobalFloat(_activeId, _savedActive);
                Shader.SetGlobalBuffer(_fieldId, (GraphicsBuffer)null);
                Shader.SetGlobalTexture(_patternsId, _savedPatterns);
                Shader.SetGlobalVector(_mappingId, _savedMapping);
                Shader.SetGlobalVector(_cellId, _savedCell);
                Shader.SetGlobalVector(_settingsId, _savedSettings);
                Shader.SetGlobalVector(_puddleId, _savedPuddle);
                Shader.SetGlobalFloat(_reflectionId, _savedReflection);
                Shader.SetGlobalInteger(_ownerId, _savedOwner);
            }
            _published = false;
        }

        public void SetUniformState(float wetness, float puddles)
        {
            if (!float.IsFinite(wetness) || !float.IsFinite(puddles) || wetness < 0.0f || wetness > 1.0f || puddles < 0.0f || puddles > 1.0f) throw new ArgumentOutOfRangeException(nameof(wetness));
            if (_current == null) throw new InvalidOperationException("Initialize the wetness field before setting its state.");
            var state = new Vector4(wetness, puddles, wetness, puddles);
            Clear(_current, Width, Height, state, true);
            Clear(_next, Width, Height, state, true);
        }

        public bool TryReadCell(int x, int y, out XRayWetnessCell cell)
        {
            cell = default;
            if (_current == null || x < 0 || y < 0 || x >= Width || y >= Height) return false;
            _current.GetData(_readback, 0, y * Width + x, 1);
            cell = _readback[0];
            return true;
        }

        public void Dispose()
        {
            Unpublish();
            _current?.Dispose();
            _next?.Dispose();
            CoreUtils.Destroy(_compute);
            _current = null;
            _next = null;
            _compute = null;
            _sourceCompute = null;
            _size[0] = 0;
            _size[1] = 0;
        }

        private void EnsureResources(ComputeShader source, int width, int height, Vector4 mapping)
        {
            EnsureCompute(source);
            if (_current != null && Width == width && Height == height) return;
            GraphicsBuffer nextCurrent = null;
            GraphicsBuffer nextTarget = null;
            try
            {
                nextCurrent = new GraphicsBuffer(GraphicsBuffer.Target.Structured, checked(width * height), CELL_STRIDE);
                nextTarget = new GraphicsBuffer(GraphicsBuffer.Target.Structured, checked(width * height), CELL_STRIDE);
                nextCurrent.name = "Monolith Wetness State";
                nextTarget.name = "Monolith Wetness Next";
                if (_current == null) Clear(nextCurrent, width, height, Vector4.zero, false);
                else
                {
                    _allocationSize[0] = width;
                    _allocationSize[1] = height;
                    _compute.SetBuffer(_resampleKernel, "_Previous", _current);
                    _compute.SetBuffer(_resampleKernel, "_Result", nextCurrent);
                    _compute.SetInts("_Size", _allocationSize);
                    _compute.SetInts("_PreviousSize", _size);
                    _compute.SetVector("_PreviousMapping", _mapping);
                    _compute.SetVector("_Mapping", mapping);
                    Dispatch(_resampleKernel, width, height);
                }
                Clear(nextTarget, width, height, Vector4.zero, false);
            }
            catch
            {
                nextCurrent?.Dispose();
                nextTarget?.Dispose();
                throw;
            }
            Unpublish();
            _current?.Dispose();
            _next?.Dispose();
            _current = nextCurrent;
            _next = nextTarget;
            _size[0] = width;
            _size[1] = height;
            _mapping = mapping;
        }

        private void EnsureCompute(ComputeShader source)
        {
            if (source == null) throw new InvalidOperationException("PrecipitationConfig requires a wetness compute shader.");
            if (_sourceCompute == source && _compute != null) return;
            ComputeShader nextCompute = UnityEngine.Object.Instantiate(source);
            int updateKernel;
            int resampleKernel;
            int clearKernel;
            try
            {
                updateKernel = nextCompute.FindKernel("UpdateWetness");
                resampleKernel = nextCompute.FindKernel("ResampleWetness");
                clearKernel = nextCompute.FindKernel("ClearWetness");
            }
            catch
            {
                CoreUtils.Destroy(nextCompute);
                throw;
            }
            CoreUtils.Destroy(_compute);
            _compute = nextCompute;
            _compute.hideFlags = HideFlags.HideAndDontSave;
            _sourceCompute = source;
            _updateKernel = updateKernel;
            _resampleKernel = resampleKernel;
            _clearKernel = clearKernel;
        }

        private void Clear(GraphicsBuffer buffer, int width, int height, Vector4 value, bool preserveHeights)
        {
            _allocationSize[0] = width;
            _allocationSize[1] = height;
            _compute.SetBuffer(_clearKernel, "_Result", buffer);
            _compute.SetInts("_Size", _allocationSize);
            _compute.SetVector("_Uniform", value);
            _compute.SetInt("_PreserveHeights", preserveHeights ? 1 : 0);
            Dispatch(_clearKernel, width, height);
        }

        private void Dispatch(int kernel, int width, int height)
        {
            _compute.Dispatch(kernel, (width + 7) / 8, (height + 7) / 8, 1);
        }
    }
}

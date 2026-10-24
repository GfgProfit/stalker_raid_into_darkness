using System;
using System.Collections.Generic;
using UnityEngine;

namespace XRay.Importer.Levels
{
    [Serializable]
    public sealed class XRayDetailModelEntry
    {
        [SerializeField] private string _shader;
        [SerializeField] private string _texture;
        [SerializeField] private Mesh _mesh;
        [SerializeField] private Material _material;
        [SerializeField] private float _inverseHeight;
        [SerializeField] private bool _noWaving;
        [SerializeField] private float _minScale;
        [SerializeField] private float _maxScale;
        [SerializeField] private int _instanceCount;

        public XRayDetailModelEntry(string shader, string texture, Mesh mesh, Material material, float inverseHeight, bool noWaving, float minScale, float maxScale, int instanceCount)
        {
            _shader = shader;
            _texture = texture;
            _mesh = mesh;
            _material = material;
            _inverseHeight = inverseHeight;
            _noWaving = noWaving;
            _minScale = minScale;
            _maxScale = maxScale;
            _instanceCount = instanceCount;
        }

        public string Shader => _shader;
        public string Texture => _texture;
        public Mesh Mesh => _mesh;
        public Material Material => _material;
        public float InverseHeight => _inverseHeight;
        public bool NoWaving => _noWaving;
        public float MinScale => _minScale;
        public float MaxScale => _maxScale;
        public int InstanceCount => _instanceCount;
    }

    [Serializable]
    public struct XRayDetailChunk
    {
        [SerializeField] private Bounds _bounds;
        [SerializeField] private Vector3 _positionOrigin;
        [SerializeField] private bool _hasPositionOrigin;
        [SerializeField] private int _instanceStart;
        [SerializeField] private int _instanceCount;
        [SerializeField] private int _slotStart;
        [SerializeField] private int _slotCount;

        public XRayDetailChunk(Bounds bounds, Vector3 positionOrigin, int instanceStart, int instanceCount, int slotStart, int slotCount)
        {
            _bounds = bounds;
            _positionOrigin = positionOrigin;
            _hasPositionOrigin = true;
            _instanceStart = instanceStart;
            _instanceCount = instanceCount;
            _slotStart = slotStart;
            _slotCount = slotCount;
        }

        public Bounds Bounds => _bounds;
        public Vector3 PositionOrigin => _hasPositionOrigin ? _positionOrigin : _bounds.min;
        public int InstanceStart => _instanceStart;
        public int InstanceCount => _instanceCount;
        public int SlotStart => _slotStart;
        public int SlotCount => _slotCount;
    }

    [Serializable]
    public struct XRayDetailSwing
    {
        [SerializeField] private float _amplitude1;
        [SerializeField] private float _amplitude2;
        [SerializeField] private float _rotation1;
        [SerializeField] private float _rotation2;
        [SerializeField] private float _speed;

        public XRayDetailSwing(float amplitude1, float amplitude2, float rotation1, float rotation2, float speed)
        {
            _amplitude1 = amplitude1;
            _amplitude2 = amplitude2;
            _rotation1 = rotation1;
            _rotation2 = rotation2;
            _speed = speed;
        }

        public float Amplitude1 => _amplitude1;
        public float Amplitude2 => _amplitude2;
        public float Rotation1 => _rotation1;
        public float Rotation2 => _rotation2;
        public float Speed => _speed;

        public static XRayDetailSwing Lerp(XRayDetailSwing a, XRayDetailSwing b, float t)
        {
            return new XRayDetailSwing(Mathf.Lerp(a._amplitude1, b._amplitude1, t), Mathf.Lerp(a._amplitude2, b._amplitude2, t), Mathf.Lerp(a._rotation1, b._rotation1, t), Mathf.Lerp(a._rotation2, b._rotation2, t), Mathf.Lerp(a._speed, b._speed, t));
        }
    }

    public sealed class XRayDetailDatabase : ScriptableObject
    {
        public const int INSTANCE_STRIDE = 16;
        public const int SLOT_STRIDE = 12;
        public const float POSITION_QUANT = 65535.0f;

        [SerializeField] private string _levelName;
        [SerializeField] private float _chunkSize;
        [SerializeField] private float _density;
        [SerializeField] private float _heightScale;
        [SerializeField] private float _slotSize;
        [SerializeField] private List<XRayDetailModelEntry> _models = new List<XRayDetailModelEntry>();
        [SerializeField] private List<XRayDetailChunk> _chunks = new List<XRayDetailChunk>();
        [SerializeField] private TextAsset _instances;
        [SerializeField] private TextAsset _slots;
        [SerializeField] private XRayDetailSwing _swingNormal;
        [SerializeField] private XRayDetailSwing _swingFast;
        [SerializeField] private int _instanceCount;
        [SerializeField] private int _slotCount;

        public string LevelName => _levelName;
        public float ChunkSize => _chunkSize;
        public float Density => _density;
        public float HeightScale => _heightScale;
        public float SlotSize => _slotSize;
        public IReadOnlyList<XRayDetailModelEntry> Models => _models;
        public IReadOnlyList<XRayDetailChunk> Chunks => _chunks;
        public TextAsset Instances => _instances;
        public TextAsset Slots => _slots;
        public XRayDetailSwing SwingNormal => _swingNormal;
        public XRayDetailSwing SwingFast => _swingFast;
        public int InstanceCount => _instanceCount;
        public int SlotCount => _slotCount;

        public void Initialize(string levelName, float chunkSize, float density, float heightScale, float slotSize, List<XRayDetailModelEntry> models, List<XRayDetailChunk> chunks, TextAsset instances, TextAsset slots, XRayDetailSwing swingNormal, XRayDetailSwing swingFast, int instanceCount, int slotCount)
        {
            _levelName = levelName;
            _chunkSize = chunkSize;
            _density = density;
            _heightScale = heightScale;
            _slotSize = slotSize;
            _models = models;
            _chunks = chunks;
            _instances = instances;
            _slots = slots;
            _swingNormal = swingNormal;
            _swingFast = swingFast;
            _instanceCount = instanceCount;
            _slotCount = slotCount;
        }
    }
}

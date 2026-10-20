using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    public enum XRayParticleBlendMode
    {
        Opaque = 0,
        Blend = 1,
        Add = 2,
        Multiply = 3,
        Multiply2X = 4,
        AlphaAdd = 5,
        Distort = 6
    }

    public sealed class XRayParticleEffectAsset : ScriptableObject
    {
        public const uint SPRITE = 1u << 0;
        public const uint FRAMED = 1u << 10;
        public const uint ANIMATED = 1u << 11;
        public const uint RANDOM_FRAME = 1u << 12;
        public const uint RANDOM_PLAYBACK = 1u << 13;
        public const uint TIME_LIMIT = 1u << 14;
        public const uint ALIGN_TO_PATH = 1u << 15;
        public const uint VELOCITY_SCALE = 1u << 18;
        public const uint WORLD_ALIGN = 1u << 20;
        public const uint FACE_ALIGN = 1u << 21;

        [SerializeField] private string _effectName;
        [SerializeField] private int _maxParticles;
        [SerializeField] private uint _flags;
        [SerializeField] private string _shaderName;
        [SerializeField] private string _textureName;
        [SerializeField] private XRayParticleBlendMode _blendMode;
        [SerializeField] private Material _material;
        [SerializeField] private Vector2 _frameSize = Vector2.one;
        [SerializeField] private int _frameColumns = 1;
        [SerializeField] private int _frameCount = 1;
        [SerializeField] private float _frameSpeed;
        [SerializeField] private float _timeLimit;
        [SerializeField] private Vector3 _velocityScale;
        [SerializeField] private Vector3 _defaultRotation;
        [SerializeField] private List<XRayParticleAction> _actions = new List<XRayParticleAction>();

        public string EffectName => _effectName;
        public int MaxParticles => _maxParticles;
        public uint Flags => _flags;
        public string ShaderName => _shaderName;
        public string TextureName => _textureName;
        public XRayParticleBlendMode BlendMode => _blendMode;
        public Material Material => _material;
        public Vector2 FrameSize => _frameSize;
        public int FrameColumns => _frameColumns;
        public int FrameCount => _frameCount;
        public float FrameSpeed => _frameSpeed;
        public float TimeLimit => _timeLimit;
        public Vector3 VelocityScale => _velocityScale;
        public Vector3 DefaultRotation => _defaultRotation;
        public IReadOnlyList<XRayParticleAction> Actions => _actions;
        public bool IsFramed => HasFlags(FRAMED);
        public bool IsAnimated => HasFlags(FRAMED | ANIMATED);
        public bool HasTimeLimit => HasFlags(TIME_LIMIT);
        public bool AlignToPath => HasFlags(ALIGN_TO_PATH);
        public bool UsesVelocityScale => HasFlags(VELOCITY_SCALE);
        public bool FaceAlign => HasFlags(FACE_ALIGN);
        public bool WorldAlign => HasFlags(WORLD_ALIGN);

        public bool HasFlags(uint mask) => (_flags & mask) == mask;

        public void SetImportData(string effectName, int maxParticles, uint flags, string shaderName, string textureName, XRayParticleBlendMode blendMode, Vector2 frameSize, int frameColumns, int frameCount, float frameSpeed, float timeLimit, Vector3 velocityScale, Vector3 defaultRotation, List<XRayParticleAction> actions)
        {
            _effectName = effectName;
            _maxParticles = maxParticles;
            _flags = flags;
            _shaderName = shaderName;
            _textureName = textureName;
            _blendMode = blendMode;
            _frameSize = frameSize;
            _frameColumns = Mathf.Max(1, frameColumns);
            _frameCount = Mathf.Max(1, frameCount);
            _frameSpeed = frameSpeed;
            _timeLimit = timeLimit;
            _velocityScale = velocityScale;
            _defaultRotation = defaultRotation;
            _actions = actions ?? new List<XRayParticleAction>();
        }

        public void SetMaterial(Material material)
        {
            _material = material;
        }
    }
}

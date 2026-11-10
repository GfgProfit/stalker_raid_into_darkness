using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Player
{
    public enum StalkerMovingMode
    {
        Walk,
        Run,
        Crouch
    }

    [Serializable]
    public sealed class StalkerSurfaceSounds
    {
        [SerializeField] private StalkerSurfaceType _type;
        [SerializeField] private string _materialName;
        [SerializeField] private List<AudioClip> _pairStepClips = new List<AudioClip>();
        [SerializeField] private List<AudioClip> _walkClips = new List<AudioClip>();
        [SerializeField] private List<AudioClip> _runClips = new List<AudioClip>();
        [SerializeField] private List<AudioClip> _crouchClips = new List<AudioClip>();
        [SerializeField] private List<AudioClip> _jumpClips = new List<AudioClip>();
        [SerializeField] private List<AudioClip> _landClips = new List<AudioClip>();
        [SerializeField] private List<AudioClip> _collideClips = new List<AudioClip>();

        public StalkerSurfaceSounds(StalkerSurfaceType type, string materialName)
        {
            _type = type;
            _materialName = materialName;
        }

        public StalkerSurfaceType Type => _type;
        public string MaterialName => _materialName;
        public List<AudioClip> PairStepClips => _pairStepClips;
        public List<AudioClip> WalkClips => _walkClips;
        public List<AudioClip> RunClips => _runClips;
        public List<AudioClip> CrouchClips => _crouchClips;
        public List<AudioClip> JumpClips => _jumpClips;
        public List<AudioClip> LandClips => _landClips;
        public List<AudioClip> CollideClips => _collideClips;

        public List<AudioClip> GetMovingModeClips(StalkerMovingMode mode)
        {
            switch (mode)
            {
                case StalkerMovingMode.Run:
                    return _runClips;
                case StalkerMovingMode.Crouch:
                    return _crouchClips;
                default:
                    return _walkClips;
            }
        }
    }

    [Serializable]
    public sealed class StalkerPhysicsMaterialSurface
    {
        [SerializeField] private PhysicsMaterial _material;
        [SerializeField] private StalkerSurfaceType _type;

        public StalkerPhysicsMaterialSurface(PhysicsMaterial material, StalkerSurfaceType type)
        {
            _material = material;
            _type = type;
        }

        public PhysicsMaterial Material => _material;
        public StalkerSurfaceType Type => _type;
    }

    [Serializable]
    public sealed class StalkerTerrainLayerSurface
    {
        [SerializeField] private TerrainLayer _layer;
        [SerializeField] private StalkerSurfaceType _type;

        public StalkerTerrainLayerSurface(TerrainLayer layer, StalkerSurfaceType type)
        {
            _layer = layer;
            _type = type;
        }

        public TerrainLayer Layer => _layer;
        public StalkerSurfaceType Type => _type;
    }

    [CreateAssetMenu(menuName = "Raid Into Darkness/Player/Surface Audio Library", fileName = "StalkerSurfaceAudioLibrary")]
    public sealed class StalkerSurfaceAudioLibrary : ScriptableObject
    {
        [SerializeField] private List<StalkerSurfaceSounds> _surfaces = new List<StalkerSurfaceSounds>();
        [SerializeField] private StalkerSurfaceType _fallbackSurface = StalkerSurfaceType.Earth;
        [SerializeField] private List<StalkerPhysicsMaterialSurface> _physicsMaterials = new List<StalkerPhysicsMaterialSurface>();
        [SerializeField] private List<StalkerTerrainLayerSurface> _terrainLayers = new List<StalkerTerrainLayerSurface>();
        [SerializeField] private List<AudioClip> _gearRustleClips = new List<AudioClip>();
        [SerializeField] private List<AudioClip> _crouchKeyClips = new List<AudioClip>();
        [Tooltip("psHUDStepSoundVolume: multiplies the step power of material pair sounds in first person.")]
        [SerializeField] private float _hudStepVolume = 1.0f;
        [Tooltip("Console 'snd_volume_eff' used by the GRA footstep, jump and gear scripts.")]
        [SerializeField] private float _effectsVolume = 1.0f;
        [Tooltip("eft_jump_sounds.actor_on_land: landing sounds play above this contact speed.")]
        [SerializeField] private float _landSoundSpeed = 5.0f;
        [Tooltip("eft_jump_sounds.actor_on_land: volume = snd_volume_eff * speed / divider.")]
        [SerializeField] private float _landVolumeDivider = 5.0f;
        [Tooltip("eft_jump_sounds.ray_cast: ray starts this high above the feet.")]
        [SerializeField] private float _rayStartHeight = 1.0f;
        [Tooltip("eft_jump_sounds.ray_cast: ray_range.")]
        [SerializeField] private float _rayLength = 1.1f;
        [Tooltip("Unity adaptation: RMS ships no crouch_* files, so crouch steps reuse the walk clips at this volume. 0 keeps the silent GRA behaviour.")]
        [SerializeField] private float _crouchFallbackVolume = 0.5f;
        [TextArea(3, 20)]
        [SerializeField] private string _sourceReport;

        private StalkerSurfaceSounds[] _byType;

        public IReadOnlyList<StalkerSurfaceSounds> Surfaces => _surfaces;
        public StalkerSurfaceType FallbackSurface => _fallbackSurface;
        public IReadOnlyList<StalkerPhysicsMaterialSurface> PhysicsMaterials => _physicsMaterials;
        public IReadOnlyList<StalkerTerrainLayerSurface> TerrainLayers => _terrainLayers;
        public IReadOnlyList<AudioClip> GearRustleClips => _gearRustleClips;
        public IReadOnlyList<AudioClip> CrouchKeyClips => _crouchKeyClips;
        public float HudStepVolume => _hudStepVolume;
        public float EffectsVolume => _effectsVolume;
        public float LandSoundSpeed => _landSoundSpeed;
        public float LandVolumeDivider => _landVolumeDivider;
        public float RayStartHeight => _rayStartHeight;
        public float RayLength => _rayLength;
        public float CrouchFallbackVolume => _crouchFallbackVolume;
        public string SourceReport => _sourceReport;

        public StalkerSurfaceSounds GetSurface(StalkerSurfaceType type)
        {
            if (_byType == null || _byType.Length != StalkerSurfaceTypes.Count)
            {
                _byType = new StalkerSurfaceSounds[StalkerSurfaceTypes.Count];
                foreach (StalkerSurfaceSounds surface in _surfaces)
                {
                    _byType[(int)surface.Type] = surface;
                }
            }

            return _byType[(int)type];
        }

        public void SetImportData(List<StalkerSurfaceSounds> surfaces, StalkerSurfaceType fallbackSurface, List<AudioClip> gearRustleClips, List<AudioClip> crouchKeyClips, float hudStepVolume, float effectsVolume, float landSoundSpeed, float landVolumeDivider, float rayStartHeight, float rayLength, string sourceReport)
        {
            _surfaces = surfaces;
            _fallbackSurface = fallbackSurface;
            _gearRustleClips = gearRustleClips;
            _crouchKeyClips = crouchKeyClips;
            _hudStepVolume = hudStepVolume;
            _effectsVolume = effectsVolume;
            _landSoundSpeed = landSoundSpeed;
            _landVolumeDivider = landVolumeDivider;
            _rayStartHeight = rayStartHeight;
            _rayLength = rayLength;
            _sourceReport = sourceReport;
            _byType = null;
        }

        public void SetSurfaceMappings(List<StalkerPhysicsMaterialSurface> physicsMaterials, List<StalkerTerrainLayerSurface> terrainLayers)
        {
            _physicsMaterials = physicsMaterials;
            _terrainLayers = terrainLayers;
        }

        public void SetImportedSurfaces(List<StalkerSurfaceSounds> surfaces)
        {
            _surfaces = surfaces;
            _byType = null;
        }

        private void OnValidate()
        {
            _byType = null;
        }
    }
}

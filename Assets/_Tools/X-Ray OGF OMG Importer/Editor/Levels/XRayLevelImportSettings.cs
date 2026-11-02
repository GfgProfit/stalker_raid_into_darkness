using System;
using UnityEngine;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    public enum XRayLevelSourceType
    {
        [InspectorName("Gamedata Folder (True Stalker / Unpacked)")] GamedataFolder,
        [InspectorName("Mod Organizer 2 Profile (Anomaly)")] Mo2Profile
    }

    public enum XRayLevelMaterialUpdateMode
    {
        [InspectorName("Preserve Existing")] Preserve,
        [InspectorName("Update Generated Properties")] Update
    }

    public enum XRayTerrainImportMode
    {
        [InspectorName("Preserve Existing Rendering")] Preserve,
        [InspectorName("Bind Source Terrain Data")] Bind,
        [InspectorName("Enable Verified Anomaly Terrain")] EnableVerifiedAnomaly,
        [InspectorName("Enable Verified TS Dry Surface (HDRP Lighting)")] EnableVerifiedTrueStalker
    }

    public enum XRayFloraImportMode
    {
        [InspectorName("Legacy / Preserve Existing Rendering")] Preserve,
        [InspectorName("Enable Verified Anomaly Flora")] EnableVerifiedAnomaly,
        [InspectorName("Enable Verified True Stalker Flora")] EnableVerifiedTrueStalker
    }

    public enum XRayFloraWindImportMode
    {
        [InspectorName("Disabled / Static Legacy Trees")] Disabled,
        [InspectorName("Enable Verified Anomaly SSFX Wind")] EnableVerifiedAnomaly,
        [InspectorName("Enable Verified True Stalker X-Ray Wind")] EnableVerifiedTrueStalker
    }

    public enum XRayTerrainQuality
    {
        [InspectorName("HQ")] High,
        [InspectorName("MID")] Medium,
        [InspectorName("LOW")] Low
    }

    [Serializable]
    public sealed class XRayLevelImportSettings
    {
        [SerializeField] private float _chunkSize = 64.0f;
        [SerializeField] private float _instanceCellSize = 128.0f;
        [SerializeField] private bool _importDetails = true;
        [SerializeField] private float _detailDensity = 0.6f;
        [SerializeField] private float _detailHeightScale = 1.0f;
        [SerializeField] private float _detailChunkSize = 32.0f;
        [SerializeField] private bool _runtimeDetails;
        [SerializeField] private bool _importCollision = true;
        [SerializeField] private float _collisionChunkSize = 64.0f;
        [SerializeField] private bool _importLights = true;
        [SerializeField] private bool _staticLightShadows;
        [SerializeField] private XRayLevelMaterialUpdateMode _materialUpdateMode = XRayLevelMaterialUpdateMode.Preserve;
        [SerializeField] private XRayTerrainImportMode _terrainImportMode;
        [SerializeField] private XRayTerrainQuality _terrainQuality;
        [SerializeField] private XRayFloraImportMode _floraImportMode;
        [SerializeField] private XRayFloraWindImportMode _floraWindMode;
        [SerializeField] private XRayWeatherProfile _weatherProfile;
        [SerializeField] private bool _openSceneAfterImport = true;
        [SerializeField] private bool _copySourceFiles = true;
        [SerializeField] private bool _autoImportDroppedLevels = true;

        public float ChunkSize
        {
            get => _chunkSize;
            set => _chunkSize = Mathf.Max(8.0f, value);
        }

        public float InstanceCellSize
        {
            get => _instanceCellSize;
            set => _instanceCellSize = Mathf.Max(8.0f, value);
        }

        public bool ImportDetails
        {
            get => _importDetails;
            set => _importDetails = value;
        }

        public float DetailDensity
        {
            get => _detailDensity;
            set => _detailDensity = Mathf.Clamp(value, 0.04f, 1.0f);
        }

        public bool RuntimeDetails
        {
            get => _runtimeDetails;
            set => _runtimeDetails = value;
        }

        public float DetailHeightScale
        {
            get => _detailHeightScale;
            set => _detailHeightScale = Mathf.Clamp(value, 0.5f, 2.0f);
        }

        public float DetailChunkSize
        {
            get => _detailChunkSize;
            set => _detailChunkSize = Mathf.Clamp(value, 8.0f, 256.0f);
        }

        public bool ImportCollision
        {
            get => _importCollision;
            set => _importCollision = value;
        }

        public float CollisionChunkSize
        {
            get => _collisionChunkSize;
            set => _collisionChunkSize = Mathf.Max(16.0f, value);
        }

        public bool ImportLights
        {
            get => _importLights;
            set => _importLights = value;
        }

        public bool StaticLightShadows
        {
            get => _staticLightShadows;
            set => _staticLightShadows = value;
        }

        public XRayLevelMaterialUpdateMode MaterialUpdateMode
        {
            get => _materialUpdateMode;
            set => _materialUpdateMode = value;
        }

        public XRayTerrainImportMode TerrainImportMode
        {
            get => _terrainImportMode;
            set => _terrainImportMode = value;
        }

        public XRayTerrainQuality TerrainQuality
        {
            get => _terrainQuality;
            set => _terrainQuality = value;
        }

        public XRayFloraImportMode FloraImportMode
        {
            get => _floraImportMode;
            set => _floraImportMode = value;
        }

        public XRayFloraWindImportMode FloraWindMode
        {
            get => _floraWindMode;
            set => _floraWindMode = value;
        }

        public XRayWeatherProfile WeatherProfile
        {
            get => _weatherProfile;
            set => _weatherProfile = value;
        }

        public bool OpenSceneAfterImport
        {
            get => _openSceneAfterImport;
            set => _openSceneAfterImport = value;
        }

        public bool CopySourceFiles
        {
            get => _copySourceFiles;
            set => _copySourceFiles = value;
        }

        public bool AutoImportDroppedLevels
        {
            get => _autoImportDroppedLevels;
            set => _autoImportDroppedLevels = value;
        }

        public XRayLevelImportSettings Clone()
        {
            return (XRayLevelImportSettings)MemberwiseClone();
        }
    }
}

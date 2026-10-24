using System;
using UnityEngine;

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

    [Serializable]
    public sealed class XRayLevelImportSettings
    {
        [SerializeField] private float _chunkSize = 64.0f;
        [SerializeField] private float _instanceCellSize = 128.0f;
        [SerializeField] private bool _importDetails = true;
        [SerializeField] private float _detailDensity = 0.6f;
        [SerializeField] private float _detailHeightScale = 1.0f;
        [SerializeField] private float _detailChunkSize = 32.0f;
        [SerializeField] private bool _importCollision = true;
        [SerializeField] private float _collisionChunkSize = 64.0f;
        [SerializeField] private bool _importLights = true;
        [SerializeField] private bool _staticLightShadows;
        [SerializeField] private XRayLevelMaterialUpdateMode _materialUpdateMode = XRayLevelMaterialUpdateMode.Preserve;
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

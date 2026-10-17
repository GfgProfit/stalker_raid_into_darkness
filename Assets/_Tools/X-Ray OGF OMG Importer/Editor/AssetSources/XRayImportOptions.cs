using System;
using UnityEngine;

namespace XRay.Importer.Editor
{
    [Serializable]
    public sealed class XRayImportOptions
    {
        [SerializeField] private bool _autoImportTextures = true;
        [SerializeField] private bool _generateMaterials = true;
        [SerializeField] private bool _createTexturesFolder = true;
        [SerializeField] private bool _createMaterialsFolder = true;
        [SerializeField] private bool _preserveExistingMaterials = true;
        [SerializeField] private bool _overwriteExistingTextures;
        [SerializeField] private bool _resolveBumpMaps = true;
        [SerializeField] private bool _logMissingTextures = true;
        [SerializeField] private bool _flipNormalGreen = true;

        public bool AutoImportTextures
        {
            get => _autoImportTextures;
            set => _autoImportTextures = value;
        }

        public bool GenerateMaterials
        {
            get => _generateMaterials;
            set => _generateMaterials = value;
        }

        public bool CreateTexturesFolder
        {
            get => _createTexturesFolder;
            set => _createTexturesFolder = value;
        }

        public bool CreateMaterialsFolder
        {
            get => _createMaterialsFolder;
            set => _createMaterialsFolder = value;
        }

        public bool PreserveExistingMaterials
        {
            get => _preserveExistingMaterials;
            set => _preserveExistingMaterials = value;
        }

        public bool OverwriteExistingTextures
        {
            get => _overwriteExistingTextures;
            set => _overwriteExistingTextures = value;
        }

        public bool ResolveBumpMaps
        {
            get => _resolveBumpMaps;
            set => _resolveBumpMaps = value;
        }

        public bool LogMissingTextures
        {
            get => _logMissingTextures;
            set => _logMissingTextures = value;
        }

        public bool FlipNormalGreen
        {
            get => _flipNormalGreen;
            set => _flipNormalGreen = value;
        }

        public XRayImportOptions Clone()
        {
            return (XRayImportOptions)MemberwiseClone();
        }
    }
}

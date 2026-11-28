using System;

namespace XRay.Importer.Tests
{
    [Serializable]
    internal sealed class XRayNativeReflectionCullingViewMetadata
    {
        public string ownerCameraEntityId;
        public string ownerCameraType;
        public int shadowCommands;
        public string viewType;
    }
}

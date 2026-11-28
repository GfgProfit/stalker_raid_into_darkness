using System;

namespace XRay.Importer.Tests
{
    [Serializable]
    internal sealed class XRayNativeReflectionCullingMetadata
    {
        public string error;
        public XRayNativeReflectionCullingViewMetadata[] views;
    }
}

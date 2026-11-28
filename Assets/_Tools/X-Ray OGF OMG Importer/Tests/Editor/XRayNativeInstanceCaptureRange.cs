using System;
using UnityEngine;

namespace XRay.Importer.Tests
{
    [Serializable]
    internal sealed class XRayNativeInstanceCaptureRange
    {
        public Color minimum;
        public Color maximum;
        public int pixelCount;
    }
}

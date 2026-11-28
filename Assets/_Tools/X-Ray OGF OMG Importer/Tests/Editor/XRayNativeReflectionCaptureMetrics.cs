using System;

namespace XRay.Importer.Tests
{
    [Serializable]
    internal sealed class XRayNativeReflectionCaptureMetrics
    {
        public uint updateBefore;
        public uint updateAfter;
        public uint renderCountBefore;
        public uint renderCountAfter;
        public int reflectionCommands;
        public int reflectionInstances;
        public int reflectionViews;
        public int shadowCommands;
        public int shadowInstances;
        public int legacySubmissionCalls;
        public int legacySubmittedInstances;
        public int reflectionCameraEnds;
        public int reflectionCamerasWithInheritedSky;
        public int reflectionCamerasWithoutShadows;
        public bool nativeBuffersStable;
        public string fallbackReason;
    }
}

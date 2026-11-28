using System;

namespace XRay.Importer.Tests
{
    [Serializable]
    internal sealed class XRayNativeInstanceCaptureMetrics
    {
        public bool activeAfterCapture;
        public int cameraCommands;
        public int cameraInstances;
        public int shadowCommands;
        public int shadowInstances;
        public int shadowViews;
        public int cullingInvocations;
        public int legacySubmissionCalls;
        public int legacySubmittedInstances;
        public long memoryBytes;
        public string fallbackReason;
    }
}

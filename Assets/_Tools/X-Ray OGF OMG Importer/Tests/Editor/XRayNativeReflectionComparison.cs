using System;

namespace XRay.Importer.Tests
{
    [Serializable]
    internal sealed class XRayNativeReflectionComparison
    {
        public int pixels;
        public int changedPixels;
        public int rawNonzeroChangedPixels;
        public int casterControlPixels;
        public int sourceControlPixels;
        public float maximumError;
        public float maximumResidual;
        public float maximumAlphaError;
        public int adjacentHalfBudgetPixels;
        public int adjacentHalfBudgetChannels;
        public string precisionFormat;
        public string precisionMethod;
    }
}

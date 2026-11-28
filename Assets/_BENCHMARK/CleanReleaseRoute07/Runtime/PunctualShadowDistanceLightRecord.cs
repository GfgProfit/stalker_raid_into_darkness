using System;
using UnityEngine;

namespace Rvm.CleanReleaseRoute07
{
    [Serializable]
    internal sealed class PunctualShadowDistanceLightRecord
    {
        public string entityId;
        public string path;
        public bool eligible;
        public string originalType;
        public bool originalEnabled;
        public bool currentEnabled;
        public bool active;
        public float originalIntensity;
        public float currentIntensity;
        public float originalRange;
        public float currentRange;
        public Color originalColor;
        public Color currentColor;
        public string originalShadows;
        public string currentShadows;
        public string currentType;
        public int originalCullingMask;
        public int currentCullingMask;
        public int originalRenderingLayerMask;
        public int currentRenderingLayerMask;
        public float originalFadeDistance;
        public float currentFadeDistance;
        public float originalShadowDimmer;
        public float currentShadowDimmer;
        public float originalVolumetricShadowDimmer;
        public float currentVolumetricShadowDimmer;
        public bool volumetricShadowDimmerGoverned;
    }
}

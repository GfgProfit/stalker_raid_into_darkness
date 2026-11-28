using System;
using UnityEngine;

namespace Rvm.Benchmark.Tests
{
    [Serializable]
    internal sealed class PunctualShadowVisualCaptureState
    {
        public string name;
        public Vector3 cameraPosition;
        public Vector3 lightPosition;
        public float cameraLightDistance;
        public string lightType;
        public bool lightEnabled;
        public string lightShadows;
        public float intensity;
        public float range;
        public float shadowFadeDistance;
        public float shadowDimmer;
        public float volumetricShadowDimmer;
        public float lightFadeDistance;
        public float lightDimmer;
    }
}

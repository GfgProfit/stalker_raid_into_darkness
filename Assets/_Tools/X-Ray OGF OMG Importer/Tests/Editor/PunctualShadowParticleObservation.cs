using System;
using UnityEngine;

namespace XRay.Importer.Tests
{
    [Serializable]
    internal sealed class PunctualShadowParticleObservation
    {
        public string phase;
        public int render;
        public int frame;
        public int slot;
        public int matchedParticle;
        public string templateIdentity;
        public string name;
        public bool templateEnabled;
        public float intensity;
        public string shadows;
        public Vector3 position;
        public Vector3 particlePosition;
        public Vector3 templatePosition;
        public Color descriptorColor;
        public float descriptorRange;
        public bool hasHdData;
        public float fade;
        public float templateFade;
    }
}

using System;
using System.Collections.Generic;

namespace XRay.Importer.Tests
{
    [Serializable]
    internal sealed class PunctualShadowParticleEvidence
    {
        public string scope = "Public endCameraRendering context.Cull observes real per-particle VisibleLight slots. They reuse the disabled template Light identity and have particle-matched transforms distinct from the template transform. HDRP consumes that reference's HD data; separate Light clone identities are not claimed. Initial75/configured120/disabled and a particle-Lights-module-off control are asserted.";
        public int particleCount;
        public int initialGeneratedObservations;
        public float templateFadeAfterDisable;
        public bool sourceIsPlaying;
        public bool sourceIsPaused;
        public bool sourceIsEmitting;
        public bool sourceLightsModuleEnabled;
        public bool sourceLightTemplateEnabled;
        public int cullSnapshots;
        public int visibleLightObservations;
        public int templateVisibleObservations;
        public int controlVisibleObservations;
        public int generatedObservationsAfterModuleDisable;
        public int controlObservationsAfterModuleDisable;
        public int particleCountAfterModuleDisable;
        public List<PunctualShadowParticleObservation> lights = new List<PunctualShadowParticleObservation>();
    }
}

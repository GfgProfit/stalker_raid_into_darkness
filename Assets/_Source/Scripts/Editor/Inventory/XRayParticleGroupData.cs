using System.Collections.Generic;

namespace RaidIntoDarkness.Inventory.Editor
{
    public sealed class XRayParticleGroupData
    {
        public string Name { get; set; }
        public float TimeLimit { get; set; }
        public List<XRayParticleGroupEffectData> Effects { get; } = new List<XRayParticleGroupEffectData>();
    }
}

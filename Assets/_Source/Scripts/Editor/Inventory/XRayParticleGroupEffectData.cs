namespace RaidIntoDarkness.Inventory.Editor
{
    public sealed class XRayParticleGroupEffectData
    {
        public string EffectName { get; set; }
        public string OnPlayChild { get; set; } = string.Empty;
        public string OnBirthChild { get; set; } = string.Empty;
        public string OnDeathChild { get; set; } = string.Empty;
        public float Time0 { get; set; }
        public float Time1 { get; set; }
        public uint Flags { get; set; }
    }
}

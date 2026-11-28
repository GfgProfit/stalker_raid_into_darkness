namespace RaidIntoDarkness.Inventory.Editor
{
    internal readonly struct XRayParticleChunkRange
    {
        public XRayParticleChunkRange(uint id, int start, int end)
        {
            Id = id;
            Start = start;
            End = end;
        }

        public uint Id { get; }
        public int Start { get; }
        public int End { get; }
    }
}

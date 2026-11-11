namespace RaidIntoDarkness.Player
{
    public readonly struct AnomalyHitEvent
    {
        public AnomalyHitEvent(int sourceId, uint generation, ulong sequence)
        {
            SourceId = sourceId;
            Generation = generation;
            Sequence = sequence;
        }

        public int SourceId { get; }
        public uint Generation { get; }
        public ulong Sequence { get; }
    }
}

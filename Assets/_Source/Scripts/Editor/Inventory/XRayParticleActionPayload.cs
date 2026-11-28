using RaidIntoDarkness.Effects;

namespace RaidIntoDarkness.Inventory.Editor
{
    public sealed class XRayParticleActionPayload
    {
        public int Ordinal { get; }
        public XRayParticleActionType Type { get; }
        public uint Flags { get; }
        public byte[] Bytes { get; }

        public XRayParticleActionPayload(int ordinal, XRayParticleActionType type, uint flags, byte[] bytes)
        {
            Ordinal = ordinal;
            Type = type;
            Flags = flags;
            Bytes = bytes;
        }
    }
}

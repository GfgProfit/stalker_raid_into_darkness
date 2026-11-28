using UnityEngine;

namespace RaidIntoDarkness.Inventory.Domain
{
    internal struct SlotBounds
    {
        public SlotBounds(int x, int y)
        {
            MinX = x;
            MaxX = x;
            MinY = y;
            MaxY = y;
            CellCount = 1;
        }

        public int MinX { get; private set; }
        public int MaxX { get; private set; }
        public int MinY { get; private set; }
        public int MaxY { get; private set; }
        public int CellCount { get; private set; }

        public void Add(int x, int y)
        {
            MinX = Mathf.Min(MinX, x);
            MaxX = Mathf.Max(MaxX, x);
            MinY = Mathf.Min(MinY, y);
            MaxY = Mathf.Max(MaxY, y);
            CellCount++;
        }
    }
}

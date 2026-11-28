namespace RaidIntoDarkness.Inventory
{
    internal readonly struct EquipmentStatusBar
    {
        public EquipmentStatusBar(string id, EquipmentStat stat)
        {
            Id = id;
            Stat = stat;
        }

        public string Id { get; }
        public EquipmentStat Stat { get; }
    }
}

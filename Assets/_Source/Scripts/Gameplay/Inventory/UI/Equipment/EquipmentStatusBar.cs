using RaidIntoDarkness.Inventory.Domain;
namespace RaidIntoDarkness.Inventory.UI
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

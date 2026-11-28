using RaidIntoDarkness.Inventory.Domain;
namespace RaidIntoDarkness.Inventory.UI
{
    internal readonly struct StatRow
    {
        public StatRow(string key, string fallback, string icon, string negativeIcon, EquipmentStat stat, double magnitude, string unit, bool sign, bool condition)
        {
            Key = key;
            Fallback = fallback;
            Icon = icon;
            NegativeIcon = negativeIcon;
            Stat = stat;
            Magnitude = magnitude;
            Unit = unit;
            Sign = sign;
            Condition = condition;
        }

        public string Key { get; }
        public string Fallback { get; }
        public string Icon { get; }
        public string NegativeIcon { get; }
        public EquipmentStat Stat { get; }
        public double Magnitude { get; }
        public string Unit { get; }
        public bool Sign { get; }
        public bool Condition { get; }

        public bool IsLiteralPercent(string percentUnit) => Unit == percentUnit && Stat != EquipmentStat.Speed;
    }
}

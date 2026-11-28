using XRay.Importer.Levels;

namespace RaidIntoDarkness.Inventory
{
    internal readonly struct EquipmentStatusDetail
    {
        public EquipmentStatusDetail(string id, string key, string title, string description, AnomalyDamageType type)
        {
            Id = id;
            Key = key;
            Title = title;
            Description = description;
            Type = type;
        }

        public string Id { get; }
        public string Key { get; }
        public string Title { get; }
        public string Description { get; }
        public AnomalyDamageType Type { get; }
    }
}

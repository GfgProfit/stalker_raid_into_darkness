namespace RaidIntoDarkness.Player
{
    public interface IStalkerCarryWeightSource
    {
        float TotalWeight { get; }
        float AdditionalWeight { get; }
    }

    public interface IStalkerPowerLossSource
    {
        float? OutfitPowerLoss { get; }
        float? HelmetPowerLoss { get; }
        float? BackpackPowerLoss { get; }
    }

    public interface IStalkerHealthSource
    {
        float Health { get; }
    }

    public interface IStalkerSatietySource
    {
        float Satiety { get; }
    }
}

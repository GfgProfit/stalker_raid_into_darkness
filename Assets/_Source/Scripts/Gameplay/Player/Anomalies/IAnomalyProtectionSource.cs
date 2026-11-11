using XRay.Importer.Levels;

namespace RaidIntoDarkness.Player
{
    public interface IAnomalyProtectionSource
    {
        float GetAnomalyProtection(AnomalyDamageType type, AnomalyStrength strength, AnomalyBalanceConfig config);
    }
}

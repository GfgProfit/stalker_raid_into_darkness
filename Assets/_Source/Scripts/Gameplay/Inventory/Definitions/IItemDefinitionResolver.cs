namespace RaidIntoDarkness.Inventory.Domain
{
    public interface IItemDefinitionResolver
    {
        bool TryResolve(string definitionId, out ItemDefinition definition);
    }
}

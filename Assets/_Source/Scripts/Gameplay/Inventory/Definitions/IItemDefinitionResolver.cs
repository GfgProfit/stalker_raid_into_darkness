namespace RaidIntoDarkness.Inventory
{
    public interface IItemDefinitionResolver
    {
        bool TryResolve(string definitionId, out ItemDefinition definition);
    }
}

namespace RaidIntoDarkness.Inventory.Domain
{
    public readonly struct InventoryResult
    {
        private InventoryResult(bool success, InventoryFailure failure, ItemInstance item, int amount)
        {
            Success = success;
            Failure = failure;
            Item = item;
            Amount = amount;
        }

        public bool Success { get; }
        public InventoryFailure Failure { get; }
        public ItemInstance Item { get; }
        public int Amount { get; }

        public static InventoryResult Ok(ItemInstance item, int amount) => new(true, InventoryFailure.None, item, amount);
        public static InventoryResult Fail(InventoryFailure failure) => new(false, failure, null, 0);

        public static implicit operator bool(InventoryResult result) => result.Success;
        public override string ToString() => Success ? $"Ok({Item}, {Amount})" : $"Fail({Failure})";
    }
}

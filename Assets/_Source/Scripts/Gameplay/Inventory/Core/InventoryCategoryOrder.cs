namespace RaidIntoDarkness.Inventory.Domain
{
    internal static class InventoryCategoryOrder
    {
        public const int WEAPONS_ORDER = 1;
        public const int OUTFITS_ORDER = 2;
        public const int ARTIFACTS_ORDER = 3;
        public const int DEVICES_ORDER = 4;
        public const int TOOLS_ORDER = 5;
        public const int MEDICAL_ORDER = 6;
        public const int FOODS_ORDER = 7;
        public const int QUEST_ORDER = 8;
        public const int AMMO_ORDER = 9;
        public const int MUTANT_PARTS_ORDER = 10;
        public const int MISC_ORDER = 11;
        public const int UPGRADES_ORDER = 12;
        public const int PARTS_ORDER = 13;
        public const int UNKNOWN_ORDER = 42;

        public static int Get(ItemDefinition definition)
        {
            if (definition == null)
            {
                return UNKNOWN_ORDER;
            }

            if (definition.IsCategory("Weapon") || definition.IsCategory("Attachment"))
            {
                return WEAPONS_ORDER;
            }

            if (definition.IsCategory("Armor") || definition.IsCategory("Head") || definition.IsCategory("Backpack"))
            {
                return OUTFITS_ORDER;
            }

            if (definition.IsCategory("Artifact"))
            {
                return ARTIFACTS_ORDER;
            }

            if (definition.IsCategory("Accessory") || definition.IsCategory("Device") || definition.IsCategory("Pda") || definition.IsCategory("Binocular"))
            {
                return DEVICES_ORDER;
            }

            if (definition.IsCategory("Tool"))
            {
                return TOOLS_ORDER;
            }

            if (definition.HasTag("Medical"))
            {
                return MEDICAL_ORDER;
            }

            if (definition.HasTag("Food") || definition.HasTag("Drink"))
            {
                return FOODS_ORDER;
            }

            if (definition.IsCategory("Quest"))
            {
                return QUEST_ORDER;
            }

            if (definition.IsCategory("Ammo"))
            {
                return AMMO_ORDER;
            }

            if (definition.IsCategory("MutantPart"))
            {
                return MUTANT_PARTS_ORDER;
            }

            if (definition.IsCategory("Misc"))
            {
                return MISC_ORDER;
            }

            if (definition.IsCategory("Upgrade"))
            {
                return UPGRADES_ORDER;
            }

            return definition.IsCategory("Part") ? PARTS_ORDER : UNKNOWN_ORDER;
        }
    }
}

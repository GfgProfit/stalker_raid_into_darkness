namespace RaidIntoDarkness.Player
{
    public enum StalkerSurfaceType
    {
        Default,
        Earth,
        Grass,
        Gravel,
        Sand,
        Dirt,
        Asphalt,
        Concrete,
        Bricks,
        Stucco,
        FlooringTile,
        Metal,
        MetalPipe,
        MetalPlate,
        Tin,
        Shifer,
        Wood,
        WoodenBoard,
        TreeTrunk,
        Bush,
        Cloth,
        SetkaRabica,
        Glass,
        Water,
        BushSux
    }

    public static class StalkerSurfaceTypes
    {
        private static readonly string[] MATERIAL_NAMES =
        {
            "default",
            "materials\\earth",
            "materials\\grass",
            "materials\\gravel",
            "materials\\sand",
            "materials\\dirt",
            "materials\\asphalt",
            "materials\\concrete",
            "materials\\bricks",
            "materials\\stucco",
            "materials\\flooring_tile",
            "materials\\metal",
            "materials\\metal_pipe",
            "materials\\metal_plate",
            "materials\\tin",
            "materials\\shifer",
            "materials\\wood",
            "materials\\wooden_board",
            "materials\\tree_trunk",
            "materials\\bush",
            "materials\\cloth",
            "materials\\setka_rabica",
            "materials\\glass",
            "materials\\water",
            "materials\\bush_sux"
        };

        public static int Count => MATERIAL_NAMES.Length;

        public static string GetMaterialName(StalkerSurfaceType type)
        {
            return MATERIAL_NAMES[(int)type];
        }

        public static string GetShortName(StalkerSurfaceType type)
        {
            string name = MATERIAL_NAMES[(int)type];
            int slash = name.LastIndexOf('\\');
            return slash >= 0 ? name.Substring(slash + 1) : name;
        }

        public static bool TryGetType(string materialName, out StalkerSurfaceType type)
        {
            for (int i = 0; i < MATERIAL_NAMES.Length; i++)
            {
                if (string.Equals(MATERIAL_NAMES[i], materialName, System.StringComparison.OrdinalIgnoreCase))
                {
                    type = (StalkerSurfaceType)i;
                    return true;
                }
            }

            type = StalkerSurfaceType.Default;
            return false;
        }
    }
}

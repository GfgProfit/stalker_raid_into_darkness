using RaidIntoDarkness.Integrations.Inventory.Editor;
using UnityEditor;

namespace RaidIntoDarkness.FirstPerson.Editor
{
    public static class BoltAssetSetup
    {
        public const string ROOT = "Assets/_Source/FirstPerson/Bolt";
        public const string DEFINITION_PATH = ROOT + "/BoltDefinition.asset";
        public const string PROJECTILE_PATH = ROOT + "/BoltProjectile.prefab";

        [MenuItem("Tools/X-Ray/Setup Bolt")]
        public static void SetupFromMenu()
        {
            Setup();
        }

        public static bool Setup()
        {
            return BoltGraVisualSetup.Restore();
        }
    }
}

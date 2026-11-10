using UnityOpenMcpBridge;
using XRay.Importer.Editor.Levels;

namespace RaidIntoDarkness.Player.Editor
{
    [BridgeToolType]
    public static class StalkerSurfaceContactsSetup
    {
        [ProjectCommand("project.player.prepare_surface_contacts", Title = "Prepare Surface Contacts", Package = "RaidIntoDarkness.Player.Editor", Description = "Import original material contact audio and attach retained CFORM data to an existing level prefab without changing scenes.", IsMutating = true, IdempotentHint = true, Lifecycle = LifecyclePolicy.EditorSettle)]
        public static string Prepare(string level_prefab_path, string game_materials_path, bool repair_collision = true)
        {
            int triangles = repair_collision ? XRayLevelContactImporter.RepairCollision(level_prefab_path, game_materials_path) : 0;
            bool changed = XRayLevelContactImporter.Install(level_prefab_path, game_materials_path);
            bool imported = StalkerSurfaceAudioBuilder.BuildContactSounds(out string report);
            if (!imported)
            {
                throw new System.InvalidOperationException(report);
            }

            return UnityEngine.JsonUtility.ToJson(new Result { CollisionTriangles = triangles, ContactSourceChanged = changed, Report = report });
        }

        [System.Serializable]
        private sealed class Result
        {
            public int CollisionTriangles;
            public bool ContactSourceChanged;
            public string Report;
        }
    }
}

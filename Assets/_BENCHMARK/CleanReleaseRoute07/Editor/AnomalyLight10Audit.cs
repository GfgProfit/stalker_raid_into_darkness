using System;
using System.Collections.Generic;
using System.IO;
using RaidIntoDarkness.Effects;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Rvm.CleanReleaseRoute07.Editor
{
    public static class AnomalyLight10Audit
    {
        public static void Run()
        {
            string output = Environment.GetEnvironmentVariable("CLEAN_RELEASE_ROUTE07_OUTPUT");
            if (Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd('\\') != @"E:\Projects\stalker_raid_into_darkness")
            {
                throw new InvalidOperationException("Canonical Production is required.");
            }
            var reference = JsonUtility.FromJson<Reference>(File.ReadAllText(Path.Combine(output, "ROUTE_REFERENCE.json")));
            var scene = EditorSceneManager.OpenScene(reference.scene, OpenSceneMode.Single);
            AnomalyVisual08Build.CaptureSource(scene);
            var visuals = new List<AnomalyVisual>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var visual in root.GetComponentsInChildren<AnomalyVisual>(true))
                {
                    if (visual.name.Contains("zone_mine_acidic_strong"))
                    {
                        visuals.Add(visual);
                    }
                }
            }
            if (visuals.Count != 43)
            {
                throw new InvalidOperationException("Expected 43 exact source Gazirovka owners, got " + visuals.Count);
            }
            File.WriteAllText(Path.Combine(output, "LIGHT_SERIALIZED_INVENTORY.json"), AnomalyLight10Inventory.Capture(visuals, null, false));
        }

        [Serializable]
        private sealed class Reference
        {
            public string scene;
        }
    }
}

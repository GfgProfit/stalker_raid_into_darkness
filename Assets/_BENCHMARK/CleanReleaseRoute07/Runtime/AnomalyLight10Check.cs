using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RaidIntoDarkness.Effects;
using UnityEngine;

namespace Rvm.CleanReleaseRoute07
{
    public sealed class AnomalyLight10Check : MonoBehaviour
    {
        public bool Completed { get; private set; }

        public IEnumerator Run(string output, Camera camera)
        {
            var samples = new List<Sample>();
            yield return new WaitForSecondsRealtime(2f);
            var targets = AnomalyLight10Inventory.Targets();
            if (targets.Count != 43)
            {
                throw new InvalidOperationException("Expected exactly 43 real Gazirovka IdleLights.");
            }
            for (int index = 0; index < 4; index++)
            {
                yield return new WaitForEndOfFrame();
                foreach (var visual in targets)
                {
                    var light = (Light)AnomalyLight10Inventory.Field(visual, "_idleIllumination");
                    samples.Add(new Sample { owner = visual.name, mode = "Light10_A", frame = Time.frameCount, time = Time.time, range = light.range, color = light.color });
                }
                yield return null;
            }
            File.WriteAllText(Path.Combine(output, "light-technical-A.json"), AnomalyLight10Inventory.Capture(targets, camera, true));
            File.WriteAllText(Path.Combine(output, "visual-technical-A.json"), AnomalyVisual08Target.Snapshot());
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "Light10_A.png"));
            foreach (var visual in targets)
            {
                visual.ConfigureDiagnosticIdleRange(true);
            }
            for (int index = 0; index < 4; index++)
            {
                yield return null;
                yield return new WaitForEndOfFrame();
                foreach (var visual in targets)
                {
                    var light = (Light)AnomalyLight10Inventory.Field(visual, "_idleIllumination");
                    samples.Add(new Sample { owner = visual.name, mode = "Light10_B", frame = Time.frameCount, time = Time.time, range = light.range, color = light.color });
                    if (light.range != Mathf.Max(0.001f, (float)AnomalyLight10Inventory.Field(visual, "_idleLightRange")) || !light.enabled)
                    {
                        throw new InvalidOperationException("Fixed nominal radius was not applied with Light enabled.");
                    }
                }
            }
            File.WriteAllText(Path.Combine(output, "light-technical-B.json"), AnomalyLight10Inventory.Capture(targets, camera, true));
            File.WriteAllText(Path.Combine(output, "visual-technical-B.json"), AnomalyVisual08Target.Snapshot());
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "Light10_B.png"));
            var randomState = UnityEngine.Random.state;
            var method = typeof(AnomalyVisual).GetMethod("UpdateIdleLight", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var visual in targets)
            {
                var saved = UnityEngine.Random.state;
                visual.ConfigureDiagnosticIdleRange(false);
                method.Invoke(visual, new object[] { Time.time });
                string afterA = JsonUtility.ToJson(UnityEngine.Random.state);
                Color colorA = ((Light)AnomalyLight10Inventory.Field(visual, "_idleIllumination")).color;
                UnityEngine.Random.state = saved;
                visual.ConfigureDiagnosticIdleRange(true);
                method.Invoke(visual, new object[] { Time.time });
                if (afterA != JsonUtility.ToJson(UnityEngine.Random.state) || colorA != ((Light)AnomalyLight10Inventory.Field(visual, "_idleIllumination")).color)
                {
                    throw new InvalidOperationException("A/B changed Random consumption or color evaluation.");
                }
            }
            UnityEngine.Random.state = randomState;
            File.WriteAllText(Path.Combine(output, "LIGHT_TECHNICAL_CHECK.json"), JsonUtility.ToJson(new Report { samples = samples.ToArray(), randomStateEquivalenceOwners = targets.Count, colorEquivalenceOwners = targets.Count, timing = false }, true));
            Completed = true;
        }

        [Serializable]
        private sealed class Report
        {
            public Sample[] samples;
            public int randomStateEquivalenceOwners;
            public int colorEquivalenceOwners;
            public bool timing;
        }

        [Serializable]
        private sealed class Sample
        {
            public string owner;
            public string mode;
            public int frame;
            public float time;
            public float range;
            public Color color;
        }
    }
}

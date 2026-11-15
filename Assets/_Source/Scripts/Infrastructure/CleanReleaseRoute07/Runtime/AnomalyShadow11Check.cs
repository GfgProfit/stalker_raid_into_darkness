using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RaidIntoDarkness.Effects;
using UnityEngine;

namespace Rvm.CleanReleaseRoute07
{
    public sealed class AnomalyShadow11Check : MonoBehaviour
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
            foreach (string mode in new[] { "A", "B" })
            {
                foreach (var visual in targets)
                {
                    visual.ConfigureDiagnosticIdleShadows(mode == "B");
                }
                for (int index = 0; index < 4; index++)
                {
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    foreach (var visual in targets)
                    {
                        var light = (Light)AnomalyLight10Inventory.Field(visual, "_idleIllumination");
                        samples.Add(new Sample { owner = visual.name, mode = "Shadow11_" + mode, frame = Time.frameCount, time = Time.time, range = light.range, color = light.color });
                        if (!light.enabled || (mode == "B" ? light.shadows != LightShadows.None : light.shadows == LightShadows.None) || (bool)AnomalyLight10Inventory.Field(visual, "_diagnosticFixedIdleRange"))
                        {
                            throw new InvalidOperationException("Shadow11 mode or original radius policy is invalid.");
                        }
                    }
                }
                File.WriteAllText(Path.Combine(output, "shadow-technical-" + mode + ".json"), AnomalyShadow11State.Capture(camera));
                File.WriteAllText(Path.Combine(output, "light-technical-" + mode + ".json"), AnomalyLight10Inventory.Capture(targets, camera, true));
                File.WriteAllText(Path.Combine(output, "visual-technical-" + mode + ".json"), AnomalyVisual08Target.Snapshot());
                ScreenCapture.CaptureScreenshot(Path.Combine(output, "Shadow11_" + mode + ".png"));
            }
            var randomState = UnityEngine.Random.state;
            var method = typeof(AnomalyVisual).GetMethod("UpdateIdleLight", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var visual in targets)
            {
                var light = (Light)AnomalyLight10Inventory.Field(visual, "_idleIllumination");
                var saved = UnityEngine.Random.state;
                light.shadows = LightShadows.Hard;
                method.Invoke(visual, new object[] { Time.time });
                string afterA = JsonUtility.ToJson(UnityEngine.Random.state);
                Color colorA = light.color;
                float rangeA = light.range;
                UnityEngine.Random.state = saved;
                visual.ConfigureDiagnosticIdleShadows(true);
                method.Invoke(visual, new object[] { Time.time });
                if (afterA != JsonUtility.ToJson(UnityEngine.Random.state) || colorA != light.color || rangeA != light.range)
                {
                    throw new InvalidOperationException("Shadow switch changed Random consumption, radius or color evaluation.");
                }
            }
            UnityEngine.Random.state = randomState;
            File.WriteAllText(Path.Combine(output, "SHADOW_TECHNICAL_CHECK.json"), JsonUtility.ToJson(new Report { samples = samples.ToArray(), equalRandomRadiusColorOwners = targets.Count, timing = false }, true));
            Completed = true;
        }

        [Serializable]
        private sealed class Report
        {
            public Sample[] samples;
            public int equalRandomRadiusColorOwners;
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

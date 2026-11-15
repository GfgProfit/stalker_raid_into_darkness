#if UNITY_EDITOR || CLEAN_RELEASE_ROUTE07
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using RaidIntoDarkness.Effects;
using RaidIntoDarkness.Player;
using UnityEngine;

namespace Rvm.CleanReleaseRoute07
{
    public sealed class AnomalyVisual08Check : MonoBehaviour
    {
        public bool Completed { get; private set; }

        public IEnumerator Run(string output, Camera camera, GameObject kiselPrefab)
        {
            yield return new WaitForSecondsRealtime(2f);
            yield return new WaitForEndOfFrame();
            File.WriteAllText(Path.Combine(output, "visual-simulation-before.json"), AnomalyVisual08Target.Snapshot());
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "POS1.png"));
            yield return new WaitForSecondsRealtime(2f);
            yield return new WaitForEndOfFrame();
            File.WriteAllText(Path.Combine(output, "visual-simulation-after.json"), AnomalyVisual08Target.Snapshot());
            var results = new List<ProbeResult>();
            foreach (string family in new[] { "Gazirovka" })
            {
                AnomalyVisual08Target selected = null;
                foreach (var target in AnomalyVisual08Target.Targets)
                {
                    var candidate = target != null ? target.GetComponent<AnomalyPlayerReaction>() : null;
                    if (target != null && target.Family == family && candidate != null && candidate.IsReady)
                    {
                        selected = target;
                        break;
                    }
                }
                if (selected == null)
                {
                    throw new InvalidOperationException("No ready target for real physics probe: " + family);
                }
                var reaction = selected.GetComponent<AnomalyPlayerReaction>();
                var visual = selected.GetComponent<AnomalyVisual>();
                var result = new ProbeResult { family = family, source = selected.name, influencing = reaction.IsInfluencing, triggerEnabled = selected.GetComponent<Collider>().enabled, influenceProfile = reaction.InfluenceProfile.name };
                Action callback = () => result.dischargeEvents++;
                visual.DischargeStarted += callback;
                camera.transform.position = selected.transform.position + new Vector3(0f, 2.5f, -6f);
                camera.transform.LookAt(selected.transform.position + Vector3.up * 0.5f);
                yield return new WaitForSecondsRealtime(0.3f);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(output, family + "_idle.png"));
                var probe = new GameObject("AnomalyVisual08 Technical Probe");
                probe.transform.position = selected.GetComponent<Collider>().bounds.center;
                probe.AddComponent<SphereCollider>().radius = 0.05f;
                probe.AddComponent<Rigidbody>().isKinematic = true;
                probe.AddComponent<AnomalyProbe>();
                float end = Time.unscaledTime + 2.0f;
                while (Time.unscaledTime < end)
                {
                    result.nonIdleObserved |= reaction.Phase != AnomalyPhase.Idle;
                    yield return null;
                }
                visual.DischargeStarted -= callback;
                Destroy(probe);
                results.Add(result);
                yield return new WaitForEndOfFrame();
                File.WriteAllText(Path.Combine(output, family + "_after-probe.json"), AnomalyVisual08Target.Snapshot());
            }
            File.WriteAllText(Path.Combine(output, "gameplay-probes.json"), JsonUtility.ToJson(new ProbeReport { probes = results.ToArray() }, true));
            Completed = true;
        }

        [Serializable]
        private sealed class ProbeReport
        {
            public ProbeResult[] probes;
        }

        [Serializable]
        private sealed class ProbeResult
        {
            public string family;
            public string source;
            public bool influencing;
            public bool triggerEnabled;
            public string influenceProfile;
            public int dischargeEvents;
            public bool nonIdleObserved;
        }
    }
}
#endif

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Rvm.CleanReleaseRoute07.Editor
{
    public static class AnomalyVisual09Audit
    {
        private static readonly List<ParticleRecord> PARTICLES = new List<ParticleRecord>();
        private static readonly List<FieldRecord> FIELDS = new List<FieldRecord>();

        public static void Reset()
        {
            PARTICLES.Clear();
            FIELDS.Clear();
        }

        public static void Inspect(GameObject root)
        {
            Inspect(root, root.name, new HashSet<GameObject>());
        }

        private static void Inspect(GameObject root, string owner, HashSet<GameObject> visited)
        {
            if (root == null || !visited.Add(root))
            {
                return;
            }
            foreach (var system in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                var emission = system.emission;
                var bursts = new ParticleSystem.Burst[emission.burstCount];
                emission.GetBursts(bursts);
                var recorded = new List<BurstRecord>();
                foreach (var burst in bursts)
                {
                    recorded.Add(new BurstRecord { time = burst.time, count = Curve(burst.count), cycles = burst.cycleCount, repeatInterval = burst.repeatInterval, probability = burst.probability });
                }
                PARTICLES.Add(new ParticleRecord { owner = owner, asset = AssetDatabase.GetAssetPath(root), path = Hierarchy(system.transform), duration = main.duration, loop = main.loop, startDelay = Curve(main.startDelay), startLifetime = Curve(main.startLifetime), startSpeed = Curve(main.startSpeed), playOnAwake = main.playOnAwake, simulationSpeed = main.simulationSpeed, useUnscaledTime = main.useUnscaledTime, autoRandomSeed = system.useAutoRandomSeed, randomSeed = system.randomSeed, emissionEnabled = emission.enabled, rateOverTime = Curve(emission.rateOverTime), rateOverDistance = Curve(emission.rateOverDistance), bursts = recorded.ToArray(), subEmitters = system.subEmitters.subEmittersCount });
            }
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null)
                {
                    continue;
                }
                var data = new SerializedObject(component);
                var property = data.GetIterator();
                while (property.NextVisible(true))
                {
                    string value = null;
                    if (property.propertyType == SerializedPropertyType.Float)
                    {
                        value = property.floatValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                    }
                    if (property.propertyType == SerializedPropertyType.Integer)
                    {
                        value = property.longValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    if (property.propertyType == SerializedPropertyType.Boolean)
                    {
                        value = property.boolValue.ToString();
                    }
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null)
                    {
                        value = AssetDatabase.GetAssetPath(property.objectReferenceValue) + "|" + property.objectReferenceValue.name;
                    }
                    if (value != null && Relevant(property.propertyPath))
                    {
                        FIELDS.Add(new FieldRecord { owner = owner, asset = AssetDatabase.GetAssetPath(root), path = Hierarchy(component.transform), component = component.GetType().FullName, field = property.propertyPath, value = value });
                    }
                    if (property.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        GameObject linked = property.objectReferenceValue as GameObject;
                        if (property.objectReferenceValue is ParticleSystem child)
                        {
                            linked = child.gameObject;
                        }
                        if (linked != null && !linked.transform.IsChildOf(root.transform))
                        {
                            Inspect(linked, owner, visited);
                        }
                    }
                }
            }
        }

        public static void Save(string output)
        {
            File.WriteAllText(Path.Combine(output, "RANDOM_DELAY_PARAMETERS.json"), JsonUtility.ToJson(new Audit { particles = PARTICLES.ToArray(), fields = FIELDS.ToArray() }, true));
        }

        private static bool Relevant(string path)
        {
            foreach (string field in new[] { "_randomStart", "_randomPlayback", "_initialAge", "_sources", "_dischargeEffectDelay", "_dischargeLightDelay", "_dischargeLightDuration", "_groupDuration", "_stopTimes", "_stopEffectIndices", "_rootIndex", "_dischargeRootIndex", "_framesPerSecond", "_frameCount", "_links", "_template", "_playIdleChildren", "_keepIdleDuringDischarge" })
            {
                if (path.IndexOf(field, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static string Hierarchy(Transform value)
        {
            string path = value.name;
            while (value.parent != null)
            {
                value = value.parent;
                path = value.name + "/" + path;
            }
            return path;
        }

        private static CurveRecord Curve(ParticleSystem.MinMaxCurve curve)
        {
            return new CurveRecord { mode = curve.mode.ToString(), minimum = curve.constantMin, maximum = curve.constantMax, multiplier = curve.curveMultiplier, minimumKeys = curve.curveMin != null ? curve.curveMin.keys : Array.Empty<Keyframe>(), maximumKeys = curve.curveMax != null ? curve.curveMax.keys : Array.Empty<Keyframe>() };
        }

        [Serializable]
        private sealed class Audit
        {
            public ParticleRecord[] particles;
            public FieldRecord[] fields;
        }

        [Serializable]
        private sealed class FieldRecord
        {
            public string owner;
            public string asset;
            public string path;
            public string component;
            public string field;
            public string value;
        }

        [Serializable]
        private sealed class ParticleRecord
        {
            public string owner;
            public string asset;
            public string path;
            public float duration;
            public bool loop;
            public CurveRecord startDelay;
            public CurveRecord startLifetime;
            public CurveRecord startSpeed;
            public bool playOnAwake;
            public float simulationSpeed;
            public bool useUnscaledTime;
            public bool autoRandomSeed;
            public uint randomSeed;
            public bool emissionEnabled;
            public CurveRecord rateOverTime;
            public CurveRecord rateOverDistance;
            public BurstRecord[] bursts;
            public int subEmitters;
        }

        [Serializable]
        private sealed class CurveRecord
        {
            public string mode;
            public float minimum;
            public float maximum;
            public float multiplier;
            public Keyframe[] minimumKeys;
            public Keyframe[] maximumKeys;
        }

        [Serializable]
        private sealed class BurstRecord
        {
            public float time;
            public CurveRecord count;
            public int cycles;
            public float repeatInterval;
            public float probability;
        }
    }
}

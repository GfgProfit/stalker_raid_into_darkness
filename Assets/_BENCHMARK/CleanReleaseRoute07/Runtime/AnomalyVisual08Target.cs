using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    [DefaultExecutionOrder(9000)]
    public sealed class AnomalyVisual08Target : MonoBehaviour
    {
        [SerializeField] private string _family;
        [SerializeField] private string _prefab;

        private Light[] _lights;
        private static readonly List<AnomalyVisual08Target> TARGETS = new List<AnomalyVisual08Target>();

        public static bool Active { get; private set; }
        public static string Mode { get; private set; }
        public static bool ParticlesOff => Mode == "P" || Mode == "B";
        public static bool LightsOff => Mode == "L" || Mode == "B";
        public static IReadOnlyList<AnomalyVisual08Target> Targets => TARGETS;
        public string Family => _family;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            TARGETS.Clear();
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-anomaly-visual");
            Active = index >= 0;
            if (Active)
            {
                throw new NotSupportedException("Legacy Visual08, Light10 and Shadow11 modes depended on retired gameplay hooks. Historical results remain in Archive. Use the location preparation route for this campaign.");
            }
            if (Active && (index + 1 >= args.Length || Array.IndexOf(new[] { "A", "N", "P", "L", "B", "Light10_A", "Light10_B", "Shadow11_A", "Shadow11_B" }, args[index + 1]) < 0))
            {
                throw new ArgumentException("-anomaly-visual requires N, P, L or B.");
            }
            Mode = Active ? args[index + 1] : "N";
            Mode = Mode == "A" ? "N" : Mode;
            if ((Mode.StartsWith("Light10_", StringComparison.Ordinal) || Mode.StartsWith("Shadow11_", StringComparison.Ordinal)) && Array.IndexOf(args, "-anomaly-events") >= 0)
            {
                throw new ArgumentException("AnomalyLight10 forbids the event journal.");
            }
            if (Array.IndexOf(args, "-anomaly-events") >= 0)
            {
                throw new NotSupportedException("Visual09 low-level event hooks were retired from production. Historical journals remain in Archive.");
            }
        }

        public void Configure(string family, string prefab)
        {
            _family = family;
            _prefab = prefab;
        }

        private void Awake()
        {
            if (!Active)
            {
                enabled = false;
                return;
            }
            TARGETS.Add(this);
            if (Mode.StartsWith("Shadow11_", StringComparison.Ordinal) && TryGetComponent<AnomalyVisual>(out var shadowVisual))
            {
                if (Mode == "Shadow11_B")
                {
                    ((Light)typeof(AnomalyVisual).GetField("_idleIllumination", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(shadowVisual)).shadows = LightShadows.None;
                }
            }
            if (Mode.StartsWith("Light10_", StringComparison.Ordinal) && TryGetComponent<AnomalyVisual>(out var visual))
            {
                if (Mode == "Light10_B")
                {
                    typeof(AnomalyVisual).GetField("_idleLightRangeJitter", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(visual, 0f);
                }
            }
            AnomalyVisual09Events.RegisterTarget(this);
            foreach (var system in GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                if (LightsOff)
                {
                    var lights = system.lights;
                    lights.enabled = false;
                }
            }
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (ParticlesOff)
                {
                    renderer.forceRenderingOff = true;
                }
            }
            _lights = GetComponentsInChildren<Light>(true);
            LateUpdate();
        }

        private void LateUpdate()
        {
            if (LightsOff && _lights != null)
            {
                foreach (var light in _lights)
                {
                    if (light != null)
                    {
                        light.enabled = false;
                    }
                }
            }
        }

        private void OnDestroy()
        {
            TARGETS.Remove(this);
        }

        public static void ApplySpawned(Component owner, GameObject instance)
        {
            if (!Active)
            {
                return;
            }
            var source = owner.GetComponentInParent<AnomalyVisual08Target>();
            if (source == null)
            {
                return;
            }
            AnomalyVisual09Events.RegisterClone(owner, instance);
            var target = instance.GetComponent<AnomalyVisual08Target>();
            if (target == null)
            {
                target = instance.AddComponent<AnomalyVisual08Target>();
            }
            target.Configure(source._family, source._prefab);
        }

        public static string Snapshot()
        {
            var records = new List<Record>();
            var seen = new HashSet<ParticleSystem>();
            foreach (var target in TARGETS)
            {
                if (target == null)
                {
                    continue;
                }
                foreach (var system in target.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (!seen.Add(system))
                    {
                        continue;
                    }
                    var particles = new ParticleSystem.Particle[Mathf.Max(1, system.particleCount)];
                    int count = system.GetParticles(particles);
                    var custom = new List<Vector4>();
                    system.GetCustomParticleData(custom, ParticleSystemCustomData.Custom1);
                    var renderer = system.GetComponent<ParticleSystemRenderer>();
                    records.Add(new Record { family = target._family, prefab = target._prefab, path = system.name, instance = system.GetEntityId().ToString(), active = system.gameObject.activeInHierarchy, playing = system.isPlaying, count = count, time = system.time, age = count > 0 ? particles[0].startLifetime - particles[0].remainingLifetime : 0f, seed = count > 0 ? particles[0].randomSeed : 0, position = count > 0 ? particles[0].position : Vector3.zero, customCount = custom.Count, customFirst = custom.Count > 0 ? custom[0] : Vector4.zero, culling = system.main.cullingMode.ToString(), rendererEnabled = renderer != null && renderer.enabled, forceRenderingOff = renderer != null && renderer.forceRenderingOff, particleLights = system.lights.enabled });
                }
            }
            var lightRecords = new List<LightRecord>();
            var lightsSeen = new HashSet<Light>();
            foreach (var target in TARGETS)
            {
                if (target == null || target._lights == null)
                {
                    continue;
                }
                foreach (var light in target._lights)
                {
                    if (light != null && lightsSeen.Add(light))
                    {
                        lightRecords.Add(new LightRecord { family = target._family, instance = light.GetEntityId().ToString(), name = light.name, enabled = light.enabled, intensity = light.intensity });
                    }
                }
            }
            return JsonUtility.ToJson(new Report { mode = Mode, systems = records.ToArray(), lights = lightRecords.ToArray() }, true);
        }

        [Serializable]
        private sealed class Report
        {
            public string mode;
            public Record[] systems;
            public LightRecord[] lights;
        }

        [Serializable]
        private sealed class Record
        {
            public string family;
            public string prefab;
            public string path;
            public string instance;
            public bool active;
            public bool playing;
            public int count;
            public float time;
            public float age;
            public uint seed;
            public Vector3 position;
            public int customCount;
            public Vector4 customFirst;
            public string culling;
            public bool rendererEnabled;
            public bool forceRenderingOff;
            public bool particleLights;
        }

        [Serializable]
        private sealed class LightRecord
        {
            public string family;
            public string instance;
            public string name;
            public bool enabled;
            public float intensity;
        }
    }
}

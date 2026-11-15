using System;
using System.Reflection;
using RaidIntoDarkness.Effects;
using UnityEngine;

namespace Rvm.Benchmark
{
    [DefaultExecutionOrder(10000)]
    public sealed class ParticleFrameDiagnosticObserver : MonoBehaviour
    {
        private static readonly FieldInfo PARTICLES = typeof(AnomalyParticleFrames).GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic);

        private double _next;

        public static bool Active { get; set; }
        public static event Action<AnomalyParticleFrames, ParticleSystem.Particle[], int> ParticlesObserved;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Active)
            {
                new GameObject("Benchmark Particle Observer").AddComponent<ParticleFrameDiagnosticObserver>();
            }
        }

        private void LateUpdate()
        {
            if (!Active || Time.realtimeSinceStartupAsDouble < _next)
            {
                return;
            }
            _next = Time.realtimeSinceStartupAsDouble + 1;
            foreach (var component in FindObjectsByType<AnomalyParticleFrames>(FindObjectsSortMode.None))
            {
                var particles = (ParticleSystem.Particle[])PARTICLES.GetValue(component);
                var system = component.GetComponent<ParticleSystem>();
                if (particles != null && system != null)
                {
                    ParticlesObserved?.Invoke(component, particles, Mathf.Min(particles.Length, system.particleCount));
                }
            }
        }
    }
}

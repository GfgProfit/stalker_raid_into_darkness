using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RaidIntoDarkness.Inventory.Infrastructure.IconGeneration
{
    internal sealed class ItemIconSceneIsolationScope
    {
        private readonly Light[] _lights;
        private readonly bool[] _lightEnabledStates;
        private readonly Volume[] _volumes;
        private readonly bool[] _volumeEnabledStates;
        private readonly ReflectionProbe[] _reflectionProbes;
        private readonly bool[] _reflectionProbeEnabledStates;
        private readonly List<Renderer> _suppressedRenderers = new();
        private bool _lightsDisabled;
        private bool _applied;

        public ItemIconSceneIsolationScope()
        {
            _lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include);
            _lightEnabledStates = new bool[_lights.Length];
            _volumes = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include);
            _volumeEnabledStates = new bool[_volumes.Length];
            _reflectionProbes = Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include);
            _reflectionProbeEnabledStates = new bool[_reflectionProbes.Length];
        }

        public bool IsApplied => _applied;

        public void Apply(bool disableSceneLights, Transform itemRoot, int renderLayer)
        {
            if (_applied)
            {
                return;
            }

            _applied = true;
            _lightsDisabled = disableSceneLights;

            if (_lightsDisabled)
            {
                DisableBehaviours(_lights, _lightEnabledStates);
            }

            DisableBehaviours(_volumes, _volumeEnabledStates);
            DisableBehaviours(_reflectionProbes, _reflectionProbeEnabledStates);

            foreach (Renderer renderer in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (renderer == null || renderer.forceRenderingOff || renderer.enabled == false)
                {
                    continue;
                }

                GameObject owner = renderer.gameObject;
                if (owner.layer != renderLayer || owner.activeInHierarchy == false || renderer.transform.IsChildOf(itemRoot))
                {
                    continue;
                }

                _suppressedRenderers.Add(renderer);
                renderer.forceRenderingOff = true;
            }
        }

        public void Restore()
        {
            if (_applied == false)
            {
                return;
            }

            foreach (Renderer renderer in _suppressedRenderers)
            {
                if (renderer != null)
                {
                    renderer.forceRenderingOff = false;
                }
            }

            _suppressedRenderers.Clear();
            RestoreBehaviours(_reflectionProbes, _reflectionProbeEnabledStates);
            RestoreBehaviours(_volumes, _volumeEnabledStates);

            if (_lightsDisabled)
            {
                RestoreBehaviours(_lights, _lightEnabledStates);
            }

            _lightsDisabled = false;
            _applied = false;
        }

        private static void DisableBehaviours<T>(T[] behaviours, bool[] enabledStates) where T : Behaviour
        {
            int count = Mathf.Min(behaviours.Length, enabledStates.Length);

            for (int i = 0; i < count; i++)
            {
                T behaviour = behaviours[i];

                if (behaviour == null)
                {
                    continue;
                }

                enabledStates[i] = behaviour.enabled;
                behaviour.enabled = false;
            }
        }

        private static void RestoreBehaviours<T>(T[] behaviours, bool[] enabledStates) where T : Behaviour
        {
            int count = Mathf.Min(behaviours.Length, enabledStates.Length);

            for (int i = 0; i < count; i++)
            {
                T behaviour = behaviours[i];

                if (behaviour != null)
                {
                    behaviour.enabled = enabledStates[i];
                }
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Inventory.IconGeneration
{
    internal static class ItemIconSceneBuilder
    {
        public static GameObject InstantiateSource(GameObject source, Transform parent)
        {
            if (source == null)
            {
                return null;
            }

            GameObject instance = Object.Instantiate(source, parent);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            return instance;
        }

        public static bool TryCalculateBounds(GameObject rootObject, out Bounds bounds)
        {
            Renderer[] renderers = rootObject.GetComponentsInChildren<Renderer>();
            return TryCalculateBounds(renderers, rootObject.transform.position, out bounds);
        }

        public static bool TryCalculateBounds(IReadOnlyList<Renderer> renderers, Vector3 fallbackPosition, out Bounds bounds)
        {
            bounds = new Bounds(fallbackPosition, Vector3.zero);
            bool hasBounds = false;

            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];

                if (renderer == null || renderer.enabled == false || renderer.gameObject.activeInHierarchy == false)
                {
                    continue;
                }

                if (hasBounds == false)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }

        public static void SetLayerRecursively(GameObject rootObject, int layer)
        {
            Transform[] transforms = rootObject.GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < transforms.Length; i++)
            {
                transforms[i].gameObject.layer = layer;
            }
        }

        public static void SetRendererIsolation(GameObject rootObject, ItemIconGeneratorSettings settings)
        {
            Renderer[] renderers = rootObject.GetComponentsInChildren<Renderer>(true);
            SetRendererIsolation(renderers, settings);
        }

        public static void SetRendererIsolation(IReadOnlyList<Renderer> renderers, ItemIconGeneratorSettings settings)
        {
            if (renderers == null)
            {
                return;
            }

            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                renderer.renderingLayerMask = settings.RenderingLayerMask;
                renderer.lightProbeUsage = settings.RendererLightProbeUsage;
                renderer.reflectionProbeUsage = settings.RendererReflectionProbeUsage;
                renderer.receiveShadows = settings.RendererReceiveShadows;
            }
        }

        public static void DisableLocalEnvironment(GameObject rootObject)
        {
            if (rootObject == null)
            {
                return;
            }

            DisableBehaviours(rootObject.GetComponentsInChildren<Light>(true));
            DisableBehaviours(rootObject.GetComponentsInChildren<Volume>(true));
            DisableBehaviours(rootObject.GetComponentsInChildren<ReflectionProbe>(true));
        }

        private static void DisableBehaviours<T>(IReadOnlyList<T> behaviours) where T : Behaviour
        {
            for (int i = 0; i < behaviours.Count; i++)
            {
                if (behaviours[i] != null)
                {
                    behaviours[i].enabled = false;
                }
            }
        }
    }

    internal sealed class ItemIconSceneIsolationScope
    {
        private readonly Light[] _lights;
        private readonly bool[] _lightEnabledStates;
        private readonly Volume[] _volumes;
        private readonly bool[] _volumeEnabledStates;
        private readonly ReflectionProbe[] _reflectionProbes;
        private readonly bool[] _reflectionProbeEnabledStates;
        private bool _lightsDisabled;
        private bool _applied;

        public ItemIconSceneIsolationScope()
        {
            _lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            _lightEnabledStates = new bool[_lights.Length];
            _volumes = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            _volumeEnabledStates = new bool[_volumes.Length];
            _reflectionProbes = Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            _reflectionProbeEnabledStates = new bool[_reflectionProbes.Length];
        }

        public bool IsApplied => _applied;

        public void Apply(bool disableSceneLights)
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
        }

        public void Restore()
        {
            if (_applied == false)
            {
                return;
            }

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

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace RaidIntoDarkness.Weapons
{
    public sealed class FirstPersonWeaponGraph
    {
        private const int BASE_LAYER = 0;
        private const int OVERLAY_LAYER = 1;
        private const float OVERLAY_FADE = 0.15f;

        private readonly PlayableGraph _graph;
        private readonly AnimationLayerMixerPlayable _handsLayers;
        private readonly Channel _hands;
        private readonly Channel _weapon;
        private AnimationClipPlayable _overlay;
        private float _overlayWeight;
        private bool _overlayActive;

        public FirstPersonWeaponGraph(string name, Animator hands, Animator weapon, AvatarMask overlayMask)
        {
            _graph = PlayableGraph.Create(name);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _handsLayers = AnimationLayerMixerPlayable.Create(_graph, 2);
            var handsOutput = AnimationPlayableOutput.Create(_graph, "Hands", hands);
            handsOutput.SetSourcePlayable(_handsLayers);
            _hands = new Channel(_graph);
            _graph.Connect(_hands.Mixer, 0, _handsLayers, BASE_LAYER);
            _handsLayers.SetInputWeight(BASE_LAYER, 1.0f);
            _handsLayers.SetInputWeight(OVERLAY_LAYER, 0.0f);
            if (overlayMask != null)
            {
                _handsLayers.SetLayerMaskFromAvatarMask(OVERLAY_LAYER, overlayMask);
            }

            if (weapon != null)
            {
                _weapon = new Channel(_graph);
                var weaponOutput = AnimationPlayableOutput.Create(_graph, "Weapon", weapon);
                weaponOutput.SetSourcePlayable(_weapon.Mixer);
            }

            _graph.Play();
        }

        public bool IsValid => _graph.IsValid();
        public bool OverlayActive => _overlayActive;

        public void Play(AnimationClip hands, AnimationClip weapon, float speed, float fade, bool loop)
        {
            _hands.Play(hands, speed, fade, loop);
            if (_weapon != null)
            {
                _weapon.Play(weapon, speed, fade, loop);
            }
        }

        public bool PlayOverlay(AnimationClip clip, float speed)
        {
            if (clip == null)
            {
                return false;
            }

            DestroyOverlay();
            _overlay = AnimationClipPlayable.Create(_graph, clip);
            _overlay.SetApplyFootIK(false);
            _overlay.SetTime(0.0);
            _overlay.SetSpeed(speed);
            _graph.Connect(_overlay, 0, _handsLayers, OVERLAY_LAYER);
            _overlayActive = true;
            return true;
        }

        public void StopOverlay()
        {
            _overlayActive = false;
        }

        public void Evaluate(float deltaTime)
        {
            _hands.Tick(deltaTime);
            if (_weapon != null)
            {
                _weapon.Tick(deltaTime);
            }

            float target = _overlayActive ? 1.0f : 0.0f;
            _overlayWeight = Mathf.MoveTowards(_overlayWeight, target, deltaTime / OVERLAY_FADE);
            _handsLayers.SetInputWeight(OVERLAY_LAYER, _overlay.IsValid() ? _overlayWeight : 0.0f);
            if (!_overlayActive && _overlayWeight <= 0.0f)
            {
                DestroyOverlay();
            }

            _graph.Evaluate(deltaTime);
        }

        public void Destroy()
        {
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }
        }

        public static AvatarMask CreateMask(Transform root, IReadOnlyList<string> bones)
        {
            if (root == null || bones == null || bones.Count == 0)
            {
                return null;
            }

            var active = new HashSet<string>(bones);
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            var mask = new AvatarMask { transformCount = transforms.Length };
            for (int i = 0; i < transforms.Length; i++)
            {
                mask.SetTransformPath(i, GetPath(root, transforms[i]));
                mask.SetTransformActive(i, IsMasked(root, transforms[i], active));
            }

            return mask;
        }

        private static bool IsMasked(Transform root, Transform target, HashSet<string> bones)
        {
            for (Transform current = target; current != null && current != root; current = current.parent)
            {
                if (bones.Contains(current.name))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetPath(Transform root, Transform target)
        {
            if (target == root)
            {
                return string.Empty;
            }

            string path = target.name;
            for (Transform parent = target.parent; parent != null && parent != root; parent = parent.parent)
            {
                path = parent.name + "/" + path;
            }

            return path;
        }

        private void DestroyOverlay()
        {
            if (!_overlay.IsValid())
            {
                return;
            }

            _handsLayers.DisconnectInput(OVERLAY_LAYER);
            _overlay.Destroy();
            _overlay = default;
            _overlayWeight = 0.0f;
        }

        private sealed class Channel
        {
            private readonly PlayableGraph _graph;
            private AnimationClipPlayable _current;
            private AnimationClipPlayable _previous;
            private float _fade;
            private float _fadeElapsed;
            private bool _loop;

            public Channel(PlayableGraph graph)
            {
                _graph = graph;
                Mixer = AnimationMixerPlayable.Create(graph, 2);
            }

            public AnimationMixerPlayable Mixer { get; }

            public void Play(AnimationClip clip, float speed, float fade, bool loop)
            {
                if (clip == null)
                {
                    return;
                }

                if (_previous.IsValid())
                {
                    Mixer.DisconnectInput(1);
                    _previous.Destroy();
                }

                _previous = default;
                if (_current.IsValid())
                {
                    Mixer.DisconnectInput(0);
                    _previous = _current;
                    _graph.Connect(_previous, 0, Mixer, 1);
                }

                _current = AnimationClipPlayable.Create(_graph, clip);
                _current.SetApplyFootIK(false);
                _current.SetTime(0.0);
                _current.SetSpeed(speed);
                _graph.Connect(_current, 0, Mixer, 0);
                _loop = loop;
                _fade = _previous.IsValid() ? fade : 0.0f;
                _fadeElapsed = 0.0f;
                ApplyWeights();
            }

            public void Tick(float deltaTime)
            {
                if (_loop && _current.IsValid())
                {
                    double length = _current.GetAnimationClip().length;
                    double time = _current.GetTime();
                    if (length > 0.0 && time >= length)
                    {
                        _current.SetTime(time % length);
                    }
                }

                _fadeElapsed += deltaTime;
                ApplyWeights();
                if (_previous.IsValid() && _fadeElapsed >= _fade)
                {
                    Mixer.DisconnectInput(1);
                    _previous.Destroy();
                    _previous = default;
                }
            }

            private void ApplyWeights()
            {
                float weight = _fade > 0.0f ? Mathf.Clamp01(_fadeElapsed / _fade) : 1.0f;
                Mixer.SetInputWeight(0, _previous.IsValid() ? weight : 1.0f);
                Mixer.SetInputWeight(1, _previous.IsValid() ? 1.0f - weight : 0.0f);
            }
        }
    }
}

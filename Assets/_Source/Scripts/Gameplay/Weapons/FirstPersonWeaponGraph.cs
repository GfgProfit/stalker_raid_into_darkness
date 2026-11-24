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
        private readonly AnimationMixerPlayable _handsMotion;
        private readonly Channel _hands;
        private readonly Channel _weapon;
        private readonly FirstPersonWeaponView _weaponView;
        private AnimationClipPlayable _emptyShot;
        private float _emptyShotTime;
        private float _emptyShotDuration;
        private float _emptyShotStrength;
        private float _emptyShotFade;
        private AnimationClipPlayable _overlay;
        private float _overlayWeight;
        private bool _overlayActive;
        private bool _magazineSwapActive;
        private bool _magazineSwapCompleted;

        public FirstPersonWeaponGraph(string name, Animator hands, Animator weapon, AvatarMask overlayMask, FirstPersonWeaponView weaponView = null)
        {
            _weaponView = weaponView;
            if (_weaponView != null)
            {
                _weaponView.SetMagazineSwapObjectsVisible(false);
            }

            _graph = PlayableGraph.Create(name);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _handsLayers = AnimationLayerMixerPlayable.Create(_graph, 2);
            var handsOutput = AnimationPlayableOutput.Create(_graph, "Hands", hands);
            handsOutput.SetSourcePlayable(_handsLayers);
            _hands = new Channel(_graph);
            _handsMotion = AnimationMixerPlayable.Create(_graph, 2);
            _graph.Connect(_hands.Mixer, 0, _handsMotion, 0);
            _handsMotion.SetInputWeight(0, 1f);
            _graph.Connect(_handsMotion, 0, _handsLayers, BASE_LAYER);
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
            _weaponView?.ApplyLauncherBoneOverrides();
        }

        public bool IsValid => _graph.IsValid();
        public bool OverlayActive => _overlayActive;
        internal double HandsClipTime => _hands.ClipTime;

        public void Play(AnimationClip hands, AnimationClip weapon, float speed, float fade, bool loop, float weaponFade = -1f, float weaponSpeed = -1f)
        {
            StopEmptyShot();
            _hands.Play(hands, speed, fade, loop);
            PlayWeapon(weapon, weaponSpeed > 0f ? weaponSpeed : speed, weaponFade >= 0f ? weaponFade : fade, loop);
        }

        public void PlayEmptyShot(AnimationClip hands, float speed, float fade, float strength)
        {
            StopEmptyShot();

            if (hands == null || strength <= 0f)
            {
                return;
            }

            _emptyShot = AnimationClipPlayable.Create(_graph, hands);
            _emptyShot.SetApplyFootIK(false);
            _emptyShot.SetTime(0.0);
            _emptyShot.SetSpeed(speed);
            _graph.Connect(_emptyShot, 0, _handsMotion, 1);
            _emptyShotTime = 0f;
            _emptyShotDuration = hands.length / Mathf.Max(0.01f, speed);
            _emptyShotStrength = Mathf.Clamp01(strength);
            _emptyShotFade = Mathf.Clamp(fade, 0f, _emptyShotDuration * 0.5f);
        }

        public void PlayWeapon(AnimationClip weapon, float speed, float fade, bool loop)
        {
            if (_weapon == null || weapon == null)
            {
                return;
            }

            bool magazineSwap = _weaponView != null && _weaponView.UsesMagazineSwap(weapon);
            _weapon.Play(weapon, speed, _magazineSwapActive || magazineSwap ? 0f : fade, loop);
            bool completed = _magazineSwapCompleted;
            if (completed)
            {
                _magazineSwapCompleted = false;
                _weaponView.SwapMagazineRoles();
            }

            if (_magazineSwapActive != magazineSwap || completed)
            {
                _magazineSwapActive = magazineSwap;
                _weaponView.SetMagazineSwapObjectsVisible(magazineSwap);
            }
        }

        internal void CompleteMagazineSwap()
        {
            _magazineSwapCompleted = _magazineSwapActive;
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

            if (_emptyShot.IsValid())
            {
                _emptyShotTime += deltaTime;
                float envelope = _emptyShotFade > 0f ? Mathf.Clamp01(Mathf.Min(_emptyShotTime, _emptyShotDuration - _emptyShotTime) / _emptyShotFade) : 1f;
                float weight = _emptyShotStrength * envelope;
                _handsMotion.SetInputWeight(0, 1f - weight);
                _handsMotion.SetInputWeight(1, weight);

                if (_emptyShotTime >= _emptyShotDuration)
                {
                    StopEmptyShot();
                }
            }

            float target = _overlayActive ? 1.0f : 0.0f;
            _overlayWeight = Mathf.MoveTowards(_overlayWeight, target, deltaTime / OVERLAY_FADE);
            _handsLayers.SetInputWeight(OVERLAY_LAYER, _overlay.IsValid() ? _overlayWeight : 0.0f);
            if (!_overlayActive && _overlayWeight <= 0.0f)
            {
                DestroyOverlay();
            }

            _graph.Evaluate(deltaTime);
            _weaponView?.ApplyLauncherBoneOverrides();
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

        private void StopEmptyShot()
        {
            if (_emptyShot.IsValid())
            {
                _handsMotion.DisconnectInput(1);
                _emptyShot.Destroy();
                _emptyShot = default;
            }

            _handsMotion.SetInputWeight(0, 1f);
            _handsMotion.SetInputWeight(1, 0f);
        }

        private sealed class Channel
        {
            private readonly PlayableGraph _graph;
            private readonly List<ClipState> _clips = new List<ClipState>();
            private float _fade;
            private float _fadeElapsed;

            public Channel(PlayableGraph graph)
            {
                _graph = graph;
                Mixer = AnimationMixerPlayable.Create(graph, 0);
            }

            public AnimationMixerPlayable Mixer { get; }
            public double ClipTime => _clips.Count > 0 ? _clips[_clips.Count - 1].Playable.GetTime() : 0.0;

            public void Play(AnimationClip clip, float speed, float fade, bool loop)
            {
                if (clip == null)
                {
                    return;
                }

                if (fade <= 0f)
                {
                    ClearPrevious(_clips.Count);
                }

                for (int i = 0; i < _clips.Count; i++)
                {
                    _clips[i].StartWeight = Mixer.GetInputWeight(i);
                }

                AnimationClipPlayable playable = AnimationClipPlayable.Create(_graph, clip);
                playable.SetApplyFootIK(false);
                playable.SetTime(0.0);
                playable.SetSpeed(speed);
                int index = _clips.Count;
                Mixer.SetInputCount(index + 1);
                _graph.Connect(playable, 0, Mixer, index);
                _clips.Add(new ClipState { Playable = playable, Loop = loop });
                _fade = index > 0 ? fade : 0f;
                _fadeElapsed = 0f;
                ApplyWeights();
            }

            public void Tick(float deltaTime)
            {
                for (int i = 0; i < _clips.Count; i++)
                {
                    ClipState state = _clips[i];
                    double length = state.Playable.GetAnimationClip().length;
                    double time = state.Playable.GetTime();

                    if (length > 0.0 && time >= length)
                    {
                        state.Playable.SetTime(state.Loop ? time % length : length);
                    }
                }

                _fadeElapsed += deltaTime;
                ApplyWeights();

                if (_clips.Count > 1 && _fadeElapsed >= _fade)
                {
                    ClearPrevious(_clips.Count - 1);
                    Mixer.SetInputWeight(0, 1f);
                }
                else
                {
                    for (int i = _clips.Count - 2; i >= 0; i--)
                    {
                        if (Mixer.GetInputWeight(i) <= 0.00001f)
                        {
                            RemoveClip(i);
                        }
                    }
                }
            }

            private void RemoveClip(int index)
            {
                Mixer.DisconnectInput(index);
                _clips[index].Playable.Destroy();
                _clips.RemoveAt(index);

                for (int i = index; i < _clips.Count; i++)
                {
                    float weight = Mixer.GetInputWeight(i + 1);
                    Mixer.DisconnectInput(i + 1);
                    _graph.Connect(_clips[i].Playable, 0, Mixer, i);
                    Mixer.SetInputWeight(i, weight);
                }

                Mixer.SetInputCount(_clips.Count);
            }

            private void ClearPrevious(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    Mixer.DisconnectInput(i);
                    _clips[i].Playable.Destroy();
                }

                if (count < _clips.Count)
                {
                    ClipState current = _clips[count];
                    Mixer.DisconnectInput(count);
                    _clips.RemoveRange(0, count);
                    Mixer.SetInputCount(1);
                    _graph.Connect(current.Playable, 0, Mixer, 0);
                }
                else
                {
                    _clips.Clear();
                    Mixer.SetInputCount(0);
                }
            }

            private void ApplyWeights()
            {
                float weight = _fade > 0f ? Mathf.Clamp01(_fadeElapsed / _fade) : 1f;

                for (int i = 0; i < _clips.Count; i++)
                {
                    Mixer.SetInputWeight(i, i == _clips.Count - 1 ? weight : _clips[i].StartWeight * (1f - weight));
                }
            }

            private sealed class ClipState
            {
                public AnimationClipPlayable Playable { get; set; }
                public float StartWeight { get; set; }
                public bool Loop { get; set; }
            }
        }
    }
}

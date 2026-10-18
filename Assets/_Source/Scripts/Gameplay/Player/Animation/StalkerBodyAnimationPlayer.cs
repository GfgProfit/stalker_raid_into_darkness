using System.Collections.Generic;
using RaidIntoDarkness.FirstPerson;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using XRay.Importer;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    public sealed class StalkerBodyAnimationPlayer : MonoBehaviour
    {
        private const int MAX_SLOTS = 6;
        private const float MIN_BLEND_RATE = 0.01f;

        private sealed class Slot
        {
            public AnimationClipPlayable Playable;
            public AnimationClip Clip;
            public string Motion;
            public float Weight;
            public float Accrue;
            public float Falloff;
            public float AccrueOverride;
            public float FalloffOverride;
            public float Speed;
            public bool Locomotion;
            public int Input;
        }

        [Tooltip("Multiplies every X-Ray blend rate; 1 keeps the OMF accrue/falloff values.")]
        [SerializeField] private float _blendRateScale = 1.0f;
        [Tooltip("Rate used when a motion has no accrue/falloff.")]
        [SerializeField] private float _fallbackBlendRate = 5.0f;
        [SerializeField] private float _playbackSpeed = 1.0f;
        [Tooltip("Keep the normalized phase when switching between looping locomotion cycles.")]
        [SerializeField] private bool _syncLocomotionPhase = true;

        private readonly List<Slot> _slots = new List<Slot>();
        private readonly Dictionary<AnimationClip, XRayMotionEntry> _entries = new Dictionary<AnimationClip, XRayMotionEntry>();
        private readonly HashSet<string> _reportedMissing = new HashSet<string>();
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private Animator _animator;
        private Slot _current;
        private GameObject _body;

        public bool IsBound => _graph.IsValid();
        public GameObject Body => _body;
        public string CurrentMotion => _current != null ? _current.Motion : null;
        public AnimationClip CurrentClip => _current != null ? _current.Clip : null;
        public float CurrentWeight => _current != null ? _current.Weight : 0.0f;
        public float CurrentNormalizedTime => _current != null && _current.Clip != null && _current.Clip.length > 0.0f ? (float)(_current.Playable.GetTime() / _current.Clip.length) : 0.0f;
        public int ActiveSlots => _slots.Count;
        public PlayableGraph Graph => _graph;

        public float PlaybackSpeed
        {
            get => _playbackSpeed;
            set => _playbackSpeed = Mathf.Max(0.0f, value);
        }

        public void Bind(GameObject body, BodyAnimationLibrary library)
        {
            string motion = CurrentMotion;
            AnimationClip clip = CurrentClip;
            float phase = CurrentNormalizedTime;
            bool locomotion = _current != null && _current.Locomotion;
            Unbind();
            if (body == null)
            {
                return;
            }

            _body = body;
            _animator = body.GetComponent<Animator>();
            if (_animator == null)
            {
                _animator = body.AddComponent<Animator>();
            }

            _animator.runtimeAnimatorController = null;
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            CollectEntries(library);
            _graph = PlayableGraph.Create(body.name + ".StalkerBody");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _mixer = AnimationMixerPlayable.Create(_graph, MAX_SLOTS);
            var output = AnimationPlayableOutput.Create(_graph, "Body", _animator);
            output.SetSourcePlayable(_mixer);
            _graph.Play();
            if (clip != null)
            {
                Slot slot = CreateSlot(motion, clip, locomotion);
                if (slot != null)
                {
                    slot.Weight = 1.0f;
                    slot.Playable.SetTime(phase * clip.length);
                    _current = slot;
                }
            }
        }

        public void Unbind()
        {
            _slots.Clear();
            _current = null;
            _entries.Clear();
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }

            _body = null;
            _animator = null;
        }

        public void Play(BodyAnimationBinding binding)
        {
            Play(binding, 0.0f);
        }

        public void Play(BodyAnimationBinding binding, float fadeDuration)
        {
            if (!_graph.IsValid() || binding == null)
            {
                return;
            }

            if (binding.Clip == null)
            {
                if (_reportedMissing.Add(binding.Motion ?? string.Empty))
                {
                    Debug.LogWarning($"{nameof(StalkerBodyAnimationPlayer)} on '{name}': body motion '{binding.Motion}' has no AnimationClip; the previous motion keeps playing.", this);
                }

                return;
            }

            if (_current != null && _current.Clip == binding.Clip)
            {
                return;
            }

            float fadeRate = fadeDuration > 0.0f ? 1.0f / fadeDuration : 0.0f;
            if (fadeRate > 0.0f)
            {
                foreach (Slot other in _slots)
                {
                    other.FalloffOverride = fadeRate;
                }
            }

            bool locomotion = (binding.Flags & (int)StalkerMoveFlags.AnyMove) != 0;
            Slot slot = _slots.Find(item => item.Clip == binding.Clip);
            if (slot == null)
            {
                slot = CreateSlot(binding.Motion, binding.Clip, locomotion);
                if (slot == null)
                {
                    return;
                }

                if (_slots.Count == 1)
                {
                    slot.Weight = 1.0f;
                }

                if (_syncLocomotionPhase && locomotion && _current != null && _current.Locomotion && _current.Clip.isLooping && binding.Clip.isLooping)
                {
                    slot.Playable.SetTime(CurrentNormalizedTime % 1.0f * binding.Clip.length);
                }
            }

            slot.AccrueOverride = fadeRate;
            slot.FalloffOverride = 0.0f;
            _current = slot;
        }

        public void Tick(float deltaTime)
        {
            if (!_graph.IsValid())
            {
                return;
            }

            for (int i = _slots.Count - 1; i >= 0; i--)
            {
                Slot slot = _slots[i];
                if (slot == _current)
                {
                    slot.Weight = Mathf.MoveTowards(slot.Weight, 1.0f, (slot.AccrueOverride > 0.0f ? slot.AccrueOverride : Rate(slot.Accrue)) * deltaTime);
                }
                else
                {
                    slot.Weight = Mathf.MoveTowards(slot.Weight, 0.0f, (slot.FalloffOverride > 0.0f ? slot.FalloffOverride : Rate(slot.Falloff)) * deltaTime);
                    if (slot.Weight <= 0.0f)
                    {
                        RemoveSlot(i);
                        continue;
                    }
                }

                slot.Playable.SetSpeed(slot.Speed * _playbackSpeed);
            }

            float total = 0.0f;
            foreach (Slot slot in _slots)
            {
                total += slot.Weight;
            }

            for (int i = 0; i < MAX_SLOTS; i++)
            {
                _mixer.SetInputWeight(i, 0.0f);
            }

            foreach (Slot slot in _slots)
            {
                _mixer.SetInputWeight(slot.Input, total > 0.0f ? slot.Weight / total : 0.0f);
            }

            _graph.Evaluate(deltaTime);
        }

        public float GetWeight(AnimationClip clip)
        {
            Slot slot = _slots.Find(item => item.Clip == clip);
            return slot != null ? slot.Weight : 0.0f;
        }

        public string DescribeBlend()
        {
            var parts = new List<string>();
            foreach (Slot slot in _slots)
            {
                parts.Add($"{slot.Motion} {slot.Weight:0.00}");
            }

            return string.Join(", ", parts);
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private float Rate(float value)
        {
            return Mathf.Max(value > 0.0f ? value * _blendRateScale : _fallbackBlendRate, MIN_BLEND_RATE);
        }

        private Slot CreateSlot(string motion, AnimationClip clip, bool locomotion)
        {
            int input = FreeInput();
            if (input < 0)
            {
                Slot weakest = null;
                foreach (Slot slot in _slots)
                {
                    if (slot != _current && (weakest == null || slot.Weight < weakest.Weight))
                    {
                        weakest = slot;
                    }
                }

                if (weakest == null)
                {
                    return null;
                }

                RemoveSlot(_slots.IndexOf(weakest));
                input = FreeInput();
            }

            _entries.TryGetValue(clip, out XRayMotionEntry entry);
            var created = new Slot
            {
                Playable = AnimationClipPlayable.Create(_graph, clip),
                Clip = clip,
                Motion = motion,
                Accrue = entry != null ? entry.Accrue : 0.0f,
                Falloff = entry != null ? entry.Falloff : 0.0f,
                Speed = entry != null && entry.Speed > 0.0f ? entry.Speed : 1.0f,
                Locomotion = locomotion,
                Input = input
            };

            created.Playable.SetApplyFootIK(false);
            _graph.Connect(created.Playable, 0, _mixer, input);
            _slots.Add(created);
            return created;
        }

        private void RemoveSlot(int index)
        {
            Slot slot = _slots[index];
            _mixer.DisconnectInput(slot.Input);
            slot.Playable.Destroy();
            _slots.RemoveAt(index);
        }

        private int FreeInput()
        {
            for (int input = 0; input < MAX_SLOTS; input++)
            {
                if (!_slots.Exists(slot => slot.Input == input))
                {
                    return input;
                }
            }

            return -1;
        }

        private void CollectEntries(BodyAnimationLibrary library)
        {
            if (library == null)
            {
                return;
            }

            foreach (XRayMotionLibrary motions in library.MotionLibraries)
            {
                if (motions == null)
                {
                    continue;
                }

                foreach (XRayMotionEntry entry in motions.Motions)
                {
                    if (entry.Clip != null && !_entries.ContainsKey(entry.Clip))
                    {
                        _entries.Add(entry.Clip, entry);
                    }
                }
            }
        }
    }
}

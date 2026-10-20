using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace RaidIntoDarkness.FirstPerson
{
    public enum FirstPersonPickupHand
    {
        Left = 0,
        Right = 1,
        LeftWithWeapon = 2
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(FirstPersonRig))]
    public sealed class FirstPersonPickupAnimator : MonoBehaviour
    {
        public const float DEFAULT_SPEED = 1.2f;
        public const float DEFAULT_TAKE_MOMENT = 0.5f;
        public const string LEFT_HAND_MOTION = "liz_pickup_left_hands";
        public const string RIGHT_HAND_MOTION = "liz_pickup_right_hands";
        public const string LEFT_HAND_WITH_WEAPON_MOTION = "liz_pickup_left_wpn_hands";

        private const float MIN_SPEED = 0.01f;
        private const int PICKUP_LAYER = 0;

        [SerializeField] private FirstPersonRig _rig;
        [SerializeField] private AnimationClip _leftHand;
        [SerializeField] private AnimationClip _rightHand;
        [SerializeField] private AnimationClip _leftHandWithWeapon;
        [SerializeField] [Min(MIN_SPEED)] private float _speed = DEFAULT_SPEED;
        [SerializeField] [Range(0.0f, 1.0f)] private float _takeMoment = DEFAULT_TAKE_MOMENT;

        private PlayableGraph _graph;
        private AnimationLayerMixerPlayable _layers;
        private AnimationClipPlayable _clip;
        private GameObject _boundHands;
        private IFirstPersonHandsOverlay _overlay;
        private Action _onTake;
        private Action<bool> _onFinished;
        private float _elapsed;
        private float _duration;
        private bool _taken;

        public bool IsPlaying { get; private set; }
        public FirstPersonPickupHand CurrentHand { get; private set; }
        public float Elapsed => _elapsed;
        public float Duration => _duration;
        public bool HasTaken => _taken;
        public bool IsOverlay => _overlay != null;
        public bool CanOverlay => _rig != null && _rig.HandsOwner is IFirstPersonHandsOverlay && _leftHandWithWeapon != null;

        public float Speed
        {
            get => _speed;
            set => _speed = Mathf.Max(MIN_SPEED, value);
        }

        public static FirstPersonPickupHand ChooseFreeHand(float randomValue) => randomValue < 0.5f ? FirstPersonPickupHand.Left : FirstPersonPickupHand.Right;

        public AnimationClip GetClip(FirstPersonPickupHand hand) => hand switch
        {
            FirstPersonPickupHand.Left => _leftHand,
            FirstPersonPickupHand.Right => _rightHand,
            FirstPersonPickupHand.LeftWithWeapon => _leftHandWithWeapon,
            _ => null
        };

        public bool TryPlay(Action onTake, Action<bool> onFinished) => TryPlay(ChooseFreeHand(UnityEngine.Random.value), onTake, onFinished);

        public bool TryPlay(FirstPersonPickupHand hand, Action onTake, Action<bool> onFinished)
        {
            if (!IsPlaying && _rig != null && _rig.HandsBusy && _rig.HandsOwner is IFirstPersonHandsOverlay overlay)
            {
                return TryPlayOverlay(overlay, onTake, onFinished);
            }

            AnimationClip clip = GetClip(hand);
            if (IsPlaying || clip == null || _rig == null || !_rig.TryAcquireHands(this))
            {
                return false;
            }

            _rig.SetHandsVisible(true);
            GameObject hands = _rig.Hands;
            if (hands == null)
            {
                _rig.SetHandsVisible(false);
                _rig.ReleaseHands(this);
                return false;
            }

            CreateGraph(hands, clip);
            _elapsed = 0.0f;
            _duration = clip.length / _speed;
            _taken = false;
            _onTake = onTake;
            _onFinished = onFinished;
            CurrentHand = hand;
            IsPlaying = true;
            _graph.Evaluate(0.0f);
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (!IsPlaying)
            {
                return;
            }

            if (_rig == null || _rig.Hands == null || _rig.Hands != _boundHands)
            {
                Cancel();
                return;
            }

            _elapsed += deltaTime;
            if (_overlay == null)
            {
                _graph.Evaluate(deltaTime);
            }

            if (!_taken && _elapsed >= _duration * _takeMoment)
            {
                _taken = true;
                _onTake?.Invoke();
            }

            if (_elapsed >= _duration)
            {
                Finish();
            }
        }

        public void Cancel()
        {
            if (IsPlaying)
            {
                Finish();
            }
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        private void OnEnable()
        {
            if (_rig != null)
            {
                _rig.OutfitApplied += OnOutfitApplied;
            }
        }

        private void OnDisable()
        {
            if (_rig != null)
            {
                _rig.OutfitApplied -= OnOutfitApplied;
            }

            Cancel();
        }

        private void OnDestroy()
        {
            DestroyGraph();
        }

        private void OnOutfitApplied(FirstPersonOutfitEntry entry, bool bodyChanged)
        {
            if (IsPlaying && _rig.Hands != _boundHands)
            {
                Cancel();
            }
        }

        private void Finish()
        {
            Action<bool> finished = _onFinished;
            bool taken = _taken;
            IFirstPersonHandsOverlay overlay = _overlay;
            IsPlaying = false;
            _onTake = null;
            _onFinished = null;
            _overlay = null;
            DestroyGraph();
            if (overlay != null)
            {
                overlay.StopOverlay();
            }
            else if (_rig != null)
            {
                _rig.SetHandsVisible(false);
                _rig.ReleaseHands(this);
            }

            finished?.Invoke(taken);
        }

        private bool TryPlayOverlay(IFirstPersonHandsOverlay overlay, Action onTake, Action<bool> onFinished)
        {
            AnimationClip clip = _leftHandWithWeapon;
            if (clip == null || _rig.Hands == null || !overlay.TryPlayOverlay(clip, _speed))
            {
                return false;
            }

            _overlay = overlay;
            _boundHands = _rig.Hands;
            _elapsed = 0.0f;
            _duration = clip.length / _speed;
            _taken = false;
            _onTake = onTake;
            _onFinished = onFinished;
            CurrentHand = FirstPersonPickupHand.LeftWithWeapon;
            IsPlaying = true;
            return true;
        }

        private void CreateGraph(GameObject hands, AnimationClip clip)
        {
            DestroyGraph();
            Animator animator = hands.GetComponent<Animator>();
            if (animator == null)
            {
                animator = hands.AddComponent<Animator>();
            }

            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _graph = PlayableGraph.Create($"{name}.FirstPersonPickup");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _layers = AnimationLayerMixerPlayable.Create(_graph, 1);
            var output = AnimationPlayableOutput.Create(_graph, "Hands", animator);
            output.SetSourcePlayable(_layers);
            _clip = AnimationClipPlayable.Create(_graph, clip);
            _clip.SetApplyFootIK(false);
            _clip.SetTime(0.0);
            _clip.SetTime(0.0);
            _clip.SetSpeed(_speed);
            _graph.Connect(_clip, 0, _layers, PICKUP_LAYER);
            _layers.SetInputWeight(PICKUP_LAYER, 1.0f);
            _graph.Play();
            _boundHands = hands;
        }

        private void DestroyGraph()
        {
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }

            _boundHands = null;
        }
    }
}

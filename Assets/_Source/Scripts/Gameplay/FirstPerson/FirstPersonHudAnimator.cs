using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using XRay.Importer;

namespace RaidIntoDarkness.FirstPerson
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FirstPersonRig))]
    public sealed class FirstPersonHudAnimator : MonoBehaviour
    {
        public const string ITEM_ANCHOR_BONE = "lead_gun";

        private const float MIN_SPEED = 0.01f;

        [SerializeField] private FirstPersonRig _rig;
        [SerializeField] private FirstPersonEquipmentAnimationLibrary _library;
        [SerializeField] private AudioSource _audio;
        [Tooltip("[actor_hud] ancor_0: script motion items are attached to this hands bone.")]
        [SerializeField] private string _itemAnchorBone = ITEM_ANCHOR_BONE;

        private PlayableGraph _graph;
        private GameObject _boundHands;
        private GameObject _item;
        private Transform _itemRoot;
        private Vector3 _handsPosition;
        private Quaternion _handsRotation;
        private float _elapsed;
        private float _duration;

        public bool IsPlaying { get; private set; }
        public FirstPersonHudMotion Current { get; private set; }
        public float Elapsed => _elapsed;
        public float Duration => _duration;
        public GameObject Item => _item;
        public FirstPersonRig Rig => _rig;
        public FirstPersonEquipmentAnimationLibrary Library => _library;

        public event Action<FirstPersonHudMotion> Started;
        public event Action<FirstPersonHudMotion, bool> Stopped;

        public static float GetDuration(FirstPersonHudMotion motion, float speed)
        {
            return motion != null && motion.Hands != null ? motion.Hands.length / Mathf.Max(MIN_SPEED, speed) : 0.0f;
        }

        public bool TryPlay(FirstPersonHudMotion motion, float speed)
        {
            if (IsPlaying || motion == null || motion.Hands == null || _rig == null || !_rig.TryAcquireHands(this))
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

            speed = Mathf.Max(MIN_SPEED, speed);
            _boundHands = hands;
            _handsPosition = hands.transform.localPosition;
            _handsRotation = hands.transform.localRotation;
            hands.transform.localPosition = motion.HandsPosition;
            hands.transform.localRotation = motion.HandsRotation;
            CreateItem(hands, motion);
            CreateGraph(hands, motion, speed);
            _elapsed = 0.0f;
            _duration = motion.Hands.length / speed;
            Current = motion;
            IsPlaying = true;
            _graph.Evaluate(0.0f);
            ResetItemRoot();
            PlaySound(motion.Sound);
            Started?.Invoke(motion);
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
                Stop(false);
                return;
            }

            _elapsed += deltaTime;
            _graph.Evaluate(deltaTime);
            ResetItemRoot();
            if (_elapsed >= _duration)
            {
                Stop(true);
            }
        }

        public void Cancel()
        {
            if (IsPlaying)
            {
                Stop(false);
            }
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        private void OnDisable()
        {
            Cancel();
        }

        private void OnDestroy()
        {
            DestroyGraph();
        }

        private void Stop(bool completed)
        {
            FirstPersonHudMotion motion = Current;
            IsPlaying = false;
            Current = null;
            DestroyGraph();
            if (_item != null)
            {
                DestroyObject(_item);
            }

            _item = null;
            _itemRoot = null;
            if (_boundHands != null)
            {
                _boundHands.transform.localPosition = _handsPosition;
                _boundHands.transform.localRotation = _handsRotation;
            }

            _boundHands = null;
            if (!completed && _audio != null)
            {
                _audio.Stop();
            }

            if (_rig != null)
            {
                _rig.SetHandsVisible(false);
                _rig.ReleaseHands(this);
            }

            Stopped?.Invoke(motion, completed);
        }

        private void CreateItem(GameObject hands, FirstPersonHudMotion motion)
        {
            if (motion.ItemModel == null)
            {
                return;
            }

            Transform anchor = FindBone(hands, _itemAnchorBone);
            _item = Instantiate(motion.ItemModel, anchor != null ? anchor : hands.transform, false);
            _item.transform.localPosition = Vector3.zero;
            _item.transform.localRotation = Quaternion.identity;
            foreach (Renderer renderer in _item.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            XRayModel model = _item.GetComponent<XRayModel>();
            if (model == null)
            {
                return;
            }

            for (int i = 0; i < model.Bones.Count; i++)
            {
                if (model.Bones[i] != null && model.Bones[i].parent == _item.transform)
                {
                    _itemRoot = model.Bones[i];
                    return;
                }
            }
        }

        private void CreateGraph(GameObject hands, FirstPersonHudMotion motion, float speed)
        {
            DestroyGraph();
            _graph = PlayableGraph.Create($"{name}.FirstPersonHud");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            Connect(hands, motion.Hands, speed, "Hands");
            if (_item != null && motion.Item != null)
            {
                Connect(_item, motion.Item, speed, "Item");
            }

            _graph.Play();
        }

        private void Connect(GameObject target, AnimationClip clip, float speed, string outputName)
        {
            Animator animator = target.GetComponent<Animator>();
            if (animator == null)
            {
                animator = target.AddComponent<Animator>();
            }

            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var output = AnimationPlayableOutput.Create(_graph, outputName, animator);
            AnimationClipPlayable playable = AnimationClipPlayable.Create(_graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetTime(0.0);
            playable.SetSpeed(speed);
            output.SetSourcePlayable(playable);
        }

        private void ResetItemRoot()
        {
            if (_itemRoot != null)
            {
                _itemRoot.localPosition = Vector3.zero;
                _itemRoot.localRotation = Quaternion.identity;
            }
        }

        private void PlaySound(AudioClip clip)
        {
            if (_audio == null || clip == null)
            {
                return;
            }

            _audio.Stop();
            _audio.clip = clip;
            _audio.Play();
        }

        private void DestroyGraph()
        {
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }
        }

        private static Transform FindBone(GameObject hands, string boneName)
        {
            XRayModel model = hands.GetComponent<XRayModel>();
            Transform bone = model != null ? model.FindBone(boneName) : null;
            return bone != null ? bone : FindChild(hands.transform, boneName);
        }

        private static Transform FindChild(Transform root, string childName)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        private static void DestroyObject(GameObject target)
        {
            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}

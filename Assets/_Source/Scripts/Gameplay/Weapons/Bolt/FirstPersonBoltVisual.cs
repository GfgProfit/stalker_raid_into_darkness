using RaidIntoDarkness.FirstPerson;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer;

namespace RaidIntoDarkness.Weapons
{
    internal sealed class FirstPersonBoltVisual
    {
        private readonly FirstPersonRig _rig;
        private readonly GameObject _hands;
        private readonly Vector3 _savedPosition;
        private readonly Quaternion _savedRotation;
        private readonly FirstPersonWeaponGraph _graph;
        private readonly AvatarMask _mask;
        private readonly Transform _itemRoot;
        private readonly Renderer[] _renderers;
        private readonly bool[] _rendererVisibility;
        private readonly float _transitionDuration;

        private AnimationClip _currentItem;
        private float _itemSpeed;
        private bool _itemLoop;

        public GameObject Model { get; }
        public bool IsBound => _rig != null && _rig.Hands == _hands;

        public FirstPersonBoltVisual(FirstPersonRig rig, BoltDefinition definition)
        {
            _rig = rig;
            _transitionDuration = definition.TransitionDuration;
            _hands = rig.Hands;
            _savedPosition = _hands.transform.localPosition;
            _savedRotation = _hands.transform.localRotation;
            _hands.transform.localPosition = definition.HandsPosition;
            _hands.transform.localRotation = Quaternion.identity;
            XRayModel handsModel = _hands.GetComponent<XRayModel>();
            Transform anchor = handsModel != null ? handsModel.FindBone(FirstPersonHudAnimator.ITEM_ANCHOR_BONE) : null;
            if (anchor == null)
            {
                foreach (Transform bone in _hands.GetComponentsInChildren<Transform>(true))
                {
                    if (bone.name == FirstPersonHudAnimator.ITEM_ANCHOR_BONE)
                    {
                        anchor = bone;
                        break;
                    }
                }
            }
            Model = Object.Instantiate(definition.HudModel, anchor != null ? anchor : _hands.transform, false);
            Model.transform.localPosition = Vector3.zero;
            Model.transform.localRotation = Quaternion.identity;
            _renderers = Model.GetComponentsInChildren<Renderer>(true);
            _rendererVisibility = new bool[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                _rendererVisibility[i] = renderer.enabled;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    skinned.updateWhenOffscreen = true;
                }
            }
            XRayModel model = Model.GetComponent<XRayModel>();
            if (model != null)
            {
                foreach (Transform bone in model.Bones)
                {
                    if (bone != null && bone.parent == Model.transform)
                    {
                        _itemRoot = bone;
                        break;
                    }
                }
            }
            _mask = FirstPersonWeaponGraph.CreateMask(_hands.transform, new[] { "l_clavicle" });
            _graph = new FirstPersonWeaponGraph("FirstPersonBolt", GetAnimator(_hands), GetAnimator(Model), _mask);
        }

        public void Play(AnimationClip hands, AnimationClip item, float handsSpeed, float itemSpeed, bool loop)
        {
            _graph.Play(hands, null, handsSpeed, _transitionDuration, loop);
            if (_currentItem != item || _itemSpeed != itemSpeed || _itemLoop != loop)
            {
                _graph.PlayWeapon(item, itemSpeed, _transitionDuration, loop);
                _currentItem = item;
                _itemSpeed = itemSpeed;
                _itemLoop = loop;
            }
            Tick(0f);
        }

        public void Tick(float deltaTime)
        {
            _graph.Evaluate(deltaTime);
            if (_itemRoot != null)
            {
                _itemRoot.localPosition = Vector3.zero;
                _itemRoot.localRotation = Quaternion.identity;
            }
        }

        public bool TryPlayOverlay(AnimationClip clip, float speed)
        {
            return _graph.PlayOverlay(clip, speed);
        }

        public void StopOverlay()
        {
            _graph.StopOverlay();
        }

        public void SetItemVisible(bool visible)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                {
                    _renderers[i].enabled = visible && _rendererVisibility[i];
                }
            }
        }

        public void Dispose()
        {
            _graph.Destroy();
            if (_hands != null)
            {
                _hands.transform.localPosition = _savedPosition;
                _hands.transform.localRotation = _savedRotation;
            }
            Destroy(Model);
            Destroy(_mask);
        }

        private static Animator GetAnimator(GameObject target)
        {
            Animator animator = target.GetComponent<Animator>();
            if (animator == null)
            {
                animator = target.AddComponent<Animator>();
            }
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return animator;
        }

        private static void Destroy(Object target)
        {
            if (target == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}

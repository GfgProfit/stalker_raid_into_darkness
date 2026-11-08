using UnityEngine;
using XRay.Importer;

namespace RaidIntoDarkness.FirstPerson
{
    [CreateAssetMenu(menuName = "Raid Into Darkness/First Person/Bolt", fileName = "BoltDefinition")]
    public sealed class BoltDefinition : ScriptableObject
    {
        [SerializeField] private GameObject _hudModel;
        [SerializeField] private GameObject _projectilePrefab;
        [SerializeField] private XRayMotionLibrary _handsMotions;
        [SerializeField] private XRayMotionLibrary _itemMotions;
        [SerializeField] private Vector3 _handsPosition = new Vector3(0.033f, -0.155f, -0.15f);
        [SerializeField] private float _hudFieldOfView = 0.78f;
        [SerializeField] private float _throwReleaseTime;
        [SerializeField, Min(0f)] private float _transitionDuration = 0.2f;
        [SerializeField] private AnimationClip _draw;
        [SerializeField] private AnimationClip _drawAlternate;
        [SerializeField] private AnimationClip _holster;
        [SerializeField] private AnimationClip _idle;
        [SerializeField] private AnimationClip _moving;
        [SerializeField] private AnimationClip _sprint;
        [SerializeField] private AnimationClip _bore;
        [SerializeField] private AnimationClip _throwBegin;
        [SerializeField] private AnimationClip _throwIdle;
        [SerializeField] private AnimationClip _throwEnd;
        [SerializeField] private AnimationClip _itemIdle;
        [SerializeField] private AnimationClip _itemFallback;
        [SerializeField] private AnimationClip _itemDraw;
        [SerializeField] private AnimationClip _itemDrawAlternate;
        [SerializeField] private AnimationClip _itemBore;
        [SerializeField] private AudioClip _drawSound;
        [SerializeField] private AudioClip _holsterSound;

        public GameObject HudModel => _hudModel;
        public GameObject ProjectilePrefab => _projectilePrefab;
        public Vector3 HandsPosition => _handsPosition;
        public float HudFieldOfView => _hudFieldOfView;
        public float ThrowReleaseTime => _throwReleaseTime;
        public float TransitionDuration => Mathf.Max(0f, _transitionDuration);
        public AnimationClip Draw => _draw;
        public AnimationClip DrawAlternate => _drawAlternate;
        public AnimationClip Holster => _holster;
        public AnimationClip Idle => _idle;
        public AnimationClip Moving => _moving;
        public AnimationClip Sprint => _sprint;
        public AnimationClip Bore => _bore;
        public AnimationClip ThrowBegin => _throwBegin;
        public AnimationClip ThrowIdle => _throwIdle;
        public AnimationClip ThrowEnd => _throwEnd;
        public AnimationClip ItemIdle => _itemIdle;
        public AnimationClip ItemFallback => _itemFallback;
        public AnimationClip ItemDraw => _itemDraw;
        public AnimationClip ItemDrawAlternate => _itemDrawAlternate;
        public AnimationClip ItemBore => _itemBore;
        public AudioClip DrawSound => _drawSound;
        public AudioClip HolsterSound => _holsterSound;

        public float GetMotionSpeed(AnimationClip clip)
        {
            if (clip == null)
            {
                return 1.0f;
            }

            if (_handsMotions != null)
            {
                foreach (XRayMotionEntry entry in _handsMotions.Motions)
                {
                    if (entry.Clip == clip)
                    {
                        return Mathf.Max(0.01f, entry.Speed);
                    }
                }
            }

            if (_itemMotions != null)
            {
                foreach (XRayMotionEntry entry in _itemMotions.Motions)
                {
                    if (entry.Clip == clip)
                    {
                        return Mathf.Max(0.01f, entry.Speed);
                    }
                }
            }

            return 1.0f;
        }
    }
}

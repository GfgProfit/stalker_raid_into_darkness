using System;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer;

namespace RaidIntoDarkness.FirstPerson
{
    [DisallowMultipleComponent]
    public sealed class FirstPersonRig : MonoBehaviour
    {
        [SerializeField] private FirstPersonOutfitCatalog _catalog;
        [SerializeField] private Transform _handsRoot;
        [SerializeField] private Transform _bodyRoot;
        [SerializeField] private string _initialOutfit = FirstPersonOutfitCatalog.NO_OUTFIT_SECTION;
        [Tooltip("When off, another component animates the body and no XRayRigPlayer is added to it.")]
        [SerializeField] private bool _driveBodyAnimation = true;
        [SerializeField] private bool _castShadows;
        [SerializeField] private bool _handsVisible;

        private GameObject _hands;
        private GameObject _body;
        private XRayRigPlayer _bodyPlayer;
        private FirstPersonOutfitEntry _current;

        public FirstPersonOutfitEntry Current => _current;
        public GameObject Hands => _hands;
        public GameObject Body => _body;
        public XRayRigPlayer BodyPlayer => _bodyPlayer;
        public FirstPersonOutfitCatalog Catalog => _catalog;
        public bool HandsVisible => _handsVisible;
        public object HandsOwner { get; private set; }
        public bool HandsBusy => HandsOwner != null;

        public event Action<FirstPersonOutfitEntry, bool> OutfitApplied;
        public event Action<object> HandsRequested;

        public void RequestHands(object requester)
        {
            if (HandsOwner != null && HandsOwner != requester)
            {
                HandsRequested?.Invoke(requester);
            }
        }

        public bool TryAcquireHands(object owner)
        {
            if (owner == null || (HandsOwner != null && HandsOwner != owner))
            {
                return false;
            }

            HandsOwner = owner;
            return true;
        }

        public void ReleaseHands(object owner)
        {
            if (HandsOwner == owner)
            {
                HandsOwner = null;
            }
        }

        private void Start()
        {
            if (_current == null && !string.IsNullOrEmpty(_initialOutfit))
            {
                ApplyOutfit(_initialOutfit);
            }
        }

        public bool ApplyOutfit(string section)
        {
            if (_catalog == null)
            {
                Debug.LogError($"{nameof(FirstPersonRig)} on '{name}': no outfit catalog assigned.", this);
                return false;
            }

            FirstPersonOutfitEntry entry = _catalog.GetOrDefault(section);
            if (entry == null)
            {
                Debug.LogError($"{nameof(FirstPersonRig)} on '{name}': outfit '{section}' and the default outfit are missing in '{_catalog.name}'.", this);
                return false;
            }

            if (_current == null || _current.HandsModel != entry.HandsModel)
            {
                Replace(ref _hands, entry.HandsModel, _handsRoot);
                ApplyHandsVisibility();
            }

            bool bodyChanged = _current == null || _current.BodyModel != entry.BodyModel;
            if (bodyChanged)
            {
                Replace(ref _body, entry.BodyModel, _bodyRoot);
                _bodyPlayer = _body != null && _driveBodyAnimation ? GetOrAddPlayer(_body) : null;
            }

            _current = entry;
            PlayBody(0, true, false, 0.0f);
            OutfitApplied?.Invoke(entry, bodyChanged);
            return true;
        }

        public void SetHandsVisible(bool visible)
        {
            _handsVisible = visible;
            ApplyHandsVisibility();
        }

        public void PlayBody(int movingState, bool isCalm, bool isSlowedDown, float fadeDuration)
        {
            if (_bodyPlayer == null || _current == null || _current.BodyAnimations == null)
            {
                return;
            }

            BodyAnimationBinding binding = _current.BodyAnimations.Resolve(movingState, isCalm, isSlowedDown);
            if (binding != null && binding.Clip != null && _bodyPlayer.CurrentClip != binding.Clip)
            {
                _bodyPlayer.Play(binding.Clip, fadeDuration);
            }
        }

        private void Replace(ref GameObject instance, GameObject model, Transform root)
        {
            if (instance != null)
            {
                Destroy(instance);
            }

            instance = model != null && root != null ? Instantiate(model, root, false) : null;
            if (instance != null && !_castShadows)
            {
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
        }

        private void ApplyHandsVisibility()
        {
            if (_hands != null && _hands.activeSelf != _handsVisible)
            {
                _hands.SetActive(_handsVisible);
            }
        }

        private static XRayRigPlayer GetOrAddPlayer(GameObject body)
        {
            if (body.GetComponent<Animator>() == null)
            {
                body.AddComponent<Animator>();
            }

            XRayRigPlayer player = body.GetComponent<XRayRigPlayer>();
            return player != null ? player : body.AddComponent<XRayRigPlayer>();
        }
    }
}

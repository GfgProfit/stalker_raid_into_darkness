using UnityEngine;
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

        private GameObject _hands;
        private GameObject _body;
        private XRayRigPlayer _bodyPlayer;
        private FirstPersonOutfitEntry _current;

        public FirstPersonOutfitEntry Current => _current;
        public GameObject Hands => _hands;
        public GameObject Body => _body;
        public XRayRigPlayer BodyPlayer => _bodyPlayer;

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
            }

            if (_current == null || _current.BodyModel != entry.BodyModel)
            {
                Replace(ref _body, entry.BodyModel, _bodyRoot);
                _bodyPlayer = _body != null ? GetOrAddPlayer(_body) : null;
            }

            _current = entry;
            PlayBody(0, true, false, 0.0f);
            return true;
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

        private static void Replace(ref GameObject instance, GameObject model, Transform root)
        {
            if (instance != null)
            {
                Destroy(instance);
            }

            instance = model != null && root != null ? Instantiate(model, root, false) : null;
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

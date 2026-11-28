using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer;

namespace RaidIntoDarkness.FirstPerson
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(150)]
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

        private readonly Dictionary<Collider, float> _weaponObstructionExclusions = new();
        private readonly List<Collider> _expiredObstructionExclusions = new();
        private GameObject _hands;
        private GameObject _body;
        private XRayRigPlayer _bodyPlayer;
        private FirstPersonOutfitEntry _current;
        private FirstPersonWeaponObstruction _weaponObstruction;

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

        public void InitializeWeaponObstruction(Transform observer, LayerMask collisionMask)
        {
            _weaponObstruction?.Dispose();
            _weaponObstruction = null;
            if (observer == null)
            {
                return;
            }

            try
            {
                FirstPersonContactCatalog calibration = FirstPersonContactCatalog.Load();
                if (calibration == null)
                {
                    Debug.LogError("First-person collision calibration is missing. Run Tools/Raid Into Darkness/Calibrate First Person Collision.", this);
                    return;
                }

                _weaponObstruction = new FirstPersonWeaponObstruction(transform, observer, collisionMask, calibration, _weaponObstructionExclusions);
            }
            catch (Exception exception) when (exception is System.IO.IOException or System.IO.InvalidDataException or ArgumentException)
            {
                Debug.LogError($"Could not load first-person collision calibration: {exception.Message}", this);
            }
        }

        public bool IsWeaponAimObstructed(object owner)
        {
            return owner != null && owner == HandsOwner && _weaponObstruction?.BlocksAim == true;
        }

        public void SetWeaponObstruction(object owner, GameObject weapon, Transform muzzle, bool useMuzzleAxis)
        {
            if (owner == HandsOwner)
            {
                _weaponObstruction?.Bind(weapon, muzzle, useMuzzleAxis);
            }
        }

        public void InvalidateWeaponObstructionGeometry()
        {
            _weaponObstruction?.InvalidateGeometry();
        }

        public void IgnoreWeaponObstruction(Collider[] colliders, float duration)
        {
            if (_weaponObstruction == null)
            {
                return;
            }

            float now = Time.time;

            foreach (KeyValuePair<Collider, float> exclusion in _weaponObstructionExclusions)
            {
                if (exclusion.Key == null || exclusion.Value <= now)
                {
                    _expiredObstructionExclusions.Add(exclusion.Key);
                }
            }

            foreach (Collider collider in _expiredObstructionExclusions)
            {
                _weaponObstructionExclusions.Remove(collider);
            }

            _expiredObstructionExclusions.Clear();

            foreach (Collider collider in colliders)
            {
                if (collider != null)
                {
                    _weaponObstructionExclusions[collider] = now + duration;
                }
            }
        }

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
                _weaponObstruction?.Bind(null, null, false);
            }
        }

        private void Start()
        {
            if (_current == null && !string.IsNullOrEmpty(_initialOutfit))
            {
                ApplyOutfit(_initialOutfit);
            }
        }

        private void LateUpdate()
        {
            if (HandsBusy && HandsVisible && _hands != null)
            {
                _weaponObstruction?.Tick(_hands, gameObject.scene.GetPhysicsScene(), Time.deltaTime);
            }
        }

        private void OnDisable()
        {
            _weaponObstruction?.Clear();
        }

        private void OnDestroy()
        {
            _weaponObstruction?.Dispose();
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
                _weaponObstruction?.Clear();
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
            if (!visible)
            {
                _weaponObstruction?.Clear();
            }

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
            if (instance != null)
            {
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (!_castShadows)
                    {
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                    }

                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        skinned.updateWhenOffscreen = true;
                    }
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

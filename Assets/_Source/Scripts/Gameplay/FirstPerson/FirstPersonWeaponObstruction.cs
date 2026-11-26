using UnityEngine;

namespace RaidIntoDarkness.FirstPerson
{
    internal sealed class FirstPersonWeaponObstruction
    {
        private const float MIN_BARREL_LENGTH = 0.08f;
        private const float COMPACT_BARREL_LENGTH = 0.33f;
        private const float CLOSE_ENTRY_DELAY = 0.06f;
        private const float CLOSE_EXIT_DELAY = 0.22f;
        private const float CONTACT_EXIT_DELAY = 0.085f;
        private const float ENTER_RESPONSE = 17.0f;
        private const float EXIT_RESPONSE = 11.0f;
        private const float SETTLE_SQUARED = 0.000001f;
        private const float SETTLE_ANGLE = 0.05f;

        private readonly FirstPersonContactGeometry _geometry;
        private readonly Transform _actor;
        private readonly Transform _view;
        private readonly int _collisionMask;

        private GameObject _geometryHands;
        private GameObject _weapon;
        private Transform _weaponPivot;
        private Transform _muzzle;
        private bool _useMuzzleAxis;
        private Transform _appliedHands;
        private Vector3 _authoredPosition;
        private Quaternion _authoredRotation;
        private Vector3 _appliedPosition;
        private Quaternion _appliedRotation;
        private Vector3 _offset;
        private Quaternion _rotation = Quaternion.identity;
        private float _closeTime;
        private float _closeExitTime;
        private float _clearTime;
        private float _lastDistance;
        private bool _hadContact;
        private bool _close;
        private bool _geometryDirty = true;

        public FirstPersonWeaponObstruction(Transform actor, Transform view, int collisionMask, FirstPersonContactCatalog calibration)
        {
            _actor = actor;
            _view = view;
            _collisionMask = collisionMask;
            _geometry = new FirstPersonContactGeometry(calibration);
        }

        public void Bind(GameObject weapon, Transform muzzle, bool useMuzzleAxis)
        {
            if (_weapon == weapon && _muzzle == muzzle && _useMuzzleAxis == useMuzzleAxis)
            {
                return;
            }

            bool sameWeapon = _weapon != null && _weapon == weapon && _muzzle != null && muzzle != null;
            if (!sameWeapon)
            {
                Clear();
            }

            _weapon = weapon;
            _weaponPivot = weapon != null ? weapon.transform.parent : null;
            _muzzle = muzzle;
            _useMuzzleAxis = useMuzzleAxis;
            InvalidateGeometry();
            if (weapon == null || muzzle == null)
            {
                _geometry.Bind(null, null);
                _geometryHands = null;
            }
        }

        public void InvalidateGeometry()
        {
            _geometryDirty = true;
        }

        public void Tick(GameObject hands, PhysicsScene scene, float deltaTime)
        {
            Restore();
            if (hands == null || _view == null || _weaponPivot == null || _muzzle == null || !scene.IsValid() || !_muzzle.IsChildOf(hands.transform))
            {
                Clear();
                return;
            }

            if (_geometryDirty || hands != _geometryHands)
            {
                _geometry.Bind(hands, _weapon);
                _geometryHands = hands;
                _geometryDirty = false;
            }

            Vector3 pivot = _weaponPivot.position;
            Vector3 muzzle = _muzzle.position;
            Vector3 axis = _useMuzzleAxis ? _muzzle.forward : (muzzle - pivot).normalized;
            float length = Vector3.Dot(muzzle - pivot, axis);
            if (length < MIN_BARREL_LENGTH)
            {
                axis = (muzzle - pivot).normalized;
                length = Vector3.Distance(muzzle, pivot);
            }

            bool compact = length < COMPACT_BARREL_LENGTH;
            float lead = Mathf.Clamp(length * (compact ? 0.10f : 0.18f), 0.025f, 0.14f);
            float distance = 0.0f;
            bool contact = length >= MIN_BARREL_LENGTH && _geometry.TryFindObstacle(scene, _collisionMask, _actor, muzzle - axis * length, axis, length, lead, out distance);
            if (contact)
            {
                _lastDistance = distance;
            }

            UpdatePose(length, lead, compact, contact, deltaTime);
            if (_offset == Vector3.zero && _rotation == Quaternion.identity)
            {
                return;
            }

            Transform root = hands.transform;
            Quaternion view = _view.rotation;
            Quaternion turn = view * _rotation * Quaternion.Inverse(view);
            Vector3 position = pivot + turn * (root.position - pivot) + view * _offset;
            _authoredPosition = root.localPosition;
            _authoredRotation = root.localRotation;
            root.SetPositionAndRotation(position, turn * root.rotation);
            _appliedPosition = root.localPosition;
            _appliedRotation = root.localRotation;
            _appliedHands = root;
        }

        public void Clear()
        {
            Restore();
            _offset = Vector3.zero;
            _rotation = Quaternion.identity;
            _closeTime = 0.0f;
            _closeExitTime = 0.0f;
            _clearTime = 0.0f;
            _lastDistance = 0.0f;
            _hadContact = false;
            _close = false;
        }

        public void Dispose()
        {
            Clear();
            _geometry.Dispose();
        }

        private void UpdatePose(float length, float lead, bool compact, bool contact, float dt)
        {
            _clearTime = contact ? 0.0f : _clearTime + dt;
            _hadContact = contact || (_hadContact && _clearTime < CONTACT_EXIT_DELAY);
            float range = length + lead;
            float threshold = length * (compact ? 0.55f : 0.57f);
            bool closeContact = contact && _lastDistance < threshold;
            _closeTime = closeContact ? _closeTime + dt : 0.0f;
            _closeExitTime = closeContact ? 0.0f : _closeExitTime + dt;
            _close = _closeTime >= CLOSE_ENTRY_DELAY || (_close && _closeExitTime < CLOSE_EXIT_DELAY);
            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            if (_hadContact)
            {
                float pressure = Mathf.Clamp01((range - _lastDistance) / range);
                float retreat = Mathf.Clamp((range - _lastDistance) * 0.72f, 0.0f, compact ? 0.22f : 0.30f);
                position = new Vector3(0f, -0.07f * pressure, -retreat);
                rotation = Quaternion.Euler(-5f * pressure, 0f, 0f);
                if (_close)
                {
                    float folded = Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(threshold, length * 0.18f, _lastDistance));
                    if (compact)
                    {
                        position += new Vector3(0.025f, -0.08f, 0f) * folded;
                        rotation = Quaternion.Euler(-32f * folded, 4f * folded, -5f * folded);
                    }
                    else
                    {
                        position += Vector3.down * (0.03f * folded);
                        rotation = Quaternion.Euler(27f * folded, -11f * folded, -9f * folded);
                    }
                }
            }

            float blend = 1.0f - Mathf.Exp(-(_hadContact ? ENTER_RESPONSE : EXIT_RESPONSE) * dt);
            _offset = Vector3.Lerp(_offset, position, blend);
            _rotation = Quaternion.Slerp(_rotation, rotation, blend);
            if (!_hadContact && _offset.sqrMagnitude < SETTLE_SQUARED && Quaternion.Angle(_rotation, Quaternion.identity) < SETTLE_ANGLE)
            {
                _offset = Vector3.zero;
                _rotation = Quaternion.identity;
            }
        }

        private void Restore()
        {
            if (_appliedHands == null)
            {
                return;
            }

            if (_appliedHands.localPosition.Equals(_appliedPosition))
            {
                _appliedHands.localPosition = _authoredPosition;
            }

            if (_appliedHands.localRotation.Equals(_appliedRotation))
            {
                _appliedHands.localRotation = _authoredRotation;
            }

            _appliedHands = null;
        }
    }
}

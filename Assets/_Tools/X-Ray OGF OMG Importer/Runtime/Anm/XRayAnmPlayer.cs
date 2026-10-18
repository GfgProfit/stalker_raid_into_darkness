using System.Collections.Generic;
using UnityEngine;

namespace XRay.Importer
{
    public enum XRayAnmApplyMode
    {
        Absolute,
        Additive
    }

    [DisallowMultipleComponent]
    public sealed class XRayAnmPlayer : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private XRayAnmApplyMode _mode = XRayAnmApplyMode.Absolute;
        [Tooltip("Additive tracks apply the motion relative to their first frame, so an effect starts from the current pose.")]
        [SerializeField] private bool _relativeToFirstFrame = true;
        [SerializeField] private bool _playOnEnable;
        [SerializeField] private bool _restoreOnFinish = true;
        [SerializeField] private bool _driveSelf = true;
        [SerializeField] private XRayAnmTrack _track = new XRayAnmTrack();

        private readonly List<XRayAnmTrack> _effects = new List<XRayAnmTrack>();
        private Vector3 _restPosition;
        private Quaternion _restRotation;
        private bool _hasRestPose;

        public Transform Target
        {
            get => _target != null ? _target : transform;
            set => _target = value;
        }

        public XRayAnmApplyMode Mode
        {
            get => _mode;
            set => _mode = value;
        }

        public bool RelativeToFirstFrame
        {
            get => _relativeToFirstFrame;
            set => _relativeToFirstFrame = value;
        }

        public bool PlayOnEnable
        {
            get => _playOnEnable;
            set => _playOnEnable = value;
        }

        public bool DriveSelf
        {
            get => _driveSelf;
            set => _driveSelf = value;
        }

        public XRayAnmAsset Asset
        {
            get => _track.Asset;
            set => _track.Asset = value;
        }

        public float Speed
        {
            get => _track.Speed;
            set => _track.Speed = value;
        }

        public bool Loop
        {
            get => _track.Loop;
            set => _track.Loop = value;
        }

        public float Weight
        {
            get => _track.Weight;
            set => _track.Weight = value;
        }

        public XRayAnmTrack Track => _track;
        public IReadOnlyList<XRayAnmTrack> Effects => _effects;
        public float Time => _track.Time;
        public bool IsPlaying => _track.IsPlaying;
        public bool IsPaused => _track.IsPaused;

        public void Play()
        {
            Play(_track.Asset, _track.StartTime);
        }

        public void Play(XRayAnmAsset asset, float startTime = 0.0f)
        {
            if (asset == null)
            {
                Debug.LogError($"{nameof(XRayAnmPlayer)} on '{name}': no ANM asset assigned.", this);
                return;
            }

            if (!asset.HasCurves)
            {
                Debug.LogError($"{nameof(XRayAnmPlayer)} on '{name}': '{asset.name}' was imported without 'Preserve Original Curves'; play its AnimationClip instead.", this);
                return;
            }

            CaptureRestPose();
            _track.Asset = asset;
            _track.Play(startTime);
            Apply();
        }

        public XRayAnmTrack PlayEffect(XRayAnmAsset asset, float weight = 1.0f, float speed = 1.0f, bool loop = false)
        {
            if (asset == null || !asset.HasCurves)
            {
                Debug.LogError($"{nameof(XRayAnmPlayer)} on '{name}': effect asset is missing or has no preserved curves.", this);
                return null;
            }

            CaptureRestPose();
            var effect = new XRayAnmTrack(asset, weight, speed, loop, 0.0f);
            effect.Play(0.0f);
            _effects.Add(effect);
            Apply();
            return effect;
        }

        public void StopEffect(XRayAnmTrack effect)
        {
            if (effect != null && _effects.Remove(effect))
            {
                effect.Stop();
                Apply();
            }
        }

        public void Stop()
        {
            _track.Stop();
            Apply();
        }

        public void StopAll()
        {
            _track.Stop();
            foreach (XRayAnmTrack effect in _effects)
            {
                effect.Stop();
            }

            _effects.Clear();
            Apply();
        }

        public void Pause()
        {
            _track.Pause();
        }

        public void Resume()
        {
            _track.Resume();
        }

        public void Seek(float time)
        {
            _track.Seek(time);
            Apply();
        }

        public void Tick(float deltaTime)
        {
            if (_track.Advance(deltaTime) && _restoreOnFinish)
            {
                _track.Stop();
            }

            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                if (_effects[i].Advance(deltaTime))
                {
                    _effects[i].Stop();
                    _effects.RemoveAt(i);
                }
            }

            Apply();
        }

        private void OnEnable()
        {
            if (_playOnEnable && _track.Asset != null)
            {
                Play();
            }
        }

        private void OnDisable()
        {
            StopAll();
        }

        private void LateUpdate()
        {
            if (_driveSelf)
            {
                Tick(UnityEngine.Time.deltaTime);
            }
        }

        private void CaptureRestPose()
        {
            if (_hasRestPose)
            {
                return;
            }

            Transform target = Target;
            _restPosition = target.localPosition;
            _restRotation = target.localRotation;
            _hasRestPose = true;
        }

        private void Apply()
        {
            if (!_hasRestPose)
            {
                return;
            }

            Transform target = Target;
            bool primaryActive = _track.IsActive;
            if (!primaryActive && _effects.Count == 0)
            {
                target.localPosition = _restPosition;
                target.localRotation = _restRotation;
                _hasRestPose = false;
                return;
            }

            Vector3 position = _restPosition;
            Quaternion rotation = _restRotation;
            if (primaryActive)
            {
                if (_mode == XRayAnmApplyMode.Absolute)
                {
                    if (_track.Evaluate(out Vector3 animatedPosition, out Quaternion animatedRotation))
                    {
                        position = Vector3.LerpUnclamped(_restPosition, animatedPosition, _track.Weight);
                        rotation = Quaternion.SlerpUnclamped(_restRotation, animatedRotation, _track.Weight);
                    }
                }
                else
                {
                    Compose(_track, ref position, ref rotation);
                }
            }

            foreach (XRayAnmTrack effect in _effects)
            {
                Compose(effect, ref position, ref rotation);
            }

            target.localPosition = position;
            target.localRotation = rotation;
        }

        private void Compose(XRayAnmTrack track, ref Vector3 position, ref Quaternion rotation)
        {
            if (!track.IsActive || !track.Evaluate(out Vector3 deltaPosition, out Quaternion deltaRotation))
            {
                return;
            }

            if (_relativeToFirstFrame && track.Asset.Evaluate(0.0f, out Vector3 firstPosition, out Quaternion firstRotation))
            {
                Quaternion inverseFirst = Quaternion.Inverse(firstRotation);
                deltaPosition = inverseFirst * (deltaPosition - firstPosition);
                deltaRotation = inverseFirst * deltaRotation;
            }

            deltaPosition *= track.Weight;
            deltaRotation = Quaternion.SlerpUnclamped(Quaternion.identity, deltaRotation, track.Weight);
            position += rotation * deltaPosition;
            rotation = rotation * deltaRotation;
        }
    }
}

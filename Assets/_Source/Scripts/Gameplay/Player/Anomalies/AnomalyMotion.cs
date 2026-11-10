using UnityEngine;
using XRay.Importer;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRayAnmPlayer))]
    public sealed class AnomalyMotion : MonoBehaviour, IXRayAnmSource, IXRayAnomalyActivitySource
    {
        [SerializeField] private XRayAnmAsset _motion;
        [SerializeField] private bool _relativeToFirstFrame = true;

        private XRayAnmPlayer _player;
        private bool _sourceActive = true;

        public XRayAnmAsset Motion => _motion;
        public bool RelativeToFirstFrame => _relativeToFirstFrame;
        public bool CanDeactivate => true;

        public void SetSourceActive(bool active)
        {
            if (_sourceActive == active)
            {
                return;
            }
            _sourceActive = active;
            ResolvePlayer();
            if (active)
            {
                Restart();
            }
            else
            {
                _player.Pause();
            }
        }

        public void Configure(XRayAnmAsset motion, bool relativeToFirstFrame = true)
        {
            ResolvePlayer();
            _player.StopAll();
            _motion = motion;
            _relativeToFirstFrame = relativeToFirstFrame;
            ApplyConfiguration();
            if (isActiveAndEnabled)
            {
                Restart();
            }
        }

        public void Restart()
        {
            if (!_sourceActive)
            {
                return;
            }
            ResolvePlayer();
            ApplyConfiguration();
            if (_motion != null)
            {
                _player.Play(_motion);
            }
        }

        private void OnEnable()
        {
            Restart();
        }

        private void OnDisable()
        {
            if (_player != null)
            {
                _player.StopAll();
            }
        }

        private void ResolvePlayer()
        {
            if (_player == null)
            {
                _player = GetComponent<XRayAnmPlayer>();
            }
        }

        private void ApplyConfiguration()
        {
            _player.Target = transform;
            _player.Mode = _relativeToFirstFrame ? XRayAnmApplyMode.Additive : XRayAnmApplyMode.Absolute;
            _player.RelativeToFirstFrame = true;
            _player.PlayOnEnable = false;
            _player.DriveSelf = true;
            _player.Asset = _motion;
            _player.Loop = true;
            _player.Speed = 1f;
            _player.Weight = 1f;
        }
    }
}

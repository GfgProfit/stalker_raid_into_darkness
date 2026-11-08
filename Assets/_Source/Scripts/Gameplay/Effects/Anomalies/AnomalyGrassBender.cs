using UnityEngine;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Effects
{
    [DisallowMultipleComponent]
    public sealed class AnomalyGrassBender : MonoBehaviour
    {
        [SerializeField] private AnomalyVisual _visual;
        [SerializeField, Min(0f)] private float _fade = 1.33f;
        [SerializeField, Min(0f)] private float _speed = 3f;
        [SerializeField, Min(0f)] private float _intensity = 1f;
        [SerializeField, Min(0f)] private float _radius = 3f;

        private XRayFloraWindProvider _provider;

        private void Awake()
        {
            if (_visual == null)
            {
                _visual = GetComponent<AnomalyVisual>();
            }
        }

        private void OnEnable()
        {
            if (_visual != null)
            {
                _visual.DischargeStarted += OnDischargeStarted;
                _visual.Hidden += RemoveBenders;
            }
        }

        private void OnDisable()
        {
            if (_visual != null)
            {
                _visual.DischargeStarted -= OnDischargeStarted;
                _visual.Hidden -= RemoveBenders;
            }
            RemoveBenders();
        }

        private void OnDischargeStarted()
        {
            XRayFloraWindProvider provider = XRayFloraWindProvider.Active;
            if (isActiveAndEnabled && provider != null && provider.TryAddGrassExplosion(this, transform.position, _fade, _speed, _intensity, _radius))
            {
                _provider = provider;
            }
        }

        private void RemoveBenders()
        {
            if (_provider != null)
            {
                _provider.GrassBenders.RemoveOwner(this);
                _provider = null;
            }
        }
    }
}

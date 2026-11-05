using System;
using UnityEngine;

namespace XRay.Importer.Levels
{
    public enum XRayWetSurfaceType
    {
        Generic,
        Asphalt,
        Concrete,
        Stone,
        Soil,
        Metal,
        Wood,
        Vegetation,
        Excluded
    }

    [Serializable]
    public sealed class XRayWetSurfaceResponse
    {
        [SerializeField] private XRayWetSurfaceType _surface;
        [SerializeField, Range(0.0f, 1.0f)] private float _albedoMultiplier;
        [SerializeField, Min(0.0f)] private float _glossScale;
        [SerializeField, Range(0.0f, 1.0f)] private float _puddleSupport;

        public XRayWetSurfaceType Surface => _surface;
        public Vector4 Response => new Vector4(_albedoMultiplier, _glossScale, _puddleSupport, 1.0f);
        public bool IsValid => _surface >= XRayWetSurfaceType.Generic && _surface < XRayWetSurfaceType.Excluded && float.IsFinite(_albedoMultiplier) && _albedoMultiplier >= 0.0f && _albedoMultiplier <= 1.0f && float.IsFinite(_glossScale) && _glossScale >= 0.0f && float.IsFinite(_puddleSupport) && _puddleSupport >= 0.0f && _puddleSupport <= 1.0f;

        public XRayWetSurfaceResponse(XRayWetSurfaceType surface, Vector4 response)
        {
            _surface = surface;
            _albedoMultiplier = response.x;
            _glossScale = response.y;
            _puddleSupport = response.z;
        }

        public static XRayWetSurfaceResponse[] CreateDefaults()
        {
            var responses = new XRayWetSurfaceResponse[(int)XRayWetSurfaceType.Excluded];
            for (int i = 0; i < responses.Length; i++) responses[i] = new XRayWetSurfaceResponse((XRayWetSurfaceType)i, DefaultResponse((XRayWetSurfaceType)i));
            return responses;
        }

        public static Vector4 DefaultResponse(XRayWetSurfaceType surface)
        {
            switch (surface)
            {
                case XRayWetSurfaceType.Asphalt: return new Vector4(0.65f, 1.0f, 1.0f, 1.0f);
                case XRayWetSurfaceType.Concrete: return new Vector4(0.75f, 0.85f, 0.65f, 1.0f);
                case XRayWetSurfaceType.Stone: return new Vector4(0.82f, 0.75f, 0.45f, 1.0f);
                case XRayWetSurfaceType.Soil: return new Vector4(0.60f, 0.60f, 0.80f, 1.0f);
                case XRayWetSurfaceType.Metal: return new Vector4(0.95f, 1.0f, 0.70f, 1.0f);
                case XRayWetSurfaceType.Wood: return new Vector4(0.72f, 0.65f, 0.10f, 1.0f);
                case XRayWetSurfaceType.Vegetation: return new Vector4(0.90f, 0.30f, 0.0f, 1.0f);
                case XRayWetSurfaceType.Excluded: return Vector4.zero;
                default: return new Vector4(0.80f, 0.75f, 0.35f, 1.0f);
            }
        }
    }
}

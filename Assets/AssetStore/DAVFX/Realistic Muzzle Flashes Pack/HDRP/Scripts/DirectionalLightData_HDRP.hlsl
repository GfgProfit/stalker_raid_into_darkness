#ifndef DIRECTIONAL_POINT_LIGHTS_HDRP_INCLUDED
#define DIRECTIONAL_POINT_LIGHTS_HDRP_INCLUDED

static const float EPS = 1e-6;
static const float MIN_DIST = 1e-4;
static const float HDR_SCALE = 0.001;
static const int PUNCT_MAX = 4;

inline float CheapAttenuation(float dist, float range)
{
    range = (range > MIN_DIST) ? range : 4.0;
    float nd = dist / range;
    float atten = saturate(1.0 - (nd * nd));
    return atten * atten;
}

void DirectionalAndPointLightsColor_float(float3 PositionWS, out float3 Directional, out float3 Point)
{
#if SHADERGRAPH_PREVIEW
    float3 dir = float3(1.0, 1.0, 1.0);
    float3 pnt = float3(0.0, 0.0, 0.0);
    Directional = dir * HDR_SCALE;
    Point       = pnt * HDR_SCALE;
#else
    float3 dirColor = 0.0;
    float3 pntColor = 0.0;

    DirectionalLightData mainLight = _DirectionalLightDatas[0];
    if (dot(mainLight.color, mainLight.color) > EPS && mainLight.lightDimmer > EPS)
    {
        dirColor += mainLight.color * mainLight.lightDimmer;
    }

    [unroll]
    for (int i = 0; i < PUNCT_MAX; i++)
    {
        LightData L = _LightDatas[i];
        if (dot(L.color, L.color) > EPS && L.lightDimmer > EPS)
        {
            float3 toL = L.positionRWS - PositionWS;
            float dist = max(length(toL), MIN_DIST);
            float atten = CheapAttenuation(dist, L.range);

            pntColor += L.color * L.lightDimmer * atten;
        }
    }

    Directional = dirColor * HDR_SCALE;
    Point = pntColor * HDR_SCALE;
#endif
}

#endif

#ifndef HDRP_POINT_LIGHTS_INCLUDED
#define HDRP_POINT_LIGHTS_INCLUDED

#define HDRP_MAX_POINT_LIGHTS 8

float4 _HDRP_PointLightPos[HDRP_MAX_POINT_LIGHTS];
float4 _HDRP_PointLightColor[HDRP_MAX_POINT_LIGHTS];
int _HDRP_PointLightCount;

void EvaluatePointLights_float(
    float3 WorldPosition,
    out float3 Color
)
{
    float3 accum = float3(0.0, 0.0, 0.0);

    int count = min(_HDRP_PointLightCount, HDRP_MAX_POINT_LIGHTS);

    for (int i = 0; i < count; i++)
    {
        float3 lightPos = _HDRP_PointLightPos[i].xyz;
        float range = max(0.0001, _HDRP_PointLightPos[i].w);

        float3 diff = lightPos - WorldPosition;
        float dist = length(diff);

        float atten = saturate(1.0 - dist / range);

        accum += _HDRP_PointLightColor[i].rgb * atten;
    }

    Color = accum;
}

#endif

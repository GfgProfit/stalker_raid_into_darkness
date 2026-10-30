#ifndef MONOLITH_FLORA_WIND_INCLUDED
#define MONOLITH_FLORA_WIND_INCLUDED

#include "MonolithFlora.hlsl"
#define MONOLITH_FLORA_WIND_ANOMALY 1.0
#define MONOLITH_FLORA_WIND_TRUE_STALKER 2.0

bool MonolithFloraWindEnabled(float materialWind, float hasWindData)
{
    return materialWind > 0.5 && hasWindData > 0.5 && _MonolithWindActive > 0.5 && abs(materialWind - _MonolithWindActive) < 0.5;
}

float3 MonolithFloraWindOffset(float profile, float part, bool shadowPass, bool previous, float3 positionAWS, float baseHeight, float tcY, float rigidity)
{
    float treeHeight = positionAWS.y - baseHeight;
    if (profile < 1.5)
    {
        MonolithWindSetup setup = MonolithSsfxWindSetup(_MonolithWindParams, float4(0.0, 0.0, 0.0, 0.0), _MonolithWindTreesSetup);
        float4 anim = previous ? _MonolithWindAnimPrev : _MonolithWindAnim;
        if (part > 0.5)
        {
            float3 trunk = MonolithSsfxWindTrunk(baseHeight, treeHeight, anim, setup);
            return float3(trunk.x, 0.0, trunk.y);
        }
        float flip = _MonolithWindWavesFlipV > 0.5 ? -1.0 : 1.0;
        float3 position = float3(positionAWS.x, positionAWS.y, positionAWS.z * flip);
        float4 flowAnim = float4(anim.x, anim.y * flip, anim.z, anim.w);
        float3 branch = MonolithSsfxWindBranches(TEXTURE2D_ARGS(_MonolithWindWaves, s_linear_repeat_sampler), position, baseHeight, treeHeight, tcY, flowAnim, setup);
        return branch;
    }
    float4 wind = previous ? _MonolithTreeWindPrev : _MonolithTreeWind;
    float4 wave = previous ? _MonolithTreeWavePrev : _MonolithTreeWave;
    float amplitude = !shadowPass && part < 0.5 ? 2.0 : 1.0;
    float stiffness = shadowPass && part > 0.5 ? 0.0 : rigidity;
    float2 offset = MonolithXRayTreeWind(positionAWS, baseHeight, wave, wind, stiffness, amplitude);
    return float3(offset.x, 0.0, offset.y);
}

#endif

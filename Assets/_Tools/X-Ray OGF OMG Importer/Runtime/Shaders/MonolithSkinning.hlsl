#if defined(_MONOLITH_SKINNED_TBN) && defined(ATTRIBUTES_NEED_COLOR) && defined(ATTRIBUTES_NEED_TEXCOORD3)
#define HAVE_MESH_MODIFICATION
StructuredBuffer<float4> _MonolithBoneRows;

float3 MonolithSkinDirection(float3 direction, float3 row0, float3 row1, float3 row2)
{
    return float3(dot(row0, direction), dot(row1, direction), dot(row2, direction));
}

AttributesMesh ApplyMeshModification(AttributesMesh input, float3 timeParameters)
{
    int bone = (int)round(input.color.a) - 2;
    if (_MonolithSkinningActive < 0.5 || bone < 0 || bone >= (int)_MonolithBoneCount)
    {
        input.color.a = 0.0;
        return input;
    }
    float3 row0 = _MonolithBoneRows[bone * 3].xyz;
    float3 row1 = _MonolithBoneRows[bone * 3 + 1].xyz;
    float3 row2 = _MonolithBoneRows[bone * 3 + 2].xyz;
    float3 t = MonolithSkinDirection(input.color.rgb, row0, row1, row2);
    float3 b = MonolithSkinDirection(float3(input.uv1, input.uv2.x), row0, row1, row2);
    float3 n = MonolithSkinDirection(float3(input.uv2.y, input.uv3), row0, row1, row2);
    input.color = float4(t, 2.0);
    input.uv1 = b.xy;
    input.uv2 = float2(b.z, n.x);
    input.uv3 = n.yz;
    return input;
}
#elif defined(_MONOLITH_LEVEL_DETAIL)
#define HAVE_MESH_MODIFICATION
#include "MonolithFlora.hlsl"

float MonolithDetailCyclic(float x)
{
    float f = 2.8284271 * frac(x) - 1.4142136;
    return f * f - 1.0;
}

AttributesMesh ApplyMeshModification(AttributesMesh input, float3 timeParameters)
{
    float4 instance = MonolithInstanceData();
    if (_MonolithGrassRuntime > 0.5)
    {
        bool previous = timeParameters.x == MONOLITH_PREVIOUS_FRAME_TIME;
        input.positionOS *= previous ? _MonolithGrassHeightPrevious : _MonolithGrassHeight;
        float3 alignedPosition = TransformObjectToWorld(input.positionOS);
        float alignedHeight = alignedPosition.y - GetObjectToWorldMatrix()._m13;
        alignedPosition.xz -= 0.5 * MonolithInstanceExtra().xz * alignedHeight * _MonolithGrassAlign;
        input.positionOS = TransformWorldToObject(alignedPosition);
        float sourceSize = previous ? _MonolithGrassHeightPrevious : _MonolithGrassHeight;
        input.uv1.x = MonolithAnomalyGrassVariation(GetAbsolutePositionWS(alignedPosition), GetObjectToWorldMatrix()._m00 * sourceSize, _MonolithFloraVariation.y);
        if (_MonolithGrassSource > 0.5 && _MonolithGrassSource < 1.5 && _MonolithLevelWind > 0.5 && instance.z > 0.5 && _MonolithWindActive > 0.5 && _MonolithWindActive < 1.5)
        {
            float3 positionAWS = GetAbsolutePositionWS(TransformObjectToWorld(input.positionOS));
            float baseHeight = GetAbsolutePositionWS(GetObjectToWorldMatrix()._m03_m13_m23).y;
            MonolithWindSetup setup = MonolithSsfxWindSetup(_MonolithWindParams, _MonolithWindGrassSetup, _MonolithWindTreesSetup);
            float4 anim = previous ? _MonolithWindAnimPrev : _MonolithWindAnim;
            float flip = _MonolithWindWavesFlipV > 0.5 ? -1.0 : 1.0;
            float3 position = float3(positionAWS.x, positionAWS.y, positionAWS.z * flip);
            float4 flowAnim = float4(anim.x, anim.y * flip, anim.z, anim.w);
            float3 offset = MonolithSsfxWindGrass(TEXTURE2D_ARGS(_MonolithWindWaves, s_linear_repeat_sampler), position, positionAWS.y - baseHeight, flowAnim, setup);
            input.positionOS += mul((float3x3)GetWorldToObjectMatrix(), offset);
        }
        return input;
    }
    if (_MonolithLevelWind < 0.5 || instance.z < 0.5)
    {
        return input;
    }
    float4 wave = instance.z > 1.5 ? _MonolithDetailWave2 : _MonolithDetailWave1;
    float4 wind = instance.z > 1.5 ? _MonolithDetailWind2 : _MonolithDetailWind1;
    float3 positionWS = TransformObjectToWorld(input.positionOS);
    float3 originWS = GetObjectToWorldMatrix()._m03_m13_m23;
    float3 absolutePosition = GetAbsolutePositionWS(positionWS);
    float dp = MonolithDetailCyclic(dot(absolutePosition, wave.xyz) + wave.w);
    float height = positionWS.y - originWS.y;
    float fraction = input.positionOS.y * instance.y;
    float2 offset = wind.xz * (height * dp) * fraction;
    input.positionOS += mul((float3x3)GetWorldToObjectMatrix(), float3(offset.x, 0.0, offset.y));
    return input;
}
#elif defined(_MONOLITH_LEVEL) && defined(ATTRIBUTES_NEED_TEXCOORD0) && defined(ATTRIBUTES_NEED_TEXCOORD3)
#define HAVE_MESH_MODIFICATION
#include "MonolithFloraWind.hlsl"

AttributesMesh ApplyMeshModification(AttributesMesh input, float3 timeParameters)
{
#if defined(ATTRIBUTES_NEED_COLOR)
    if ((_MonolithSourceProfile > 0.5 && _MonolithSourceTerrain > 0.5 && _MonolithLevelTerrain > 0.5 && _MonolithTerrainSource > 0.5 && _MonolithTerrainSource < 1.5 && _MonolithHasTerrainSourceColor > 0.5) || MonolithTrueStalkerTerrainEnabled())
    {
        input.color.a = input.normalOS.x;
        input.uv3 = input.normalOS.yz;
        return input;
    }
#endif
    if (MonolithFloraWindEnabled(_MonolithFloraWind, input.uv3.y))
    {
        bool previous = timeParameters.x == MONOLITH_PREVIOUS_FRAME_TIME;
        float4x4 objectToWorld = previous ? GetPrevObjectToWorldMatrix() : GetObjectToWorldMatrix();
        float4x4 worldToObject = previous ? GetPrevWorldToObjectMatrix() : GetWorldToObjectMatrix();
        float3 positionAWS = GetAbsolutePositionWS(mul(objectToWorld, float4(input.positionOS, 1.0)).xyz);
        float baseHeight = GetAbsolutePositionWS(objectToWorld._m03_m13_m23).y;
        float3 offset = MonolithFloraWindOffset(_MonolithFloraWind, _MonolithFloraWindPart, SHADERPASS == SHADERPASS_SHADOWS, previous, positionAWS, baseHeight, 1.0 - input.uv0.y, input.uv3.x);
        input.positionOS += mul((float3x3)worldToObject, offset);
    }
    return input;
}
#endif

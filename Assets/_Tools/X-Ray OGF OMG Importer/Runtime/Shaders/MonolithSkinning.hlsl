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

float MonolithDetailCyclic(float x)
{
    float f = 2.8284271 * frac(x) - 1.4142136;
    return f * f - 1.0;
}

AttributesMesh ApplyMeshModification(AttributesMesh input, float3 timeParameters)
{
    float4 instance = MonolithInstanceData();
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
#elif defined(_MONOLITH_LEVEL) && defined(ATTRIBUTES_NEED_COLOR) && defined(ATTRIBUTES_NEED_TEXCOORD3)
#define HAVE_MESH_MODIFICATION

AttributesMesh ApplyMeshModification(AttributesMesh input, float3 timeParameters)
{
    if ((_MonolithSourceProfile > 0.5 && _MonolithSourceTerrain > 0.5 && _MonolithLevelTerrain > 0.5 && _MonolithTerrainSource > 0.5 && _MonolithTerrainSource < 1.5 && _MonolithHasTerrainSourceColor > 0.5) || MonolithTrueStalkerTerrainEnabled())
    {
        input.color.a = input.normalOS.x;
        input.uv3 = input.normalOS.yz;
    }
    return input;
}
#endif

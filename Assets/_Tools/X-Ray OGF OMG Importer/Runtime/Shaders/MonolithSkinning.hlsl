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
#endif

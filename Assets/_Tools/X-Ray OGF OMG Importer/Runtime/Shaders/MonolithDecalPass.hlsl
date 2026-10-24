#ifndef MONOLITH_DECAL_PASS_INCLUDED
#define MONOLITH_DECAL_PASS_INCLUDED

TEXTURE2D(_BaseColorMap);

CBUFFER_START(UnityPerMaterial)
float4 _BaseColorMap_ST;
float _MonolithAlbedoSrgb;
float _MonolithDecalNormalShift;
float _MonolithDecalMinShift;
float _MonolithDecalMaxShift;
float _MonolithDecalProjectionShift;
float _MonolithDecalViewShift;
float _MonolithDecalXRayNear;
float _MonolithDecalAlphaRef;
CBUFFER_END

struct MonolithDecalAttributes
{
    float3 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv0 : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct MonolithDecalVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv0 : TEXCOORD0;
    UNITY_VERTEX_OUTPUT_STEREO
};

float3 MonolithWallmarkShift(float3 positionRWS, float3 normalWS)
{
    float3 eye = GetPrimaryCameraPosition();
    float3 eyeDirection = GetViewForwardDir();
    float d = length(eye - positionRWS);
    float s = lerp(_MonolithDecalMinShift, _MonolithDecalMaxShift, d);
    float3 p = positionRWS + normalWS * _MonolithDecalNormalShift;
    p -= normalize(eyeDirection + normalize(p - eye)) * s;
    return p - eyeDirection * _MonolithDecalViewShift;
}

MonolithDecalVaryings Vert(MonolithDecalAttributes input)
{
    MonolithDecalVaryings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    float3 positionRWS = TransformObjectToWorld(input.positionOS);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.positionCS = TransformWorldToHClip(MonolithWallmarkShift(positionRWS, normalWS));
    float projectionShift = _MonolithDecalProjectionShift * _ProjectionParams.y / max(_MonolithDecalXRayNear, 1e-4);
#if UNITY_REVERSED_Z
    output.positionCS.z += projectionShift;
#else
    output.positionCS.z -= projectionShift;
#endif
    output.uv0 = input.uv0 * _BaseColorMap_ST.xy + _BaseColorMap_ST.zw;
    return output;
}

void Frag(MonolithDecalVaryings input, OUTPUT_DBUFFER(outDBuffer))
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float4 texel = SAMPLE_TEXTURE2D(_BaseColorMap, s_trilinear_clamp_sampler, input.uv0);
    clip(texel.a - _MonolithDecalAlphaRef - 1e-6);
    float3 encoded = _MonolithAlbedoSrgb > 0.5 ? LinearToSRGB(texel.rgb) : texel.rgb;
    DecalSurfaceData surfaceData;
    ZERO_INITIALIZE(DecalSurfaceData, surfaceData);
    surfaceData.baseColor = float4(SRGBToLinear(saturate(encoded)), texel.a);
    surfaceData.normalWS = float4(0.0, 0.0, 0.0, 1.0);
    surfaceData.mask = float4(0.0, 0.0, 0.0, 1.0);
    ENCODE_INTO_DBUFFER(surfaceData, outDBuffer);
}

#endif

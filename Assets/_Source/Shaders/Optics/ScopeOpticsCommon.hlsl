#ifndef RAID_SCOPE_OPTICS_COMMON_INCLUDED
#define RAID_SCOPE_OPTICS_COMMON_INCLUDED

CBUFFER_START(UnityPerMaterial)
    float4 _ReticleScale;
    float4 _ReticleCenter;
    float4 _ReticleOffset;
    float4 _ReticleKeyParams;
    float4 _ReticleOptions;
    float4 _IlluminationColor;
    float4 _EyeAperture;
    float4 _OpticalDistortion;
    float4 _OpticalFalloff;
    float4 _CoatingTint;
    float4 _ScopeTexture_TexelSize;
    float _ScopeAvailable;
    float _ReticleRotation;
    float _ReticleMipBias;
    float _ColorKeyUnpremultiply;
    float _IlluminationIntensity;
    float _EyeTransmission;
    float _ApertureSoftness;
    float _CullMode;
CBUFFER_END

TEXTURE2D(_ScopeTexture);
SAMPLER(sampler_ScopeTexture);
TEXTURE2D(_ReticleTexture);
SAMPLER(sampler_ReticleTexture);
TEXTURE2D(_ReticleAlphaMask);
SAMPLER(sampler_ReticleAlphaMask);
TEXTURE2D(_IlluminationMask);
SAMPLER(sampler_IlluminationMask);

struct ScopeAttributes
{
    float3 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct ScopeVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float2 uv : TEXCOORD2;
    UNITY_VERTEX_OUTPUT_STEREO
};

float ScopeInsideUv(float2 uv)
{
    return step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
}

void ScopeClipLens(float2 uv)
{
    float2 centered = uv * 2.0 - 1.0;
    clip(1.0 - dot(centered, centered));
}

float ScopeEyeVisibility(float2 uv)
{
    float2 radius = max(_EyeAperture.zw, 0.00001);
    float2 pupil = (uv - _EyeAperture.xy) / radius;
    float distanceFromPupil = length(pupil);
    float softness = max(max(fwidth(distanceFromPupil), _ApertureSoftness / min(radius.x, radius.y)), 0.00001);
    return (1.0 - smoothstep(1.0 - softness, 1.0 + softness, distanceFromPupil)) * saturate(_EyeTransmission);
}

#endif

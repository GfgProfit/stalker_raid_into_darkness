#ifndef RAID_SCOPE_PRO_LENS_INCLUDED
#define RAID_SCOPE_PRO_LENS_INCLUDED

CBUFFER_START(UnityPerMaterial)
    float4 _ReticleColor;
    float4 _ScopeRightWS;
    float4 _ScopeUpWS;
    float _ReticleSize;
    float _ScopeDepth;
    float _ScopeApperture;
    float _ScopeFade;
    float _RenderSize;
    float _CullMode;
    float _ScopeProStyle;
    float _ScopeAvailable;
    float _ReticleRuntimeScale;
CBUFFER_END

TEXTURE2D(_Reticle);
TEXTURE2D(_RenderTexture);
SAMPLER(sampler_RenderTexture);

struct ScopeProAttributes
{
    float3 positionOS : POSITION;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct ScopeProVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float2 uv : TEXCOORD1;
    UNITY_VERTEX_OUTPUT_STEREO
};

ScopeProVaryings ScopeProVertex(ScopeProAttributes input)
{
    ScopeProVaryings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.positionWS = TransformObjectToWorld(input.positionOS);
    output.positionCS = TransformWorldToHClip(output.positionWS);
    output.uv = input.uv;
    return output;
}

float4 ScopeProDepth(ScopeProVaryings input) : SV_Target
{
    return 0.0;
}

float4 ScopeProFragment(ScopeProVaryings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    if (_ScopeAvailable < 0.5)
    {
        return float4(0.0, 0.0, 0.0, 1.0);
    }

    float3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
    float2 viewDirectionTangent = float2(dot(viewDirection, _ScopeRightWS.xyz), dot(viewDirection, _ScopeUpWS.xyz));
    float2 centered = input.uv - viewDirectionTangent - 0.5;
    float radius = length(centered * max(_ScopeDepth, 0.0));
    float fade = min(_ScopeFade, _ScopeApperture - 0.0001);
    float aperture = 1.0 - smoothstep(fade, max(_ScopeApperture, fade + 0.0001), radius);
    float2 worldUv = centered * clamp(_RenderSize, 0.001, 2.0) + 0.5;
    float2 reticleUv = centered * max(_ReticleSize, 0.0001) / max(_ReticleRuntimeScale, 0.0001) + 0.5;
    float3 world = SAMPLE_TEXTURE2D(_RenderTexture, sampler_RenderTexture, worldUv).rgb;
    float reticleInside = step(0.0, reticleUv.x) * step(reticleUv.x, 1.0) * step(0.0, reticleUv.y) * step(reticleUv.y, 1.0);
    float4 reticle = SAMPLE_TEXTURE2D(_Reticle, s_linear_clamp_sampler, saturate(reticleUv));
    float3 exposedWorld = max(world, 0.0) * GetCurrentExposureMultiplier();
    float3 reticleColor = saturate(reticle.rgb) * max(_ReticleColor.rgb, 0.0);
    float3 color = lerp(exposedWorld, reticleColor, saturate(reticle.a) * reticleInside) * aperture;
    return float4(max(color, 0.0), 1.0);
}

#endif

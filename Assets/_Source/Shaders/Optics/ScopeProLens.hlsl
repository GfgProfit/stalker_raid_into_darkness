#ifndef RAID_SCOPE_PRO_LENS_INCLUDED
#define RAID_SCOPE_PRO_LENS_INCLUDED

CBUFFER_START(UnityPerMaterial)
    float4 _ReticleColor;
    float4 _LensTint;
    float _LensReflectionStrength;
    float _LensEnvironmentIntensity;
    float _LensRoughness;
    float _LensAdsReflection;
    float4 _LensTransmissionTint;
    float _LensUseEnvironment;
    float _ScopeVisibility;
    float4 _IlluminationColor;
    float4 _ScopeRightWS;
    float4 _ScopeUpWS;
    float4 _ScopeLensCenterWS;
    float4 _ScopeLensHalfSize;
    float _ScopeCalibratedAperture;
    float _ScopeApertureShape;
    float _ScopeEyeRelief;
    float4 _ScopeEyeReference;
    float _ReticleSize;
    float _ScopeDepth;
    float _ScopeApperture;
    float _ScopeFade;
    float _RenderSize;
    float _CullMode;
    float _ScopeProStyle;
    float _ScopeAvailable;
    float _ReticleRuntimeScale;
    float _ReticleMode;
    float _ReticleSdfAvailable;
    float _IlluminationIntensity;
    float _ReticleExtendLeft;
    float _ReticleExtendRight;
    float _ReticleExtendUp;
    float _ReticleExtendDown;
CBUFFER_END

TEXTURECUBE(_LensEnvironment);
TEXTURE2D(_Reticle);
TEXTURE2D(_ReticleSdf);
TEXTURE2D(_RenderTexture);
SAMPLER(sampler_RenderTexture);

struct ScopeProAttributes
{
    float3 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct ScopeProVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float2 uv : TEXCOORD1;
    float3 normalWS : TEXCOORD2;
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
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    return output;
}

float4 ScopeProDepth(ScopeProVaryings input) : SV_Target
{
    return 0.0;
}

#if defined(WRITE_MSAA_DEPTH)
void ScopeProMotionVectors(ScopeProVaryings input, out float4 depthColor : SV_Target0, out float4 motionVector : SV_Target1)
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    depthColor = float4(input.positionCS.zzz, 1.0);
    motionVector = float4(2.0, 0.0, 0.0, 0.0);
}
#else
float4 ScopeProMotionVectors(ScopeProVaryings input) : SV_Target0
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    return float4(2.0, 0.0, 0.0, 0.0);
}
#endif

float2 ScopeProSdfCoverage(float2 field)
{
    float2 gradientX = ddx(field);
    float2 gradientY = ddy(field);
    float2 halfWidth = max(0.5 * sqrt(gradientX * gradientX + gradientY * gradientY), 0.00001);
    return smoothstep(0.5 - halfWidth, 0.5 + halfWidth, field);
}

float4 ScopeProSampleSdf(float2 uv)
{
    float2 field = SAMPLE_TEXTURE2D(_ReticleSdf, s_linear_clamp_sampler, saturate(uv)).rg;
    float2 coverage = ScopeProSdfCoverage(field);
    float intensity = max(_IlluminationIntensity, 0.0);
    float illuminationBlend = saturate(intensity);
    float3 ink = max(_ReticleColor.rgb, 0.0);
    float3 illuminatedInk = ink * (1.0 - illuminationBlend) + max(_IlluminationColor.rgb, 0.0) * intensity;
    float illuminatedOpacity = lerp(saturate(_ReticleColor.a), saturate(_IlluminationColor.a), illuminationBlend);
    float inkAlpha = coverage.r * (1.0 - coverage.g) * saturate(_ReticleColor.a);
    float illuminationAlpha = coverage.g * illuminatedOpacity;
    return float4(ink * inkAlpha + illuminatedInk * illuminationAlpha, inkAlpha + illuminationAlpha);
}

float ScopeProSdfBounds(float2 uv, float inside)
{
    float2 lower = step(0.0, uv);
    float2 upper = step(uv, 1.0);
    float2 insideAxes = lower * upper;
    float horizontal = ((1.0 - lower.x) * saturate(_ReticleExtendLeft) + (1.0 - upper.x) * saturate(_ReticleExtendRight)) * insideAxes.y;
    float vertical = ((1.0 - lower.y) * saturate(_ReticleExtendDown) + (1.0 - upper.y) * saturate(_ReticleExtendUp)) * insideAxes.x;
    return saturate(inside + horizontal + vertical);
}

float3 ScopeProLensReflection(ScopeProVaryings input)
{
    float3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
    float3 normal = normalize(input.normalWS);
    float3 direction = reflect(-viewDirection, normal);
    float mip = saturate(_LensRoughness) * 6.0;
    float3 environment;
    if (_LensUseEnvironment > 0.5)
    {
        environment = SAMPLE_TEXTURECUBE_LOD(_LensEnvironment, s_trilinear_clamp_sampler, direction, mip).rgb;
    }
    else
    {
        environment = SampleSkyTexture(direction, mip, 0).rgb;
    }

    float fresnel = 0.04 + 0.96 * pow(1.0 - saturate(abs(dot(normal, viewDirection))), 5.0);
    return max(environment, 0.0) * max(_LensTint.rgb, 0.0) * max(_LensReflectionStrength, 0.0) * max(_LensEnvironmentIntensity, 0.0) * fresnel * GetCurrentExposureMultiplier();
}

float4 ScopeProFragment(ScopeProVaryings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float visibility = saturate(_ScopeVisibility) * step(0.5, _ScopeAvailable);
    float3 reflection = 0.0;
    if (visibility < 1.0 || _LensAdsReflection > 0.0)
    {
        reflection = ScopeProLensReflection(input);
    }
    if (visibility <= 0.0)
    {
        return float4(reflection, 1.0);
    }

    float3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
    float2 viewDirectionTangent = float2(dot(viewDirection, _ScopeRightWS.xyz), dot(viewDirection, _ScopeUpWS.xyz));
    float2 centered = input.uv - viewDirectionTangent - 0.5;
    float2 pupilOffset = 0.0;
    if (_ScopeCalibratedAperture > 0.5)
    {
        float3 relative = input.positionWS - GetCameraRelativePositionWS(_ScopeLensCenterWS.xyz);
        float2 lensPosition = float2(dot(relative, _ScopeRightWS.xyz), dot(relative, _ScopeUpWS.xyz));
        centered = lensPosition / max(2.0 * _ScopeLensHalfSize.xy, 0.0001);
        float3 eyeToCenter = _ScopeLensCenterWS.xyz - _WorldSpaceCameraPos;
        float2 eyeOffset = float2(dot(eyeToCenter, _ScopeRightWS.xyz), dot(eyeToCenter, _ScopeUpWS.xyz)) - _ScopeEyeReference.xy;
        pupilOffset = eyeOffset * (_ScopeEyeRelief / 2.0) / max(2.0 * _ScopeLensHalfSize.xy, 0.0001);
    }
    float2 pupilPosition = (centered + pupilOffset) * max(_ScopeDepth, 0.0);
    float radius = _ScopeApertureShape > 0.5 ? max(abs(pupilPosition.x), abs(pupilPosition.y)) : length(pupilPosition);
    float fade = min(_ScopeFade, _ScopeApperture - 0.0001);
    float aperture = 1.0 - smoothstep(fade, max(_ScopeApperture, fade + 0.0001), radius);
    float2 worldUv = centered * clamp(_RenderSize, 0.001, 2.0) + 0.5;
    float2 reticleUv = centered * max(_ReticleSize, 0.0001) / max(_ReticleRuntimeScale, 0.0001) + 0.5;
    float3 world = SAMPLE_TEXTURE2D(_RenderTexture, sampler_RenderTexture, worldUv).rgb;
    float reticleInside = step(0.0, reticleUv.x) * step(reticleUv.x, 1.0) * step(0.0, reticleUv.y) * step(reticleUv.y, 1.0);
    float3 exposedWorld = max(world, 0.0) * max(_LensTransmissionTint.rgb, 0.0) * GetCurrentExposureMultiplier();
    float3 color;
    if (_ReticleMode > 0.5 && _ReticleSdfAvailable > 0.5)
    {
        float4 reticle = ScopeProSampleSdf(reticleUv) * ScopeProSdfBounds(reticleUv, reticleInside);
        color = (exposedWorld * (1.0 - reticle.a) + reticle.rgb) * aperture;
    }
    else
    {
        float4 reticle = SAMPLE_TEXTURE2D(_Reticle, s_linear_clamp_sampler, saturate(reticleUv));
        float3 reticleColor = saturate(reticle.rgb) * max(_ReticleColor.rgb, 0.0);
        color = lerp(exposedWorld, reticleColor, saturate(reticle.a) * reticleInside) * aperture;
    }

    color += reflection * _LensAdsReflection * aperture;
    return float4(lerp(reflection, max(color, 0.0), visibility), 1.0);
}

#endif

#ifndef RAID_SCOPE_GLASS_INCLUDED
#define RAID_SCOPE_GLASS_INCLUDED

float3 ScopeSampleWorld(float2 uv)
{
    float3 color = SAMPLE_TEXTURE2D(_ScopeTexture, sampler_ScopeTexture, saturate(uv)).rgb;
    if (_OpticalDistortion.w < 0.999)
    {
        float2 texel = abs(_ScopeTexture_TexelSize.xy);
        float3 blurred = SAMPLE_TEXTURE2D(_ScopeTexture, sampler_ScopeTexture, saturate(uv + float2(texel.x, 0.0))).rgb;
        blurred += SAMPLE_TEXTURE2D(_ScopeTexture, sampler_ScopeTexture, saturate(uv - float2(texel.x, 0.0))).rgb;
        blurred += SAMPLE_TEXTURE2D(_ScopeTexture, sampler_ScopeTexture, saturate(uv + float2(0.0, texel.y))).rgb;
        blurred += SAMPLE_TEXTURE2D(_ScopeTexture, sampler_ScopeTexture, saturate(uv - float2(0.0, texel.y))).rgb;
        color = lerp(blurred * 0.25, color, saturate(_OpticalDistortion.w));
    }

    return color * ScopeInsideUv(uv);
}

float3 ScopeSampleGlass(float2 lensUv, float3 positionWS, float3 normalWS)
{
    float2 centered = lensUv * 2.0 - 1.0;
    float radiusSquared = dot(centered, centered);
    float radialScale = 1.0 + _OpticalDistortion.x * radiusSquared + _OpticalDistortion.y * radiusSquared * radiusSquared;
    float2 imageUv = centered * radialScale * 0.5 + 0.5;
    float3 world = ScopeSampleWorld(imageUv);
    if (_OpticalDistortion.z > 0.0)
    {
        float2 chromaticOffset = centered * radiusSquared * _OpticalDistortion.z;
        world.r = ScopeSampleWorld(imageUv + chromaticOffset).r;
        world.b = ScopeSampleWorld(imageUv - chromaticOffset).b;
    }

    float vignette = 1.0 - saturate(_OpticalFalloff.x) * radiusSquared * radiusSquared;
    float edgeContrast = saturate(_OpticalFalloff.y) * radiusSquared;
    world = lerp(world, float3(0.18, 0.18, 0.18), edgeContrast) * vignette;
    float3 viewDirection = GetWorldSpaceNormalizeViewDir(positionWS);
    float fresnel = pow(1.0 - saturate(abs(dot(normalize(normalWS), viewDirection))), 5.0);
    float reflection = saturate(_OpticalFalloff.z) * (0.04 + 0.96 * fresnel);
    return lerp(world, max(_CoatingTint.rgb, 0.0), reflection);
}

#endif

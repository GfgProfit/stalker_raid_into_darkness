#ifndef RAID_SCOPE_RETICLE_INCLUDED
#define RAID_SCOPE_RETICLE_INCLUDED

float2 ScopeReticleUv(float2 lensUv)
{
    float2 centered = lensUv - 0.5 - _ReticleOffset.xy;
    float sine;
    float cosine;
    sincos(_ReticleRotation, sine, cosine);
    centered = float2(cosine * centered.x + sine * centered.y, -sine * centered.x + cosine * centered.y);
    return centered / max(_ReticleScale.xy, 0.00001) + _ReticleCenter.xy;
}

void ScopeReticleGradients(float2 uv, out float2 gradientX, out float2 gradientY)
{
    float bias = exp2(clamp(_ReticleMipBias, -4.0, 4.0));
    gradientX = ddx(uv) * bias;
    gradientY = ddy(uv) * bias;
}

float4 ScopeSampleReticle(float2 lensUv)
{
    float2 uv = ScopeReticleUv(lensUv);
    float2 gradientX;
    float2 gradientY;
    ScopeReticleGradients(uv, gradientX, gradientY);
    float4 reticleSample = SAMPLE_TEXTURE2D_GRAD(_ReticleTexture, sampler_ReticleTexture, uv, gradientX, gradientY);
    float alpha = reticleSample.a;
    if (_ReticleKeyParams.x > 0.5 && _ReticleKeyParams.x < 1.5)
    {
        float3 encoded = _ReticleKeyParams.w > 0.5 ? LinearToSRGB(max(reticleSample.rgb, 0.0)) : reticleSample.rgb;
        float intensity = max(encoded.r, max(encoded.g, encoded.b));
        alpha = smoothstep(_ReticleKeyParams.y, _ReticleKeyParams.y + max(_ReticleKeyParams.z, 0.00001), intensity);
        float3 restored = lerp(encoded, saturate(encoded / max(alpha, 0.00001)), saturate(_ColorKeyUnpremultiply));
        reticleSample.rgb = _ReticleKeyParams.w > 0.5 ? SRGBToLinear(restored) : restored;
    }
    else if (_ReticleKeyParams.x > 1.5)
    {
        alpha = SAMPLE_TEXTURE2D_GRAD(_ReticleAlphaMask, sampler_ReticleAlphaMask, uv, gradientX, gradientY).r * saturate(_ReticleOptions.y);
    }

    reticleSample.a = saturate(alpha) * ScopeInsideUv(uv) * saturate(_ReticleOptions.x);
    return reticleSample;
}

float3 ScopeSampleIllumination(float2 lensUv)
{
    if (_IlluminationIntensity <= 0.0 || _ReticleOptions.z < 0.5)
    {
        return 0.0;
    }

    float2 uv = ScopeReticleUv(lensUv);
    float2 gradientX;
    float2 gradientY;
    ScopeReticleGradients(uv, gradientX, gradientY);
    float mask = SAMPLE_TEXTURE2D_GRAD(_IlluminationMask, sampler_IlluminationMask, uv, gradientX, gradientY).r;
    return saturate(mask) * ScopeInsideUv(uv) * max(_IlluminationColor.rgb, 0.0) * max(_IlluminationIntensity, 0.0);
}

#endif

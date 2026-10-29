#ifndef MONOLITH_TRUE_STALKER_TERRAIN_INCLUDED
#define MONOLITH_TRUE_STALKER_TERRAIN_INCLUDED

float4 MonolithTrueStalkerWeights(float4 mask)
{
    float sum = dot(mask, 1.0);
    return all(isfinite(mask)) && all(mask >= 0.0) && sum > 0.0 ? mask / sum : float4(1.0, 0.0, 0.0, 0.0);
}

float3 MonolithTrueStalkerLayerNormal(float4 packed)
{
    return all(isfinite(packed)) ? packed.abg - 0.5 : float3(0.0, 0.0, 0.5);
}

void MonolithTrueStalkerBlend(float3 baseColor, float4 weights, float3 colorR, float3 colorG, float3 colorB, float3 colorA, float4 bumpR, float4 bumpG, float4 bumpB, float4 bumpA, out float3 color, out float3 normal, out float gloss)
{
    float3 detail = colorR * weights.r + colorG * weights.g + colorB * weights.b + colorA * weights.a;
    color = 2.0 * baseColor * detail;
    normal = MonolithTrueStalkerLayerNormal(bumpR) * weights.r + MonolithTrueStalkerLayerNormal(bumpG) * weights.g + MonolithTrueStalkerLayerNormal(bumpB) * weights.b + MonolithTrueStalkerLayerNormal(bumpA) * weights.a;
    normal *= float3(0.6, 0.6, 1.0);
    gloss = bumpR.r * weights.r + bumpG.r * weights.g + bumpB.r * weights.b + bumpA.r * weights.a;
}

float3 MonolithTrueStalkerNormal(float3 tangent, float3 bitangent, float3 geometric, float3 normal)
{
    float3 result = tangent * normal.x + bitangent * normal.y + geometric * normal.z;
    if (all(isfinite(result)) && dot(result, result) > 1e-20) return normalize(result);
    return all(isfinite(geometric)) && dot(geometric, geometric) > 1e-20 ? normalize(geometric) : float3(0.0, 1.0, 0.0);
}

float3 MonolithTrueStalkerPosition(float3 position, float3 shadedNormal)
{
    return position + shadedNormal * 0.025;
}

float MonolithTrueStalkerMaterial(float material, bool local)
{
    return !local && abs(material - 0.95) <= 0.04 ? 0.0 : material;
}

float MonolithTrueStalkerQuantizeMaterial(float material)
{
    uint quantized = (uint)(material / 1.333333333 * 31.0);
    return (quantized & 31u) * (1.0 / 31.0) * 1.333333333;
}

float2 MonolithTrueStalkerPackNormal(float3 normal)
{
    return float2(normal.z, 0.5 * (normal.x + 1.0) * (normal.y < 0.0 ? -1.0 : 1.0));
}

float3 MonolithTrueStalkerUnpackNormal(float2 packed)
{
    float x = 2.0 * abs(packed.y) - 1.0;
    return float3(x, (packed.y < 0.0 ? -1.0 : 1.0) * sqrt(abs(1.0 - x * x - packed.x * packed.x)), packed.x);
}

float4 MonolithTrueStalkerDirect(Texture3D materialLut, SamplerState materialSampler, float material, float3 normal, float3 view, float3 light, bool local, float attenuation)
{
    float3 halfVector = view + light;
    halfVector = dot(halfVector, halfVector) > 1e-20 ? normalize(halfVector) : normal;
    return materialLut.SampleLevel(materialSampler, float3(dot(light, normal), dot(halfVector, normal), MonolithTrueStalkerMaterial(material, local)), 0).xxxy * attenuation;
}

float3 MonolithTrueStalkerReflection(float3 normal, float3 view)
{
    float3 reflected = reflect(-view, normal);
    reflected.y = reflected.y * 2.0 - 1.0;
    return reflected;
}

float2 MonolithTrueStalkerEnvironmentCoordinates(float3 normal, float3 view, float hemisphere, bool gamma22)
{
    return float2(gamma22 ? hemisphere * hemisphere : hemisphere, 0.5 + 0.5 * dot(reflect(-view, normal), -view));
}

void MonolithTrueStalkerEnvironment(Texture3D materialLut, SamplerState materialSampler, float material, float hemisphere, float gloss, float3 normal, float3 view, bool gamma22, float3 env0Diffuse, float3 env1Diffuse, float3 env0Specular, float3 env1Specular, float4 envColor, float3 ambient, out float3 diffuse, out float3 specular)
{
    float2 coordinates = MonolithTrueStalkerEnvironmentCoordinates(normal, view, hemisphere, gamma22);
    float4 light = materialLut.SampleLevel(materialSampler, float3(coordinates, MonolithTrueStalkerMaterial(material, false)), 0).xxxy;
    float3 envDiffuse = envColor.rgb * lerp(env0Diffuse, env1Diffuse, envColor.w);
    float3 envSpecular = envColor.rgb * lerp(env0Specular, env1Specular, envColor.w);
    diffuse = envDiffuse * envDiffuse * light.rgb + ambient;
    specular = envSpecular * envSpecular * light.w * gloss;
}

#endif

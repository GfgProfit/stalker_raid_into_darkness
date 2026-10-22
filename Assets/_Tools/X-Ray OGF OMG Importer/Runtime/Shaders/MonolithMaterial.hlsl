#ifndef MONOLITH_MATERIAL_INCLUDED
#define MONOLITH_MATERIAL_INCLUDED

float MonolithDecodeMaterial(float id, float weight)
{
    float raw = id + (id < 4.0 ? weight : 0.0);
    float normalized = raw < 5.0 ? (raw + 0.5) / 4.0 : raw;
    uint quantized = (uint)(normalized / 1.333333333 * 31.0);
    return (quantized & 31u) / 31.0 * 1.333333333;
}

float MonolithMetalness(float gloss, float material)
{
    float threshold = pow(0.125, exp2(max(0.0, material * 4.0 - 0.5) / 4.0));
    float softness = threshold * 0.9;
    return material > 0.751 ? saturate((gloss - (threshold - softness)) / (2.0 * softness)) : 0.0;
}

float3 MonolithAlbedoBoost(float3 albedo)
{
    float blend = lerp(0.5, 1.0 - dot(albedo, float3(0.2125, 0.7154, 0.0721)), 0.45);
    return blend <= 0.5 ? albedo - (1.0 - 2.0 * blend) * albedo * (1.0 - albedo) : albedo + (2.0 * blend - 1.0) * (sqrt(albedo) - albedo);
}

float3 MonolithAlbedo(float3 encodedAlbedo, float metalness)
{
    float3 albedo = MonolithAlbedoBoost(encodedAlbedo);
    float3 screen = 1.0 - (1.0 - albedo) * (1.0 - albedo) - lerp(dot(albedo, float3(0.2125, 0.7154, 0.0721)), albedo, 0.5);
    return saturate(lerp(pow(max(0.0, albedo), 2.2), pow(max(0.0, screen), 2.2), metalness) * 1.25);
}

float3 MonolithSpecular(float3 encodedAlbedo, float gloss, float material)
{
    float metalness = MonolithMetalness(gloss, material);
    float3 metalColor = pow(MonolithAlbedoBoost(encodedAlbedo), 2.2);
    float boost = exp2(1.15 * ((saturate(material * 1.425) * 2.0 - 1.0) + (sqrt(gloss) * 2.0 - 1.0)));
    return saturate(lerp(0.04 * pow(boost, 1.15), metalColor, metalness));
}

float MonolithRoughness(float gloss, float material, float dynamicGloss)
{
    float boostedGloss = pow(max(0.0, gloss), 1.0 - MonolithMetalness(gloss, material) * 0.25);
    float rough = pow(max(0.0, lerp(1.0, 0.45, boostedGloss)), 0.5 / max(0.001, 1.0 - dynamicGloss));
    return saturate(rough * rough);
}

float3 MonolithDecodeNormal(float4 packed, float flipGreen)
{
    float3 normal = packed.abg * 2.0 - 1.0;
    normal.y *= flipGreen > 0.5 ? -1.0 : 1.0;
    return dot(normal, normal) > 1e-8 ? normalize(normal) : float3(0.0, 0.0, 1.0);
}

float MonolithBurley(float nl, float nv, float vh, float rough)
{
    float fd90 = 0.5 + 2.0 * vh * vh * rough;
    return (1.0 + (fd90 - 1.0) * pow(1.0 - nl, 5.0)) * (1.0 + (fd90 - 1.0) * pow(1.0 - nv, 5.0)) / PI;
}

float3 MonolithGGX(float nl, float nh, float nv, float vh, float3 f0, float rough)
{
    float a = rough * rough;
    float a2 = a * a;
    float denominator = (nh * a2 - nh) * nh + 1.0;
    float distribution = a2 / max(1e-20, PI * denominator * denominator);
    float visibility = 0.5 / max(1e-20, nl * (nv * (1.0 - a) + a) + nv * (nl * (1.0 - a) + a));
    float3 fresnel = f0 + (saturate(50.0 * f0) - f0) * pow(1.0 - vh, 5.0);
    return distribution * visibility * fresnel;
}

float3 MonolithEnvGGX(float3 f0, float rough, float nv)
{
    float4 r = rough * float4(-1.0, -0.0275, -0.572, 0.022) + float4(1.0, 0.0425, 1.04, -0.04);
    float a004 = min(r.x * r.x, exp2(-9.28 * nv)) * r.x + r.y;
    float2 ab = float2(-1.04, 1.04) * a004 + r.zw;
    return f0 * ab.x + ab.y * saturate(50.0 * f0);
}

float MonolithEnvBurley(float rough, float nv)
{
    float d0 = 0.97619 - 0.488095 * pow(1.0 - nv, 5.0);
    float d1 = 1.55754 + (-2.02221 + (2.56283 - 1.06244 * nv) * nv) * nv;
    return lerp(d0, d1, rough);
}

#endif

#ifndef MONOLITH_ENVIRONMENT_INCLUDED
#define MONOLITH_ENVIRONMENT_INCLUDED

#ifdef _MONOLITH_ORIGINAL_ENVIRONMENT
TEXTURECUBE(_MonolithEnv0);
TEXTURECUBE(_MonolithEnv1);
static float4 _MonolithEnvironmentAlbedoGloss;

struct MonolithEnvironmentData
{
    float3 normalRemap;
    float3 reflection;
    float3 reflectionRemap;
    float3 dominant;
    float3 mipViewHemi;
    float3 cube0Diffuse;
    float3 cube1Diffuse;
    float3 cube0Specular;
    float3 cube1Specular;
    float3 diffuseSample;
    float3 specularSample;
    float3 diffuseIrradiance;
    float3 specularIrradiance;
    float3 albedo;
    float3 f0;
    float diffuseBrdf;
    float3 specularBrdf;
    float3 diffuse;
    float3 specular;
    float3 total;
    float3 combined;
};

float3 MonolithEnvironmentRemap(float3 direction)
{
    direction /= max(abs(direction.x), max(abs(direction.y), abs(direction.z)));
    if (direction.y < 0.999) direction.y = direction.y * 2.0 - 1.0;
    return normalize(direction);
}

float3 MonolithEnvironmentDominant(float3 normal, float3 reflection, float rough)
{
    float smoothness = saturate(1.0 - rough);
    float factor = smoothness * (sqrt(smoothness) + rough);
    return lerp(normal, reflection, factor);
}

MonolithEnvironmentData MonolithEvaluateEnvironment(float3 normal, float3 view, float4 albedoGloss)
{
    MonolithEnvironmentData data;
    ZERO_INITIALIZE(MonolithEnvironmentData, data);
    normal = normalize(normal);
    view = normalize(view);
    if (_MonolithDebug >= 39.5 && _MonolithEnvTestNormal.w > 0.5)
    {
        normal = normalize(_MonolithEnvTestNormal.xyz);
        view = normalize(_MonolithEnvTestView.xyz);
    }
    float material = MonolithDecodeMaterial(_MonolithMaterialId, _MonolithMaterialWeight);
    bool flora = abs(material - 0.15) <= 0.04;
    if (abs(material - 0.95) <= 0.04) material = 0.0;
    float metalness = MonolithMetalness(albedoGloss.w, material);
    data.albedo = MonolithAlbedo(albedoGloss.rgb, metalness);
    data.f0 = MonolithSpecular(albedoGloss.rgb, albedoGloss.w, material);
    float rough = MonolithRoughness(albedoGloss.w, material, _MonolithDynamicGloss);
    if (_MonolithDebug >= 39.5 && _MonolithEnvTestView.w >= 0.0) rough = _MonolithEnvTestView.w;
    float mip = 6.0 - ((1.0 - rough) * 6.0);
    data.normalRemap = MonolithEnvironmentRemap(normal);
    data.reflection = reflect(-view, normal);
    data.reflectionRemap = MonolithEnvironmentRemap(data.reflection);
    data.dominant = MonolithEnvironmentDominant(data.normalRemap, data.reflectionRemap, rough);
    float3 squared = normal * normal;
    float3 dx = float3(data.normalRemap.x, 0.001, 0.001);
    float3 dy = float3(0.001, data.normalRemap.y, 0.001);
    float3 dz = float3(0.001, 0.001, data.normalRemap.z);
    data.cube0Diffuse = squared.x * SAMPLE_TEXTURECUBE_LOD(_MonolithEnv0, s_trilinear_clamp_sampler, dx, 6.0).rgb + squared.y * SAMPLE_TEXTURECUBE_LOD(_MonolithEnv0, s_trilinear_clamp_sampler, dy, 6.0).rgb + squared.z * SAMPLE_TEXTURECUBE_LOD(_MonolithEnv0, s_trilinear_clamp_sampler, dz, 6.0).rgb;
    data.cube1Diffuse = squared.x * SAMPLE_TEXTURECUBE_LOD(_MonolithEnv1, s_trilinear_clamp_sampler, dx, 6.0).rgb + squared.y * SAMPLE_TEXTURECUBE_LOD(_MonolithEnv1, s_trilinear_clamp_sampler, dy, 6.0).rgb + squared.z * SAMPLE_TEXTURECUBE_LOD(_MonolithEnv1, s_trilinear_clamp_sampler, dz, 6.0).rgb;
    data.cube0Specular = SAMPLE_TEXTURECUBE_LOD(_MonolithEnv0, s_trilinear_clamp_sampler, data.dominant, mip).rgb;
    data.cube1Specular = SAMPLE_TEXTURECUBE_LOD(_MonolithEnv1, s_trilinear_clamp_sampler, data.dominant, mip).rgb;
    data.diffuseSample = lerp(data.cube0Diffuse, data.cube1Diffuse, _MonolithEnvColor.w);
    data.specularSample = lerp(data.cube0Specular, data.cube1Specular, _MonolithEnvColor.w);
    float factor = 1.0 - saturate(dot(_MonolithEnvColor.rgb, 0.15));
    float baseHemi = MonolithBaseHemisphere();
    float hemi = lerp(baseHemi, baseHemi + _MonolithEnvHudHemi * factor, _MonolithEnvHudMask);
    data.mipViewHemi = float3(mip, dot(normal, view), hemi);
    data.diffuseIrradiance = pow(max(0.0, data.diffuseSample * _MonolithEnvColor.rgb * hemi + _MonolithEnvAmbient.rgb), 2.2);
    data.specularIrradiance = pow(max(0.0, data.specularSample * _MonolithEnvColor.rgb * hemi + _MonolithEnvAmbient.rgb), 2.2) * (flora ? 0.25 : 1.0);
    float nv = max(1e-5, data.mipViewHemi.y);
    data.diffuseBrdf = MonolithEnvBurley(rough, nv);
    data.specularBrdf = MonolithEnvGGX(data.f0, rough, nv);
    float horizon = saturate(data.mipViewHemi.y * 2.0);
    data.diffuse = data.diffuseBrdf * data.diffuseIrradiance * data.albedo;
    data.specular = data.specularBrdf * data.specularIrradiance * horizon * horizon;
    data.total = data.diffuse + data.specular;
    data.combined = _MonolithEnvCombineClamp > 0.5 ? saturate(data.total) : data.total;
    return data;
}

float3 MonolithEnvironmentDiagnostic(MonolithEnvironmentData data, int stage)
{
    if (stage == 40) return data.normalRemap;
    if (stage == 41) return data.reflection;
    if (stage == 42) return data.reflectionRemap;
    if (stage == 43) return data.dominant;
    if (stage == 44) return data.mipViewHemi;
    if (stage == 45) return data.cube0Diffuse;
    if (stage == 46) return data.cube1Diffuse;
    if (stage == 47) return data.cube0Specular;
    if (stage == 48) return data.cube1Specular;
    if (stage == 49) return data.diffuseSample;
    if (stage == 50) return data.specularSample;
    if (stage == 51) return data.diffuseIrradiance;
    if (stage == 52) return data.specularIrradiance;
    if (stage == 53) return data.albedo;
    if (stage == 54) return data.f0;
    if (stage == 55) return data.diffuseBrdf.xxx;
    if (stage == 56) return data.specularBrdf;
    if (stage == 57) return data.diffuse;
    if (stage == 58) return data.specular;
    if (stage == 59) return data.total;
    return data.combined;
}
#endif
#endif

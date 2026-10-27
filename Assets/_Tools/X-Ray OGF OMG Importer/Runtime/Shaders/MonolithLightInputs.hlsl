#ifndef MONOLITH_LIGHT_INPUTS_INCLUDED
#define MONOLITH_LIGHT_INPUTS_INCLUDED

float MonolithResolveDynamicGloss(float profile, float active, float localGloss, float globalGloss)
{
    return profile > 0.5 && active > 0.5 ? globalGloss : localGloss;
}

float MonolithSourceAttenuation(float distanceSquared, float range)
{
    if (range <= 0.0) return 0.0;
    float effectiveRange = range * 0.95;
    return pow(saturate(1.0 - max(distanceSquared, 0.1) / (effectiveRange * effectiveRange)), 2.2);
}

float3 MonolithSourceAccumulation(float3 sourceColor, float3 lightmap, float3 shadow)
{
    return pow(max(0.0, sourceColor * lightmap * shadow), 2.2);
}

float3 MonolithCalibratedSourceColor(float3 linearLightColor, float scale)
{
    return pow(max(0.0, linearLightColor * scale), 1.0 / 2.2);
}

float MonolithWeatherGloss(float minimum, float maximum, float autoMaximum, float wetness)
{
    float factor = (minimum + max(autoMaximum - minimum, 0.0) * saturate(wetness)) * 0.96;
    return minimum + saturate(maximum - minimum) * factor;
}

float4 MonolithResolveFloraFixes(float4 localFixes, float4 globalFixes, float profile, float active, float mask)
{
    float4 fixes = profile > 0.5 && active > 0.5 ? globalFixes : localFixes;
    fixes.x *= saturate(mask);
    return fixes;
}

#endif

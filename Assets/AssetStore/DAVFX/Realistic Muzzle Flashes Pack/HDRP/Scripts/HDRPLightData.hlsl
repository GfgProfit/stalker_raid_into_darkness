#ifndef GET_MAIN_DIRECTIONAL_LIGHT_INCLUDED
#define GET_MAIN_DIRECTIONAL_LIGHT_INCLUDED

float4 _MainDirectionalLightColor;

void GetMainDirectionalLight_float(
    out float3 LightColor
)
{
    LightColor = _MainDirectionalLightColor.rgb;
}

#endif

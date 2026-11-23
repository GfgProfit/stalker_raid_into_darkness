
#ifndef HDRP_PARAMS
#define HDRP_PARAMS

#define unity_ObjectToWorld unity_ObjectToWorld
#define unity_WorldToObject unity_WorldToObject


#if HDRP_ENABLED
//Main light HDRP
float3 _DavfxMainLightDirection;
half4 _DavfxMainLightColor;

//Additional lights HDRP
int _DavfxAdditionalLightsCount = 0;
half _DavfxAdditionalLightsRange[10];
float4 _DavfxAdditionalLightsPosition[10];
float4 _DavfxAdditionalLightsColor[10];
#endif



//Top, Left, Right; Bottom, Back, Front
void Calculate6DLight_float(float3 lightDir, float3 lightCol, float3 sixPntTLR, float3 sixPntBBF, float3 tangentWS, float3 bitangentWS, float3 normalWS, float ShadowMultiplier, out float3 lightMult)
{
    //float3 spRLT = float3(sixPntTLR.z, sixPntTLR.y, sixPntTLR.x);
    float3 spBBF = float3(sixPntBBF.x, sixPntBBF.z, sixPntBBF.y);
    float3 spTLR = sixPntTLR;
    //float3 spBBF = sixPntBBF;

    float3x3 tangentTransform_World_dir = float3x3(tangentWS.rgb, bitangentWS.rgb, normalWS.rgb);
    float3 worldToTangent_dir = TransformWorldToTangent(lightDir, tangentTransform_World_dir);

    float add1a = spTLR.r * saturate(worldToTangent_dir.r);
    float add2a = spTLR.g * saturate(worldToTangent_dir.r * -1);
    float add3a = spTLR.b * saturate(worldToTangent_dir.g);

    float add1b = spBBF.r * saturate(worldToTangent_dir.g * -1);
    float add2b = spBBF.g * saturate(worldToTangent_dir.b);
    float add3b = spBBF.b * saturate(worldToTangent_dir.b * -1);

    float sceneLightsPow = lerp(1, (add1a + add2a + add3a + add1b + add2b + add3b), ShadowMultiplier);
    lightMult = clamp(sceneLightsPow, 0, 1) * lightCol;
}

// Main Light
void MainLight_float(out half3 Direction, out half3 Color)
{    
#if SHADERGRAPH_PREVIEW
        Direction = half3(0.5, 0.5, 0);  // Dummy direction for preview
        Color = 1;                        // Dummy color for preview
#else
#ifdef URP_ENABLED // For URP
            Light light = GetMainLight();
            Direction = light.direction;
            Color = light.color;
#elif HDRP_ENABLED  // For HDRP
            // HDRP specific way of getting the main directional light
            Direction = _DavfxMainLightDirection;
            Color = _DavfxMainLightColor;
#else
            Direction = half3(0, 0, 0); // Fallback if no pipeline is detected
            Color = half3(1, 1, 1); // Default white color 
#endif
#endif

}

// Additional Lights function compatible with URP and HDRP
void AdditionalLights_half(half3 MainLightColor, half3 WorldPosition, half3 LocalPosition, float3 sixPntTLR, float3 sixPntBBF, float3 tangentWS, float3 bitangentWS, float3 normalWS, float ShadowMultiplier, out half3 OutColor)
{
    // Initialize with main light color
    OutColor = MainLightColor;

#if !SHADERGRAPH_PREVIEW
#ifdef URP_ENABLED  // For URP (Lightweight/Universal Render Pipeline)
            int pixelLightCount = GetAdditionalLightsCount();
            for (int i = 0; i < pixelLightCount; ++i)
            {
                Light light = GetAdditionalLight(i, WorldPosition);
                
                float3 Color;
                Calculate6DLight_float(light.direction, light.color, sixPntTLR, sixPntBBF, tangentWS, bitangentWS, normalWS, ShadowMultiplier, Color);
                OutColor += light.distanceAttenuation * Color;
            }
#elif HDRP_ENABLED  // For HDRP (High Definition Render Pipeline) 
            // In HDRP, the additional lights are handled differently.
            for (int i = 0; i < _DavfxAdditionalLightsCount; ++i)
            {
                // Point light calculation
                float3 toLight = _DavfxAdditionalLightsPosition[i] - LocalPosition;
                float distanceSqr = dot(toLight, toLight)/ _DavfxAdditionalLightsRange[i];
                float attenuation = saturate(1.0 - distanceSqr) ;
                float3 Direction = normalize(toLight);
                float3 Color;
    
                Calculate6DLight_float(Direction, _DavfxAdditionalLightsColor[i].xyz, sixPntTLR, sixPntBBF, tangentWS, bitangentWS, normalWS, ShadowMultiplier, Color);
                OutColor += 2 * attenuation * Color;
            }
#else
    OutColor = MainLightColor; // Fallback if no pipeline is detected
#endif
#endif
}

#endif
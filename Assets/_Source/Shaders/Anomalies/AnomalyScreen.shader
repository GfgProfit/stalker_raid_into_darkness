Shader "FullScreen/RaidIntoDarkness/AnomalyScreen"
{
    Properties
    {
        _AnomalyNoiseTexture("Noise", 2D) = "gray" {}
        _AnomalyGradient0("Gradient 0", 2D) = "gray" {}
        _AnomalyGradient1("Gradient 1", 2D) = "gray" {}
    }

    HLSLINCLUDE

    #pragma vertex Vert
    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

    TEXTURE2D_X(_AnomalyScreenSource);
    TEXTURE2D(_AnomalyNoiseTexture);
    TEXTURE2D(_AnomalyGradient0);
    TEXTURE2D(_AnomalyGradient1);
    float4 _AnomalyNoiseTexture_TexelSize;
    float3 _AnomalyBaseColor;
    float3 _AnomalyAddColor;
    float3 _AnomalyGrayColor;
    float4 _AnomalyGeometry;
    float4 _AnomalyNoise;
    float4 _AnomalyColorMap;

    float3 SampleAnomalyScene(float2 uv)
    {
        float2 sourceUv = clamp(uv, 0.5 * _ScreenSize.zw, 1.0 - 0.5 * _ScreenSize.zw) * _RTHandleScale.xy;
        return LinearToSRGB(saturate(SAMPLE_TEXTURE2D_X_LOD(_AnomalyScreenSource, s_linear_clamp_sampler, sourceUv, 0).rgb));
    }

    float4 FullScreenPass(Varyings varyings) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);
        float2 uv = varyings.positionCS.xy * _ScreenSize.zw;
        float2 shift = _AnomalyGeometry.zw * 0.5;
        float2 blur = 0.5 * _ScreenSize.zw * _AnomalyGeometry.y;
        float3 image = (SampleAnomalyScene(uv + blur - shift * uv) + SampleAnomalyScene(uv + blur + shift * (1.0 - uv))) * 0.5;
        float gradientIndex = dot(image, 0.3333);
        float3 gradient0 = SAMPLE_TEXTURE2D_LOD(_AnomalyGradient0, s_linear_clamp_sampler, float2(gradientIndex, 0.5), 0).rgb;
        float3 gradient1 = SAMPLE_TEXTURE2D_LOD(_AnomalyGradient1, s_linear_clamp_sampler, float2(gradientIndex, 0.5), 0).rgb;
        image = lerp(image, lerp(gradient0, gradient1, saturate(_AnomalyColorMap.y)), saturate(_AnomalyColorMap.x));
        float gray = dot(image, saturate(_AnomalyGrayColor));
        image = lerp(gray.xxx, image, floor((1.0 - saturate(_AnomalyGeometry.x)) * 255.0) / 255.0);
        float2 noiseSize = ceil(_AnomalyNoiseTexture_TexelSize.zw * max(_AnomalyNoise.y, 0.0001));
        float2 noiseUv = _AnomalyNoise.zw + 0.5 / noiseSize + uv * (floor(_ScreenSize.xy / noiseSize) + 1.0);
        float3 noise = SAMPLE_TEXTURE2D_LOD(_AnomalyNoiseTexture, s_linear_repeat_sampler, noiseUv, 0).rgb;
        image = lerp(image * noise * 2.0, image, floor((1.0 - saturate(_AnomalyNoise.x)) * 255.0) / 255.0);
        image = (image * (floor(saturate(_AnomalyBaseColor) * 255.0) / 255.0) + _AnomalyAddColor) * 2.0;
        return float4(SRGBToLinear(saturate(image)), 1.0);
    }

    ENDHLSL

    SubShader
    {
        Tags{ "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "Anomaly Screen"
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
                #pragma fragment FullScreenPass
            ENDHLSL
        }
    }
    Fallback Off
}

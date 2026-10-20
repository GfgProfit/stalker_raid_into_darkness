Shader "FullScreen/RaidIntoDarkness/ScreenAddColor"
{
    Properties
    {
        _AddColor("Add Color", Float) = 0
    }

    HLSLINCLUDE

    #pragma vertex Vert

    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

    TEXTURE2D_X(_ScreenAddColorSource);
    float _AddColor;

    float4 FullScreenPass(Varyings varyings) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);
        float3 color = LOAD_TEXTURE2D_X(_ScreenAddColorSource, varyings.positionCS.xy).rgb;
        float3 gamma = saturate(LinearToSRGB(saturate(color)) + _AddColor);
        return float4(SRGBToLinear(gamma), 1.0);
    }

    ENDHLSL

    SubShader
    {
        Tags{ "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "Custom Pass 0"

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

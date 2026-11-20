Shader "Hidden/RaidIntoDarkness/VideoSharpening"
{
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "Video Sharpening"
            Cull Off
            ZTest Always
            ZWrite Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _VideoSharpenStrength;
            float2 _VideoSharpenPixelSize;

            float3 ReadColor(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).rgb;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 texel = _VideoSharpenPixelSize;
                float2 lower = _BlitScaleBias.zw + texel * 0.5;
                float2 upper = _BlitScaleBias.zw + _BlitScaleBias.xy - texel * 0.5;
                float4 center = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);
                float3 left = ReadColor(clamp(uv - float2(texel.x, 0), lower, upper));
                float3 right = ReadColor(clamp(uv + float2(texel.x, 0), lower, upper));
                float3 top = ReadColor(clamp(uv - float2(0, texel.y), lower, upper));
                float3 bottom = ReadColor(clamp(uv + float2(0, texel.y), lower, upper));
                float3 localMin = min(center.rgb, min(min(left, right), min(top, bottom)));
                float3 localMax = max(center.rgb, max(max(left, right), max(top, bottom)));
                float3 detail = center.rgb - (left + right + top + bottom) * 0.25;
                return float4(clamp(center.rgb + detail * _VideoSharpenStrength, localMin, localMax), center.a);
            }
            ENDHLSL
        }
    }
}

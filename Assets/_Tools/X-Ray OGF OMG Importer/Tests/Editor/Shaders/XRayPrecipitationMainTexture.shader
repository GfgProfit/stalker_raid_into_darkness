Shader "Hidden/XRay/Tests/PrecipitationMainTexture"
{
    Properties
    {
        [MainTexture] _SourceTexture("Source Texture", 2D) = "white" {}
        _MainTex("Legacy Texture", 2D) = "white" {}
    }

    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Vert(float4 position : POSITION) : SV_POSITION
            {
                return position;
            }

            float4 Frag() : SV_Target
            {
                return 1.0;
            }
            ENDHLSL
        }
    }
}

Shader "Hidden/Shiyin/SettingsBrightness"
{
    Properties
    {
        _MainTex ("Image", 2D) = "white" {}
        _Brightness ("Brightness", Range(0.7, 1.3)) = 1
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Brightness;

            float4 frag(v2f_img i) : SV_Target
            {
                float4 color = tex2D(_MainTex, i.uv);
                // Perceived brightness in display space; the scene lights and shadow projection are untouched.
                #if defined(UNITY_COLORSPACE_GAMMA)
                    color.rgb *= _Brightness;
                #else
                    color.rgb = GammaToLinearSpace(LinearToGammaSpace(max(color.rgb, 0.0)) * _Brightness);
                #endif
                return color;
            }
            ENDCG
        }
    }
    Fallback Off
}

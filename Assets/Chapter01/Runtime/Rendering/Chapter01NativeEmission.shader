Shader "Chapter01/Native Night Emission"
{
    Properties
    {
        _Color ("Base color", Color) = (1,1,1,1)
        _MainTex ("Base color texture", 2D) = "white" {}
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.25
        _MetallicGlossMap ("Metallic (R), smoothness (A)", 2D) = "white" {}
        _GlossMapScale ("Texture smoothness scale", Range(0,1)) = 1
        _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal scale", Float) = 1
        _EmissionMap ("Native sRGB glass mask", 2D) = "white" {}
        [HDR] _EmissionColor ("Native linear emission multiplier", Color) = (0,0,0,1)
        [PerRendererData] _VioletWick ("Violet wick marker", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma shader_feature_local _NORMALMAP
        #pragma shader_feature_local _METALLICGLOSSMAP
        #pragma shader_feature_local _EMISSION
        #include "UnityStandardUtils.cginc"
        sampler2D _MainTex, _MetallicGlossMap, _BumpMap, _EmissionMap;
        fixed4 _Color;
        half4 _EmissionColor;
        half _Metallic, _Glossiness, _GlossMapScale, _BumpScale, _VioletWick;
        struct Input { float2 uv_MainTex; };
        void surf(Input input, inout SurfaceOutputStandard output)
        {
            fixed4 base = tex2D(_MainTex, input.uv_MainTex) * _Color;
            output.Albedo = base.rgb;
            output.Alpha = base.a;
            output.Occlusion = 1;
            #if defined(_METALLICGLOSSMAP)
                half4 packed = tex2D(_MetallicGlossMap, input.uv_MainTex);
                output.Metallic = packed.r;
                output.Smoothness = packed.a * _GlossMapScale;
            #else
                output.Metallic = _Metallic;
                output.Smoothness = _Glossiness;
            #endif
            #if defined(_NORMALMAP)
                output.Normal = UnpackScaleNormal(tex2D(_BumpMap, input.uv_MainTex), _BumpScale);
            #endif
            #if defined(_EMISSION)
                float3 mask = tex2D(_EmissionMap, input.uv_MainTex).rgb;
                #if defined(UNITY_COLORSPACE_GAMMA)
                    // Blender's sRGB image is decoded before its linear strength is applied.
                    // A Gamma project otherwise multiplies encoded green .4 instead of linear .133.
                    mask = GammaToLinearSpace(mask);
                #endif
                // The painted glass mask is amber. Its brightness marks the panes independently of hue.
                float pane = max(mask.r,max(mask.g,mask.b));
                output.Albedo = lerp(base.rgb,Luminance(base.rgb)*half3(.60,.12,.92),saturate(pane*4)*_VioletWick);
                float3 radiance = lerp(mask,pane.xxx,_VioletWick) * _EmissionColor.rgb;
                #if defined(UNITY_COLORSPACE_GAMMA)
                    // Encode the low range for this project's Gamma framebuffer, retaining HDR
                    // radiance above one. NightPost's SceneToLinear is the matching inverse.
                    radiance = LinearToGammaSpace(saturate(radiance)) + max(radiance - 1, 0);
                #endif
                output.Emission = radiance;
            #endif
        }
        ENDCG
    }
    FallBack "Standard"
}

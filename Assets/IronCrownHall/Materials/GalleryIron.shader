Shader "IronCrown/Gallery Iron" {
    Properties {
        _MainTex ("Ironwork and open panes", 2D) = "white" {}
        _MetallicGlossMap ("Metal and gloss", 2D) = "white" {}
        _GlossMapScale ("Gloss scale", Range(0,1)) = 1
        _Cutoff ("Pane cutout", Range(0,1)) = .4
        _Color ("Tint", Color) = (1,1,1,1)
        _MoonOrigin ("Roof opening", Vector) = (0,10,0,1)
    }
    SubShader {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf GalleryIron fullforwardshadows alphatest:_Cutoff addshadow exclude_path:deferred
        #include "UnityPBSLighting.cginc"
        sampler2D _MainTex, _MetallicGlossMap;
        half4 _Color;
        half _GlossMapScale;
        float4 _MoonOrigin;
        struct Input { float2 uv_MainTex; };
        void surf(Input i, inout SurfaceOutputStandard s) {
            half4 paint=tex2D(_MainTex,i.uv_MainTex)*_Color;
            half4 metal=tex2D(_MetallicGlossMap,i.uv_MainTex);
            s.Albedo=paint.rgb;
            s.Metallic=metal.r;
            s.Smoothness=metal.a*_GlossMapScale;
            s.Alpha=paint.a;
        }
        inline void LightingGalleryIron_GI(SurfaceOutputStandard s,UnityGIInput data,inout UnityGI gi) {
            LightingStandard_GI(s,data,gi);
            #if defined(UNITY_PASS_FORWARDBASE)
            // The vault's cool indirect reflection remains when the wick is out. It
            // moves across raised lips and bars as the view angle changes.
            float3 toWindow=normalize(_MoonOrigin.xyz-data.worldPos);
            half3 reflection=reflect(-normalize(data.worldViewDir),s.Normal);
            half angle=saturate(dot(reflection,toWindow)*.5+.5);
            half broad=pow(angle,4);
            half glint=pow(angle,lerp(9,30,s.Smoothness));
            gi.indirect.specular+=half3(.32,.46,.62)*(.04+.52*broad+.88*glint);
            gi.indirect.diffuse+=half3(.065,.082,.11)*saturate(.22+.78*dot(s.Normal,toWindow));
            #endif
        }
        inline half4 LightingGalleryIron(SurfaceOutputStandard s,half3 viewDir,UnityGI gi) {
            return LightingStandard(s,viewDir,gi);
        }
        ENDCG
    }
    Fallback "Transparent/Cutout/Diffuse"
}

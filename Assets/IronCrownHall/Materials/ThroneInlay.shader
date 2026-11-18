Shader "IronCrown/Throne Inlay" {
    Properties {
        _MainTex ("Albedo",2D)="white" {}
        _MetallicGlossMap ("Metal and gloss",2D)="white" {}
        _GlossMapScale ("Gloss scale",Range(0,1))=.8
        _Color ("Tint",Color)=(1,1,1,1)
    }
    SubShader {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        #include "UnityCG.cginc"
        sampler2D _MainTex, _MetallicGlossMap;
        half _GlossMapScale; fixed4 _Color;
        struct Input { float2 uv_MainTex; float3 local; float3 viewDir; };
        void vert(inout appdata_full v, out Input o) { UNITY_INITIALIZE_OUTPUT(Input,o); o.local=v.vertex.xyz; }
        void surf(Input IN,inout SurfaceOutputStandard o) {
            fixed3 albedo=tex2D(_MainTex,IN.uv_MainTex).rgb*_Color.rgb;
            half4 mask=tex2D(_MetallicGlossMap,IN.uv_MainTex);
            o.Albedo=albedo; o.Metallic=mask.r; o.Smoothness=mask.a*_GlossMapScale; o.Alpha=1;
            // The carved metal provides its own luminous edge. Keep the upholstered seat dark.
            float2 p=float2(abs(IN.local.x),IN.local.y);
            float spires=smoothstep(.15,.38,p.y);
            float posts=smoothstep(.24,.39,p.x)*smoothstep(-.20,-.04,p.y);
            float arms=smoothstep(.23,.30,p.x)*smoothstep(-.26,-.19,p.y)
                      *(1-smoothstep(-.08,-.03,p.y))*smoothstep(-.02,.21,IN.local.z);
            float surround=saturate(max(spires,max(posts,arms)));
            float grazing=1-saturate(abs(normalize(IN.viewDir).z));
            float bevel=.25+.75*grazing*grazing;
            float luminance=dot(albedo,float3(.299,.587,.114));
            float silver=.08+.58*smoothstep(.07,.43,luminance)+.34*mask.r;
            float tips=smoothstep(.30,.46,p.y);
            float pulse=.94+.06*sin(_Time.y*1.05+IN.local.y*2.8);
            o.Emission=float3(.32,.81,1.12)*surround*silver*(bevel+.32*tips)*pulse;
        }
        ENDCG
    }
    Fallback "Standard"
}

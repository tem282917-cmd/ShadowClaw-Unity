Shader "ShadowClaw/DeathMist" {
    Properties {
        _MainTex ("Imported smoke artwork", 2D) = "white" {}
        _Color ("Charcoal highlight and opacity", Color) = (.17,.15,.20,.82)
    }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off Cull Off
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 _Color;
            struct Input { float4 vertex:POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Output { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; };
            Output vert(Input v) {
                Output o; o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=v.uv; return o;
            }
            fixed4 frag(Output i):SV_Target {
                // The imported sprite stores its rolling wisps in alpha. Reuse
                // their density for both coverage and modelling inside the plume.
                half density=tex2D(_MainTex,i.uv).a;
                half highlight=.18+.60*(1-density)+.22*i.color.r;
                return fixed4(_Color.rgb*highlight, density*i.color.a*_Color.a);
            }
            ENDCG
        }
    }
}

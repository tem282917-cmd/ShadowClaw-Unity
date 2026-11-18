Shader "ShadowClaw/SpiritParticles" {
    Properties { _MainTex ("Particle artwork", 2D) = "black" {} }
    SubShader {
        Tags { "Queue"="Transparent+4" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off Cull Off
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            struct In { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Out { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            Out vert(In i) {
                Out o; o.vertex=UnityObjectToClipPos(i.vertex); o.color=i.color; o.uv=i.uv; return o;
            }
            half4 frag(Out i):SV_Target {
                half4 art=tex2D(_MainTex,i.uv);
                return half4(i.color.rgb*(1+.35*art.r), art.r*art.a*i.color.a);
            }
            ENDCG
        }
    }
}

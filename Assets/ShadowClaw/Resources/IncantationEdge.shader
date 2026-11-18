Shader "ShadowClaw/IncantationEdge" {
    Properties { _Color ("Edge color",Color)=(.8,.45,1,1) }
    SubShader {
        Tags { "Queue"="Transparent+3" "RenderType"="Transparent" }
        Blend SrcAlpha One
        ZWrite Off Cull Off
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct In {float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0;};
            struct Out {float4 vertex:SV_POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;};
            float4 _Color;
            Out vert(In i){Out o;o.vertex=UnityObjectToClipPos(i.vertex);o.color=i.color*_Color;o.uv=i.uv;return o;}
            float4 frag(Out i):SV_Target {
                float d=abs(i.uv.y-.5)*2;
                float thread=exp(-28*d*d);
                float knot=pow(saturate(.5+.5*cos(i.uv.x*27-_Time.y*6.5)),12);
                return float4(lerp(i.color.rgb,float3(1,.88,.85),thread*.58),i.color.a*(.55*(1-d)+.42*thread+.20*knot*(1-d)));
            }
            ENDCG
        }
    }
}

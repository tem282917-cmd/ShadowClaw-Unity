Shader "IronCrown/Leadlight" {
    SubShader {
        Tags { "RenderType"="Opaque" }
        Cull Off
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct V { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            V vert(appdata_base v) {
                V o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.texcoord; return o;
            }
            fixed4 frag(V i):SV_Target {
                float2 p=i.uv*2-1;
                float r=length(p);
                float angle=atan2(p.y,p.x+1e-6);
                float pale=.5+.5*cos(8*angle);
                float3 glass=lerp(float3(.15,.32,.50),float3(.61,.76,.84),
                    saturate(.24+.46*pale+.30*(1-r)));
                glass=lerp(glass,float3(.26,.47,.67),smoothstep(.87,.91,r));
                float wave=sin(p.x*28+sin(p.y*17))*sin(p.y*19+2*sin(p.x*7));
                glass*=.96+.07*wave;
                float roundel=1-smoothstep(.20,.215,r);
                return float4(lerp(glass,float3(.88,.94,1.)*(.98+.025*wave),roundel),1);
            }
            ENDCG
        }
    }
}

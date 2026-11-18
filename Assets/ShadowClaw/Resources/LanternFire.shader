Shader "ShadowClaw/Lantern fire" {
    Properties {
        _MainTex ("Animated flame sheet", 2D) = "white" {}
        _Cinder ("Cinder", Float) = 0
        _Power ("Wick power", Range(0,1)) = 1
        _Dormant ("Violet unlit marker", Range(0,1)) = 0
    }
    SubShader {
        Tags { "Queue"="Transparent+4" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Vertex { float4 vertex:POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Varyings { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; };
            float _Cinder, _Power, _Dormant;
            sampler2D _MainTex;
            Varyings vert(Vertex v) {
                Varyings o; o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=v.uv; return o;
            }
            fixed4 frag(Varyings i):SV_Target {
                float y=saturate(i.uv.y);
                float x=i.uv.x*2-1;
                if (_Cinder>.5) {
                    float r=length(float2(x,(y-.5)*2));
                    float spark=pow(saturate(1-r),2);
                    float3 tint=lerp(float3(1,.36,.07),float3(.62,.12,1),_Dormant);
                    return float4(tint*spark*i.color.a*_Power,0);
                }
                half4 flame=tex2D(_MainTex,i.uv);
                half brightness=max(flame.r,max(flame.g,flame.b));
                half3 tint=lerp(flame.rgb,brightness*half3(.62,.12,1),_Dormant);
                half3 radiance=tint*i.color.rgb*flame.a*i.color.a;
                return half4(radiance*lerp(.5,2.1,_Power),0);
            }
            ENDCG
        }
    }
}

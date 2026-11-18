Shader "ShadowLab/Chapel Black Mist" {
    Properties { _MainTex ("Smoke", 2D) = "white" {}
        _WhiteMist ("White gate", Float) = 0
        _Opacity ("Opacity", Range(0,1)) = 1
        _FlowRate ("Flow", Float) = 1
        _FreezeAt ("Frozen clock", Float) = -1
        _ClockOffset ("Clock offset", Float) = 0
    }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass {
            Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _WhiteMist, _Opacity, _FlowRate, _FreezeAt, _ClockOffset;
            struct Varyings { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            Varyings vert(appdata_img v) {
                Varyings o;
                o.pos=UnityObjectToClipPos(v.vertex);
                o.uv=v.texcoord;
                return o;
            }
            fixed4 frag(Varyings i):SV_Target {
                // Stretch the existing smoke into two unequal, falling streams.
                float2 uv=i.uv;
                float clock=(_FreezeAt<0 ? _Time.y-_ClockOffset : _FreezeAt)*_FlowRate;
                half cloud=tex2D(_MainTex,frac(uv*float2(3.7,1.1)+float2(.13,clock*.12))).r;
                half wisp=tex2D(_MainTex,frac(uv*float2(6.3,1.8)+float2(-.32,clock*.23))).r;
                half density=saturate(cloud*1.65h+wisp*1.1h);
                half sides=saturate(min(uv.x,1-uv.x)*30);
                half3 dark=lerp(half3(.014,.014,.018),half3(.23,.22,.255),density);
                half3 pale=lerp(half3(.36,.43,.52),half3(.94,.96,1),density);
                return fixed4(lerp(dark,pale,_WhiteMist),sides*_Opacity*lerp(.83h+.15h*density,.36h+.58h*density,_WhiteMist));
            }
            ENDCG
        }
    }
}

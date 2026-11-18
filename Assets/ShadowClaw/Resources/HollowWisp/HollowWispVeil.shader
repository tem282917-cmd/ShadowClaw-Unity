Shader "ShadowClaw/Hollow Wisp Veil" {
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            struct Vertex {
                float4 position : POSITION;
                float3 normal : NORMAL;
                fixed4 tint : COLOR;
            };
            struct Varying {
                float4 position : SV_POSITION;
                half4 tint : COLOR0;
                UNITY_FOG_COORDS(0)
            };
            Varying vert(Vertex input) {
                Varying o;
                o.position = UnityObjectToClipPos(input.position);
                float3 normal = UnityObjectToWorldNormal(input.normal);
                float3 eye = normalize(WorldSpaceViewDir(input.position));
                half rim = pow(1 - saturate(dot(normal, eye)), 2);
                half key = .80 + .25 * saturate(dot(normal, normalize(float3(-.4,.8,.35))));
                half3 cloth = input.tint.rgb * key + rim * half3(.035,.060,.078);
                #ifndef UNITY_COLORSPACE_GAMMA
                    cloth = GammaToLinearSpace(cloth);
                #endif
                o.tint = half4(cloth, input.tint.a);
                UNITY_TRANSFER_FOG(o,o.position);
                return o;
            }
            half4 frag(Varying i) : SV_Target {
                UNITY_APPLY_FOG(i.fogCoord, i.tint);
                return i.tint;
            }
            ENDCG
        }
    }
}

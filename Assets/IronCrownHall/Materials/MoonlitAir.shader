Shader "IronCrown/Moonlit Air" {
    SubShader {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass {
            Blend One One
            ZWrite Off ZTest Always Cull Front
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            struct V { float4 pos:SV_POSITION; float4 screen:TEXCOORD0; float3 world:TEXCOORD1; };
            V vert(appdata_base v) {
                V o; o.pos=UnityObjectToClipPos(v.vertex); o.screen=ComputeScreenPos(o.pos);
                o.world=mul(unity_ObjectToWorld,v.vertex).xyz; return o;
            }
            fixed4 frag(V i):SV_Target {
                float3 worldDir=normalize(i.world-_WorldSpaceCameraPos);
                float3 origin=mul(unity_WorldToObject,float4(_WorldSpaceCameraPos,1)).xyz;
                float3 dir=mul((float3x3)unity_WorldToObject,worldDir);
                float3 inv=1.0/(sign(dir+1e-8)*max(abs(dir),1e-6));
                float3 a=(-.5-origin)*inv,b=(.5-origin)*inv;
                float3 nearT=min(a,b),farT=max(a,b);
                float enter=max(0,max(nearT.x,max(nearT.y,nearT.z)));
                float leave=min(farT.x,min(farT.y,farT.z));
                float sceneZ=LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture,UNITY_PROJ_COORD(i.screen)));
                leave=min(leave,sceneZ/max(.001,-mul((float3x3)UNITY_MATRIX_V,worldDir).z));
                float stepLength=max(0,leave-enter)/12.0;
                float depth=0;
                [unroll] for(int k=0;k<12;k++) {
                    float3 p=origin+dir*(enter+(k+.5)*stepLength);
                    float z=p.z+.5;
                    // The local Z axis starts at the circular pane and runs straight down to the paving.
                    float2 q=p.xy/(.33+.15*z);
                    float r=length(q);
                    float feather=1-smoothstep(.55,1.0,r);
                    float angle=atan2(q.y,q.x+1e-6);
                    float petals=.5+.5*cos(8*angle);
                    float panes=1-.22*petals*smoothstep(.18,.75,r)*exp(-2.1*z);
                    depth+=feather*panes*(1-.48*z)*smoothstep(0,.05,1-z);
                }
                return float4(float3(.40,.57,.83)*(1-exp(-.046*stepLength*depth)),0);
            }
            ENDCG
        }
    }
}

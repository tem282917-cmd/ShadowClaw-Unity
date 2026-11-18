Shader "ShadowLab/ShadowFireVolume" {
 Properties {_Strength("Scattering strength",Range(0,1))=1}
 SubShader {
  Tags {"Queue"="Transparent+8" "RenderType"="Transparent" "IgnoreProjector"="True"}
  Pass {
   Cull Front ZWrite Off ZTest Always Blend One One ColorMask RGB
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
   float _Strength;
   struct Out {float4 pos:SV_POSITION;float4 screen:TEXCOORD0;float3 world:TEXCOORD1;};
   Out vert(appdata_base v) {
       Out o;o.pos=UnityObjectToClipPos(v.vertex);o.screen=ComputeScreenPos(o.pos);
       o.world=mul(unity_ObjectToWorld,v.vertex).xyz;return o;
   }
   fixed4 frag(Out i):SV_Target {
       float3 dir=normalize(i.world-_WorldSpaceCameraPos);
       float3 origin=mul(unity_WorldToObject,float4(_WorldSpaceCameraPos,1)).xyz;
       float3 ray=mul((float3x3)unity_WorldToObject,dir);
       float a=dot(ray,ray),b=dot(origin,ray),c=dot(origin,origin)-.25;
       float h=b*b-a*c;clip(h);
       float nearT=max(0,(-b-sqrt(h))/a),farT=(-b+sqrt(h))/a;
       float z=LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture,UNITY_PROJ_COORD(i.screen)));
       farT=min(farT,z/max(.001,-mul((float3x3)UNITY_MATRIX_V,dir).z));
       float step=max(0,farT-nearT)/6,integral=0;
       [unroll] for(int s=0;s<6;s++) {
           float3 p=origin+ray*(nearT+(s+.5)*step);
           float density=saturate(1-4*dot(p,p));
           float drift=.76+.24*sin(p.y*12-_Time.y*2+2*sin(p.x*11+p.z*7));
           integral+=density*density*drift*step;
       }
       return float4(float3(.23,.067,.011)*integral*_Strength,0);
   }
   ENDCG
  }
 }
}

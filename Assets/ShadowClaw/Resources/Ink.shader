Shader "ShadowLab/Ink" {
 Properties { _Flame ("Wick flame",Range(0,1)) = 0  _SelfLit ("Self illumination",Range(0,1)) = 0  _Color ("Color", Color) = (1,1,1,1) }
 SubShader {
  Tags { "RenderType"="Opaque" }
  Pass {
   Cull Off
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   fixed4 _Color;
   float _HallBrightness, _SelfLit, _Flame;
   float4 _HallLamps[6];
   struct Out { float4 vertex:SV_POSITION; float3 world:TEXCOORD0; float shade:TEXCOORD1; float height:TEXCOORD2; };
   Out vert(appdata_base v) {
       Out o;
       o.height=saturate(v.vertex.y+0.5);
       if(_Flame>0.5) {
           float tip=o.height*o.height;
           v.vertex.xz*=1.0-0.86*tip;
           v.vertex.z+=tip*(0.09+0.04*sin(_Time.y*8.0));
       }
       o.vertex=UnityObjectToClipPos(v.vertex); o.world=mul(unity_ObjectToWorld,v.vertex).xyz; o.shade=0.76+0.24*max(0,dot(UnityObjectToWorldNormal(v.normal),normalize(float3(-0.35,0.8,-0.6)))); return o; }
   fixed4 frag(Out i):SV_Target {
       if(_Flame>0.5) {
           float3 core=lerp(float3(1,.89,.63),_Color.rgb,i.height);
           return fixed4(core*lerp(1.18,.85,i.height),1);
       }
       float3 light=0;
       [unroll] for(int k=0;k<6;k++) {
           float3 d=i.world-_HallLamps[k].xyz;
           float3 tint=k<3 ? float3(1.4,.70,.25) : float3(.24,.66,1.35);
           light+=_HallLamps[k].w*tint/(1+dot(d,d)*.18);
       }
       return fixed4(_Color.rgb*(i.shade+light)*lerp(_HallBrightness,1.0,_SelfLit),_Color.a); }
   ENDCG
  }
 }
}

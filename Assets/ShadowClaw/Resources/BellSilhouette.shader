Shader "ShadowLab/BellSilhouette" {
 Properties {
     _MainTex ("Animated coverage",2D)="black"{}
     _FireHeat ("Burning perimeter",Float)=0
 }
 SubShader {
  Tags { "Queue"="Transparent-20" "RenderType"="Transparent" }
  Pass {
   ZWrite Off Cull Off Blend One SrcAlpha
   ColorMask RGB
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   sampler2D _MainTex;
   float4 _MainTex_TexelSize;
   float _FireHeat;
   float4 _FireOrigin;
   struct Varyings { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float2 floorXZ:TEXCOORD1; };
   Varyings vert(appdata_img v) {
       Varyings o;
       o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.texcoord.xy;
       o.floorXZ=mul(unity_ObjectToWorld,v.vertex).xz;
       return o;
   }
   fixed4 frag(Varyings i):SV_Target {
       float2 d=_MainTex_TexelSize.xy*1.3;
       float c=tex2D(_MainTex,i.uv).r;
       float blur=(tex2D(_MainTex,i.uv+float2(d.x,0)).r+tex2D(_MainTex,i.uv-float2(d.x,0)).r+
                   tex2D(_MainTex,i.uv+float2(0,d.y)).r+tex2D(_MainTex,i.uv-float2(0,d.y)).r)*.25;
       float shade=c*.63+blur*.37;
       float edge=abs(c-blur)*(.78+.22*sin(13.0*i.uv.y+_Time.y*2.2));
       // Let joints in the paving remain visible through the umbra. A narrow
       // violet fringe keeps the boundary legible on the darkest flagstones.
       half3 tint=shade*half3(.007,.003,.012)+edge*half3(.11,.062,.16);
       if(_FireHeat>0) {
           float2 offset=i.floorXZ-_FireOrigin.xy;
           float angle=atan2(offset.y,offset.x)-_FireOrigin.z;
           float front=cos(angle)-cos(UNITY_PI*_FireOrigin.w);
           float rim=saturate(abs(c-blur)*3.2);
           float ignition=smoothstep(-.025,.025,front+.026);
           tint+=half3(.89,.23,.031)*rim*ignition*_FireHeat;
       }
       return fixed4(tint,lerp(1,.27,shade));
   }
   ENDCG
  }
 }
}

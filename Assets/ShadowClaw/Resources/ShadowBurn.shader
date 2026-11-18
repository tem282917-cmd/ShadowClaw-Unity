Shader "ShadowLab/ShadowBurn" {
 Properties { _Style ("Haze, ember or cinder bed",Float)=1 }
 SubShader {
  Tags { "Queue"="Transparent+10" "RenderType"="Transparent" }
  Pass {
   Cull Off ZWrite Off Blend SrcAlpha One
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   float _Style;
   struct In {float4 pos:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;};
   struct Out {float4 pos:SV_POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;};
   Out vert(In v){Out o;o.pos=UnityObjectToClipPos(v.pos);o.color=v.color;o.uv=v.uv;return o;}
   fixed4 frag(Out i):SV_Target {
       float2 uv=i.uv*2-1;
       if(_Style>2.5) {
           float n=sin(uv.x*28+2*sin(uv.y*12))*cos(uv.y*23-2*sin(uv.x*16));
           float sparks=smoothstep(.44,.90,n);
           float hot=saturate(1-length(uv)*1.1);
           return fixed4(i.color.rgb,i.color.a*hot*hot*(.10+.85*sparks));
       }
       float r=saturate(1-dot(uv,uv));
       float glow=r*r*r*(_Style<1.5?.60:1);
       return fixed4(i.color.rgb,i.color.a*glow);
   }
   ENDCG
  }
 }
}

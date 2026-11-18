Shader "ShadowLab/ReceiverShadow" {
 Properties { _InkBit("Union stencil",Float)=64 }
 SubShader {
  Tags { "Queue"="Transparent-21" "RenderType"="Transparent" }
  Pass {
   ZWrite Off Cull Off Blend One SrcAlpha ColorMask RGB
   Stencil {Ref [_InkBit] ReadMask [_InkBit] WriteMask [_InkBit] Comp NotEqual Pass Replace}
   CGPROGRAM
   #pragma vertex vert_img
   #pragma fragment frag
   #include "UnityCG.cginc"
   fixed4 frag(v2f_img i):SV_Target {return fixed4(.003,.004,.009,.43);}
   ENDCG
  }
 }
}

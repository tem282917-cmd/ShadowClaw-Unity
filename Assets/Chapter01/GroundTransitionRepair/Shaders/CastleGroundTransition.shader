Shader "Chapter01/Castle Ground Transition"
{
    Properties
    {
        _SoilTex ("Fine soil and leaf litter, 2 metres", 2D) = "white" {}
        _GrassTex ("Short grass and moss, 2 metres", 2D) = "white" {}
        _SoilDetail ("Soil slope RG / roughness B", 2D) = "gray" {}
        _GrassDetail ("Grass slope RG / roughness B", 2D) = "gray" {}
        _Distribution ("Low contrast coverage", 2D) = "gray" {}
        _DetailScale ("Repeats per metre", Float) = 0.5
        _NormalStrength ("Fine surface relief", Range(0,1)) = 0.48
        _Coverage ("Moss and short grass coverage", Range(0,1)) = 0.62
        _EdgeDistance ("Distance to grass / rock boundary, 8 metres", 2D) = "white" {}
        _EdgeBounds ("World XZ origin and inverse size", Vector) = (0,0,1,1)
        _RockTex ("Existing cliff rock albedo", 2D) = "gray" {}
        _RockNormal ("Existing cliff rock relief", 2D) = "bump" {}
        _RockScale ("Rock repeats per metre", Float) = 0.1666667
        _RockTint ("Existing cliff rock tint", Color) = (1,1,1,1)
        _UseRimVertexAlpha ("Use authored rim edge weights", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 250
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard fullforwardshadows
        #include "UnityCG.cginc"
        sampler2D _SoilTex, _GrassTex, _SoilDetail, _GrassDetail, _Distribution;
        sampler2D _EdgeDistance, _RockTex, _RockNormal;
        float _DetailScale, _NormalStrength, _Coverage, _RockScale, _UseRimVertexAlpha;
        float4 _EdgeBounds;
        fixed4 _RockTint;
        struct Input { float3 worldPos; float3 worldNormal; float4 color:COLOR; INTERNAL_DATA };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 p = IN.worldPos.xz;
            float2 uv = p * _DetailScale;
            float2 grassUV = float2(uv.x*.8-uv.y*.6,uv.x*.6+uv.y*.8)+float2(.317,.719);
            fixed4 soil = tex2D(_SoilTex,uv);
            fixed4 grass = tex2D(_GrassTex,grassUV);
            fixed3 soilData = tex2D(_SoilDetail,uv).rgb;
            fixed3 grassData = tex2D(_GrassDetail,grassUV).rgb;
            float3 macro = tex2D(_Distribution,p/64.0).rgb;
            float local = tex2D(_Distribution,p/17.0+float2(.31,.76)).g;
            float3 n = normalize(WorldNormalVector(IN,float3(0,0,1)));
            float distance = tex2D(_EdgeDistance,(p-_EdgeBounds.xy)*_EdgeBounds.zw).r*8.0;
            float detailNoise = tex2D(_Distribution,p/3.8+float2(.71,.19)).r;
            float edgeWidth = 1.35+detailNoise*2.0;
            float edge = 1-smoothstep(.15,edgeWidth,distance);
            // The rim's vertex alpha is zero at the buried outside seat and one
            // on the inner top. Imported terrain has white vertex colours.
            float bankOutside=saturate(1-IN.color.a)*_UseRimVertexAlpha;
            edge = max(edge,bankOutside);
            float coverage = smoothstep(.29,.72,macro.r*.68+local*.32+(_Coverage-.5)*.65);
            coverage *= smoothstep(.55,.96,n.y) * (1-smoothstep(.05,.72,edge));
            fixed3 base = lerp(soil.rgb,grass.rgb,coverage)*(0.95+macro.b*.10);
            // A broad dirt fringe and small, uneven rock exposures. The earlier
            // continuous fully pale band read as a manufactured concrete skirt.
            float chips=smoothstep(.44,.71,detailNoise*.7+local*.3);
            float topRock=(1-smoothstep(.06,.35+detailNoise*.80,distance))*(.08+chips*.30);
            float rockWeight=max(topRock*(1-_UseRimVertexAlpha*.65),
                smoothstep(.70,.98,bankOutside)*(.35+chips*.45));
            float2 rockSlope=0;
            if (rockWeight>.001)
            {
                float3 w=pow(abs(n),4); w/=max(w.x+w.y+w.z,.0001);
                fixed3 rock=tex2D(_RockTex,IN.worldPos.zy*_RockScale).rgb*w.x
                    +tex2D(_RockTex,IN.worldPos.xz*_RockScale).rgb*w.y
                    +tex2D(_RockTex,IN.worldPos.xy*_RockScale).rgb*w.z;
                // Soil staining makes exposed chips belong to the bank; the
                // untouched cliff below supplies the clean, pale rock faces.
                rock=lerp(soil.rgb,rock*_RockTint.rgb,.66);
                base=lerp(base,rock,rockWeight);
                float3 rockNormal=UnpackNormal(tex2D(_RockNormal,p*_RockScale));
                rockSlope=clamp(rockNormal.xy/max(rockNormal.z,.35),-.8,.8)*.48;
            }
            o.Albedo=base;
            float2 a=soilData.rg*2-1,b=grassData.rg*2-1;
            b=float2(b.x*.8+b.y*.6,-b.x*.6+b.y*.8);
            float2 slope=lerp(lerp(a,b,coverage)*_NormalStrength,rockSlope,rockWeight);
            float3 t=normalize(WorldNormalVector(IN,float3(1,0,0)));
            float3 bt=normalize(WorldNormalVector(IN,float3(0,1,0)));
            float3 detailNormal=normalize(n+float3(slope.x,0,slope.y));
            o.Normal=normalize(float3(dot(detailNormal,t),dot(detailNormal,bt),dot(detailNormal,n)));
            o.Metallic=0;
            o.Smoothness=lerp(1-lerp(soilData.b,grassData.b,coverage),.035,rockWeight);
            o.Occlusion=1;
            o.Alpha=1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}

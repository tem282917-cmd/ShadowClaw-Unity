Shader "Chapter01/Castle Forest Floor"
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
        float _DetailScale, _NormalStrength, _Coverage;
        struct Input { float3 worldPos; float3 worldNormal; INTERNAL_DATA };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 p = IN.worldPos.xz;
            float2 uv = p * _DetailScale;
            float2 grassUV = float2(uv.x * .8 - uv.y * .6, uv.x * .6 + uv.y * .8) + float2(.317,.719);
            fixed4 soil = tex2D(_SoilTex, uv);
            fixed4 grass = tex2D(_GrassTex, grassUV);
            fixed3 soilData = tex2D(_SoilDetail, uv).rgb;
            fixed3 grassData = tex2D(_GrassDetail, grassUV).rgb;
            float3 macro = tex2D(_Distribution, p / 64.0).rgb;
            float local = tex2D(_Distribution, p / 17.0 + float2(.31,.76)).g;
            float3 n = normalize(WorldNormalVector(IN,float3(0,0,1)));
            float coverage = smoothstep(.29,.72, macro.r*.68 + local*.32 + (_Coverage-.5)*.65);
            coverage *= smoothstep(.55,.96,n.y);
            // Colours stay within one soil/olive family: no bright rock-cloud atlas.
            o.Albedo = lerp(soil.rgb,grass.rgb,coverage) * (0.95 + macro.b*.10);
            float2 a = (soilData.rg*2-1);
            float2 b = (grassData.rg*2-1);
            b = float2(b.x*.8 + b.y*.6, -b.x*.6 + b.y*.8);
            float2 slope = lerp(a,b,coverage) * _NormalStrength;
            float3 t = normalize(WorldNormalVector(IN,float3(1,0,0)));
            float3 bt = normalize(WorldNormalVector(IN,float3(0,1,0)));
            float3 detailNormal = normalize(n + float3(slope.x,0,slope.y));
            o.Normal = normalize(float3(dot(detailNormal,t),dot(detailNormal,bt),dot(detailNormal,n)));
            o.Metallic = 0;
            o.Smoothness = 1-lerp(soilData.b,grassData.b,coverage);
            o.Occlusion = 1;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}

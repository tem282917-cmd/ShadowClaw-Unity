Shader "Chapter01/Castle Understory"
{
    Properties
    {
        _Color ("Foliage tint", Color) = (1,1,1,1)
        _Wind ("Tip movement in metres", Range(0,.1)) = .018
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        LOD 150
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Understory vertex:vert addshadow dithercrossfade noforwardadd exclude_path:deferred exclude_path:prepass
        #pragma multi_compile _ LOD_FADE_CROSSFADE
        #include "UnityCG.cginc"
        #include "Lighting.cginc"
        fixed4 _Color;
        float _Wind;
        struct Input { float4 color : COLOR; };
        inline fixed4 LightingUnderstory(SurfaceOutput s, UnityGI gi)
        {
            // Thin foliage has two lit faces. Symmetric diffuse handles both raster
            // sides without flipping the intentionally upward-biased sky normal.
            // gi.light.color already contains shadow attenuation; this adds no light.
            half diffuse = saturate(abs(dot(s.Normal,gi.light.dir))*.85h + .15h);
            fixed3 lit = gi.light.color*diffuse;
            #ifdef UNITY_LIGHT_FUNCTION_APPLY_INDIRECT
            lit += gi.indirect.diffuse;
            #endif
            return fixed4(s.Albedo*lit,s.Alpha);
        }
        inline void LightingUnderstory_GI(SurfaceOutput s, UnityGIInput data, inout UnityGI gi)
        {
            gi = UnityGlobalIllumination(data,1.0,s.Normal);
        }
        void vert(inout appdata_full v)
        {
            float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
            float sway = sin(_Time.y*1.3 + world.x*.53 + world.z*.43) * _Wind * v.color.a;
            v.vertex.x += sway;
            v.vertex.z += sway*.43;
        }
        void surf(Input IN, inout SurfaceOutput o)
        {
            // Fully modelled blades and leaflets; no alpha cards or specular shimmer.
            fixed3 tint = IN.color.rgb;
            #ifndef UNITY_COLORSPACE_GAMMA
            tint = GammaToLinearSpace(tint);
            #endif
            o.Albedo = tint * _Color.rgb;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}

Shader "ShadowClaw/BellSleeveSilk" {
    Properties {
        _MainTex ("Fabric and metal", 2D)="white" {}
        _MetallicGlossMap ("Metallic and smoothness", 2D)="black" {}
        _Color ("Surface tint",Color)=(1,1,1,1)
        _Spirit ("Moonlit edge",Color)=(.53,.68,.91,1)
        _CrystalEyes ("Crystal eye inlays", Range(0,1))=1
        _MistDissolve ("Death mist dissolve", Range(0,1))=0
        _MoonOrigin ("Moon window",Vector)=(0,10.82,0,1)
    }
    SubShader {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        sampler2D _MainTex,_MetallicGlossMap;
        fixed4 _Color,_Spirit;
        float4 _MoonOrigin;
        float4x4 _WorldToBellHead;
        half _CrystalEyes, _MistDissolve;
        struct Input { float2 uv_MainTex; float3 worldNormal; float3 worldPos; float3 socketPos; };
        void vert(inout appdata_full v, out Input i) {
            UNITY_INITIALIZE_OUTPUT(Input,i);
            i.socketPos=mul(_WorldToBellHead,mul(unity_ObjectToWorld,v.vertex)).xyz;
        }

        void CrystalEye(float3 p, half3 view, inout SurfaceOutputStandard o) {
            if (p.y<.137 || p.y>.205 || p.z<.054 || p.z>.131) return;
            // Both openings are part of the original mask. The turned face gives the
            // farther socket a steeper depth gradient. Clip the inlay behind each gold lip.
            bool nearSocket=p.x>-.033;
            float u=nearSocket ? (.033-p.x)/.040 : (p.x+.112)/.042;
            float v=(p.y-.175+.0078*u+.0024*u*u)/.0170;
            float radius=length(float2(u,v));
            if (radius>=1.0) return;
            float lipDepth=nearSocket ? .117+.006*u : .096+.020*u;
            half coverage=(1-smoothstep(.86,1.,radius))*
                (1-smoothstep(lipDepth-.003,lipDepth,p.z))*_CrystalEyes;
            if (coverage<=0) return;

            // Crown and table cuts rotate through the reflection as the hunter circles.
            float2 bv=float2(nearSocket?-u:u,v);
            float angle=atan2(bv.y,bv.x)+3.141593;
            float sector=floor(angle*1.273240);
            float prism=(sector+.5)*.785398-3.141593;
            float2 slope=float2(cos(prism),sin(prism));
            float3 headView=normalize(mul((float3x3)_WorldToBellHead,view));
            float tilt=dot(slope,headView.xy);
            half crown=smoothstep(.37,.40,radius);
            half facet=saturate(.48+.32*dot(slope,float2(-.58,.82))+.40*tilt);
            half3 deep=half3(.31,.029,.12);
            half3 ruby=half3(.96,.20,.34);
            half3 cut=lerp(deep,ruby,facet);
            // Light concentrates on the far lip, with a darker body of glass above.
            half depthTone=saturate(.55-bv.y*.42-bv.x*.14);
            half3 glass=lerp(half3(.69,.065,.18),cut,crown)*(.64+.49*depthTone);
            float2 glintAt=float2(-.20,.28)+headView.xy*.18;
            half glint=1-smoothstep(.065,.16,length((bv-glintAt)*float2(1.,1.4)));
            float2 inside=bv-float2(.14,-.62)+headView.xy*.17;
            half caustic=exp2(-dot(inside*float2(2.,5.),inside*float2(2.,5.)));
            half vein=(1-smoothstep(.011,.05,abs(radius-.38)))*.11*facet;
            half3 fire=glass+half3(1.,.71,.72)*(glint*.85+vein)+half3(.46,.18,.22)*caustic;
            o.Albedo=lerp(o.Albedo,glass*.23,coverage);
            o.Metallic=lerp(o.Metallic,.22,coverage);
            o.Smoothness=lerp(o.Smoothness,.93,coverage);
            o.Emission=lerp(o.Emission,fire,coverage);
        }
        void surf(Input i,inout SurfaceOutputStandard o) {
            half3 texel=tex2D(_MainTex,i.uv_MainTex).rgb;
            if (_MistDissolve>0) {
                float2 uv=i.uv_MainTex;
                half wisps=.50+.20*sin(uv.y*39.+sin(uv.x*31.))+.14*sin(uv.x*57.+uv.y*19.)
                          +.10*sin(uv.y*109.-uv.x*75.)+.06*(texel.r*2.-1.);
                clip(wisps-_MistDissolve);
            }
            half4 metal=tex2D(_MetallicGlossMap,i.uv_MainTex);
            half cloth=smoothstep(.015,.08,texel.b-texel.g)*smoothstep(.06,.20,texel.r-texel.g);
            half ivory=smoothstep(.42,.85,dot(texel,half3(.24,.62,.14)));
            // Dye the burgundy pigment into plum; keep the neutral skin and the gold embroidery separate.
            half3 dye=lerp(half3(.87,.88,.93),half3(.66,.76,.98),cloth);
            half3 albedo=texel*_Color.rgb*dye*lerp(1.,.63,ivory);
            half3 n=normalize(i.worldNormal);
            half3 view=normalize(_WorldSpaceCameraPos-i.worldPos);
            half moon=saturate(dot(n,normalize(_MoonOrigin.xyz-i.worldPos))*.8+.2);
            half edge=pow(1-saturate(dot(n,view)),3.8);
            half gilding=smoothstep(.13,.35,texel.g-texel.b)*smoothstep(.22,.42,texel.r-texel.b);
            o.Albedo=albedo;
            o.Metallic=metal.r*lerp(.72,.18,cloth);
            o.Smoothness=metal.a*lerp(.59,.30,cloth);
            // Most of the modelling now comes from scene light. Only grazing edges and gold get an accent.
            half faceLight=saturate(dot(n,normalize(view*.65+half3(-.38,.58,.18))));
            o.Emission=albedo*(.17+.38*moon+.67*faceLight)*half3(1.,.95,1.)
                      + _Spirit.rgb*edge*(.10+.51*moon)
                      + half3(.47,.23,.075)*gilding*(.27+.45*moon+.37*edge);
            CrystalEye(i.socketPos,view,o);
        }
        ENDCG
    }
    FallBack "Standard"
}

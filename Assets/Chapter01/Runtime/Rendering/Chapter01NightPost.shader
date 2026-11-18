Shader "Hidden/Chapter01/NightPost"
{
    Properties { _MainTex ("Scene", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _BloomTex;
        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
        float4 _MainTex_TexelSize;
        float4x4 _ViewOrigins, _ViewRays;
        float4 _CameraClips, _FogSettings, _FogColor, _BloomThreshold;
        float4 _MistCenters[4], _MistInverseRadii[4], _MistClearZones[4];
        float _Exposure, _Saturation, _BloomIntensity, _BlurRadius, _DiagnosticMode;

        struct Varyings
        {
            float4 vertex : SV_POSITION;
            float2 uv : TEXCOORD0;
            float2 depthUV : TEXCOORD1;
            float3 origin : TEXCOORD2;
            float3 ray : TEXCOORD3;
        };
        Varyings Vert(appdata_img v)
        {
            Varyings o;
            o.vertex = UnityObjectToClipPos(v.vertex);
            o.uv = v.texcoord;
            o.depthUV = v.texcoord;
            #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0) o.depthUV.y = 1 - o.depthUV.y;
            #endif
            o.origin = lerp(lerp(_ViewOrigins[0].xyz, _ViewOrigins[1].xyz, v.texcoord.x),
                lerp(_ViewOrigins[2].xyz, _ViewOrigins[3].xyz, v.texcoord.x), v.texcoord.y);
            o.ray = lerp(lerp(_ViewRays[0].xyz, _ViewRays[1].xyz, v.texcoord.x),
                lerp(_ViewRays[2].xyz, _ViewRays[3].xyz, v.texcoord.x), v.texcoord.y);
            return o;
        }
        float3 SceneToLinear(float3 value)
        {
            #if defined(UNITY_COLORSPACE_GAMMA)
                // Gamma projects still contain HDR emission. Keep the HDR tail as radiance;
                // applying an sRGB power curve above one would inflate a 7.8 light to ~90.
                return GammaToLinearSpace(saturate(value)) + max(value - 1, 0);
            #else
                return max(value, 0);
            #endif
        }
        float Profile(float3 p, float3 world, int index)
        {
            float radial = saturate(1 - dot(p.xz, p.xz));
            float vertical = saturate(1 - abs(p.y));
            float4 zone = _MistClearZones[index];
            float clear = 1;
            if (zone.w > zone.z)
            {
                float t = saturate((length(world.xz - zone.xy) - zone.z) / (zone.w - zone.z));
                clear = t * t * t * (t * (t * 6 - 15) + 10);
            }
            return radial * vertical * vertical * clear;
        }
        float IntegrateMist(float3 origin, float3 direction, float distance, int index)
        {
            float density = _MistCenters[index].w;
            if (density <= 0) return 0;
            float3 inverseRadius = _MistInverseRadii[index].xyz;
            float3 ro = (origin - _MistCenters[index].xyz) * inverseRadius;
            float3 rd = direction * inverseRadius;
            float a = dot(rd, rd), b = dot(ro, rd), c = dot(ro, ro) - 1;
            float determinant = b * b - a * c;
            if (determinant <= 0) return 0;
            float root = sqrt(determinant);
            float begin = max(18, (-b - root) / a);
            float end = min(distance, (-b + root) / a);
            if (end <= begin) return 0;
            // Three quadrature samples per intersected volume, rather than a full ray march.
            float span = end - begin;
            float t0 = begin + span * 0.1127, t1 = begin + span * 0.5, t2 = begin + span * 0.8873;
            float value = Profile(ro + rd * t0, origin + direction * t0, index) * 0.2778;
            value += Profile(ro + rd * t1, origin + direction * t1, index) * 0.4444;
            value += Profile(ro + rd * t2, origin + direction * t2, index) * 0.2778;
            return value * span * density;
        }
        float2 RayDistance(Varyings i)
        {
            float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.depthUV);
            float forwardDepth = raw;
            #if defined(UNITY_REVERSED_Z)
                forwardDepth = 1 - raw;
            #endif
            // Sky still passes through local lowland volumes; only uniform distance haze is skipped.
            // The tighter far-depth threshold avoids classifying distant meshes as sky with a long far clip.
            float sky = forwardDepth >= 0.9999999 ? 1 : 0;
            float eyeDepth = _CameraClips.z > 0.5 ? lerp(_CameraClips.x, _CameraClips.y, forwardDepth) : LinearEyeDepth(raw);
            float distance = sky > 0.5 ? _FogSettings.w : min(length(i.ray * eyeDepth), _FogSettings.w);
            return float2(distance, sky);
        }
        float FogOpacity(Varyings i, float2 rayDistance)
        {
            float distance = rayDistance.x;
            float3 direction = normalize(i.ray);
            float opticalDepth = rayDistance.y > 0.5 ? 0 : max(0, distance - _FogSettings.x) * _FogSettings.y;
            [unroll] for (int index = 0; index < 4; ++index)
                opticalDepth += IntegrateMist(i.origin, direction, distance, index);
            return min(_FogSettings.z, 1 - exp(-opticalDepth));
        }
        float4 Fog(Varyings i) : SV_Target
        {
            float4 source = tex2D(_MainTex, i.uv);
            float3 color = SceneToLinear(source.rgb);
            float opacity = FogOpacity(i, RayDistance(i));
            return float4(lerp(color, _FogColor.rgb, opacity), source.a);
        }
        float4 Diagnostic(Varyings i) : SV_Target
        {
            float2 distance = RayDistance(i);
            float value = _DiagnosticMode < 1.5 ? saturate(log2(1 + distance.x) / log2(1 + _FogSettings.w))
                : FogOpacity(i, distance);
            return float4(value, value, value, 1);
        }
        float3 FilterBloom(float3 color)
        {
            float brightness = max(color.r, max(color.g, color.b));
            float soft = clamp(brightness - _BloomThreshold.x + _BloomThreshold.y, 0, 2 * _BloomThreshold.y);
            soft = soft * soft / (4 * _BloomThreshold.y + 0.0001);
            return color * max(soft, brightness - _BloomThreshold.x) / max(brightness, 0.0001);
        }
        float4 Prefilter(Varyings i) : SV_Target
        {
            float2 d = abs(_MainTex_TexelSize.xy) * 0.5;
            float3 c = FilterBloom(tex2D(_MainTex, i.uv + d).rgb);
            c += FilterBloom(tex2D(_MainTex, i.uv - d).rgb);
            c += FilterBloom(tex2D(_MainTex, i.uv + float2(d.x, -d.y)).rgb);
            c += FilterBloom(tex2D(_MainTex, i.uv + float2(-d.x, d.y)).rgb);
            return float4(c * 0.25, 1);
        }
        float4 Blur(Varyings i) : SV_Target
        {
            float2 d = abs(_MainTex_TexelSize.xy) * _BlurRadius;
            float3 c = tex2D(_MainTex, i.uv + d).rgb + tex2D(_MainTex, i.uv - d).rgb;
            c += tex2D(_MainTex, i.uv + float2(d.x, -d.y)).rgb + tex2D(_MainTex, i.uv + float2(-d.x, d.y)).rgb;
            return float4(c * 0.25, 1);
        }
        float4 Composite(Varyings i) : SV_Target
        {
            float3 color = (tex2D(_MainTex, i.uv).rgb + tex2D(_BloomTex, i.uv).rgb * _BloomIntensity) * _Exposure;
            // Compact filmic shoulder preserves warm, bright lamp cores without clipping them flat.
            color = saturate((color * (2.51 * color + 0.03)) / (color * (2.43 * color + 0.59) + 0.14));
            float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
            color = lerp(luminance.xxx, color, _Saturation);
            #if defined(UNITY_COLORSPACE_GAMMA)
                color = LinearToGammaSpace(max(color, 0));
            #endif
            return float4(color, 1);
        }
        float3 FxaaSample(float2 uv)
        {
            float3 color = tex2D(_MainTex, uv).rgb;
            #if !defined(UNITY_COLORSPACE_GAMMA)
                color = LinearToGammaSpace(max(color, 0));
            #endif
            return color;
        }
        float4 FxaaOutput(float3 color)
        {
            #if !defined(UNITY_COLORSPACE_GAMMA)
                color = GammaToLinearSpace(max(color, 0));
            #endif
            return float4(color, 1);
        }
        float4 Fxaa(Varyings i) : SV_Target
        {
            // Work on display luminance after tone mapping, preserving the filmic curve and bloom.
            float2 texel = abs(_MainTex_TexelSize.xy);
            float3 center = FxaaSample(i.uv);
            float3 northWest = FxaaSample(i.uv + texel * float2(-1, -1));
            float3 northEast = FxaaSample(i.uv + texel * float2(1, -1));
            float3 southWest = FxaaSample(i.uv + texel * float2(-1, 1));
            float3 southEast = FxaaSample(i.uv + texel * float2(1, 1));
            const float3 weights = float3(0.299, 0.587, 0.114);
            float luma = dot(center, weights);
            float nw = dot(northWest, weights), ne = dot(northEast, weights);
            float sw = dot(southWest, weights), se = dot(southEast, weights);
            float low = min(luma, min(min(nw, ne), min(sw, se)));
            float high = max(luma, max(max(nw, ne), max(sw, se)));
            // Avoid smoothing low-contrast stone, foliage texture and gradients in the night sky.
            if (high - low < max(0.025, high * 0.125)) return tex2D(_MainTex, i.uv);
            float2 direction = float2(-((nw + ne) - (sw + se)), (nw + sw) - (ne + se));
            float reduction = max((nw + ne + sw + se) * (0.25 * 0.125), 0.0078125);
            direction *= rcp(min(abs(direction.x), abs(direction.y)) + reduction);
            direction = clamp(direction, -8, 8) * texel;
            float3 inner = 0.5 * (FxaaSample(i.uv + direction * (-1.0 / 6.0))
                + FxaaSample(i.uv + direction * (1.0 / 6.0)));
            float3 outer = inner * 0.5 + 0.25 * (FxaaSample(i.uv - direction * 0.5)
                + FxaaSample(i.uv + direction * 0.5));
            float resultLuma = dot(outer, weights);
            return FxaaOutput(resultLuma < low || resultLuma > high ? inner : outer);
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Fog
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Prefilter
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Blur
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Composite
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Diagnostic
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Fxaa
            ENDCG
        }
    }
    Fallback Off
}

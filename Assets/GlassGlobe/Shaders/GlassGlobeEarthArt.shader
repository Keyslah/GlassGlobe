Shader "GlassGlobe/Earth Art"
{
    Properties
    {
        _LandMask ("Land Mask", 2D) = "black" {}
        _WaterAlpha ("Water Alpha", Range(0, 1)) = 0.75
        _LandAlpha ("Land Alpha", Range(0, 1)) = 0.65
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Front

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _LandMask;
            float _WaterAlpha;
            float _LandAlpha;

            struct Attributes
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.position);
                output.uv = input.uv;
                return output;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(234.34, 435.345));
                p += dot(p, p + 34.23);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float Fbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    value += amplitude * ValueNoise(p);
                    p = p * 2.03 + 17.7;
                    amplitude *= 0.5;
                }

                return value;
            }

            float4 WaterArt(float2 uv, float2 p, float2 warp, float t)
            {
                // Calm deep basins, glowing shallow fields, caustic filaments
                // that only dance where the water glows.
                float depth = Fbm(p * 0.22 + warp * 1.2 + t * 0.004);
                float3 deepColor = float3(0.02, 0.09, 0.26);
                float3 midColor = float3(0.05, 0.26, 0.50);
                float3 glowColor = float3(0.16, 0.55, 0.66);
                float3 color = lerp(deepColor, midColor, smoothstep(0.30, 0.72, depth));
                color = lerp(color, glowColor, smoothstep(0.62, 0.90, depth) * 0.55);

                float ridge1 = 1.0 - abs(2.0 * Fbm(p * 0.9 + warp * 2.0 + float2(t * 0.020, t * 0.011)) - 1.0);
                float ridge2 = 1.0 - abs(2.0 * Fbm(p * 1.7 - warp * 1.5 + float2(-t * 0.014, t * 0.017)) - 1.0);
                float calm = 0.30 + 0.70 * smoothstep(0.35, 0.68, depth);
                float caustic = pow(saturate(ridge1 * ridge2), 5.0) * calm;
                color += caustic * float3(0.45, 0.80, 0.90) * 0.7;

                float2 glintGrid = uv * float2(900.0, 450.0);
                float2 glintCell = floor(glintGrid);
                float glintHash = Hash21(glintCell);
                float twinkle = 0.5 + 0.5 * sin(t * (0.8 + glintHash * 2.0) + glintHash * 40.0);
                float2 glintOffset = frac(glintGrid) - 0.5;
                float spot = saturate(1.0 - dot(glintOffset, glintOffset) * 8.0);
                float glint = step(0.987, glintHash) * pow(spot, 3.0) * twinkle;
                color += glint * float3(0.9, 1.0, 1.0) * 0.7;

                return float4(color, _WaterAlpha * (0.90 + 0.10 * depth));
            }

            float4 LandArt(float2 p, float t)
            {
                // Moss to sage to gold with rose crests: drifting patches with
                // fine grain and a soft luminous top.
                float2 lp = p * 0.5;
                float2 landWarp = float2(
                    Fbm(lp * 0.5 + t * 0.006),
                    Fbm(lp * 0.5 + 11.7 - t * 0.005));
                float patch = smoothstep(0.32, 0.70, Fbm(lp * 0.45 + landWarp * 1.4));
                float grain = Fbm(lp * 3.1 + landWarp * 0.5);

                float3 moss = float3(0.13, 0.34, 0.25);
                float3 sage = float3(0.42, 0.62, 0.40);
                float3 gold = float3(0.85, 0.68, 0.38);
                float3 rose = float3(0.80, 0.52, 0.46);
                float3 color = lerp(moss, sage, smoothstep(0.15, 0.5, patch));
                color = lerp(color, gold, smoothstep(0.5, 0.8, patch));
                color = lerp(color, rose, smoothstep(0.78, 0.97, patch) * 0.5);
                color *= 0.86 + 0.24 * grain;
                color += patch * patch * patch * float3(0.20, 0.16, 0.08);

                return float4(color, _LandAlpha * (0.92 + 0.08 * patch));
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float mask = tex2D(_LandMask, input.uv).r;
                float t = _Time.y;
                float2 p = input.uv * float2(72.0, 36.0);

                // Shared slow domain warp: the source of the dreamy flow.
                float2 warp = float2(
                    Fbm(p * 0.35 + t * 0.010),
                    Fbm(p * 0.35 + 7.3 - t * 0.008));

                // Evaluate only the sides the pixel needs; blend softly along
                // the bilinear coastline edge.
                float4 water = float4(0.0, 0.0, 0.0, 0.0);
                float4 land = float4(0.0, 0.0, 0.0, 0.0);
                if (mask < 0.95)
                {
                    water = WaterArt(input.uv, p, warp, t);
                }

                if (mask > 0.05)
                {
                    land = LandArt(p, t);
                }

                float blend = smoothstep(0.25, 0.75, mask);
                float4 result = lerp(water, land, blend);
                return fixed4(result.rgb, result.a);
            }
            ENDCG
        }
    }
}

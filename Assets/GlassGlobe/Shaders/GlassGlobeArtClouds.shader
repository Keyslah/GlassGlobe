Shader "GlassGlobe/Art Clouds"
{
    Properties
    {
        _MainTex ("Cloud Data", 2D) = "black" {}
        _Opacity ("Opacity", Range(0, 1)) = 0.8
        _DriftSpeed ("Drift Speed (uv per second)", Float) = 0.0002
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

            sampler2D _MainTex;
            float _Opacity;
            float _DriftSpeed;

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
                for (int i = 0; i < 3; i++)
                {
                    value += amplitude * ValueNoise(p);
                    p = p * 2.03 + 17.7;
                    amplitude *= 0.5;
                }

                return value;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float t = _Time.y;

                // The whole sheet creeps along like real weather - slow enough
                // that you only catch the movement by staring.
                float2 uv = input.uv + float2(t * _DriftSpeed, 0.0);

                // Soften the live satellite field with neighbor taps so decks
                // go pillowy instead of pixel-crisp.
                float d0 = tex2D(_MainTex, uv).a;
                float d1 = tex2D(_MainTex, uv + float2(0.0018, 0.0009)).a;
                float d2 = tex2D(_MainTex, uv - float2(0.0016, 0.0011)).a;
                float density = (d0 * 2.0 + d1 + d2) * 0.25;

                // Billow the interior with drifting noise so decks are not
                // flat sheets; the noise drifts against the sheet motion so
                // shapes slowly morph.
                float billow = Fbm(input.uv * float2(140.0, 70.0) + float2(t * 0.015, -t * 0.006));
                float body = smoothstep(0.18, 0.80, density * (0.72 + 0.5 * billow));

                // Faint luminous halo hugging the deck edges.
                float rim = smoothstep(0.04, 0.22, density) * (1.0 - body);

                // Warm/cool tinge wandering slowly across the field.
                float tinge = Fbm(input.uv * float2(10.0, 5.0) + t * 0.002);
                float3 warmWhite = float3(1.0, 0.96, 0.92);
                float3 coolWhite = float3(0.90, 0.95, 1.0);
                float3 color = lerp(warmWhite, coolWhite, tinge);

                float alpha = saturate(body + rim * 0.35) * _Opacity;
                return fixed4(color, alpha);
            }
            ENDCG
        }
    }
}

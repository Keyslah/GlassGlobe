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

            // Cheap smooth value noise from layered sines; good enough for a
            // dreamy drift and free of texture fetches.
            float SoftNoise(float2 p, float t)
            {
                return 0.5 + 0.25 * sin(p.x * 6.2 + t * 0.11 + sin(p.y * 5.1 - t * 0.07))
                           + 0.25 * sin(p.y * 7.3 - t * 0.09 + sin(p.x * 4.7 + t * 0.05));
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float mask = tex2D(_LandMask, input.uv).r;
                float t = _Time.y;
                float2 uv = input.uv;

                // --- Dreamy water: deep indigo to teal, breathing slowly, with
                // traveling sparkle interference bands.
                float waterFlow = SoftNoise(uv * float2(14.0, 8.0), t);
                float3 deepWater = float3(0.05, 0.16, 0.38);
                float3 shallowWater = float3(0.10, 0.55, 0.62);
                float3 waterColor = lerp(deepWater, shallowWater, waterFlow);

                float sparkleA = sin(uv.x * 640.0 + t * 0.9 + sin(uv.y * 590.0 - t * 0.7) * 2.0);
                float sparkleB = sin(uv.y * 700.0 - t * 0.8 + sin(uv.x * 610.0 + t * 0.6) * 2.0);
                float sparkle = pow(saturate(sparkleA * sparkleB), 12.0);
                waterColor += sparkle * float3(0.65, 0.85, 0.95);

                float waterAlpha = _WaterAlpha * (0.82 + 0.18 * waterFlow);

                // --- Dreamy land: soft sage, amber and lavender patches that
                // drift into each other and gently breathe.
                float landPatch = SoftNoise(uv * float2(9.0, 6.0) + 3.7, t * 0.6);
                float landPatchB = SoftNoise(uv * float2(21.0, 13.0) + 11.3, t * 0.4);
                float3 sage = float3(0.35, 0.62, 0.42);
                float3 amber = float3(0.82, 0.64, 0.35);
                float3 lavender = float3(0.62, 0.52, 0.78);
                float3 landColor = lerp(lerp(sage, amber, landPatch), lavender, landPatchB * 0.45);
                landColor *= 0.9 + 0.1 * sin(t * 0.13 + uv.x * 12.0);

                float landAlpha = _LandAlpha * (0.88 + 0.12 * landPatch);

                float3 color = lerp(waterColor, landColor, mask);
                float alpha = lerp(waterAlpha, landAlpha, mask);
                return fixed4(color, alpha);
            }
            ENDCG
        }
    }
}

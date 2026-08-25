Shader "GlassGlobe/Earth at Night Tile"
{
    Properties
    {
        _NightTileTex ("Earth at Night Tile", 2D) = "black" {}
        _NightOpacity ("Earth at Night Opacity", Range(0, 1)) = 1
        _RimColor ("Rim Glow Color", Color) = (0.35, 0.78, 1, 0.85)
        _RimIntensity ("Rim Glow Intensity", Range(0, 3)) = 0
        _RimPower ("Rim Glow Falloff", Range(0.5, 8)) = 3
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

            // NightTileSurface builds its patches with EarthMath's physical
            // mirrored-longitude embedding. That winding is the inverse of the
            // generated base globe and therefore keeps the far side with Front.
            Cull Front

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 position : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 worldPosition : TEXCOORD2;
            };

            sampler2D _NightTileTex;
            half _NightOpacity;
            fixed4 _RimColor;
            half _RimIntensity;
            half _RimPower;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.position);
                output.uv = input.uv;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.worldPosition = mul(unity_ObjectToWorld, input.position).xyz;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                fixed3 color = tex2D(_NightTileTex, input.uv).rgb;
                float3 viewDirection =
                    normalize(_WorldSpaceCameraPos - input.worldPosition);
                half facing =
                    abs(dot(viewDirection, normalize(input.worldNormal)));
                half rim =
                    pow(saturate(1.0 - facing), _RimPower) * _RimIntensity;

                // The base globe suppresses its low-resolution fallback exactly
                // under this patch. Including the same rim in both passes makes
                // SrcAlpha blending reduce to lerp(base, fullResolution, opacity)
                // while retaining one copy of the rim at every opacity.
                color += _RimColor.rgb * rim;
                return fixed4(color, saturate(_NightOpacity));
            }
            ENDCG
        }
    }
}

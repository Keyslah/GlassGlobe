Shader "GlassGlobe/Transparent Globe"
{
    Properties
    {
        _Color ("Color", Color) = (0.05, 0.24, 0.32, 0.16)
        _BlueMarbleTex ("Blue Marble Map", 2D) = "black" {}
        _BlueMarbleOpacity ("Blue Marble Opacity", Range(0, 1)) = 0
        _NightTex ("Earth at Night Map", 2D) = "black" {}
        _NightCoverageTex ("Full-Resolution Night Coverage", 2D) = "black" {}
        _NightOpacity ("Earth at Night Opacity", Range(0, 1)) = 0
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
            // Keep the FAR hemisphere, the one the border lines are drawn on.
            // GlobeRenderer.BuildSphereMesh uses +sin(lon) while the art and
            // weather shells use EarthMath.GeoToPoint's mirrored -sin(lon), so
            // this mesh winds the opposite way and needs the opposite cull from
            // those shells. Cull Front was the central failure in the original
            // Night Lights experiment: it sampled the surface under the user.
            Cull Back

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

            fixed4 _Color;
            sampler2D _BlueMarbleTex;
            half _BlueMarbleOpacity;
            sampler2D _NightTex;
            sampler2D _NightCoverageTex;
            half _NightOpacity;
            fixed4 _RimColor;
            half _RimIntensity;
            half _RimPower;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.position);
                // The sphere mesh maps longitude with +sin(lon) while EarthMath
                // uses the mirrored -sin(lon) embedding for physical truth, so
                // flip U to keep both NASA maps aligned with the border lines.
                output.uv = float2(1.0 - input.uv.x, input.uv.y);
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.worldPosition = mul(unity_ObjectToWorld, input.position).xyz;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                fixed4 color = _Color;

                fixed3 marble = tex2D(_BlueMarbleTex, input.uv).rgb;
                half marbleOpacity = saturate(_BlueMarbleOpacity);
                color.rgb = lerp(color.rgb, marble, marbleOpacity);
                color.a = lerp(color.a, 1.0, marbleOpacity);

                // Earth at Night is a true surface blend. The old version added
                // every RGB value as light, which washed the glass blue while the
                // actual city lights remained weak. A lerp makes 100% reproduce
                // the official Black Marble image and lower values transparently
                // reveal Blue Moon or Blue Marble beneath it.
                fixed3 nightMap = tex2D(_NightTex, input.uv).rgb;
                // Android region-decodes the literal 500 m NASA source only
                // where the phone is looking. Keep this global map as an
                // immediate fallback, but suppress it beneath a loaded tile so
                // the later tile pass performs exactly one night-surface blend.
                half fullResolutionCoverage =
                    saturate(tex2D(_NightCoverageTex, input.uv).r);
                half nightOpacity =
                    saturate(_NightOpacity) * (1.0 - fullResolutionCoverage);
                color.rgb = lerp(color.rgb, nightMap, nightOpacity);
                color.a = lerp(color.a, 1.0, nightOpacity);

                float3 viewDirection = normalize(_WorldSpaceCameraPos - input.worldPosition);
                half facing = abs(dot(viewDirection, normalize(input.worldNormal)));
                half rim = pow(saturate(1.0 - facing), _RimPower) * _RimIntensity;

                color.rgb += _RimColor.rgb * rim;
                color.a = saturate(color.a + rim * _RimColor.a);
                return color;
            }
            ENDCG
        }
    }
}

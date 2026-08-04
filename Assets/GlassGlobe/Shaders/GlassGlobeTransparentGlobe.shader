Shader "GlassGlobe/Transparent Globe"
{
    Properties
    {
        _Color ("Color", Color) = (0.05, 0.24, 0.32, 0.16)
        _BlueMarbleTex ("Blue Marble Map", 2D) = "black" {}
        _BlueMarbleOpacity ("Blue Marble Opacity", Range(0, 1)) = 0
        _NightTex ("Night Lights Map", 2D) = "black" {}
        _NightTint ("Night Lights Tint", Color) = (1, 0.87, 0.62, 1)
        _NightIntensity ("Night Lights Intensity", Range(0, 3)) = 0
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
            // those shells. With Cull Front this pass drew the near hemisphere
            // and sampled the map under the observer's feet - invisible while
            // the glass was a flat 6% tint, wrong as soon as it carries a map.
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
            fixed4 _NightTint;
            half _NightIntensity;
            fixed4 _RimColor;
            half _RimIntensity;
            half _RimPower;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.position);
                // The sphere mesh maps longitude with +sin(lon) while EarthMath
                // uses the mirrored -sin(lon) embedding for physical truth, so
                // flip U to keep the map aligned with the border lines.
                output.uv = float2(1.0 - input.uv.x, input.uv.y);
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.worldPosition = mul(unity_ObjectToWorld, input.position).xyz;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                fixed4 color = _Color;

                // Blue Marble sits between the glass tint and the night/rim
                // layers: at 0 the glass is untouched, at 1 the daylight map is
                // fully opaque. It shares input.uv, so it inherits the same
                // mirrored-longitude correction as the night-lights map.
                fixed3 marble = tex2D(_BlueMarbleTex, input.uv).rgb;
                color.rgb = lerp(color.rgb, marble, _BlueMarbleOpacity);
                color.a = lerp(color.a, 1.0, _BlueMarbleOpacity);

                fixed3 night = tex2D(_NightTex, input.uv).rgb * _NightTint.rgb * _NightIntensity;
                half nightLuminance = dot(night, half3(0.299, 0.587, 0.114));

                float3 viewDirection = normalize(_WorldSpaceCameraPos - input.worldPosition);
                half facing = abs(dot(viewDirection, normalize(input.worldNormal)));
                half rim = pow(saturate(1.0 - facing), _RimPower) * _RimIntensity;

                color.rgb += night + _RimColor.rgb * rim;
                color.a = saturate(color.a + nightLuminance + rim * _RimColor.a);
                return color;
            }
            ENDCG
        }
    }
}

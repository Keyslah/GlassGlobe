Shader "GlassGlobe/Transparent Globe"
{
    Properties
    {
        _Color ("Color", Color) = (0.05, 0.24, 0.32, 0.16)
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

            fixed4 _Color;
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

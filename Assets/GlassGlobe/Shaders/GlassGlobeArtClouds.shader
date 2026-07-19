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

            fixed4 Frag(Varyings input) : SV_Target
            {
                float t = _Time.y;
                float2 uv = input.uv;

                // Two copies of the live cloud field drifting at slightly
                // different rates: the offset between them grows so shapes
                // slowly morph, and the whole sheet creeps along like real
                // weather. Slow enough that you only catch it staring.
                float2 drift1 = float2(t * _DriftSpeed, 0.0);
                float2 drift2 = float2(t * _DriftSpeed * 0.62, t * _DriftSpeed * 0.05);

                float density1 = tex2D(_MainTex, uv + drift1).a;
                float density2 = tex2D(_MainTex, uv + drift2 + float2(0.003, 0.001)).a;
                float density = max(density1, density2 * 0.85);

                // Soft dreamy remap: thin haze disappears, solid decks go
                // pillowy with soft edges.
                float body = smoothstep(0.12, 0.85, density);

                // Faint warm/cool tinge wandering across the deck.
                float tinge = 0.5 + 0.5 * sin(uv.x * 9.0 + t * 0.05 + sin(uv.y * 7.0 - t * 0.03));
                float3 warmWhite = float3(1.0, 0.97, 0.93);
                float3 coolWhite = float3(0.93, 0.96, 1.0);
                float3 color = lerp(warmWhite, coolWhite, tinge);

                // Gentle breathing so the deck feels alive without moving fast.
                float breathe = 0.92 + 0.08 * sin(t * 0.07 + uv.x * 14.0 + uv.y * 6.0);

                return fixed4(color, body * breathe * _Opacity);
            }
            ENDCG
        }
    }
}

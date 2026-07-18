Shader "GlassGlobe/Galaxy"
{
    Properties
    {
        _MainTex ("Panorama", 2D) = "black" {}
        _Exposure ("Exposure", Range(0.2, 3.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background-10"
            "RenderType" = "Opaque"
        }

        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Exposure;

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
                fixed4 color = tex2D(_MainTex, input.uv);
                return fixed4(color.rgb * _Exposure, 1.0);
            }
            ENDCG
        }
    }
}

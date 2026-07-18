Shader "GlassGlobe/Transparent Globe"
{
    Properties
    {
        _Color ("Color", Color) = (0.05, 0.24, 0.32, 0.16)
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
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
            };

            fixed4 _Color;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.position);
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}

Shader "GlassGlobe/Line"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _ClipNearHemisphere ("Clip Near Hemisphere", Float) = 0
        [HideInInspector] _GlobeCenter ("Globe Center", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+10"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

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
                float3 worldPosition : TEXCOORD0;
            };

            fixed4 _Color;
            float _ClipNearHemisphere;
            float4 _GlobeCenter;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.position);
                output.worldPosition = mul(unity_ObjectToWorld, input.position).xyz;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                if (_ClipNearHemisphere > 0.5)
                {
                    float3 outward = normalize(input.worldPosition - _GlobeCenter.xyz);
                    float3 towardCamera = normalize(_WorldSpaceCameraPos - input.worldPosition);
                    clip(-dot(outward, towardCamera) - 0.002);
                }

                return _Color;
            }
            ENDCG
        }
    }
}

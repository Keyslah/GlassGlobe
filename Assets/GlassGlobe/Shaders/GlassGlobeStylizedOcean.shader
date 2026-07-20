// =============================================================================
// GlassGlobe / Stylized Ocean
// -----------------------------------------------------------------------------
// A single-pass, mobile-friendly stylized ocean surface. Drop the material on
// any sphere or mesh with UVs and it renders as calm, dreamy animated water:
//
//   * Deep blue -> turquoise patchy color variation (animated value noise)
//   * Two scrolling normal-map layers for gentle ripples
//   * Subtle caustic-style bright filaments drifting through the surface
//   * Soft Fresnel brightening toward grazing angles
//   * Small twinkling sun glints (masked Blinn-Phong)
//   * Slow shimmer (low-frequency brightness modulation)
//
// Deliberately excluded: FFT, tessellation, compute, displacement, foam,
// shorelines, reflections, buoyancy. Everything is per-pixel ALU plus two
// texture fetches, which is comfortable on mobile GPUs.
//
// Lighting is self-contained: the sun comes from the _SunDirection property,
// not from scene lights, so the shader works in any scene without setup.
//
// The ripple texture (GlassGlobeOceanRippleNormal.png) must be imported with
// Texture Type = "Normal map". If no texture is assigned, the "bump" default
// keeps the surface flat but everything else still animates.
//
// Integration hooks (all default to standalone behavior): _Opacity and an
// optional _LandMask (black = water) let the globe's art stack fade the ocean
// and carve out continents with a soft coastline, while _Cull and _ZWrite let
// it render the inside of the far hemisphere like the other GlassGlobe shells.
// The shading normal is flipped toward the camera per-pixel, so the water
// looks correct from either side of the surface (including seen through the
// inside of a globe's far hemisphere).
// =============================================================================
Shader "GlassGlobe/Stylized Ocean"
{
    Properties
    {
        [Header(Water Colors)]
        _DeepColor      ("Deep Water Color", Color) = (0.015, 0.11, 0.30, 1)
        _ShallowColor   ("Turquoise Water Color", Color) = (0.10, 0.62, 0.66, 1)
        _ColorScale     ("Color Patch Scale", Float) = 3.0
        _ColorDrift     ("Color Drift Speed", Float) = 0.015

        [Header(Ripples Two Scrolling Normal Layers)]
        _BumpMap        ("Ripple Normal Map", 2D) = "bump" {}
        _RippleTiling   ("Ripple Tiling (XY layer 1 ZW layer 2)", Vector) = (4, 4, 7.3, 7.3)
        _RippleSpeed    ("Ripple Scroll Speed (XY layer 1 ZW layer 2)", Vector) = (0.018, 0.011, -0.014, 0.008)
        _RippleStrength ("Ripple Strength", Range(0, 2)) = 0.55

        [Header(Sun Glints)]
        _SunDirection   ("Sun Direction (world space)", Vector) = (0.35, 0.65, 0.4, 0)
        _SunColor       ("Sun and Glint Color", Color) = (1.0, 0.95, 0.82, 1)
        _GlintSharpness ("Glint Sharpness", Range(16, 512)) = 220
        _GlintStrength  ("Glint Strength", Range(0, 4)) = 1.6
        _SparkleScale   ("Sparkle Mask Scale", Float) = 48
        _SparkleSpeed   ("Sparkle Twinkle Speed", Float) = 0.5

        [Header(Caustics)]
        _CausticScale    ("Caustic Scale", Float) = 9.0
        _CausticSpeed    ("Caustic Drift Speed", Float) = 0.03
        _CausticStrength ("Caustic Strength", Range(0, 1)) = 0.22

        [Header(Fresnel and Shimmer)]
        _FresnelColor    ("Fresnel Edge Color", Color) = (0.55, 0.85, 0.95, 1)
        _FresnelPower    ("Fresnel Power", Range(1, 8)) = 3.0
        _FresnelStrength ("Fresnel Strength", Range(0, 2)) = 0.45
        _ShimmerStrength ("Shimmer Strength", Range(0, 0.5)) = 0.10
        _ShimmerSpeed    ("Shimmer Speed", Float) = 0.05

        [Header(Integration)]
        _LandMask        ("Land Mask (black is water)", 2D) = "black" {}
        _Opacity         ("Overall Opacity", Range(0, 1)) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
        [Enum(Off, 0, On, 1)] _ZWrite ("Depth Write", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
        }

        Pass
        {
            Cull [_Cull]
            ZWrite [_ZWrite]
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _BumpMap;
            sampler2D _LandMask;
            half   _Opacity;

            half4  _DeepColor;
            half4  _ShallowColor;
            float  _ColorScale;
            float  _ColorDrift;

            float4 _RippleTiling;
            float4 _RippleSpeed;
            half   _RippleStrength;

            float4 _SunDirection;
            half4  _SunColor;
            half   _GlintSharpness;
            half   _GlintStrength;
            float  _SparkleScale;
            float  _SparkleSpeed;

            float  _CausticScale;
            float  _CausticSpeed;
            half   _CausticStrength;

            half4  _FresnelColor;
            half   _FresnelPower;
            half   _FresnelStrength;
            half   _ShimmerStrength;
            float  _ShimmerSpeed;

            struct Attributes
            {
                float4 position : POSITION;
                float3 normal   : NORMAL;
                float4 tangent  : TANGENT;
                float2 uv       : TEXCOORD0;
            };

            struct Varyings
            {
                float4 position  : SV_POSITION;
                float2 uv        : TEXCOORD0; // base mesh UV (noise, caustics, sparkles)
                float4 uvRipple  : TEXCOORD1; // xy = ripple layer 1, zw = ripple layer 2
                float3 worldPos  : TEXCOORD2;
                half3  tangentX  : TEXCOORD3; // world-space TBN, one row per interpolator
                half3  tangentY  : TEXCOORD4;
                half3  tangentZ  : TEXCOORD5;
            };

            // ----------------------------------------------------------------
            // Cheap ALU-only value noise (same family as the other GlassGlobe
            // shaders). Used for color patches, caustics, sparkle twinkle and
            // shimmer, so no extra textures are needed beyond the normal map.
            // ----------------------------------------------------------------
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

            // Two octaves are enough for soft, dreamy patches and cost little.
            float Fbm2(float2 p)
            {
                return ValueNoise(p) * 0.667 + ValueNoise(p * 2.13 + 17.7) * 0.333;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.position);
                output.uv = input.uv;
                output.worldPos = mul(unity_ObjectToWorld, input.position).xyz;

                // Scrolled ripple UVs are computed here in full float precision
                // so long play sessions never degrade into half-precision
                // stutter on mobile GPUs.
                float t = _Time.y;
                output.uvRipple.xy = input.uv * _RippleTiling.xy + _RippleSpeed.xy * t;
                output.uvRipple.zw = input.uv * _RippleTiling.zw + _RippleSpeed.zw * t;

                // World-space TBN rows for tangent -> world normal transform.
                half3 worldNormal  = UnityObjectToWorldNormal(input.normal);
                half3 worldTangent = UnityObjectToWorldDir(input.tangent.xyz);
                half tangentSign   = input.tangent.w * unity_WorldTransformParams.w;
                half3 worldBitangent = cross(worldNormal, worldTangent) * tangentSign;
                output.tangentX = half3(worldTangent.x, worldBitangent.x, worldNormal.x);
                output.tangentY = half3(worldTangent.y, worldBitangent.y, worldNormal.y);
                output.tangentZ = half3(worldTangent.z, worldBitangent.z, worldNormal.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float t = _Time.y;

                // --------------------------------------------------------
                // 1. Ripples: blend two scrolling normal-map layers moving
                //    in different directions at different scales. Whiteout
                //    blending keeps the combined slopes gentle and natural.
                // --------------------------------------------------------
                half3 ripple1 = UnpackNormal(tex2D(_BumpMap, input.uvRipple.xy));
                half3 ripple2 = UnpackNormal(tex2D(_BumpMap, input.uvRipple.zw));
                half3 rippleTangent = normalize(half3(
                    (ripple1.xy + ripple2.xy) * _RippleStrength,
                    ripple1.z * ripple2.z));

                half3 viewDir = normalize(_WorldSpaceCameraPos - input.worldPos);
                half3 sunDir  = normalize(_SunDirection.xyz);

                // Geometric normal, then the ripple-perturbed normal, both
                // flipped to face the camera so the surface shades correctly
                // whether we see its front or its back (the globe's far-side
                // shell is viewed from the inside).
                half3 geoNormal = normalize(half3(
                    input.tangentX.z, input.tangentY.z, input.tangentZ.z));
                half faceSign = dot(geoNormal, viewDir) < 0.0 ? -1.0 : 1.0;
                geoNormal *= faceSign;
                half3 normal = faceSign * normalize(half3(
                    dot(input.tangentX, rippleTangent),
                    dot(input.tangentY, rippleTangent),
                    dot(input.tangentZ, rippleTangent)));

                // --------------------------------------------------------
                // 2. Base color: slow-drifting noise patches blend deep blue
                //    into turquoise, and grazing angles lean slightly deeper
                //    so curved surfaces read as a volume of water.
                // --------------------------------------------------------
                float patches = Fbm2(input.uv * _ColorScale + t * _ColorDrift);
                half depthMix = smoothstep(0.25, 0.75, patches);
                half3 water = lerp(_DeepColor.rgb, _ShallowColor.rgb, depthMix);

                half ndv = saturate(dot(geoNormal, viewDir));
                water = lerp(water, _DeepColor.rgb, (1.0 - ndv) * 0.35);

                // Soft wrapped diffuse from the self-contained sun keeps the
                // dark side gently lit instead of harshly shadowed (dreamy,
                // not dramatic).
                half wrap = saturate(dot(normal, sunDir) * 0.5 + 0.5);
                water *= lerp(0.6, 1.0, wrap);

                // --------------------------------------------------------
                // 3. Caustics: two noise fields drift in opposite directions
                //    and the ridge of their difference forms thin, wandering
                //    bright filaments - a convincing caustic impression for
                //    the price of two noise evaluations.
                // --------------------------------------------------------
                float2 causticUV = input.uv * _CausticScale;
                float causticA = ValueNoise(causticUV + float2(t * _CausticSpeed, t * _CausticSpeed * 0.6));
                float causticB = ValueNoise(causticUV * 1.27 - float2(t * _CausticSpeed * 0.8, t * _CausticSpeed * 0.5));
                half ridge = 1.0 - abs(causticA - causticB) * 2.0;
                half caustic = smoothstep(0.78, 1.0, ridge);
                water += _ShallowColor.rgb * caustic * _CausticStrength * depthMix;

                // --------------------------------------------------------
                // 4. Shimmer: very slow, low-frequency brightness breathing
                //    across the surface.
                // --------------------------------------------------------
                half shimmer = ValueNoise(input.uv * 6.0 - t * _ShimmerSpeed);
                water *= 1.0 + (shimmer - 0.5) * 2.0 * _ShimmerStrength;

                // --------------------------------------------------------
                // 5. Fresnel: soft additive brightening toward the rim.
                // --------------------------------------------------------
                half fresnel = pow(1.0 - ndv, _FresnelPower);
                water += _FresnelColor.rgb * fresnel * _FresnelStrength;

                // --------------------------------------------------------
                // 6. Sun glints: tight Blinn-Phong highlight over the rippled
                //    normal, modulated by an animated sparkle mask so the
                //    glints twinkle on and off instead of forming one static
                //    hotspot.
                // --------------------------------------------------------
                half3 halfDir = normalize(sunDir + viewDir);
                half spec = pow(saturate(dot(normal, halfDir)), _GlintSharpness);
                float twinkle = ValueNoise(input.uv * _SparkleScale + t * _SparkleSpeed)
                              * ValueNoise(input.uv * _SparkleScale * 1.7 - t * _SparkleSpeed * 0.8);
                half sparkle = smoothstep(0.18, 0.55, twinkle);
                water += _SunColor.rgb * spec * _GlintStrength * (0.25 + 0.75 * sparkle);

                // Optional land mask carves continents out of the water with a
                // soft coastline (same blend curve as the Earth Art shader).
                // The default black mask leaves the whole surface as ocean.
                half landBlend = smoothstep(0.25, 0.75, tex2D(_LandMask, input.uv).r);
                return half4(water, _Opacity * (1.0 - landBlend));
            }
            ENDCG
        }
    }

    Fallback Off
}

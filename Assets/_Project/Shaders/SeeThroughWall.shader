Shader "BoundByLight/SeeThroughWall"
{
    // Muro URP che si "buca" con dithering attorno ai player quando si trova
    // TRA la camera e il player (effetto see-through tipo Hades).
    //
    // Le posizioni dei player sono passate come proprietà GLOBALI da
    // WallSeeThroughController.cs:
    //   _SeeThroughPositions[i] = (viewportX, viewportY, distanzaDallaCamera, 0)
    //   _SeeThroughCount        = numero di player validi
    //
    // Assegna questo shader ai materiali dei MURI (e solo a quelli).
    Properties
    {
        _BaseMap   ("Base Map", 2D)        = "white" {}
        _BaseColor ("Base Color", Color)   = (1,1,1,1)

        [Header(See Through)]
        _SeeThroughRadius    ("Raggio buco (frazione altezza schermo)", Range(0,0.5)) = 0.12
        _SeeThroughSoftness  ("Morbidezza bordo", Range(0.001,0.3))                   = 0.05
        _SeeThroughDepthBias ("Bias profondità (unità mondo)", Range(0,3))            = 0.3
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }

        // ── Pass principale (forward) ─────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Illuminazione URP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                float  _SeeThroughRadius;
                float  _SeeThroughSoftness;
                float  _SeeThroughDepthBias;
            CBUFFER_END

            // ── Globali (impostate da WallSeeThroughController.cs) ─────────────
            // xy = posizione viewport (0..1), z = distanza dalla camera
            float4 _SeeThroughPositions[8];
            float  _SeeThroughCount;

            // Matrice di Bayer 4x4 per il dithering (screen-door transparency)
            static const float BayerMatrix4x4[16] =
            {
                 0.0/16.0,  8.0/16.0,  2.0/16.0, 10.0/16.0,
                12.0/16.0,  4.0/16.0, 14.0/16.0,  6.0/16.0,
                 3.0/16.0, 11.0/16.0,  1.0/16.0,  9.0/16.0,
                15.0/16.0,  7.0/16.0, 13.0/16.0,  5.0/16.0
            };

            float DitherThreshold(float2 pixelPos)
            {
                int x = (int)fmod(pixelPos.x, 4.0);
                int y = (int)fmod(pixelPos.y, 4.0);
                return BayerMatrix4x4[y * 4 + x];
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = pos.positionCS;
                OUT.positionWS  = pos.positionWS;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            // Quanto questo frammento deve "bucarsi" (0 = pieno, 1 = trasparente)
            float ComputeHoleAmount(float4 positionHCS, float3 positionWS)
            {
                // GetNormalizedScreenSpaceUV gestisce il flip Y tra piattaforme
                // (DX11 top-left vs viewport bottom-left) → coerente con WorldToViewportPoint.
                float2 screenUV  = GetNormalizedScreenSpaceUV(positionHCS);
                float  aspect    = _ScreenParams.x / _ScreenParams.y;
                float  fragDepth = -TransformWorldToView(positionWS).z; // distanza dalla camera

                float hole  = 0.0;
                int   count = (int)_SeeThroughCount;

                [loop]
                for (int i = 0; i < count; i++)
                {
                    float4 p = _SeeThroughPositions[i];

                    // Buca SOLO i muri davanti al player (più vicini alla camera)
                    if (fragDepth >= p.z - _SeeThroughDepthBias) continue;

                    float2 d = screenUV - p.xy;
                    d.x *= aspect;                 // cerchio tondo a schermo
                    float dist = length(d);

                    float h = 1.0 - smoothstep(_SeeThroughRadius - _SeeThroughSoftness,
                                               _SeeThroughRadius, dist);
                    hole = max(hole, h);
                }
                return hole;
            }

            half3 ShadeSimple(float3 positionWS, half3 normalWS, half3 albedo)
            {
                float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half3 color = albedo * mainLight.color *
                    (saturate(dot(normalWS, mainLight.direction)) * mainLight.shadowAttenuation);

                // Ambiente (SH / luce ambientale)
                color += albedo * SampleSH(normalWS);

                // Luci aggiuntive (point/spot — es. glow proiettili, candele)
                uint addCount = GetAdditionalLightsCount();
                for (uint li = 0u; li < addCount; li++)
                {
                    Light l = GetAdditionalLight(li, positionWS);
                    half atten = l.distanceAttenuation * l.shadowAttenuation;
                    color += albedo * l.color * (saturate(dot(normalWS, l.direction)) * atten);
                }
                return color;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 baseCol = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                // Dithering: buca il muro attorno ai player
                float hole    = ComputeHoleAmount(IN.positionHCS, IN.positionWS);
                float dither  = DitherThreshold(IN.positionHCS.xy);
                clip((1.0 - hole) - dither);

                half3 lit = ShadeSimple(IN.positionWS, normalize(IN.normalWS), baseCol.rgb);
                return half4(lit, 1.0);
            }
            ENDHLSL
        }

        // ── Shadow caster (i muri continuano a proiettare ombre) ──────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct AttributesS { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct VaryingsS   { float4 positionHCS : SV_POSITION; };

            VaryingsS ShadowVert(AttributesS IN)
            {
                VaryingsS OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nWS   = TransformObjectToWorldNormal(IN.normalOS);
                float4 hcs   = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    hcs.z = min(hcs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    hcs.z = max(hcs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionHCS = hcs;
                return OUT;
            }

            half4 ShadowFrag(VaryingsS IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // ── Depth (per depth prepass / effetti che usano la depth texture) ────
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct AttributesD { float4 positionOS : POSITION; };
            struct VaryingsD   { float4 positionHCS : SV_POSITION; };

            VaryingsD DepthVert(AttributesD IN)
            {
                VaryingsD OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 DepthFrag(VaryingsD IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}

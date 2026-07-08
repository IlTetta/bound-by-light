Shader "BoundByLight/ReviveIndicator"
{
    // Anello di revive disegnato a terra dai LineRenderer di ReviveIndicator.cs.
    // Unlit, additivo, e visibile ATTRAVERSO la geometria (muri, colonne) a
    // intensità ridotta, così il compagno svenuto si individua sempre.
    //
    // Il colore viene dai VERTEX COLOR (LineRenderer.startColor/endColor):
    // tieni _BaseColor bianco, altrimenti i due si moltiplicano.
    Properties
    {
        _BaseColor        ("Tint (lascia bianco)", Color)           = (1,1,1,1)
        _Intensity        ("Intensità", Range(0,4))                 = 1
        _OccludedStrength ("Intensità dietro i muri", Range(0,1))   = 0.35

        // Default: SrcAlpha One = additivo, con l'alpha del vertex color come intensità.
        // Per un blend alpha classico: Src = SrcAlpha (5), Dst = OneMinusSrcAlpha (10).
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent+100"
            "IgnoreProjector" = "True"
        }

        Cull   Off      // il LineRenderer genera quad piatti: niente backface culling
        ZWrite Off
        Blend  [_SrcBlend] [_DstBlend]

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4  _BaseColor;
            half   _Intensity;
            half   _OccludedStrength;
            float  _SrcBlend;
            float  _DstBlend;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            half4  color      : COLOR;   // scritto da LineRenderer.startColor/endColor
        };

        struct Varyings
        {
            float4 positionHCS : SV_POSITION;
            half4  color       : COLOR;
        };

        Varyings vert(Attributes IN)
        {
            Varyings OUT;
            OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
            OUT.color       = IN.color * _BaseColor;
            return OUT;
        }

        // strength modula solo l'alpha: con Blend SrcAlpha One l'alpha È l'intensità,
        // con un alpha blend classico è l'opacità. Funziona in entrambi i casi.
        half4 Shade(Varyings IN, half strength)
        {
            half4 c = IN.color;
            c.rgb *= _Intensity;
            c.a   *= strength;
            return c;
        }
        ENDHLSL

        // ── Dietro la geometria ───────────────────────────────────────────────
        // I due pass hanno LightMode diversi di proposito: URP disegna un solo
        // pass per ShaderTagId, quindi due pass con lo stesso LightMode
        // renderizzerebbero solo il primo.
        Pass
        {
            Name "ReviveOccluded"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ZTest Greater

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment fragOccluded
            half4 fragOccluded(Varyings IN) : SV_Target { return Shade(IN, _OccludedStrength); }
            ENDHLSL
        }

        // ── In vista diretta ──────────────────────────────────────────────────
        Pass
        {
            Name "ReviveVisible"
            Tags { "LightMode" = "UniversalForward" }

            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment fragVisible
            half4 fragVisible(Varyings IN) : SV_Target { return Shade(IN, 1.0h); }
            ENDHLSL
        }
    }

    Fallback Off
}

Shader "Wasteland/ToonVAT"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.45, 0.42, 0.38, 1)
        [NoScaleOffset] _TeamMaskTex ("Team Mask (RGBA)", 2D) = "black" {}
        _UseVertexColorMask ("Use Vertex Color Mask", Float) = 1
        _WeatheringColor ("Weathering Color", Color) = (0.25, 0.22, 0.18, 1)
        _HatchScale ("Hatch Scale", Float) = 8
        _SmoothStep ("Toon Smooth Step", Range(0.01, 0.25)) = 0.08
        _ShadowTint ("Shadow Tint", Color) = (0.227, 0.247, 0.333, 1)
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.4
        _RimColor ("Rim Color", Color) = (0.72, 0.76, 0.82, 1)
        _RimWidth ("Rim Width", Range(0.05, 0.5)) = 0.2
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.35
        _EnableRim ("Enable Rim", Float) = 1
        _TeamIndex ("Team Index (packed bits)", Float) = 0
        _AnimIndex ("Anim Index (packed bits)", Float) = 0
        _AnimTime ("Anim Start Time", Float) = 0
        [NoScaleOffset] _VATPos ("VAT Positions", 2D) = "black" {}
        [NoScaleOffset] _VATNrm ("VAT Normals", 2D) = "bump" {}
        [NoScaleOffset] _VATClipTex ("VAT Clip Table", 2D) = "black" {}
        _VATRowsPerFrame ("VAT Rows Per Frame", Float) = 1
        [HideInInspector] _Cull ("Cull", Float) = 2
        [HideInInspector] _Cutoff ("Alpha Cutoff", Float) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 100
        Cull [_Cull]
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex WastelandToonVert
            #pragma fragment WastelandToonFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _FORWARD_PLUS
            #define WASTELAND_VAT
            #include "Includes/WastelandToonPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex WastelandToonShadowVert
            #pragma fragment WastelandToonShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define WASTELAND_VAT
            #include "Includes/WastelandToonPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex WastelandToonDepthVert
            #pragma fragment WastelandToonDepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #define WASTELAND_VAT
            #include "Includes/WastelandToonPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex WastelandToonDepthVert
            #pragma fragment WastelandToonDepthNormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #define WASTELAND_VAT
            #include "Includes/WastelandToonPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex WastelandToonDepthVert
            #pragma fragment WastelandToonDepthNormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #define WASTELAND_VAT
            #include "Includes/WastelandToonPass.hlsl"
            ENDHLSL
        }
    }
    FallBack Off
}

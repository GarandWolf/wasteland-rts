Shader "Wasteland/ToonOutline"
{
    Properties
    {
        _OutlineColor ("Fallback Outline Color", Color) = (0.12, 0.11, 0.10, 1)
        _OutlineMultiply ("Albedo Multiply", Range(0.1, 1)) = 0.35
        _DepthThreshold ("Depth Threshold (m, linear eye)", Range(0.01, 5)) = 0.4
        _NormalThreshold ("Normal Threshold", Range(0.01, 1)) = 0.35
        _OutlineWidth ("Width (px at 65m)", Range(0.5, 4)) = 1.5
        _CameraHeight ("Camera Height (m)", Float) = 65
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "ToonOutline"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _OutlineColor;
            float4 _BlitTexture_TexelSize;
            float _OutlineMultiply;
            float _DepthThreshold;
            float _NormalThreshold;
            float _OutlineWidth;
            float _CameraHeight;

            float SampleLinearDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                // Style-guide widths: 2 px at 30 m, 1.5 px at 110 m. _OutlineWidth is the 65 m scale.
                float heightT = saturate((_CameraHeight - 30.0) / 80.0);
                float specPx = lerp(2.0, 1.5, heightT);
                float width = specPx * (_OutlineWidth / 1.5);

                float2 texel = width * _BlitTexture_TexelSize.xy;
                float depth = SampleLinearDepth(uv);
                float3 normal = SampleSceneNormals(uv);

                float depthN = SampleLinearDepth(uv + float2(0, texel.y));
                float depthS = SampleLinearDepth(uv + float2(0, -texel.y));
                float depthE = SampleLinearDepth(uv + float2(texel.x, 0));
                float depthW = SampleLinearDepth(uv + float2(-texel.x, 0));
                float depthEdge = abs(depthN - depth) + abs(depthS - depth) + abs(depthE - depth) + abs(depthW - depth);

                float3 nN = SampleSceneNormals(uv + float2(0, texel.y));
                float3 nE = SampleSceneNormals(uv + float2(texel.x, 0));
                float normalEdge = (1.0 - saturate(dot(normal, nN))) + (1.0 - saturate(dot(normal, nE)));

                float edge = saturate(max(depthEdge / max(_DepthThreshold, 1e-4), normalEdge / max(_NormalThreshold, 1e-4)));
                float3 outline = color.rgb * _OutlineMultiply;
                outline = lerp(_OutlineColor.rgb, outline, 0.65);
                color.rgb = lerp(color.rgb, outline, edge);
                return color;
            }
            ENDHLSL
        }
    }
    FallBack Off
}

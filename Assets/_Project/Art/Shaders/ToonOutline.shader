Shader "Wasteland/ToonOutline"
{
    Properties
    {
        _OutlineColor ("Fallback Outline Color", Color) = (0.12, 0.11, 0.10, 1)
        _OutlineMultiply ("Albedo Multiply", Range(0.1, 1)) = 0.35
        _DepthThreshold ("Depth Threshold", Range(0.0001, 0.05)) = 0.003
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);
            TEXTURE2D_X(_CameraNormalsTexture);
            SAMPLER(sampler_CameraNormalsTexture);

            float4 _OutlineColor;
            float4 _BlitTexture_TexelSize;
            float _OutlineMultiply;
            float _DepthThreshold;
            float _NormalThreshold;
            float _OutlineWidth;
            float _CameraHeight;

            float SampleDepth(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;
            }

            float3 SampleNormal(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_CameraNormalsTexture, sampler_CameraNormalsTexture, uv).xyz * 2.0 - 1.0;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float width = _OutlineWidth;
                if (_CameraHeight > 60.0)
                {
                    width = lerp(2.0, 1.5, saturate((_CameraHeight - 30.0) / 80.0));
                    width = min(width, _OutlineWidth);
                }

                float2 texel = width * _BlitTexture_TexelSize.xy;
                float depth = SampleDepth(uv);
                float3 normal = SampleNormal(uv);

                float depthN = SampleDepth(uv + float2(0, texel.y));
                float depthS = SampleDepth(uv + float2(0, -texel.y));
                float depthE = SampleDepth(uv + float2(texel.x, 0));
                float depthW = SampleDepth(uv + float2(-texel.x, 0));
                float depthEdge = abs(depthN - depth) + abs(depthS - depth) + abs(depthE - depth) + abs(depthW - depth);

                float3 nN = SampleNormal(uv + float2(0, texel.y));
                float3 nE = SampleNormal(uv + float2(texel.x, 0));
                float normalEdge = (1.0 - saturate(dot(normal, nN))) + (1.0 - saturate(dot(normal, nE)));

                float edge = saturate(max(depthEdge / max(_DepthThreshold, 1e-5), normalEdge / max(_NormalThreshold, 1e-4)));
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

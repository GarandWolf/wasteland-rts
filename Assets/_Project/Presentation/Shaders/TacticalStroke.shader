Shader "Wasteland/Tactical Stroke"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _WidthPx ("WidthPx", Float) = 1
        _WorldPerPixel ("WorldPerPixel", Float) = 1
        _DashOn ("DashOn", Float) = 0
        _DashOff ("DashOff", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _WidthPx;
                float _WorldPerPixel;
                float _DashOn;
                float _DashOff;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 sideAlong : TEXCOORD0;
                float2 perp : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float alongWorld : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                float3 pos = input.positionOS;
                float halfWidth = _WidthPx * 0.5 * _WorldPerPixel;
                pos.x += input.sideAlong.x * input.perp.x * halfWidth;
                pos.z += input.sideAlong.x * input.perp.y * halfWidth;
                Varyings output;
                output.positionCS = TransformObjectToHClip(pos);
                output.alongWorld = input.sideAlong.y;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_DashOn > 0.001)
                {
                    float alongPx = input.alongWorld / max(_WorldPerPixel, 0.00001);
                    float period = _DashOn + _DashOff;
                    if (fmod(alongPx, period) > _DashOn)
                    {
                        discard;
                    }
                }

                return _Color;
            }
            ENDHLSL
        }
    }
}

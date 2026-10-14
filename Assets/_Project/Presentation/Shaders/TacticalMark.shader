Shader "Wasteland/Tactical Mark"
{
    Properties
    {
        _WorldPerPixel ("WorldPerPixel", Float) = 1
        _BonePx ("BonePx", Float) = 2
        _InkPx ("InkPx", Float) = 1
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
                float _WorldPerPixel;
                float _BonePx;
                float _InkPx;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 offsetPx : TEXCOORD0;
                float4 shape : TEXCOORD1;
                float4 extra : TEXCOORD2;
                float4 secondary : TEXCOORD3;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 offsetPx : TEXCOORD0;
                float4 shape : TEXCOORD1;
                float4 extra : TEXCOORD2;
                float4 secondary : TEXCOORD3;
                float4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                float3 pos = input.positionOS;
                pos.x += input.offsetPx.x * _WorldPerPixel;
                pos.z += input.offsetPx.y * _WorldPerPixel;
                Varyings output;
                output.positionCS = TransformObjectToHClip(pos);
                output.offsetPx = input.offsetPx.xy;
                output.shape = input.shape;
                output.extra = input.extra;
                output.secondary = input.secondary;
                output.color = input.color;
                return output;
            }

            float Star(float2 p, float radius)
            {
                float angle = atan2(p.x, p.y);
                float sector = 6.2831853 / 5.0;
                float m = fmod(angle, sector);
                if (m < 0.0)
                {
                    m += sector;
                }

                float t = abs(m - sector * 0.5) / (sector * 0.5);
                float reach = lerp(radius * 0.48, radius * 0.2, t);
                return length(p) < reach ? 1.0 : 0.0;
            }

            float Glyph(float2 p, float kind)
            {
                if (kind < 0.5)
                {
                    return 0.0;
                }

                if (kind < 1.5)
                {
                    return (abs(abs(p.x) - 3.0) < 1.2 && abs(p.y) < 5.0) ? 1.0 : 0.0;
                }

                if (kind < 2.5)
                {
                    float y = p.y + 1.0;
                    return (abs(abs(p.x) - (3.0 - y)) < 0.8 && y > -2.0 && y < 3.0) ? 1.0 : 0.0;
                }

                if (kind < 3.5)
                {
                    return (abs(p.x) / 5.0 + abs(p.y) / 4.0) < 1.0 ? 1.0 : 0.0;
                }

                if (kind < 4.5)
                {
                    return (p.y > -3.5 && p.y < 4.0 && abs(p.x) < (4.0 - p.y) * 0.7) ? 1.0 : 0.0;
                }

                if (kind < 5.5)
                {
                    return length(p) < 3.2 ? 1.0 : 0.0;
                }

                if (kind < 6.5)
                {
                    return (abs(p.x) < 1.1 && abs(p.y) < 5.0) || (abs(p.y) < 1.1 && abs(p.x) < 5.0) ? 1.0 : 0.0;
                }

                if (kind < 7.5)
                {
                    float2 a = abs(p);
                    return (a.x < 4.5 && a.y < 4.5 && (a.x > 2.8 || a.y > 2.8)) ? 1.0 : 0.0;
                }

                if (kind < 8.5)
                {
                    return (abs(p.y) < 1.4 && abs(p.x) < 6.0) ? 1.0 : 0.0;
                }

                if (kind < 9.5)
                {
                    return abs(abs(p.x) - abs(p.y)) < 1.1 && abs(p.x) < 5.0 ? 1.0 : 0.0;
                }

                if (kind < 10.5)
                {
                    return (abs(p.x) < 1.2 && p.y > -5.0 && p.y < 2.0) || (p.y > 1.2 && abs(p.x) < (5.0 - p.y)) ? 1.0 : 0.0;
                }

                if (kind < 11.5)
                {
                    float barrel = (abs(p.y) < 0.8 && p.x > -1.0 && p.x < 6.0) ? 1.0 : 0.0;
                    float trail = (abs(p.y + 1.6) < 0.55 && p.x > -6.0 && p.x < -0.5) ? 1.0 : 0.0;
                    float wheelA = length(p - float2(-1.5, -2.2)) < 1.45 ? 1.0 : 0.0;
                    float wheelB = length(p - float2(1.6, -2.2)) < 1.45 ? 1.0 : 0.0;
                    return max(max(barrel, trail), max(wheelA, wheelB));
                }

                return 0.0;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.offsetPx;
                float mode = input.shape.z;
                float widthPx = input.shape.x;
                float heightPx = input.shape.y;
                float hq = input.shape.w;
                float contested = input.extra.x;
                float capture = input.extra.y;
                float glyph = input.extra.z;
                float4 ink = float4(0.1176, 0.1216, 0.1137, 1.0);
                float4 bone = float4(0.9098, 0.8863, 0.8157, 1.0);
                float4 paper = float4(0.9020, 0.8667, 0.7765, 1.0);

                if (mode < 1.5)
                {
                    float radius = widthPx * 0.5;
                    float dist = length(p);
                    float ringOut = hq > 0.5 ? radius + 4.0 : radius;
                    if (capture > 0.001)
                    {
                        float ang = atan2(p.x, p.y);
                        if (ang < 0.0)
                        {
                            ang += 6.2831853;
                        }

                        if (dist > ringOut + 1.5 && dist < ringOut + 4.5 && ang < capture * 6.2831853)
                        {
                            return input.secondary;
                        }
                    }

                    if (hq > 0.5 && dist > radius + 1.5 && dist < radius + 3.5)
                    {
                        return ink;
                    }

                    if (dist > radius)
                    {
                        discard;
                    }

                    if (dist > radius - 1.5)
                    {
                        return ink;
                    }

                    if (hq > 0.5 && Star(p, radius) > 0.5)
                    {
                        return ink;
                    }

                    if (contested > 0.5)
                    {
                        float band = fmod(p.x + p.y, 8.0);
                        if (band < 0.0)
                        {
                            band += 8.0;
                        }

                        return band < 4.0 ? input.color : input.secondary;
                    }

                    return input.color;
                }

                if (mode < 2.5)
                {
                    float2 halfSize = float2(widthPx, heightPx) * 0.5;
                    float2 absP = abs(p);
                    if (absP.x > halfSize.x || absP.y > halfSize.y)
                    {
                        discard;
                    }

                    if (absP.x > halfSize.x - 1.5 || absP.y > halfSize.y - 1.5)
                    {
                        return ink;
                    }

                    if (glyph < 0.5)
                    {
                        return paper;
                    }

                    if (Glyph(p, glyph) > 0.5)
                    {
                        return ink;
                    }

                    return input.color;
                }

                float bonePx = _BonePx < 0.5 ? 2.0 : _BonePx;
                float inkPx = _InkPx < 0.5 ? 1.0 : _InkPx;
                float band = bonePx + inkPx;
                if (mode < 3.5)
                {
                    float rect = input.shape.w;
                    if (rect > 0.5)
                    {
                        float2 halfSize = float2(widthPx, heightPx) * 0.5;
                        float outside = max(abs(p.x) - halfSize.x, abs(p.y) - halfSize.y);
                        if (outside < 0.0 || outside > band)
                        {
                            discard;
                        }

                        return outside > bonePx ? ink : bone;
                    }

                    float radius = widthPx * 0.5;
                    float dist = length(p);
                    if (dist < radius || dist > radius + band)
                    {
                        discard;
                    }

                    return dist > radius + bonePx ? ink : bone;
                }

                float alertRadius = widthPx * 0.5;
                float alertDist = length(p);
                if (alertDist < alertRadius || alertDist > alertRadius + 3.5)
                {
                    discard;
                }

                return alertDist > alertRadius + 1.6 ? ink : input.color;
            }
            ENDHLSL
        }
    }
}

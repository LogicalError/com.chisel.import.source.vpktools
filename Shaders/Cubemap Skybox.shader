// A Source 2D skybox: the six sky faces as one cubemap, turned about the vertical axis, tinted and exposed.
Shader "Skybox/Cubemap Skybox"
{
    Properties
    {
        _Tex ("Cubemap", Cube) = "" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _Exposure ("Exposure", Range(0,8)) = 1
        _Rotation ("Horizontal Rotation", Range(0,360)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
        }

        ZWrite Off
        ZTest LEqual

        Pass
        {
            Name "Skybox"

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Exposure;
                float _Rotation;
            CBUFFER_END

            TEXTURECUBE(_Tex);
            SAMPLER(sampler_Tex);

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float4 positionCS = TransformWorldToHClip(positionWS);

                // Force the skybox to the far depth plane.
                // This makes it behave as if it is infinitely far away.
                #if UNITY_REVERSED_Z
                    positionCS.z = 0.0;
                #else
                    positionCS.z = positionCS.w;
                #endif

                output.positionCS = positionCS;
                output.direction = positionWS - GetCameraPositionWS();
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);

                // Rotate around world Y.
                float sinAngle, cosAngle;
                sincos(radians(_Rotation), sinAngle, cosAngle);
                float3 rotatedDirection = float3(direction.x * cosAngle - direction.z * sinAngle,
                                                 direction.y,
                                                 direction.x * sinAngle + direction.z * cosAngle);

                half4 color = SAMPLE_TEXTURECUBE(_Tex, sampler_Tex, rotatedDirection);
                color.rgb *= _Tint.rgb * _Exposure;
                return color;
            }

            ENDHLSL
        }
    }

    FallBack Off
}

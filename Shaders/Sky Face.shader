// A Source sky face: a face of the level textured with the sky (toolsskybox). The game draws nothing there, so the sky
// shows through. Here the face draws what the sky shows at that pixel - the 3D skybox's view when a SourceSkyCamera
// draws one (_SourceSky3D), the 2D sky's cubemap otherwise.
//
// It is a BACKDROP: it draws only where nothing else has, and never hides what is behind it. Its depth goes to the far
// plane and it writes none, so every other surface wins - the 3D skybox miniature above all, which sits behind these
// faces in the level and used to be sliced up by them. Source has the same effect for a different reason: it draws the
// sky view first over the whole frame and then skips sky faces entirely while drawing the world, so whatever the sky
// left behind stays. The properties are the 2D sky's (Skybox/Cubemap Skybox).
Shader "Chisel/Source/Sky Face"
{
    Properties
    {
        [NoScaleOffset] _Tex ("Cubemap", Cube) = "" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _Exposure ("Exposure", Range(0,8)) = 1
        _Rotation ("Horizontal Rotation", Range(0,360)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            half _Exposure;
            float _Rotation;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
        };
        ENDHLSL

        Pass
        {
            Name "SkyFace"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            TEXTURECUBE(_Tex); SAMPLER(sampler_Tex);
            TEXTURE2D(_SourceSky3D); SAMPLER(sampler_SourceSky3D);
            float _SourceSky3DEnabled;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                // Just inside the far plane: it passes against a cleared depth buffer and loses to everything drawn
                #if UNITY_REVERSED_Z
                    output.positionCS.z = 1e-6f * output.positionCS.w;
                #else
                    output.positionCS.z = output.positionCS.w * (1.0f - 1e-6f);
                #endif
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_BRANCH
                if (_SourceSky3DEnabled > 0.5)
                    return SAMPLE_TEXTURE2D(_SourceSky3D, sampler_SourceSky3D, GetNormalizedScreenSpaceUV(input.positionCS));

                // The 2D sky along the view, as Skybox/Cubemap Skybox draws it
                float3 direction = normalize(input.positionWS - GetCameraPositionWS());
                float sinAngle, cosAngle;
                sincos(radians(_Rotation), sinAngle, cosAngle);
                direction = float3(direction.x * cosAngle - direction.z * sinAngle, direction.y, direction.x * sinAngle + direction.z * cosAngle);
                half4 color = SAMPLE_TEXTURECUBE(_Tex, sampler_Tex, direction);
                color.rgb *= _Tint.rgb * _Exposure;
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite Off
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Vert(Attributes input) : SV_POSITION
            {
                // A backdrop contributes no depth of its own, here as in the pass above
                float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);
                #if UNITY_REVERSED_Z
                    positionCS.z = 1e-6f * positionCS.w;
                #else
                    positionCS.z = positionCS.w * (1.0f - 1e-6f);
                #endif
                return positionCS;
            }

            half Frag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}

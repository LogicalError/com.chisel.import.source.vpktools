// A Source sky face: a face of the level textured with the sky (toolsskybox). The game draws nothing there, so the sky
// shows through. Here the face draws what the sky shows at that pixel - the 3D skybox's view when a SourceSkyCamera
// draws one (_SourceSky3D), the 2D sky's cubemap otherwise.
//
// It is a BACKDROP: it draws only where nothing else has, and never hides what is behind it. Its depth goes to the far
// plane, so every other surface wins - the 3D skybox miniature above all, which sits behind these faces in the level
// and used to be sliced up by them. Source has the same effect for a different reason: it draws the sky view first over
// the whole frame and then skips sky faces entirely while drawing the world, so whatever the sky left behind stays. The
// properties are the 2D sky's (Skybox/Cubemap Skybox).
//
// EXCEPT WHEN THE SKY VIEW ITSELF IS DRAWING (_SourceSkyIsSkyView), where a sky face is an ordinary opaque surface that
// writes its own depth. A 3D skybox is built in a room sealed off by sky faces, and Source's sky view never sees past
// them because it submits that room's area and no other (CSkyboxView sets the area bits to the skybox's alone,
// viewrender.cpp:4742-4745, from engine->GetArea of sky_camera's origin, SkyCamera.cpp:106). We cannot reproduce that
// without vbsp's areas, but we do not have to: the room is sealed, so every ray out of the sky camera meets one of its
// walls, and a wall that writes depth hides what is beyond it exactly as the area restriction would. Making these faces
// a backdrop for every view is what let the level show up in the vista.
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
            // On, so the sky view's walls can seal. In every other view the depth written is the far plane, which is
            // behind everything and hides nothing - the backdrop behaves as it did when this pass wrote no depth.
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            TEXTURECUBE(_Tex); SAMPLER(sampler_Tex);
            TEXTURE2D(_SourceSky3D); SAMPLER(sampler_SourceSky3D);
            float _SourceSky3DEnabled;
            float _SourceSkyIsSkyView;

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
                // The sky view keeps the face where it is, so the sealed room's own walls hide the level beyond them.
                // Every other view pushes it just inside the far plane, where it passes against a cleared depth buffer
                // and loses to everything drawn.
                if (_SourceSkyIsSkyView < 0.5)
                {
                    #if UNITY_REVERSED_Z
                        output.positionCS.z = 1e-6f * output.positionCS.w;
                    #else
                        output.positionCS.z = output.positionCS.w * (1.0f - 1e-6f);
                    #endif
                }
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

            float _SourceSkyIsSkyView;

            float4 Vert(Attributes input) : SV_POSITION
            {
                // ZWrite is off here, so this pass never seals anything by itself; the depth still follows the pass
                // above so that anything reading the camera's depth sees a sky face where that pass put it.
                float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);
                if (_SourceSkyIsSkyView < 0.5)
                {
                    #if UNITY_REVERSED_Z
                        positionCS.z = 1e-6f * positionCS.w;
                    #else
                        positionCS.z = positionCS.w * (1.0f - 1e-6f);
                    #endif
                }
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

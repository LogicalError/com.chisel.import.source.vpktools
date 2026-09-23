// A Source sky face: a face of the level textured with the sky (toolsskybox). The game draws nothing there, so the sky
// shows through. Here the face draws what the sky shows at that pixel - the 3D skybox's view when a SourceSkyCamera
// drew one (_SourceSky3D), the 2D sky's cubemap otherwise. The properties are the 2D sky's (Skybox/Cubemap Skybox).
//
// IT IS AN ORDINARY OPAQUE SURFACE. It writes its depth and hides what is behind it, in every view. That is not a
// detail: a Source map is sealed, and a sky face is one of the solids that seals it, so everything beyond one is
// something the player is never meant to see. Source keeps it out by visibility instead - it draws the sky view over
// the whole frame first, then skips sky faces while drawing the world, and its areas and PVS keep the rest of the map
// out of the sky view (CSkyboxView submits the sky camera's area alone, viewrender.cpp, from engine->GetArea of
// sky_camera's origin, SkyCamera.cpp). We have no vbsp areas, but we do not need them: the rooms are sealed, so a wall
// that writes depth hides what is beyond it exactly as the area restriction would.
//
// It was a backdrop once - depth at the far plane, hiding nothing - so that the 3D skybox miniature, which is real
// geometry in the level, would not be sliced up by the sky faces standing in front of it. That was the wrong fix for
// it. Sealing removes the slicing too, because a sealed play area never sees the miniature directly at all; it sees it
// through these faces, which is what the game shows. And a backdrop breaks the miniature from the inside: a map's
// 3D skybox is a box of toolsskybox brushes around its sky_camera, and the backdrops it paints inside it are
// vista/surfacetension_skyline_0*, UnlitGeneric with "$translucent" "1" - which can never occlude anything. A
// translucent card in front of a wall that also refuses to occlude shows the level two kilometres away through both.
//
// ALPHA 0 MARKS THE PIXEL AS SKY, for Chisel/Source/Fog, which never fogs the sky - Source's world fog is for the
// world, and the 3D skybox carries its own (CSkyboxView::Enable3dSkyboxFog). The fog cannot read that off the depth
// any more: a sky face now writes the depth of a wall a few metres away, and fogging it by that distance would paint
// the level's haze over the vista. The marker has to be written here, where the surface knows what it is.
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
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        ENDHLSL

        Pass
        {
            Name "SkyFace"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
			// A BatchRendererGroup - the GPU Resident Drawer, or Entities Graphics - refuses a pass with no
			// DOTS_INSTANCING_ON variant, at draw submission rather than at compile, so the symptom is silence.
			#pragma multi_compile _ DOTS_INSTANCING_ON

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
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_BRANCH
                if (_SourceSky3DEnabled > 0.5)
                    return half4(SAMPLE_TEXTURE2D(_SourceSky3D, sampler_SourceSky3D, GetNormalizedScreenSpaceUV(input.positionCS)).rgb, 0);

                // The 2D sky along the view, as Skybox/Cubemap Skybox draws it
                float3 direction = normalize(input.positionWS - GetCameraPositionWS());
                float sinAngle, cosAngle;
                sincos(radians(_Rotation), sinAngle, cosAngle);
                direction = float3(direction.x * cosAngle - direction.z * sinAngle, direction.y, direction.x * sinAngle + direction.z * cosAngle);
                half4 color = SAMPLE_TEXTURECUBE(_Tex, sampler_Tex, direction);
                color.rgb *= _Tint.rgb * _Exposure;
                return half4(color.rgb, 0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
			// A BatchRendererGroup - the GPU Resident Drawer, or Entities Graphics - refuses a pass with no
			// DOTS_INSTANCING_ON variant, at draw submission rather than at compile, so the symptom is silence.
			#pragma multi_compile _ DOTS_INSTANCING_ON

            float4 Vert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformObjectToHClip(input.positionOS.xyz);
            }

            half Frag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // Without this pass a sky face is missing from the camera's depth texture whenever URP runs a depth-NORMALS
        // prepass rather than a plain depth one, which the Decal renderer feature is enough to bring about. A sky face
        // then seals the frame while writing nothing any depth reader can see, and everything drawn over it - a
        // translucent backdrop card in the 3D skybox, say - is measured against whatever lies beyond the face instead
        // of against the face. Measured: 2.27% of the sky view came back as far-plane depth in a room that is sealed.
        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
			// A BatchRendererGroup - the GPU Resident Drawer, or Entities Graphics - refuses a pass with no
			// DOTS_INSTANCING_ON variant, at draw submission rather than at compile, so the symptom is silence.
			#pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct NormalAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct NormalVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
            };

            NormalVaryings Vert(NormalAttributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                NormalVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            // Encoded the way URP's own depth-normals passes encode it, as ChiselSourceUnlitPasses.hlsl does
            half4 Frag(NormalVaryings input) : SV_Target
            {
            #if defined(_GBUFFER_NORMALS_OCT)
                float3 normalWS = normalize(input.normalWS);
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                return half4(half3(PackFloat2To888(remappedOctNormalWS)), 0.0);
            #else
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}

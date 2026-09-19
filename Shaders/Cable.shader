Shader "Chisel/Source/Cable"
{
    Properties
    {
        [MainTexture] _BaseMap ("$basetexture", 2D) = "white" {}
        [Normal] _BumpMap ("$bumpmap", 2D) = "bump" {}
        _Cutoff ("$alphatestreference", Range(0,1)) = 0.5

        [HideInInspector] _SrcBlend ("__src", Float) = 1.0
        [HideInInspector] _DstBlend ("__dst", Float) = 0.0
        [HideInInspector] _ZWrite ("__zw", Float) = 1.0

        [Enum(UnityEngine.Rendering.CullMode)]
        _Cull ("$nocull", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        LOD 300

        Cull [_Cull]
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]


        // ================================================================
        // FORWARD
        // ================================================================

        Pass
        {
            Name "ForwardLit"

            Tags
            {
                "LightMode" = "UniversalForward"
            }

            HLSLPROGRAM

            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local_fragment _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half _Cutoff;
            CBUFFER_END


            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };


            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                half3 normalWS    : TEXCOORD2;
            };


            Varyings vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;

                output.uv =
                    TRANSFORM_TEX(input.uv, _BaseMap);

                output.normalWS =
                    TransformObjectToWorldNormal(input.normalOS);

                return output;
            }


            half4 frag(Varyings input) : SV_Target
            {
                // LineRenderer UV:
                // U = along rope
                // V = across rope
                //
                // Source cable:
                // V = along rope
                // U = across rope

                float2 coord = input.uv.yx;


                half4 baseColor =
                    SAMPLE_TEXTURE2D(
                        _BaseMap,
                        sampler_BaseMap,
                        coord
                    );


                #ifdef _ALPHATEST_ON
                    clip(baseColor.a - _Cutoff);
                #endif


                half3 cableNormal =
                    UnpackNormal(
                        SAMPLE_TEXTURE2D(
                            _BumpMap,
                            sampler_BumpMap,
                            coord
                        )
                    );


                // Source cable half-Lambert:
                //
                // (0.5 * N.z + 0.5)^2

                half facing =
                    cableNormal.z * 0.5h + 0.5h;

                facing *= facing;


                float4 shadowCoord =
                    TransformWorldToShadowCoord(input.positionWS);

                Light mainLight =
                    GetMainLight(shadowCoord);


                half3 ambient =
                    SampleSH(
                        normalize(input.normalWS)
                    );


                half3 direct =
                    mainLight.color *
                    mainLight.distanceAttenuation *
                    mainLight.shadowAttenuation;


                half3 lighting =
                    ambient + direct;


                return half4(
                    baseColor.rgb * facing * lighting,
                    baseColor.a
                );
            }

            ENDHLSL
        }


        // ================================================================
        // DEFERRED
        // ================================================================

        Pass
        {
            Name "GBuffer"

            Tags
            {
                "LightMode" = "UniversalGBuffer"
            }

            HLSLPROGRAM

            #pragma target 3.5

            #pragma vertex vertDeferred
            #pragma fragment fragDeferred

            #pragma shader_feature_local_fragment _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half _Cutoff;
            CBUFFER_END


            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };


            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                half3 normalWS    : TEXCOORD2;
            };


            Varyings vertDeferred(Attributes input)
            {
                Varyings output;

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;

                output.uv =
                    TRANSFORM_TEX(input.uv, _BaseMap);

                output.normalWS =
                    TransformObjectToWorldNormal(input.normalOS);

                return output;
            }


            FragmentOutput fragDeferred(Varyings input)
            {
                float2 coord = input.uv.yx;


                half4 baseColor =
                    SAMPLE_TEXTURE2D(
                        _BaseMap,
                        sampler_BaseMap,
                        coord
                    );


                #ifdef _ALPHATEST_ON
                    clip(baseColor.a - _Cutoff);
                #endif


                half3 normalTS =
                    UnpackNormal(
                        SAMPLE_TEXTURE2D(
                            _BumpMap,
                            sampler_BumpMap,
                            coord
                        )
                    );


                // We don't have a proper tangent frame for the
                // LineRenderer's generated mesh, so use the generated
                // mesh normal for the GBuffer normal.

                half3 normalWS =
                    normalize(input.normalWS);


                // Source's cable normal map is a special lighting
                // mask rather than a conventional surface normal.
                //
                // Preserve it as an intensity modifier.

                half facing =
                    normalTS.z * 0.5h + 0.5h;

                facing *= facing;


                baseColor.rgb *= facing;


                // --------------------------------------------------------
                // URP Deferred GBuffer
                // --------------------------------------------------------

                FragmentOutput output;

                output.GBuffer0 =
                    half4(baseColor.rgb, 1.0h);

                output.GBuffer1 =
                    half4(0.5h, 0.5h, 0.0h, 1.0h);

                output.GBuffer2 =
                    half4(
                        normalWS * 0.5h + 0.5h,
                        1.0h
                    );

                output.GBuffer3 =
                    half4(0, 0, 0, 1);

                return output;
            }

            ENDHLSL
        }


        // ================================================================
        // SHADOW CASTER
        // ================================================================

        Pass
        {
            Name "ShadowCaster"

            Tags
            {
                "LightMode" = "ShadowCaster"
            }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM

            #pragma target 3.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #pragma shader_feature_local_fragment _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"

            ENDHLSL
        }


        // ================================================================
        // DEPTH ONLY
        // ================================================================

        Pass
        {
            Name "DepthOnly"

            Tags
            {
                "LightMode" = "DepthOnly"
            }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM

            #pragma target 3.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #pragma shader_feature_local_fragment _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"

            ENDHLSL
        }
		
		Pass
		{
			Name "ShadowCaster"
			Tags { "LightMode" = "ShadowCaster" }

			Cull [_Cull]
			ZWrite On
			ZTest LEqual
			ColorMask 0

			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex SourceShadowVert
			#pragma fragment SourceShadowFrag

			#pragma shader_feature_local _ALPHATEST_ON
			#pragma shader_feature_local _SELFILLUM

			#pragma multi_compile_instancing
			#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

			#include "ChiselSourceGenericURP.hlsl"

			ENDHLSL
		}
    }

    FallBack Off
}

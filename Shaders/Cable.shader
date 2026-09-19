// Unity equivalent of Source's "Cable" shader, which the game draws its ropes with (move_rope and keyframe_rope, see
// VmfRope in the map importer). Source colours a rope with its base texture, lit by the light the client found at the
// rope's nodes, and shapes that across the rope's width with the normal map: (0.5 * N.z + 0.5)^2, a half-Lambert
// against a light straight out of the strip (cable_ps2x.fxc). The shader reads nothing else - $color, $minlight and
// $maxlight do nothing there, and so nothing here.
//
// The importer draws a rope with a LineRenderer, which lays a texture out along the line in U and across it in V.
// Source's ropes run along V and across U, so the two are swapped when sampling.
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
		[Enum(UnityEngine.Rendering.CullMode)] _Cull ("$nocull", Float) = 0
	}

	// Forward only, under Deferred+ too: C_RopeKeyframe::CalcLightValues takes the light at each node from whichever side
	// it is brightest, and the shader only shapes it across the width, so a light's direction plays no part. A G-buffer can
	// only be lit through a normal. Ropes cast no shadows, as they cast none from the built-in shader either.
	SubShader
	{
		Tags
		{
			"RenderType" = "Opaque"
			"Queue" = "Geometry"
			"RenderPipeline" = "UniversalPipeline"
			"IgnoreProjector" = "True"
		}
		LOD 300

		HLSLINCLUDE
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

		CBUFFER_START(UnityPerMaterial)
			float4 _BaseMap_ST;
			half   _Cutoff;
		CBUFFER_END

		TEXTURE2D(_BaseMap);    SAMPLER(sampler_BaseMap);
		// Read at the base texture's coordinates, with its sampler
		TEXTURE2D(_BumpMap);

		// Across the rope in U and along it in V, as Source lays the texture out
		float2 SourceCableCoord(float2 texcoord)
		{
			return TRANSFORM_TEX(texcoord, _BaseMap).yx;
		}

		half4 SourceCableBase(float2 coord)
		{
			half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, coord);
		#ifdef _ALPHATEST_ON
			clip(base.a - _Cutoff);
		#endif
			return base;
		}
		ENDHLSL

		Pass
		{
			Name "ForwardLit"
			Tags { "LightMode" = "UniversalForwardOnly" }

			Blend [_SrcBlend] [_DstBlend]
			ZWrite [_ZWrite]
			Cull [_Cull]

			HLSLPROGRAM
			#pragma target 2.0
			#pragma vertex SourceCableVertex
			#pragma fragment SourceCableFragment

			#pragma shader_feature_local_fragment _ALPHATEST_ON

			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			// Per pixel only: URP's per-vertex lights come already weighted by N.L
			#pragma multi_compile _ _ADDITIONAL_LIGHTS
			#pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
			#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
			#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
			#pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
			#pragma multi_compile_fragment _ _LIGHT_COOKIES
			#pragma multi_compile _ _LIGHT_LAYERS
			#pragma multi_compile _ _CLUSTER_LIGHT_LOOP
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

			// Baked shadows of mixed lights, which a dynamic surface takes from the light probes
			#pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
			#pragma multi_compile _ SHADOWS_SHADOWMASK
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

			#pragma multi_compile_instancing
			#pragma instancing_options renderinglayer
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			struct SourceCableAttributes
			{
				float4 positionOS : POSITION;
				float3 normalOS   : NORMAL;
				float2 texcoord   : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct SourceCableVaryings
			{
				float2 coord          : TEXCOORD0;
				float3 positionWS     : TEXCOORD1;
				half3  normalWS       : TEXCOORD2;
				half3  vertexSH       : TEXCOORD3;
				half   fogFactor      : TEXCOORD4;
			#ifdef USE_APV_PROBE_OCCLUSION
				float4 probeOcclusion : TEXCOORD5;
			#endif
				float4 positionCS     : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			SourceCableVaryings SourceCableVertex(SourceCableAttributes input)
			{
				SourceCableVaryings output = (SourceCableVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
				output.positionCS = vertexInput.positionCS;
				output.positionWS = vertexInput.positionWS;
				output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
				output.coord      = SourceCableCoord(input.texcoord);
			#if !defined(_FOG_FRAGMENT)
				output.fogFactor  = ComputeFogFactor(vertexInput.positionCS.z);
			#endif
				OUTPUT_SH4(vertexInput.positionWS, output.normalWS, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, output.probeOcclusion);
				return output;
			}

			// A light's colour where the rope is, without its direction
			half3 SourceCableLight(Light light)
			{
				return light.color * (light.distanceAttenuation * light.shadowAttenuation);
			}

			half4 SourceCableFragment(SourceCableVaryings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				half4 base = SourceCableBase(input.coord);
				half facing = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, input.coord)).z * 0.5h + 0.5h;

				InputData inputData = (InputData)0;
				inputData.positionWS              = input.positionWS;
				inputData.positionCS              = input.positionCS;
				inputData.normalWS                = NormalizeNormalPerPixel(input.normalWS);
				inputData.viewDirectionWS         = GetWorldSpaceNormalizeViewDir(input.positionWS);
				inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
				inputData.shadowCoord             = TransformWorldToShadowCoord(input.positionWS);
				inputData.fogCoord                = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
			#if !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
				inputData.bakedGI = SAMPLE_GI(input.vertexSH, GetAbsolutePositionWS(inputData.positionWS), inputData.normalWS,
											  inputData.viewDirectionWS, input.positionCS.xy, input.probeOcclusion, inputData.shadowMask);
			#else
				inputData.bakedGI    = SampleSHPixel(input.vertexSH, inputData.normalWS);
				inputData.shadowMask = unity_ProbesOcclusion;
			#endif

				half4 shadowMask = CalculateShadowMask(inputData);
				AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData.normalizedScreenSpaceUV, 1.0h);
				uint meshRenderingLayers = GetMeshRenderingLayer();

				// The ambient light and light probes, and every light's attenuated colour
				half3 lighting = inputData.bakedGI * aoFactor.indirectAmbientOcclusion;
				Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);
			#ifdef _LIGHT_LAYERS
				if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
			#endif
					lighting += SourceCableLight(mainLight);

			#if defined(_ADDITIONAL_LIGHTS)
				uint pixelLightCount = GetAdditionalLightsCount();

				#if USE_CLUSTER_LIGHT_LOOP
				[loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
				{
					CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK

					Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
				#ifdef _LIGHT_LAYERS
					if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
				#endif
						lighting += SourceCableLight(light);
				}
				#endif

				LIGHT_LOOP_BEGIN(pixelLightCount)
					Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
				#ifdef _LIGHT_LAYERS
					if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
				#endif
						lighting += SourceCableLight(light);
				LIGHT_LOOP_END
			#endif

				half3 color = base.rgb * (facing * facing) * lighting;
				return half4(MixFog(color, inputData.fogCoord), base.a);
			}
			ENDHLSL
		}

		Pass
		{
			Name "DepthOnly"
			Tags { "LightMode" = "DepthOnly" }

			ZWrite On
			ColorMask R
			Cull [_Cull]

			HLSLPROGRAM
			#pragma target 2.0
			#pragma vertex SourceCableDepthVertex
			#pragma fragment SourceCableDepthFragment

			#pragma shader_feature_local_fragment _ALPHATEST_ON

			#pragma multi_compile_instancing
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

			struct SourceCableDepthAttributes
			{
				float4 positionOS : POSITION;
				float2 texcoord   : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct SourceCableDepthVaryings
			{
				float2 coord      : TEXCOORD0;
				float4 positionCS : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			SourceCableDepthVaryings SourceCableDepthVertex(SourceCableDepthAttributes input)
			{
				SourceCableDepthVaryings output = (SourceCableDepthVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				output.coord      = SourceCableCoord(input.texcoord);
				return output;
			}

			half SourceCableDepthFragment(SourceCableDepthVaryings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				SourceCableBase(input.coord);
				return input.positionCS.z;
			}
			ENDHLSL
		}

		// The normals of a forward-only surface, which Deferred+ writes before its screen-space ambient occlusion
		Pass
		{
			Name "DepthNormalsOnly"
			Tags { "LightMode" = "DepthNormalsOnly" }

			ZWrite On
			Cull [_Cull]

			HLSLPROGRAM
			#pragma target 2.0
			#pragma vertex SourceCableNormalsVertex
			#pragma fragment SourceCableNormalsFragment

			#pragma shader_feature_local_fragment _ALPHATEST_ON

			#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

			#pragma multi_compile_instancing
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			struct SourceCableNormalsAttributes
			{
				float4 positionOS : POSITION;
				float3 normalOS   : NORMAL;
				float2 texcoord   : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct SourceCableNormalsVaryings
			{
				float2 coord      : TEXCOORD0;
				half3  normalWS   : TEXCOORD1;
				float4 positionCS : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			SourceCableNormalsVaryings SourceCableNormalsVertex(SourceCableNormalsAttributes input)
			{
				SourceCableNormalsVaryings output = (SourceCableNormalsVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
				output.coord      = SourceCableCoord(input.texcoord);
				return output;
			}

			void SourceCableNormalsFragment(
				SourceCableNormalsVaryings input
				, out half4 outNormalWS : SV_Target0
			#ifdef _WRITE_RENDERING_LAYERS
				, out uint outRenderingLayers : SV_Target1
			#endif
			)
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				SourceCableBase(input.coord);

				float3 normalWS = normalize(input.normalWS);
			#if defined(_GBUFFER_NORMALS_OCT)
				float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
				outNormalWS = half4(PackFloat2To888(saturate(octNormalWS * 0.5 + 0.5)), 0.0);
			#else
				outNormalWS = half4(normalWS, 0.0);
			#endif

			#ifdef _WRITE_RENDERING_LAYERS
				outRenderingLayers = EncodeMeshRenderingLayer();
			#endif
			}
			ENDHLSL
		}
	}

	FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

// Unity equivalent of Source's "UnlitGeneric" shader: no lighting at all, the base texture is the
// final colour. Source uses it for screens, sprites, light glows, HUD-like decals and anything that
// is meant to be fullbright, so routing these through a lit shader is what makes them look dull.
Shader "Chisel/Source/UnlitGeneric"
{
	Properties
	{
		[MainColor] _Color ("$color", Color) = (1,1,1,1)
		[MainTexture] _BaseMap ("$basetexture", 2D) = "white" {}
		_Cutoff ("$alphatestreference", Range(0,1)) = 0.5
		[Toggle] _VertexColor ("$vertexcolor", Float) = 0
		[Toggle] _VertexAlpha ("$vertexalpha", Float) = 0

		_EnvMapMask ("$envmapmask", 2D) = "white" {}
		[NoScaleOffset] _EnvMap ("$envmap (a named cubemap)", Cube) = "black" {}
		[Toggle] _EnvMapFromProbes ("$envmap is env_cubemap: the nearest probe", Float) = 1
		_EnvMapTint ("$envmaptint", Color) = (1,1,1,1)
		_EnvMapContrast ("$envmapcontrast", Vector) = (0,0,0,0)
		_EnvMapSaturation ("$envmapsaturation", Vector) = (1,1,1,0)
		_EnvMapMaskWeights ("Envmap mask: -, base alpha, 1 - base alpha, -", Vector) = (0,0,0,0)
		[Toggle] _EnvMapMaskTexture ("Envmap masked by $envmapmask", Float) = 0

		[HideInInspector] _SrcBlend ("__src", Float) = 1.0
		[HideInInspector] _DstBlend ("__dst", Float) = 0.0
		[HideInInspector] _ZWrite ("__zw", Float) = 1.0
		[Enum(UnityEngine.Rendering.CullMode)] _Cull ("$nocull", Float) = 2
	}

	SubShader
	{
		// The Unlit material type keeps the deferred lights off the surface: the G-buffer pass only lays down its depth
		// and normals, and the colour pass draws it after the lights.
		Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Unlit" }
		LOD 100

		Cull [_Cull]
		Blend [_SrcBlend] [_DstBlend]
		ZWrite [_ZWrite]

		HLSLINCLUDE
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

		// Every pass has the same buffer, which the SRP batcher requires
		CBUFFER_START(UnityPerMaterial)
			float4 _BaseMap_ST;
			half4  _Color;
			half   _Cutoff;
			half   _VertexColor;
			half   _VertexAlpha;
			half   _EnvMapFromProbes;
			half4  _EnvMapTint;
			half4  _EnvMapContrast;
			half4  _EnvMapSaturation;
			half4  _EnvMapMaskWeights;
			half   _EnvMapMaskTexture;
			half   _DstBlend;
		CBUFFER_END

		TEXTURE2D(_BaseMap);
		SAMPLER(sampler_BaseMap);

		// $vertexalpha multiplies the alpha that $alphatest tests
		half SourceUnlitAlpha(float2 uv, half4 vertexColor)
		{
			return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, TRANSFORM_TEX(uv, _BaseMap)).a * _Color.a *
				   lerp(1.0h, vertexColor.a, _VertexAlpha);
		}
		ENDHLSL

		Pass
		{
			Name "Unlit"
			// Forward even under Deferred+, which draws an opaque pass only when it is tagged G-buffer or forward-only
			Tags { "LightMode" = "UniversalForwardOnly" }

			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma shader_feature_local _ALPHATEST_ON
			#pragma shader_feature_local _ENVMAP
			#pragma multi_compile_instancing
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			// The reflection probes come as a cubemap array (ChiselSourceProbes.hlsl)
			#pragma require cubearray

		#ifdef _ENVMAP
			#include "ChiselSourceProbes.hlsl"

			TEXTURE2D(_EnvMapMask);
			SAMPLER(sampler_EnvMapMask);
			TEXTURECUBE(_EnvMap);
			SAMPLER(sampler_EnvMap);
		#endif

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS   : NORMAL;
				float2 uv         : TEXCOORD0;
				half4  color      : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv         : TEXCOORD0;
				half4  color      : COLOR;
				float3 positionWS : TEXCOORD1;
				half   fogFactor  : TEXCOORD2;
			#ifdef _ENVMAP
				float3 normalWS   : TEXCOORD3;
			#endif
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
				output.positionCS = position.positionCS;
				output.positionWS = position.positionWS;
				output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
				output.color = input.color;
				output.fogFactor = ComputeFogFactor(position.positionCS.z);
			#ifdef _ENVMAP
				output.normalWS = TransformObjectToWorldNormal(input.normalOS);
			#endif
				return output;
			}

			void frag(Varyings input
				, out half4 outColor : SV_Target0
			#ifdef _WRITE_RENDERING_LAYERS
				, out uint outRenderingLayers : SV_Target1
			#endif
				)
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
				half4 col = base * _Color;
				// $vertexcolor: UnlitGeneric takes the vertex colour out of gamma space first
				// (vertexlit_and_unlit_generic_vs20.fxc: GammaToLinear, which is pow(x, 2.2)).
				col.rgb *= lerp(half3(1, 1, 1), pow(abs(input.color.rgb), 2.2h), _VertexColor);
				col.a   *= lerp(1.0h, input.color.a, _VertexAlpha);
			#ifdef _ALPHATEST_ON
				clip(col.a - _Cutoff);
			#endif
			#ifdef _ENVMAP
				// Source adds the reflection to the unlit colour (vertexlit_and_unlit_generic_ps2x.fxc): masked by
				// $envmapmask or one minus the base alpha, tinted, squared by $envmapcontrast and desaturated by
				// $envmapsaturation, without a Fresnel term.
				float3 viewWorld = GetWorldSpaceNormalizeViewDir(input.positionWS);
				float3 reflection = reflect(-viewWorld, normalize(input.normalWS));
				half3 environment = (_EnvMapFromProbes > 0.5h) ? SourceProbeReflection(input.positionWS, reflection)
															   : SAMPLE_TEXTURECUBE(_EnvMap, sampler_EnvMap, reflection).rgb;
				half envMask = (1.0h - dot(_EnvMapMaskWeights, half4(1, 1, 1, 1))) + _EnvMapMaskWeights.y * base.a
							 + _EnvMapMaskWeights.z * (1.0h - base.a);
				half3 reflected = environment * envMask *
								  lerp(half3(1, 1, 1), SAMPLE_TEXTURE2D(_EnvMapMask, sampler_EnvMapMask, input.uv).rgb, _EnvMapMaskTexture);
				reflected *= _EnvMapTint.rgb;
				reflected = lerp(reflected, reflected * reflected, _EnvMapContrast.rgb);
				reflected = lerp(dot(reflected, half3(0.299h, 0.587h, 0.114h)).xxx, reflected, _EnvMapSaturation.rgb);
				col.rgb += reflected;
			#endif
				// Source fogs an additive surface toward black (CBaseShader::DefaultFog): the fog colour added on top
				// would only brighten it. Additive is a destination factor of BlendMode.One.
				bool additive = abs(_DstBlend - 1.0h) < 0.5h;
				col.rgb = MixFogColor(col.rgb, additive ? half3(0, 0, 0) : unity_FogColor.rgb,
									  InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor));
				outColor = col;

			#ifdef _WRITE_RENDERING_LAYERS
				outRenderingLayers = EncodeMeshRenderingLayer();
			#endif
			}
			ENDHLSL
		}

		Pass
		{
			Name "GBuffer"
			Tags { "LightMode" = "UniversalGBuffer" }

			HLSLPROGRAM
			#pragma target 4.5
			// URP's deferred path has no OpenGL support
			#pragma exclude_renderers gles3 glcore
			#pragma vertex SourceUnlitVertex
			#pragma fragment SourceUnlitGBufferFragment
			#pragma shader_feature_local _ALPHATEST_ON
			#pragma multi_compile_fragment _ _RENDER_PASS_ENABLED
			#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
			#pragma multi_compile_fragment _ SHADOWS_SHADOWMASK
			#pragma multi_compile_instancing
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

			#define SOURCE_UNLIT_GBUFFER_PASS
			#include "ChiselSourceUnlitPasses.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferOutputFormat.hlsl"
			ENDHLSL
		}

		Pass
		{
			Name "DepthOnly"
			Tags { "LightMode" = "DepthOnly" }

			ZWrite On
			ColorMask R

			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex SourceUnlitVertex
			#pragma fragment SourceUnlitDepthOnlyFragment
			#pragma shader_feature_local _ALPHATEST_ON
			#pragma multi_compile_instancing

			#define SOURCE_UNLIT_DEPTH_ONLY_PASS
			#include "ChiselSourceUnlitPasses.hlsl"
			ENDHLSL
		}

		Pass
		{
			Name "DepthNormalsOnly"
			Tags { "LightMode" = "DepthNormalsOnly" }

			ZWrite On

			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex SourceUnlitVertex
			#pragma fragment SourceUnlitDepthNormalsFragment
			#pragma shader_feature_local _ALPHATEST_ON
			#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
			#pragma multi_compile_instancing
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

			#define SOURCE_UNLIT_DEPTH_NORMALS_PASS
			#include "ChiselSourceUnlitPasses.hlsl"
			ENDHLSL
		}

		Pass
		{
			Name "ShadowCaster"
			Tags { "LightMode" = "ShadowCaster" }

			// ZWrite stays [_ZWrite]: a translucent or additive surface writes no depth, so it casts no shadow.
			ZTest LEqual
			ColorMask 0

			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex SourceUnlitVertex
			#pragma fragment SourceUnlitShadowFragment
			#pragma shader_feature_local _ALPHATEST_ON
			#pragma multi_compile_instancing
			#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

			#define SOURCE_UNLIT_SHADOW_CASTER_PASS
			#include "ChiselSourceUnlitPasses.hlsl"
			ENDHLSL
		}
	}

	FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

// Unity equivalent of Source's "UnlitTwoTexture": unlit, the base texture multiplied by a second
// texture that carries a transform of its own. Source uses it for screens, fake light shafts and energy
// effects, and the second texture is usually the moving part - 88 of the game's 117 UnlitTwoTexture
// materials scroll it with a TextureScroll proxy.
Shader "Chisel/Source/UnlitTwoTexture"
{
	Properties
	{
		[MainColor] _Color ("$color", Color) = (1,1,1,1)
		[MainTexture] _BaseMap ("$basetexture", 2D) = "white" {}
		_Texture2 ("$texture2", 2D) = "white" {}
		// TextureScroll proxies, as a uv offset per second. Set by the importer from the VMT.
		_BaseTextureScroll ("$basetexturetransform scroll", Vector) = (0,0,0,0)
		_Texture2Scroll ("$texture2transform scroll", Vector) = (0,0,0,0)
		_Cutoff ("$alphatestreference", Range(0,1)) = 0.5

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
			float4 _Texture2_ST;
			float4 _BaseTextureScroll;
			float4 _Texture2Scroll;
			half4  _Color;
			half   _Cutoff;
			half   _DstBlend;
		CBUFFER_END

		TEXTURE2D(_BaseMap);
		SAMPLER(sampler_BaseMap);
		TEXTURE2D(_Texture2);
		SAMPLER(sampler_Texture2);

		// frac() keeps the offset in [0,1) however long the level has been running, which is
		// also what the proxy does - an ever-growing offset loses precision.
		float2 BaseTextureUV(float2 uv) { return TRANSFORM_TEX(uv, _BaseMap) + frac(_BaseTextureScroll.xy * _Time.y); }
		float2 Texture2UV(float2 uv)    { return TRANSFORM_TEX(uv, _Texture2) + frac(_Texture2Scroll.xy * _Time.y); }

		half SourceUnlitAlpha(float2 uv, half4 vertexColor)
		{
			return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, BaseTextureUV(uv)).a *
				   SAMPLE_TEXTURE2D(_Texture2, sampler_Texture2, Texture2UV(uv)).a * _Color.a;
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
			#pragma multi_compile_instancing
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv         : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv         : TEXCOORD0;
				float2 uv2        : TEXCOORD1;
				float3 positionWS : TEXCOORD2;
				half   fogFactor  : TEXCOORD3;
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
				output.uv = BaseTextureUV(input.uv);
				output.uv2 = Texture2UV(input.uv);
				output.fogFactor = ComputeFogFactor(position.positionCS.z);
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

				half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) *
							SAMPLE_TEXTURE2D(_Texture2, sampler_Texture2, input.uv2) * _Color;
			#ifdef _ALPHATEST_ON
				clip(col.a - _Cutoff);
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

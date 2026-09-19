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
		Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
		LOD 100

		Cull [_Cull]
		Blend [_SrcBlend] [_DstBlend]
		ZWrite [_ZWrite]

		Pass
		{
			Tags { "LightMode" = "UniversalForward" }
			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex SourceUnlitVert
			#pragma fragment SourceUnlitFrag
			#pragma multi_compile_fog
			#pragma shader_feature_local _ALPHATEST_ON
			#define CHISEL_TWO_TEXTURE
			#include "ChiselSourceUnlitURP.hlsl"
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

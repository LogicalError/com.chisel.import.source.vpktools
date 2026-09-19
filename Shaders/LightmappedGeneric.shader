// Unity equivalent of Source's "LightmappedGeneric" shader, used by world brush materials.
// MaterialImporter looks this shader up by the VMT's shader name (see SourceShaderLookup), so the
// name below must stay "Chisel/Source/<vmt shader name>".
Shader "Chisel/Source/LightmappedGeneric"
{
	Properties
	{
		[MainColor] _Color ("$color", Color) = (1,1,1,1)
		[MainTexture] _BaseMap ("$basetexture", 2D) = "white" {}
		_Cutoff ("$alphatestreference", Range(0,1)) = 0.5

		_Smoothness ("Smoothness", Range(0,1)) = 0.0
		_SpecColorTint ("$phongtint", Color) = (0,0,0,1)
		_SpecularScale ("$phongexponent (scaled)", Range(0,1)) = 0.0

		[Normal] _BumpMap ("$bumpmap", 2D) = "bump" {}
		_BumpScale ("Bump scale", Float) = 1.0

		_BaseTexture2 ("$basetexture2", 2D) = "white" {}
		[Normal] _BumpMap2 ("$bumpmap2", 2D) = "bump" {}
		_BlendModulateTexture ("$blendmodulatetexture", 2D) = "white" {}
		[Toggle] _BlendBase2 ("Blend $basetexture2", Float) = 0
		[Toggle] _BlendBump2 ("Blend $bumpmap2", Float) = 0
		[Toggle] _BlendModulate ("Modulate the blend", Float) = 0
		[Toggle] _MaskedBlending ("$maskedblending", Float) = 0
		_BlendMaskTransformU ("$blendmasktransform u row", Vector) = (1,0,0,0)
		_BlendMaskTransformV ("$blendmasktransform v row", Vector) = (0,1,0,0)
		[Toggle] _VertexColor ("$vertexcolor", Float) = 0
		[Toggle] _VertexAlpha ("Vertex alpha is opacity", Float) = 0

		_DetailTex ("$detail", 2D) = "grey" {}
		_DetailBlendFactor ("$detailblendfactor", Range(0,1)) = 1.0
		_DetailBlendMode ("$detailblendmode", Float) = 0

		_EnvMapMask ("$envmapmask", 2D) = "white" {}
		[NoScaleOffset] _EnvMap ("$envmap (a named cubemap)", Cube) = "black" {}
		[Toggle] _EnvMapFromProbes ("$envmap is env_cubemap: the nearest probe", Float) = 1
		_EnvMapTint ("$envmaptint", Color) = (1,1,1,1)
		_EnvMapContrast ("$envmapcontrast", Vector) = (0,0,0,0)
		_EnvMapSaturation ("$envmapsaturation", Vector) = (1,1,1,0)
		_FresnelReflection ("$fresnelreflection", Float) = 1
		_EnvMapPhongFresnel ("$envmapfresnel", Float) = 0
		_EnvMapMaskWeights ("Envmap mask: normal alpha, base alpha, 1 - base alpha, phong mask", Vector) = (0,0,0,0)
		[Toggle] _EnvMapInvertMask ("$invertphongmask", Float) = 0
		[Toggle] _EnvMapMaskTexture ("Envmap masked by $envmapmask", Float) = 0

		_PhongBoost ("$phongboost", Float) = 1.0
		_PhongExponent ("$phongexponent", Float) = -1.0
		_PhongExponentFactor ("$phongexponentfactor", Float) = 0
		_PhongFresnelRanges ("$phongfresnelranges", Vector) = (1,1,1,0)
		_PhongMaskFromBaseAlpha ("Phong mask is base alpha", Float) = 0
		_PhongMask ("Phong mask (the $bumpmap's alpha)", 2D) = "white" {}
		_PhongExponentMap ("$phongexponenttexture", 2D) = "white" {}
		_PhongAlbedoTint ("$phongalbedotint", Float) = 0
		_PhongWarp ("$phongwarptexture", 2D) = "white" {}

		_SelfIllumMask ("$selfillummask", 2D) = "white" {}
		_SelfIllumTint ("$selfillumtint", Color) = (0,0,0,1)
		_SelfIllumFromBaseAlpha ("Self-illum mask is base alpha", Float) = 1

		[HideInInspector] _SrcBlend ("__src", Float) = 1.0
		[HideInInspector] _DstBlend ("__dst", Float) = 0.0
		[HideInInspector] _ZWrite ("__zw", Float) = 1.0
		[Enum(UnityEngine.Rendering.CullMode)] _Cull ("$nocull", Float) = 2
	}

	SubShader
	{
		Tags
		{
			"RenderType" = "Opaque"
			"Queue" = "Geometry"
			"RenderPipeline" = "UniversalPipeline"
		}

		LOD 300

		Pass
		{
			Name "ForwardLit"
			Tags { "LightMode" = "UniversalForward" }

			Cull [_Cull]
			Blend [_SrcBlend] [_DstBlend]
			ZWrite [_ZWrite]

			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex SourceLitVert
			#pragma fragment SourceLitFrag

			#pragma shader_feature_local _NORMALMAP
			#pragma shader_feature_local _VERTEXBLEND
			#pragma shader_feature_local _DETAIL
			#pragma shader_feature_local _ENVMAP
			#pragma shader_feature_local _PHONG
			#pragma shader_feature_local _PHONGMASK
			#pragma shader_feature_local _PHONGEXPONENTMAP
			#pragma shader_feature_local _PHONGWARP
			#pragma shader_feature_local _SELFILLUM
			#pragma shader_feature_local _ALPHATEST_ON

			#pragma multi_compile _ _ADDITIONAL_LIGHTS
			#pragma multi_compile _ _CLUSTER_LIGHT_LOOP
			#pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			#pragma multi_compile _ LIGHTMAP_ON
			#pragma multi_compile _ DYNAMICLIGHTMAP_ON
			#pragma multi_compile _ DIRLIGHTMAP_COMBINED
			#pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
			#pragma multi_compile _ SHADOWS_SHADOWMASK
			#pragma multi_compile_fog

			#include "ChiselSourceGenericURP.hlsl"
			ENDHLSL
		}

		Pass
		{
			Name "GBuffer"
			Tags
			{
				"LightMode" = "UniversalGBuffer"
				"UniversalMaterialType" = "Lit"
			}

			Cull [_Cull]
			Blend [_SrcBlend] [_DstBlend]
			ZWrite [_ZWrite]

			HLSLPROGRAM
			#pragma target 4.5
			#pragma exclude_renderers gles3 glcore
			#pragma vertex SourceLitVert
			#pragma fragment SourceLitGBufferFrag

			#pragma shader_feature_local _NORMALMAP
			#pragma shader_feature_local _VERTEXBLEND
			#pragma shader_feature_local _SELFILLUM
			#pragma shader_feature_local _ALPHATEST_ON

			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			#pragma multi_compile _ LIGHTMAP_ON
			#pragma multi_compile _ DYNAMICLIGHTMAP_ON
			#pragma multi_compile _ DIRLIGHTMAP_COMBINED
			#pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
			#pragma multi_compile _ SHADOWS_SHADOWMASK
			#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
			#pragma multi_compile_fragment _ _RENDER_PASS_ENABLED

			#define _SPECULAR_SETUP

			#include "ChiselSourceGenericURP.hlsl"
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

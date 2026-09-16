// Unity equivalent of Source's "LightmappedGeneric" shader, used by world brush materials.
// MaterialImporter looks this shader up by the VMT's shader name (see SourceShaderLookup), so the
// name below must stay "Chisel/Source/<vmt shader name>".
Shader "Chisel/Source/LightmappedGeneric"
{
	Properties
	{
		[MainColor] _Color ("$color", Color) = (1,1,1,1)
		[MainTexture] _MainTex ("$basetexture", 2D) = "white" {}
		_Cutoff ("$alphatestreference", Range(0,1)) = 0.5

		_Smoothness ("Smoothness", Range(0,1)) = 0.0
		_SpecColorTint ("$phongtint", Color) = (0,0,0,1)
		_SpecularScale ("$phongexponent (scaled)", Range(0,1)) = 0.0

		[Normal] _BumpMap ("$bumpmap", 2D) = "bump" {}
		_BumpScale ("Bump scale", Float) = 1.0

		// The second layer, blended in by vertex alpha (displacements), and which parts of it there are.
		_BaseTexture2 ("$basetexture2", 2D) = "white" {}
		[Normal] _BumpMap2 ("$bumpmap2", 2D) = "bump" {}
		_BlendModulateTexture ("$blendmodulatetexture", 2D) = "white" {}
		[Toggle] _BlendBase2 ("Blend $basetexture2", Float) = 0
		[Toggle] _BlendBump2 ("Blend $bumpmap2", Float) = 0
		[Toggle] _BlendModulate ("Modulate the blend", Float) = 0
		[Toggle] _MaskedBlending ("$maskedblending", Float) = 0
		_BlendMaskTransformU ("$blendmasktransform u row", Vector) = (1,0,0,0)
		_BlendMaskTransformV ("$blendmasktransform v row", Vector) = (0,1,0,0)
		// $vertexcolor: the vertex colour tints, and - without a second layer - its alpha is opacity.
		[Toggle] _VertexColor ("$vertexcolor", Float) = 0
		[Toggle] _VertexAlpha ("Vertex alpha is opacity", Float) = 0

		_DetailTex ("$detail", 2D) = "grey" {}
		_DetailBlendFactor ("$detailblendfactor", Range(0,1)) = 1.0
		_DetailBlendMode ("$detailblendmode", Float) = 0

		_EnvMapMask ("$envmapmask", 2D) = "white" {}

		_PhongBoost ("$phongboost", Float) = 1.0
		// Negative hands the exponent to _PhongExponentMap, which is Source's own default.
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

		// Blend state, driven from the material exactly as the Standard shader drives its own, so
		// that one shader covers opaque / alpha tested / translucent / additive.
		[HideInInspector] _SrcBlend ("__src", Float) = 1.0
		[HideInInspector] _DstBlend ("__dst", Float) = 0.0
		[HideInInspector] _ZWrite ("__zw", Float) = 1.0
		// $nocull. The Standard shader has no cull control at all, which is why Source's glass,
		// grates and foliage all came through single sided.
		[Enum(UnityEngine.Rendering.CullMode)] _Cull ("$nocull", Float) = 2
	}

	SubShader
	{
		Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
		LOD 300

		Cull [_Cull]
		Blend [_SrcBlend] [_DstBlend]
		ZWrite [_ZWrite]

		CGPROGRAM
		#pragma target 3.0

		#pragma shader_feature_local _NORMALMAP
		#pragma shader_feature_local _VERTEXBLEND
		#pragma shader_feature_local _DETAIL
		#pragma shader_feature_local _ENVMAPMASK
		#pragma shader_feature_local _PHONG
		#pragma shader_feature_local _PHONGMASK
		#pragma shader_feature_local _PHONGEXPONENTMAP
		#pragma shader_feature_local _PHONGWARP
		#pragma shader_feature_local _SELFILLUM
		#pragma shader_feature_local _ALPHATEST_ON

		#include "ChiselSourceGeneric.cginc"
		// SourceSpecular (ChiselSourceGeneric.cginc) is Unity's StandardSpecular BRDF without the reflection
		// probes and the energy conservation, neither of which Source has. It provides
		// Lighting<Name>_Deferred on purpose: a lighting model without one has no deferred pass, and Unity
		// then draws the surface in FORWARD even under a deferred camera. There a map-spanning Chisel
		// renderer is lit per pixel by QualitySettings.pixelLightCount lights, picked among the ones the
		// camera can see, and every other light is folded into ambient without any falloff - the level's
		// lighting went flat and changed as the camera turned. Source's phong reaches Unity's lighting
		// through the specular inputs instead (SourcePhongToStandard).
#pragma surface surf SourceSpecular fullforwardshadows addshadow keepalpha
		ENDCG
	}

	FallBack "Standard (Specular setup)"
}

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

		_DetailTex ("$detail", 2D) = "grey" {}
		_DetailBlendFactor ("$detailblendfactor", Range(0,1)) = 1.0
		_DetailBlendMode ("$detailblendmode", Float) = 0

		_EnvMapMask ("$envmapmask", 2D) = "white" {}

		_PhongBoost ("$phongboost", Float) = 1.0
		_PhongExponent ("$phongexponent", Float) = 5.0
		_PhongFresnelRanges ("$phongfresnelranges", Vector) = (1,1,1,0)
		_PhongMaskFromBaseAlpha ("Phong mask is base alpha", Float) = 0

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
		#pragma shader_feature_local _DETAIL
		#pragma shader_feature_local _ENVMAPMASK
		#pragma shader_feature_local _PHONG
		#pragma shader_feature_local _SELFILLUM
		#pragma shader_feature_local _ALPHATEST_ON

		#include "SourceGeneric.cginc"
#pragma surface surf StandardSpecular fullforwardshadows addshadow keepalpha
		ENDCG
	}

	FallBack "Standard (Specular setup)"
}

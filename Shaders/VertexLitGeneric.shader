// Unity equivalent of Source's "VertexLitGeneric" shader, used by model (.mdl) materials.
// In Source the difference from LightmappedGeneric is where the lighting comes from - a baked
// lightmap versus per-vertex/dynamic lighting - which a Unity surface shader already decides for
// itself per renderer. So the body is shared; what differs is the name the importer looks up and
// the specular defaults, since $phong is a model-only parameter and is usually on.
Shader "Chisel/Source/VertexLitGeneric"
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

		[HideInInspector] _SrcBlend ("__src", Float) = 1.0
		[HideInInspector] _DstBlend ("__dst", Float) = 0.0
		[HideInInspector] _ZWrite ("__zw", Float) = 1.0
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

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
		// Negative hands the exponent to _PhongExponentMap, which is Source's own default.
		_PhongExponent ("$phongexponent", Float) = -1.0
		_PhongExponentFactor ("$phongexponentfactor", Float) = 0
		_PhongFresnelRanges ("$phongfresnelranges", Vector) = (1,1,1,0)
		_PhongMaskFromBaseAlpha ("Phong mask is base alpha", Float) = 0
		[Toggle] _RimLight ("$rimlight", Float) = 0
		_RimLightBoost ("$rimlightboost", Float) = 1
		[Toggle] _RimMask ("$rimmask", Float) = 0
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
		Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
		LOD 300

		Cull [_Cull]
		Blend [_SrcBlend] [_DstBlend]
		ZWrite [_ZWrite]

		CGPROGRAM
		#pragma target 3.0
		// The reflection probes come as a cubemap array (ChiselSourceProbes.cginc)
		#pragma require cubearray

		#pragma shader_feature_local _NORMALMAP
		#pragma shader_feature_local _DETAIL
		#pragma shader_feature_local _ENVMAP
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

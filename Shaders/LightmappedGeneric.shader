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
		#pragma shader_feature_local _DETAIL
		#pragma shader_feature_local _ENVMAPMASK
		#pragma shader_feature_local _PHONG
		#pragma shader_feature_local _PHONGMASK
		#pragma shader_feature_local _PHONGEXPONENTMAP
		#pragma shader_feature_local _PHONGWARP
		#pragma shader_feature_local _SELFILLUM
		#pragma shader_feature_local _ALPHATEST_ON

		// The surface output struct and the lighting functions are read out of the .shader file by
		// Unity's surface shader generator, which does not follow #include for either - so they cannot
		// move into the shared body, and VertexLitGeneric carries the same block. The phong maths they
		// call does live in the shared body.
		struct SourceSurfaceOutput
		{
			fixed3 Albedo;
			fixed3 Specular;
			fixed3 Normal;
			half3  Emission;
			half   Smoothness;
			half   Occlusion;
			fixed  Alpha;
			// Everything in Source's phong that does not depend on a light. PhongScale is the mask times
			// $phongboost, and times the fresnel as well unless a warp texture is reading the fresnel
			// as its own lookup coordinate; the exponent and tint are per pixel because a
			// $phongexponenttexture makes them so.
			half   PhongScale;
			half   PhongFresnel;
			half   PhongExponent;
			half3  PhongTint;
		};

		#include "SourceGeneric.cginc"

		void SourceToStandardSpecular(SourceSurfaceOutput s, out SurfaceOutputStandardSpecular standard)
		{
			standard.Albedo     = s.Albedo;
			standard.Specular   = s.Specular;
			standard.Normal     = s.Normal;
			standard.Emission   = s.Emission;
			standard.Smoothness = s.Smoothness;
			standard.Occlusion  = s.Occlusion;
			standard.Alpha      = s.Alpha;
		}

		half4 LightingSourceSpecular(SourceSurfaceOutput s, half3 viewDir, UnityGI gi)
		{
			SurfaceOutputStandardSpecular standard;
			SourceToStandardSpecular(s, standard);
			half4 color = LightingStandardSpecular(standard, viewDir, gi);
		#ifdef _PHONG
			// Source adds its highlight once per light, which is why it belongs here: this is where the
			// light's direction is known, and where its colour already carries the attenuation Source
			// multiplies in. The forward base pass brings the brightest light, the additive pass the rest.
			color.rgb += SourcePhongLight(s, viewDir, gi.light.dir, gi.light.color);
		#endif
			return color;
		}

		void LightingSourceSpecular_GI(SourceSurfaceOutput s, UnityGIInput data, inout UnityGI gi)
		{
			SurfaceOutputStandardSpecular standard;
			SourceToStandardSpecular(s, standard);
			LightingStandardSpecular_GI(standard, data, gi);
		}
#pragma surface surf SourceSpecular fullforwardshadows addshadow keepalpha
		ENDCG
	}

	FallBack "Standard (Specular setup)"
}

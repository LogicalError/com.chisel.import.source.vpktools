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
		_PhongMask ("Phong mask (the $bumpmap's alpha)", 2D) = "white" {}

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
		#pragma shader_feature_local _PHONGMASK
		#pragma shader_feature_local _SELFILLUM
		#pragma shader_feature_local _ALPHATEST_ON

		// The surface output struct and the lighting functions are read out of the .shader file by
		// Unity's surface shader generator, which does not follow #include for either - so they cannot
		// move into the shared body, and LightmappedGeneric carries the same block. The phong maths
		// they call does live in the shared body.
		struct SourceSurfaceOutput
		{
			fixed3 Albedo;
			fixed3 Specular;
			fixed3 Normal;
			half3  Emission;
			half   Smoothness;
			half   Occlusion;
			fixed  Alpha;
			// Everything in Source's phong that does not depend on a light: mask * fresnel * $phongboost.
			half   PhongScale;
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
			color.rgb += SourcePhongLight(s.Normal, viewDir, gi.light.dir, gi.light.color, s.PhongScale);
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

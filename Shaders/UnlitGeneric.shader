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
			#pragma shader_feature_local _ENVMAP
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

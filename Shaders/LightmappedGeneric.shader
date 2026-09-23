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
		// ToggleUI rather than Toggle, here and below: these are read as float uniforms and branched on, while
		// [Toggle] would also enable a keyword named after the property that no pass declares, leaving it in the
		// material's m_InvalidKeywords doing nothing.
		[ToggleUI] _BlendBase2 ("Blend $basetexture2", Float) = 0
		[ToggleUI] _BlendBump2 ("Blend $bumpmap2", Float) = 0
		[ToggleUI] _BlendModulate ("Modulate the blend", Float) = 0
		[ToggleUI] _MaskedBlending ("$maskedblending", Float) = 0
		_BlendMaskTransformU ("$blendmasktransform u row", Vector) = (1,0,0,0)
		_BlendMaskTransformV ("$blendmasktransform v row", Vector) = (0,1,0,0)
		[ToggleUI] _VertexColor ("$vertexcolor", Float) = 0
		[ToggleUI] _VertexAlpha ("Vertex alpha is opacity", Float) = 0

		_DetailTex ("$detail", 2D) = "grey" {}
		_DetailBlendFactor ("$detailblendfactor", Range(0,1)) = 1.0
		_DetailBlendMode ("$detailblendmode", Float) = 0

		_EnvMapMask ("$envmapmask", 2D) = "white" {}
		[NoScaleOffset] _EnvMap ("$envmap (a named cubemap)", Cube) = "black" {}
		[ToggleUI] _EnvMapFromProbes ("$envmap is env_cubemap: the nearest probe", Float) = 1
		_EnvMapTint ("$envmaptint", Color) = (1,1,1,1)
		_EnvMapContrast ("$envmapcontrast", Vector) = (0,0,0,0)
		_EnvMapSaturation ("$envmapsaturation", Vector) = (1,1,1,0)
		_FresnelReflection ("$fresnelreflection", Float) = 1
		_EnvMapPhongFresnel ("$envmapfresnel", Float) = 0
		_EnvMapMaskWeights ("Envmap mask: normal alpha, base alpha, 1 - base alpha, phong mask", Vector) = (0,0,0,0)
		[ToggleUI] _EnvMapInvertMask ("$invertphongmask", Float) = 0
		[ToggleUI] _EnvMapMaskTexture ("Envmap masked by $envmapmask", Float) = 0

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

		// VertexLitGeneric's rim light. UnityPerMaterial is shared by both shaders (ChiselSourceGenericInput.hlsl),
		// and the GPU Resident Drawer only takes a shader whose every buffer variable is a property. The world
		// shader leaves it off.
		[HideInInspector] _RimLight ("$rimlight", Float) = 0
		[HideInInspector] _RimLightBoost ("$rimlightboost", Float) = 1
		[HideInInspector] _RimLightExponent ("$rimlightexponent", Float) = 4
		[HideInInspector] _NoFog ("$nofog", Float) = 0
		[HideInInspector] _OpaqueAlpha ("Opaque: write alpha 1, not the base texture's", Float) = 0
		[HideInInspector] _RimMask ("$rimmask", Float) = 0

		[HideInInspector] _SrcBlend ("__src", Float) = 1.0
		[HideInInspector] _DstBlend ("__dst", Float) = 0.0
		[HideInInspector] _ZWrite ("__zw", Float) = 1.0
		[ToggleUI] _AlphaToMask ("Alpha to coverage ($alphatest, resolved against the MSAA mask)", Float) = 0.0
		[Enum(UnityEngine.Rendering.CullMode)] _Cull ("$nocull", Float) = 2
	}

	// URP's Deferred+ lays opaque materials down in the GBuffer pass and lights them there; translucent ones, and every
	// material under a forward renderer, are lit in ForwardLit. Both light the surface as Source does
	// (ChiselSourceGeneric.hlsl): its highlight is added rather than taken out of the diffuse, and it reflects nothing but its
	// $envmap.
	SubShader
	{
		Tags
		{
			"RenderType" = "Opaque"
			"Queue" = "Geometry"
			"RenderPipeline" = "UniversalPipeline"
			"UniversalMaterialType" = "Lit"
			"IgnoreProjector" = "True"
		}
		LOD 300

		Pass
		{
			Name "ForwardLit"
			Tags { "LightMode" = "UniversalForward" }

			Blend [_SrcBlend] [_DstBlend]
			ZWrite [_ZWrite]
			Cull [_Cull]
			// Only on the pass that draws the surface, as URP's own Lit does it: the shadow and depth passes write no
			// colour, and the GBuffer's targets are not multisampled.
			AlphaToMask [_AlphaToMask]

			HLSLPROGRAM
			#pragma target 4.5
			// The reflection probes come as a cubemap array (ChiselSourceProbes.hlsl)
			#pragma require cubearray
			#pragma vertex SourceLitVertex
			#pragma fragment SourceForwardFragment

			#pragma shader_feature_local _NORMALMAP
			#pragma shader_feature_local_fragment _VERTEXBLEND
			#pragma shader_feature_local_fragment _DETAIL
			#pragma shader_feature_local_fragment _ENVMAP
			#pragma shader_feature_local_fragment _PHONG
			#pragma shader_feature_local_fragment _PHONGMASK
			#pragma shader_feature_local_fragment _PHONGEXPONENTMAP
			#pragma shader_feature_local_fragment _PHONGWARP
			#pragma shader_feature_local_fragment _SELFILLUM
			#pragma shader_feature_local_fragment _ALPHATEST_ON

			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
			#pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
			#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
			#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
			#pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
			#pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
			#pragma multi_compile_fragment _ _LIGHT_COOKIES
			#pragma multi_compile _ _LIGHT_LAYERS
			#pragma multi_compile _ _CLUSTER_LIGHT_LOOP
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

			#pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
			#pragma multi_compile _ SHADOWS_SHADOWMASK
			#pragma multi_compile _ DIRLIGHTMAP_COMBINED
			#pragma multi_compile _ LIGHTMAP_ON
			#pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
			#pragma multi_compile _ DYNAMICLIGHTMAP_ON
			#pragma multi_compile _ USE_LEGACY_LIGHTMAPS
			#pragma multi_compile _ LOD_FADE_CROSSFADE
			#pragma multi_compile_fragment _ DEBUG_DISPLAY
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

			#pragma multi_compile_instancing
			#pragma instancing_options renderinglayer
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

			#include "ChiselSourceGenericLitPass.hlsl"
			ENDHLSL
		}

		Pass
		{
			Name "GBuffer"
			Tags { "LightMode" = "UniversalGBuffer" }

			ZWrite [_ZWrite]
			ZTest LEqual
			Cull [_Cull]

			HLSLPROGRAM
			#pragma target 4.5
			// Deferred rendering does not support the OpenGL-based graphics APIs
			#pragma exclude_renderers gles3 glcore
			#pragma require cubearray
			#pragma vertex SourceLitVertex
			#pragma fragment SourceGBufferFragment

			#pragma shader_feature_local _NORMALMAP
			#pragma shader_feature_local_fragment _VERTEXBLEND
			#pragma shader_feature_local_fragment _DETAIL
			#pragma shader_feature_local_fragment _ENVMAP
			#pragma shader_feature_local_fragment _PHONG
			#pragma shader_feature_local_fragment _PHONGMASK
			#pragma shader_feature_local_fragment _PHONGEXPONENTMAP
			#pragma shader_feature_local_fragment _PHONGWARP
			#pragma shader_feature_local_fragment _SELFILLUM
			#pragma shader_feature_local_fragment _ALPHATEST_ON

			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
			#pragma multi_compile_fragment _ _RENDER_PASS_ENABLED
			#pragma multi_compile _ _CLUSTER_LIGHT_LOOP
			#pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

			#pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
			#pragma multi_compile _ SHADOWS_SHADOWMASK
			#pragma multi_compile _ DIRLIGHTMAP_COMBINED
			#pragma multi_compile _ LIGHTMAP_ON
			#pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
			#pragma multi_compile _ DYNAMICLIGHTMAP_ON
			#pragma multi_compile _ USE_LEGACY_LIGHTMAPS
			#pragma multi_compile _ LOD_FADE_CROSSFADE
			#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
			#pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

			#pragma multi_compile_instancing
			#pragma instancing_options renderinglayer
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

			#include "ChiselSourceGenericGBufferPass.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferOutputFormat.hlsl"
			ENDHLSL
		}

		Pass
		{
			Name "ShadowCaster"
			Tags { "LightMode" = "ShadowCaster" }

			ZWrite On
			ZTest LEqual
			ColorMask 0
			Cull [_Cull]

			HLSLPROGRAM
			#pragma target 2.0
			#pragma vertex SourceShadowVertex
			#pragma fragment SourceShadowFragment

			#pragma shader_feature_local_fragment _ENVMAP
			#pragma shader_feature_local_fragment _SELFILLUM
			#pragma shader_feature_local _ALPHATEST_ON

			#pragma multi_compile_instancing
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#pragma multi_compile _ LOD_FADE_CROSSFADE
			// Directional and punctual lights bias their shadows differently
			#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

			#include "ChiselSourceGenericDepthPasses.hlsl"
			ENDHLSL
		}

		Pass
		{
			Name "DepthOnly"
			Tags { "LightMode" = "DepthOnly" }

			ZWrite On
			ColorMask R
			Cull [_Cull]

			HLSLPROGRAM
			#pragma target 2.0
			#pragma vertex SourceDepthVertex
			#pragma fragment SourceDepthOnlyFragment

			#pragma shader_feature_local_fragment _ENVMAP
			#pragma shader_feature_local_fragment _SELFILLUM
			#pragma shader_feature_local _ALPHATEST_ON

			#pragma multi_compile _ LOD_FADE_CROSSFADE

			#pragma multi_compile_instancing
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

			#include "ChiselSourceGenericDepthPasses.hlsl"
			ENDHLSL
		}

		Pass
		{
			Name "DepthNormals"
			Tags { "LightMode" = "DepthNormals" }

			ZWrite On
			Cull [_Cull]

			HLSLPROGRAM
			#pragma target 2.0
			#pragma vertex SourceDepthVertex
			#pragma fragment SourceDepthNormalsFragment

			#pragma shader_feature_local _NORMALMAP
			#pragma shader_feature_local_fragment _VERTEXBLEND
			#pragma shader_feature_local_fragment _ENVMAP
			#pragma shader_feature_local_fragment _SELFILLUM
			#pragma shader_feature_local _ALPHATEST_ON

			#pragma multi_compile _ LOD_FADE_CROSSFADE
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

			#pragma multi_compile_instancing
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

			#define SOURCE_DEPTH_NORMALS 1
			#include "ChiselSourceGenericDepthPasses.hlsl"
			ENDHLSL
		}

		// Only for lightmap baking
		Pass
		{
			Name "Meta"
			Tags { "LightMode" = "Meta" }

			Cull Off

			HLSLPROGRAM
			#pragma target 2.0
			#pragma vertex SourceMetaVertex
			#pragma fragment SourceMetaFragment

			#pragma shader_feature_local_fragment _VERTEXBLEND
			#pragma shader_feature_local_fragment _DETAIL
			#pragma shader_feature_local_fragment _ENVMAP
			#pragma shader_feature_local_fragment _SELFILLUM
			#pragma shader_feature_local_fragment _ALPHATEST_ON
			#pragma shader_feature EDITOR_VISUALIZATION

			#include "ChiselSourceGenericMetaPass.hlsl"
			ENDHLSL
		}
	}

	FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

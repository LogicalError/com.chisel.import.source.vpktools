#ifndef CHISEL_SOURCE_UNLIT_PASSES_INCLUDED
#define CHISEL_SOURCE_UNLIT_PASSES_INCLUDED

// The passes Source's unlit shaders share under URP next to their own colour pass: G-buffer, depth, depth-normals and
// shadow caster. None of them draws colour, so all they need from a shader is where its surface is cut away. A shader
// including this file declares _Cutoff in its UnityPerMaterial buffer and defines, first:
//   half SourceUnlitAlpha(float2 uv, half4 vertexColor)
// the alpha its colour pass tests against _Cutoff under _ALPHATEST_ON, from the mesh's own uv and vertex colour.
// A pass picks its part by defining one of SOURCE_UNLIT_GBUFFER_PASS, SOURCE_UNLIT_DEPTH_ONLY_PASS,
// SOURCE_UNLIT_DEPTH_NORMALS_PASS or SOURCE_UNLIT_SHADOW_CASTER_PASS before the include.
//
// Include after URP's Core.hlsl.

#if defined(SOURCE_UNLIT_GBUFFER_PASS)
	#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferOutput.hlsl"
#elif defined(SOURCE_UNLIT_SHADOW_CASTER_PASS)
	#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#endif

#if defined(SOURCE_UNLIT_GBUFFER_PASS) || defined(SOURCE_UNLIT_DEPTH_NORMALS_PASS)
	#define SOURCE_UNLIT_NEEDS_NORMAL
#endif

struct SourceUnlitAttributes
{
	float4 positionOS : POSITION;
	float3 normalOS   : NORMAL;
	float2 uv         : TEXCOORD0;
	half4  color      : COLOR;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct SourceUnlitVaryings
{
	float4 positionCS : SV_POSITION;
#if defined(_ALPHATEST_ON)
	float2 uv         : TEXCOORD0;
	half4  color      : COLOR;
#endif
#if defined(SOURCE_UNLIT_NEEDS_NORMAL)
	float3 normalWS   : TEXCOORD1;
#endif
	UNITY_VERTEX_INPUT_INSTANCE_ID
	UNITY_VERTEX_OUTPUT_STEREO
};

// Whatever the colour pass cuts away is cut away here too
void SourceUnlitClip(SourceUnlitVaryings input)
{
#if defined(_ALPHATEST_ON)
	clip(SourceUnlitAlpha(input.uv, input.color) - _Cutoff);
#endif
}

#if defined(SOURCE_UNLIT_SHADOW_CASTER_PASS)
// Set by URP for the light whose shadow map is being drawn, for the normal bias: a directional light's direction, or
// the position of a spot or point light (_CASTING_PUNCTUAL_LIGHT_SHADOW).
float3 _LightDirection;
float3 _LightPosition;

float4 SourceUnlitShadowPositionCS(float3 positionOS, float3 normalOS)
{
	float3 positionWS = TransformObjectToWorld(positionOS);
	float3 normalWS = TransformObjectToWorldNormal(normalOS);
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
	float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
	float3 lightDirectionWS = _LightDirection;
#endif
	return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
}
#endif

SourceUnlitVaryings SourceUnlitVertex(SourceUnlitAttributes input)
{
	SourceUnlitVaryings output = (SourceUnlitVaryings)0;
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_TRANSFER_INSTANCE_ID(input, output);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

#if defined(SOURCE_UNLIT_SHADOW_CASTER_PASS)
	output.positionCS = SourceUnlitShadowPositionCS(input.positionOS.xyz, input.normalOS);
#else
	output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
#endif
#if defined(_ALPHATEST_ON)
	output.uv = input.uv;
	output.color = input.color;
#endif
#if defined(SOURCE_UNLIT_NEEDS_NORMAL)
	output.normalWS = TransformObjectToWorldNormal(input.normalOS);
#endif
	return output;
}

#if defined(SOURCE_UNLIT_GBUFFER_PASS)

// Depth and normals only. The UniversalMaterialType tag Unlit keeps the deferred lights off the surface, and the colour
// pass draws it after them.
GBufferFragOutput SourceUnlitGBufferFragment(SourceUnlitVaryings input)
{
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
	SourceUnlitClip(input);

	InputData inputData = (InputData)0;
	inputData.positionCS = input.positionCS;
	inputData.normalWS = normalize(input.normalWS);
	inputData.shadowMask = half4(1, 1, 1, 1);

	SurfaceData surfaceData = (SurfaceData)0;
	surfaceData.occlusion = 1;
	return PackGBuffersSurfaceData(surfaceData, inputData, half3(0, 0, 0));
}

#elif defined(SOURCE_UNLIT_DEPTH_ONLY_PASS)

half SourceUnlitDepthOnlyFragment(SourceUnlitVaryings input) : SV_Target
{
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
	SourceUnlitClip(input);
	return input.positionCS.z;
}

#elif defined(SOURCE_UNLIT_DEPTH_NORMALS_PASS)

void SourceUnlitDepthNormalsFragment(SourceUnlitVaryings input
	, out half4 outNormalWS : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
	, out uint outRenderingLayers : SV_Target1
#endif
	)
{
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
	SourceUnlitClip(input);

	// Encoded the way URP's own depth-normals passes encode it
#if defined(_GBUFFER_NORMALS_OCT)
	float3 normalWS = normalize(input.normalWS);
	float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
	float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
	outNormalWS = half4(half3(PackFloat2To888(remappedOctNormalWS)), 0.0);
#else
	outNormalWS = half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
#endif

#ifdef _WRITE_RENDERING_LAYERS
	outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

#elif defined(SOURCE_UNLIT_SHADOW_CASTER_PASS)

half4 SourceUnlitShadowFragment(SourceUnlitVaryings input) : SV_Target
{
	UNITY_SETUP_INSTANCE_ID(input);
	SourceUnlitClip(input);
	return 0;
}

#endif

#endif // CHISEL_SOURCE_UNLIT_PASSES_INCLUDED

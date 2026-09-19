#ifndef CHISEL_SOURCE_GENERIC_DEPTH_PASSES_INCLUDED
#define CHISEL_SOURCE_GENERIC_DEPTH_PASSES_INCLUDED

// The passes that only need a Source surface's shape: ShadowCaster, DepthOnly and DepthNormals. They cut an alpha tested
// surface out as the lit passes do (SourceOpacity), and DepthNormals writes its normal-mapped normal.

#include "ChiselSourceGenericInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#if defined(LOD_FADE_CROSSFADE)
	#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

// The light a shadow is being drawn for, set by URP (ShadowUtils.SetupShadowCasterConstantBuffer): its direction for a
// directional light, its position for a spot or point light, where the direction changes from vertex to vertex
float3 _LightDirection;
float3 _LightPosition;

struct SourceDepthAttributes
{
	float4 positionOS : POSITION;
	float3 normalOS   : NORMAL;
	float4 tangentOS  : TANGENT;
	half4  color      : COLOR;
	float2 texcoord   : TEXCOORD0;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

// Only a surface that is cut out, or whose normal is written, reads its textures
#if defined(_ALPHATEST_ON) || defined(SOURCE_DEPTH_NORMALS)
	#define SOURCE_DEPTH_READS_SURFACE 1
#endif

struct SourceDepthVaryings
{
	float4 positionCS : SV_POSITION;
#ifdef SOURCE_DEPTH_READS_SURFACE
	float2 uv         : TEXCOORD0;
	half4  color      : TEXCOORD1;
#endif
#ifdef SOURCE_DEPTH_NORMALS
	half3  normalWS   : TEXCOORD2;
	#ifdef _NORMALMAP
	half4  tangentWS  : TEXCOORD3;    // xyz: tangent, w: sign
	#endif
#endif
	UNITY_VERTEX_INPUT_INSTANCE_ID
	UNITY_VERTEX_OUTPUT_STEREO
};

SourceDepthVaryings SourceDepthVertexCommon(SourceDepthAttributes input, float4 positionCS)
{
	SourceDepthVaryings output = (SourceDepthVaryings)0;
	UNITY_TRANSFER_INSTANCE_ID(input, output);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

	output.positionCS = positionCS;
#ifdef SOURCE_DEPTH_READS_SURFACE
	output.uv         = TRANSFORM_TEX(input.texcoord, _BaseMap);
	output.color      = input.color;
#endif
#ifdef SOURCE_DEPTH_NORMALS
	VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
	output.normalWS = normalInput.normalWS;
	#ifdef _NORMALMAP
	output.tangentWS = half4(normalInput.tangentWS, input.tangentOS.w * GetOddNegativeScale());
	#endif
#endif
	return output;
}

// Cuts the surface out where the lit passes do
void SourceDepthClip(SourceDepthVaryings input)
{
#ifdef _ALPHATEST_ON
	SourceAlphaClip(SourceOpacity(SourceSampleBase(input.uv, input.color), input.color));
#endif
#if defined(LOD_FADE_CROSSFADE)
	LODFadeCrossFade(input.positionCS);
#endif
}

SourceDepthVaryings SourceShadowVertex(SourceDepthAttributes input)
{
	UNITY_SETUP_INSTANCE_ID(input);

	float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
	float3 normalWS   = TransformObjectToWorldNormal(input.normalOS);
#if _CASTING_PUNCTUAL_LIGHT_SHADOW
	float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
	float3 lightDirectionWS = _LightDirection;
#endif
	float4 positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
	return SourceDepthVertexCommon(input, positionCS);
}

half4 SourceShadowFragment(SourceDepthVaryings input) : SV_TARGET
{
	UNITY_SETUP_INSTANCE_ID(input);
	SourceDepthClip(input);
	return 0;
}

SourceDepthVaryings SourceDepthVertex(SourceDepthAttributes input)
{
	UNITY_SETUP_INSTANCE_ID(input);
	return SourceDepthVertexCommon(input, TransformObjectToHClip(input.positionOS.xyz));
}

half SourceDepthOnlyFragment(SourceDepthVaryings input) : SV_TARGET
{
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
	SourceDepthClip(input);
	return input.positionCS.z;
}

#ifdef SOURCE_DEPTH_NORMALS
// The normal the lit passes shade with. Smoothness stays 0: a Source surface reflects only its $envmap, never URP's
// screen-space reflections.
void SourceDepthNormalsFragment(
	SourceDepthVaryings input
	, out half4 outNormalWS : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
	, out uint outRenderingLayers : SV_Target1
#endif
)
{
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

	SourceBase base = SourceSampleBase(input.uv, input.color);
#ifdef _ALPHATEST_ON
	SourceAlphaClip(SourceOpacity(base, input.color));
#endif
#if defined(LOD_FADE_CROSSFADE)
	LODFadeCrossFade(input.positionCS);
#endif

#ifdef _NORMALMAP
	float3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
	float3 normalWS = TransformTangentToWorld(SourceNormalTS(input.uv, base), half3x3(input.tangentWS.xyz, bitangent, input.normalWS));
#else
	float3 normalWS = input.normalWS;
#endif
	normalWS = normalize(normalWS);

#if defined(_GBUFFER_NORMALS_OCT)
	float2 octNormalWS = PackNormalOctQuadEncode(normalWS);           // values between [-1, +1], must use fp32 on some platforms
	float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);   // values between [ 0,  1]
	outNormalWS = half4(PackFloat2To888(remappedOctNormalWS), 0.0);
#else
	outNormalWS = half4(normalWS, 0.0);
#endif

#ifdef _WRITE_RENDERING_LAYERS
	outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}
#endif

#endif // CHISEL_SOURCE_GENERIC_DEPTH_PASSES_INCLUDED

#ifndef CHISEL_SOURCE_GENERIC_GBUFFER_PASS_INCLUDED
#define CHISEL_SOURCE_GENERIC_GBUFFER_PASS_INCLUDED

// The UniversalGBuffer pass: an opaque Source surface laid down for URP's deferred lighting, which lights it as the
// forward pass does (ChiselSourceGenericLitPass.hlsl).

// The G-buffer holds a specular colour rather than a metallic value
#define _SPECULAR_SETUP 1

#include "ChiselSourceGenericLitPass.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferOutput.hlsl"

GBufferFragOutput SourceGBufferFragment(SourceVaryings input)
{
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

	SourceSurface surface = SourceLitSurface(input);

#ifdef LOD_FADE_CROSSFADE
	LODFadeCrossFade(input.positionCS);
#endif

	InputData inputData;
	SourceInitializeInputData(input, surface, inputData);
	SETUP_DEBUG_TEXTURE_DATA(inputData, UNDO_TRANSFORM_TEX(input.uv.xy, _BaseMap));
	SourceInitializeBakedGI(input, inputData);

	BRDFData brdfData;
	SourceBRDFData(surface, brdfData);

	// What the deferred lighting does not add: the lightmap or light probes, and the emission
	Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
	MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI, inputData.shadowMask);
	half3 color = SourceGlobalIllumination(brdfData, inputData.bakedGI, 1.0h);

	// URP's deferred lighting takes the specular's share out of the diffuse again (GBufferDataToBRDFData:
	// albedo * (1 - reflectivity)), which Source does not do, so the G-buffer holds the albedo that leaves the surface's own
	brdfData.albedo = saturate(brdfData.diffuse / max(1.0h - brdfData.reflectivity, 1.0h / 255.0h));

	return PackGBuffersBRDFData(brdfData, inputData, surface.smoothness, surface.emission + color, 1.0h);
}

#endif // CHISEL_SOURCE_GENERIC_GBUFFER_PASS_INCLUDED

#ifndef CHISEL_SOURCE_GENERIC_GBUFFER_PASS_INCLUDED
#define CHISEL_SOURCE_GENERIC_GBUFFER_PASS_INCLUDED

// The UniversalGBuffer pass: an opaque Source surface laid down for URP's deferred lighting, which lights it as the
// forward pass does (ChiselSourceGenericLitPass.hlsl).
//
// KEPT BUT NOT DRAWN. The project renders Forward+ (ChiselURP_Renderer m_RenderingMode 2), where URP draws
// UniversalForward for opaque and never reaches this pass; under Deferred+ it is the other way round and this is the
// ONLY thing that draws them. It is compiled on every shader change
//  so it cannot rot silently, and it is kept against a future switch.
//
// WHAT DOES NOT SURVIVE THAT SWITCH, so nobody has to rediscover it:
//  - THE FOG'S ALPHA MARK. Our fog is a screen-space pass that reads the camera colour's alpha: 0 means "do not fog
//    this pixel" (SourceFog.shader, the sky-face convention, and $nofog through _NoFog). The forward fragment writes
//    that mark; this one cannot, because under deferred it is URP's lighting pass that produces the final colour and
//    owns its alpha. So a move to Deferred+ silently makes $nofog inert on every lit material. Sky faces are fine -
//    Sky Face.shader is UniversalForwardOnly, which deferred does draw.
//  - ALPHA TO COVERAGE. AlphaToMask is on the ForwardLit pass alone, as URP's own Lit has it, and URP's deferred path
//    has no MSAA to resolve against anyway (_AlphaToMaskAvailable is set only for a multisampled opaque pass).
// Neither is a defect in this file; both are properties of where the final colour is written.

// The G-buffer holds a specular colour rather than a metallic value
#define _SPECULAR_SETUP 1

#include "ChiselSourceGenericLitPass.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferOutput.hlsl"

GBufferFragOutput SourceGBufferFragment(SourceVaryings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC)
{
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
	// A two-sided material's back faces carry the front face's normal; see SourceFlipNormalForBackFace
	SourceFlipNormalForBackFace(input, frontFace);

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

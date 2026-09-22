#ifndef CHISEL_SOURCE_GENERIC_LIT_PASS_INCLUDED
#define CHISEL_SOURCE_GENERIC_LIT_PASS_INCLUDED

// The vertex stage both lit passes share, and the UniversalForward pass, which draws translucent materials (and
// everything under a forward renderer). ChiselSourceGenericGBufferPass.hlsl lays opaque materials down for URP's
// deferred lighting from the same surface. Both light a Source surface as Source does (SourceBRDFData,
// SourceGlobalIllumination).

#include "ChiselSourceGeneric.hlsl"
#if defined(LOD_FADE_CROSSFADE)
	#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

struct SourceAttributes
{
	float4 positionOS        : POSITION;
	float3 normalOS          : NORMAL;
	float4 tangentOS         : TANGENT;
	half4  color             : COLOR;
	float2 texcoord          : TEXCOORD0;
	float2 staticLightmapUV  : TEXCOORD1;
	float2 dynamicLightmapUV : TEXCOORD2;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct SourceVaryings
{
	float4 uv                       : TEXCOORD0;    // xy: the base texture's, zw: the detail texture's
	float3 positionWS               : TEXCOORD1;
	half3  normalWS                 : TEXCOORD2;
#ifdef _NORMALMAP
	half4  tangentWS                : TEXCOORD3;    // xyz: tangent, w: sign
#endif
	half4  color                    : TEXCOORD4;
#ifdef _ADDITIONAL_LIGHTS_VERTEX
	half4  fogFactorAndVertexLight  : TEXCOORD5;    // x: fog factor, yzw: vertex light
#else
	half   fogFactor                : TEXCOORD5;
#endif
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
	float4 shadowCoord              : TEXCOORD6;
#endif
	DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 7);
#ifdef DYNAMICLIGHTMAP_ON
	float2 dynamicLightmapUV        : TEXCOORD8;
#endif
#ifdef USE_APV_PROBE_OCCLUSION
	float4 probeOcclusion           : TEXCOORD9;
#endif
	float4 positionCS               : SV_POSITION;
	UNITY_VERTEX_INPUT_INSTANCE_ID
	UNITY_VERTEX_OUTPUT_STEREO
};

SourceVaryings SourceLitVertex(SourceAttributes input)
{
	SourceVaryings output = (SourceVaryings)0;

	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_TRANSFER_INSTANCE_ID(input, output);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

	VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
	// Normalized, so that interpolation does not skew them
	VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

	output.uv.xy      = TRANSFORM_TEX(input.texcoord, _BaseMap);
	output.uv.zw      = TRANSFORM_TEX(input.texcoord, _DetailTex);
	output.positionWS = vertexInput.positionWS;
	output.normalWS   = normalInput.normalWS;
#ifdef _NORMALMAP
	output.tangentWS  = half4(normalInput.tangentWS, input.tangentOS.w * GetOddNegativeScale());
#endif
	output.color      = input.color;

	half fogFactor = 0;
#if !defined(_FOG_FRAGMENT)
	fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
#endif
#ifdef _ADDITIONAL_LIGHTS_VERTEX
	output.fogFactorAndVertexLight = half4(fogFactor, VertexLighting(vertexInput.positionWS, normalInput.normalWS));
#else
	output.fogFactor = fogFactor;
#endif

	OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
#ifdef DYNAMICLIGHTMAP_ON
	output.dynamicLightmapUV = input.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
#endif
	OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, output.probeOcclusion);

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
	output.shadowCoord = GetShadowCoord(vertexInput);
#endif

	output.positionCS = vertexInput.positionCS;
	return output;
}

// A two-sided material ($nocull, which foliage, fences and grates all use) draws its back faces with the FRONT
// face's normal, so every back-facing pixel is shaded as a surface pointing AWAY from the viewer. Everything
// downstream then reasons about a surface that isn't there: `1 - saturate(dot(N, V))` evaluates to exactly 1, so the
// rim light and the environment fresnel fire at full strength on every back face whatever their exponent, and the
// diffuse and phong terms light the wrong hemisphere. Flip it, as URP's own two-sided shaders do; the tangent's
// handedness goes with it so the normal map's bitangent stays right-handed.
//
// Found on a map's agave: its inner leaves are the ones seen from behind, and they came out flat pale grey
// because the rim term was adding the ambient probe over them at full strength.
void SourceFlipNormalForBackFace(inout SourceVaryings input, FRONT_FACE_TYPE frontFace)
{
	half facing = IS_FRONT_VFACE(frontFace, 1.0h, -1.0h);
	input.normalWS *= facing;
#ifdef _NORMALMAP
	input.tangentWS.w *= facing;
#endif
}

SourceSurface SourceLitSurface(SourceVaryings input)
{
	SourceSurfaceInput surfaceInput;
	surfaceInput.uv          = input.uv.xy;
	surfaceInput.detailUV    = input.uv.zw;
	surfaceInput.positionWS  = input.positionWS;
	surfaceInput.normalWS    = input.normalWS;
#ifdef _NORMALMAP
	surfaceInput.tangentWS   = input.tangentWS;
#else
	surfaceInput.tangentWS   = half4(0, 0, 0, 1);
#endif
	surfaceInput.viewDirWS   = GetWorldSpaceNormalizeViewDir(input.positionWS);
	surfaceInput.vertexColor = input.color;

	SourceSurface surface;
	SourceSurfaceFunction(surfaceInput, surface);
	return surface;
}

void SourceInitializeInputData(SourceVaryings input, SourceSurface surface, out InputData inputData)
{
	inputData = (InputData)0;

	inputData.positionWS      = input.positionWS;
	inputData.positionCS      = input.positionCS;
	inputData.normalWS        = surface.normalWS;
	inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
#ifdef _NORMALMAP
	float3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
	inputData.tangentToWorld = half3x3(input.tangentWS.xyz, bitangent, input.normalWS);
#endif

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
	inputData.shadowCoord = input.shadowCoord;
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
	inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
#else
	inputData.shadowCoord = float4(0, 0, 0, 0);
#endif

#ifdef _ADDITIONAL_LIGHTS_VERTEX
	inputData.fogCoord       = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndVertexLight.x);
	inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
#else
	inputData.fogCoord       = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
#endif

	inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

#if defined(DEBUG_DISPLAY)
	#if defined(DYNAMICLIGHTMAP_ON)
	inputData.dynamicLightmapUV = input.dynamicLightmapUV;
	#endif
	#if defined(LIGHTMAP_ON)
	inputData.staticLightmapUV = input.staticLightmapUV;
	#else
	inputData.vertexSH = input.vertexSH;
	#endif
	#if defined(USE_APV_PROBE_OCCLUSION)
	inputData.probeOcclusion = input.probeOcclusion;
	#endif
#endif
}

// The lightmap, or the light probes, as URP's Lit passes read them
void SourceInitializeBakedGI(SourceVaryings input, inout InputData inputData)
{
#if defined(_SCREEN_SPACE_IRRADIANCE)
	inputData.bakedGI = SAMPLE_GI(_ScreenSpaceIrradiance, input.positionCS.xy, inputData.normalWS);
#elif defined(DYNAMICLIGHTMAP_ON)
	inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.dynamicLightmapUV, input.vertexSH, inputData.normalWS);
	inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
#elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
	inputData.bakedGI = SAMPLE_GI(input.vertexSH,
		GetAbsolutePositionWS(inputData.positionWS),
		inputData.normalWS,
		inputData.viewDirectionWS,
		input.positionCS.xy,
		input.probeOcclusion,
		inputData.shadowMask);
#else
	inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
	inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
#endif
}

#if defined(DEBUG_DISPLAY)
// What URP's rendering debugger reads of the surface
SurfaceData SourceDebugSurfaceData(SourceSurface surface)
{
	SurfaceData surfaceData = (SurfaceData)0;
	surfaceData.albedo     = surface.albedo;
	surfaceData.specular   = surface.specular;
	surfaceData.smoothness = surface.smoothness;
	surfaceData.normalTS   = surface.normalTS;
	surfaceData.emission   = surface.emission;
	surfaceData.occlusion  = 1;
	surfaceData.alpha      = surface.alpha;
	return surfaceData;
}
#endif

// URP's UniversalFragmentPBR, lighting a Source surface: every light through URP's BRDF, the lightmap or light probes
// without the reflections (SourceGlobalIllumination), and the surface's own emission. Source has no occlusion map; the
// screen-space ambient occlusion still applies.
half4 SourceLighting(InputData inputData, SourceSurface surface)
{
	BRDFData brdfData;
	SourceBRDFData(surface, brdfData);

#if defined(DEBUG_DISPLAY)
	SurfaceData debugSurfaceData = SourceDebugSurfaceData(surface);
	half4 debugColor;
	if (CanDebugOverrideOutputColor(inputData, debugSurfaceData, brdfData, debugColor))
		return debugColor;
#endif

	half4 shadowMask = CalculateShadowMask(inputData);
	AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData.normalizedScreenSpaceUV, 1.0h);
	uint meshRenderingLayers = GetMeshRenderingLayer();

	Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);
	// The occlusion is applied to the GI below, as URP does
	MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);

	LightingData lightingData = (LightingData)0;
	lightingData.giColor       = SourceGlobalIllumination(brdfData, inputData.bakedGI, aoFactor.indirectAmbientOcclusion);
	lightingData.emissionColor = surface.emission;

#ifdef _LIGHT_LAYERS
	if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
		lightingData.mainLightColor = LightingPhysicallyBased(brdfData, mainLight, inputData.normalWS, inputData.viewDirectionWS, false);

#if defined(_ADDITIONAL_LIGHTS)
	uint pixelLightCount = GetAdditionalLightsCount();

	#if USE_CLUSTER_LIGHT_LOOP
	[loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
	{
		CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK

		Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
	#ifdef _LIGHT_LAYERS
		if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
	#endif
			lightingData.additionalLightsColor += LightingPhysicallyBased(brdfData, light, inputData.normalWS, inputData.viewDirectionWS, false);
	}
	#endif

	LIGHT_LOOP_BEGIN(pixelLightCount)
		Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
	#ifdef _LIGHT_LAYERS
		if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
	#endif
			lightingData.additionalLightsColor += LightingPhysicallyBased(brdfData, light, inputData.normalWS, inputData.viewDirectionWS, false);
	LIGHT_LOOP_END
#endif

#if defined(_ADDITIONAL_LIGHTS_VERTEX)
	lightingData.vertexLightingColor += inputData.vertexLighting * brdfData.diffuse;
#endif

	return CalculateFinalColor(lightingData, surface.alpha);
}

void SourceForwardFragment(
	SourceVaryings input
	, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC
	, out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
	, out uint outRenderingLayers : SV_Target1
#endif
)
{
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
	SourceFlipNormalForBackFace(input, frontFace);

	SourceSurface surface = SourceLitSurface(input);

#ifdef LOD_FADE_CROSSFADE
	LODFadeCrossFade(input.positionCS);
#endif

	InputData inputData;
	SourceInitializeInputData(input, surface, inputData);
	SETUP_DEBUG_TEXTURE_DATA(inputData, UNDO_TRANSFORM_TEX(input.uv.xy, _BaseMap));
	SourceInitializeBakedGI(input, inputData);

	half4 color = SourceLighting(inputData, surface);
	color.rgb = MixFog(color.rgb, inputData.fogCoord);
	// The surface's own alpha, which is what the material's blend state expects - except that $nofog asks not to be
	// fogged, and our fog is a screen-space pass rather than something mixed in here. A surface opts out the way sky
	// faces do, by leaving 0 in the alpha; SourceFogPass tests it and restores the alpha to 1. The importer only sets
	// _NoFog where the alpha is not also the blend weight, so this cannot make a translucent surface vanish.
	outColor = half4(color.rgb, _NoFog > 0.5h ? 0.0h : color.a);

#ifdef _WRITE_RENDERING_LAYERS
	outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

#endif // CHISEL_SOURCE_GENERIC_LIT_PASS_INCLUDED

#ifndef CHISEL_SOURCE_EYE_PASS_INCLUDED
#define CHISEL_SOURCE_EYE_PASS_INCLUDED

// Source's "EyeRefract", ported from eye_refract_ps2x.fxc / eye_refract_vs20.fxc.
//
// The thing to know before reading any of it: an eye material has NO $basetexture. Its colour comes from
// $Iris, and Valve's own comment at the combine calls that "Unlit iris, pupil, and sclera color" - the
// white of the eye is in that texture too. That is why an EyeRefract material fell through to URP/Lit and
// came out pure white: the fallback looked for a base texture and there has never been one.
//
// The original combine, which this follows:
//     result.rgb  = cIrisColor.rgb
//     result.rgb += fCorneaNoise * 0.1
//     result.rgb *= cVertexLight + cIrisLighting
//     result.rgb += cReflection * cVertexLight
//     result.rgb += cSpecularHighlights
//
// WHAT IS APPROXIMATED, and why. Source projects the iris onto the eyeball with $IrisU/$IrisV and takes the
// surface normal from the sphere centre $EyeOrigin - and the client rewrites all three every frame from the
// eye attachment, which is how an eye tracks what it is looking at. An imported scene has no such runtime,
// so the projection is frozen at the pose the model was authored in: an eye looking straight ahead. A static
// eye is the honest result, not a bug.
//
// It is NOT frozen at a guess, though. The planes come from the model's own eyeball - mstudioeyeball_t
// (studio.h:1240) gives its centre, up and forward vectors and radius in the eye bone's space - resolved at
// the bind pose and measured into a uv per vertex at import (ModelImporter.EyeProjection), carried in uv2.
// Per vertex rather than per material because an eye material is shared between models, and because these
// meshes are skinned: after skinning, object space is the character's, not the eyeball's, so nothing that
// depends on where the eye sits in object space survives being posed.
//
// A mesh with no such uv falls back to a projection about the mesh's own origin, which is right for exactly
// one thing - a material preview's primitive, centred and one unit across - and wrong for a character, whose
// object space is centred between its feet.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

TEXTURE2D(_Iris);                   SAMPLER(sampler_Iris);
TEXTURE2D(_CorneaTexture);          SAMPLER(sampler_CorneaTexture);
TEXTURE2D(_AmbientOcclTexture);     SAMPLER(sampler_AmbientOcclTexture);
TEXTURECUBE(_EnvMap);               SAMPLER(sampler_EnvMap);

CBUFFER_START(UnityPerMaterial)
	float4 _Iris_ST;
	half4  _AmbientOcclColor;
	half   _EyeballRadius;
	half   _Dilation;
	half   _Glossiness;
	half   _CorneaBumpStrength;
	half   _ParallaxStrength;
	half   _HalfLambert;
CBUFFER_END

// Under a BatchRendererGroup - the GPU Resident Drawer, or Entities Graphics - UnityPerMaterial is not per draw,
// so a whole batch would shade with one material's values unless these are read from the instance data instead.
// The cached statics are URP's own shape (LitInput.hlsl): reading each property once rather than at every use
// stops the compiler regenerating the load, which URP measured at about 10% of GPU time on a Quest 2.
//
// _Iris_ST is left out deliberately, as URP leaves its own _BaseMap_ST out: a texture's tiling is not per
// instance, and a batch only ever mixes materials that share the texture.
#ifdef UNITY_DOTS_INSTANCING_ENABLED
UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
	UNITY_DOTS_INSTANCED_PROP(float4, _AmbientOcclColor)
	UNITY_DOTS_INSTANCED_PROP(float , _EyeballRadius)
	UNITY_DOTS_INSTANCED_PROP(float , _Dilation)
	UNITY_DOTS_INSTANCED_PROP(float , _Glossiness)
	UNITY_DOTS_INSTANCED_PROP(float , _CorneaBumpStrength)
	UNITY_DOTS_INSTANCED_PROP(float , _ParallaxStrength)
	UNITY_DOTS_INSTANCED_PROP(float , _HalfLambert)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

static float4 unity_DOTS_Sampled_AmbientOcclColor;
static float  unity_DOTS_Sampled_EyeballRadius;
static float  unity_DOTS_Sampled_Dilation;
static float  unity_DOTS_Sampled_Glossiness;
static float  unity_DOTS_Sampled_CorneaBumpStrength;
static float  unity_DOTS_Sampled_ParallaxStrength;
static float  unity_DOTS_Sampled_HalfLambert;

void SetupDOTSEyeMaterialPropertyCaches()
{
	unity_DOTS_Sampled_AmbientOcclColor   = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _AmbientOcclColor);
	unity_DOTS_Sampled_EyeballRadius      = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _EyeballRadius);
	unity_DOTS_Sampled_Dilation           = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Dilation);
	unity_DOTS_Sampled_Glossiness         = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Glossiness);
	unity_DOTS_Sampled_CorneaBumpStrength = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _CorneaBumpStrength);
	unity_DOTS_Sampled_ParallaxStrength   = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _ParallaxStrength);
	unity_DOTS_Sampled_HalfLambert        = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _HalfLambert);
}

#undef UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES
#define UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES() SetupDOTSEyeMaterialPropertyCaches()

#define _AmbientOcclColor   unity_DOTS_Sampled_AmbientOcclColor
#define _EyeballRadius      unity_DOTS_Sampled_EyeballRadius
#define _Dilation           unity_DOTS_Sampled_Dilation
#define _Glossiness         unity_DOTS_Sampled_Glossiness
#define _CorneaBumpStrength unity_DOTS_Sampled_CorneaBumpStrength
#define _ParallaxStrength   unity_DOTS_Sampled_ParallaxStrength
#define _HalfLambert        unity_DOTS_Sampled_HalfLambert
#endif

struct Attributes
{
	float4 positionOS : POSITION;
	float3 normalOS   : NORMAL;
	float2 uv         : TEXCOORD0;
	float2 eyeUv      : TEXCOORD1;   // the importer's iris uv; see SourceEyeFragment
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
	float4 positionCS : SV_POSITION;
	float3 positionWS : TEXCOORD0;
	float3 positionOS : TEXCOORD1;   // the iris is projected in object space; see the note above
	float3 normalWS   : TEXCOORD2;
	float2 uv         : TEXCOORD3;   // $AmbientOcclTexture uses the MESH's own uvs (eye_refract_vs20.fxc:189)
	float4 shadowCoord : TEXCOORD4;
	float2 eyeUv      : TEXCOORD5;
	UNITY_VERTEX_INPUT_INSTANCE_ID
	UNITY_VERTEX_OUTPUT_STEREO
};

Varyings SourceEyeVertex(Attributes input)
{
	Varyings output = (Varyings)0;
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_TRANSFER_INSTANCE_ID(input, output);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

	VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
	output.positionCS  = positions.positionCS;
	output.positionWS  = positions.positionWS;
	output.positionOS  = input.positionOS.xyz;
	output.uv          = TRANSFORM_TEX(input.uv, _Iris);
	output.eyeUv       = input.eyeUv;
	// Source takes the normal from the eyeball's CENTRE rather than the mesh (eye_refract_vs20.fxc:126), which
	// keeps the eye reading as a sphere even where the mesh is coarse. The centre is not available here, and the
	// thing that used to stand in for it - normalize(positionOS) - is the direction away from the MESH's origin,
	// which on a character is a point between its feet. The mesh's own normal is the eyeball's actual surface
	// direction and identical to Source's on a sphere; the difference is only that a normal an artist smoothed
	// into the surrounding face stays smoothed.
	output.normalWS    = TransformObjectToWorldNormal(input.normalOS);
	output.shadowCoord = TransformWorldToShadowCoord(positions.positionWS);
	return output;
}

// Source's eye takes its ambient from the model's lighting state, the ambient cube of its leaf
// (DoLightingUnrolled). Here that is the same probes every other surface reads: APV when the renderer has it -
// plain SampleSH would read the object's light probes, which APV does not fill - and the probes otherwise.
half3 SourceEyeAmbient(float3 positionWS, float3 normalWS, float3 viewDirWS, float2 positionSS)
{
#if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
	return SampleProbeVolumePixel(half3(0, 0, 0), GetAbsolutePositionWS(positionWS), normalWS, viewDirWS, positionSS);
#else
	return SampleSH(normalWS);
#endif
}

// What one light contributes, all three ways Source's eye takes it: diffuse into the "vertex light" on the bent
// normal (eye_refract_vs20.fxc:157), the iris caustic (eye_refract_ps2x.fxc:332-372) and the cornea glint
// (:408-424). The iris caustic and the glint take the light's falloff WITHOUT its cosine term, as Valve's do
// (vLightFalloffCosine.x/.y are attenuation only).
struct SourceEyeLight
{
	half3 vertexLight;
	half3 irisLighting;
	half3 glints;
};

void AccumulateSourceEyeLight(Light light, float3 bentNormal, float3 irisTangentNormal, float3 corneaReflection,
							  float3 T, float3 B, float3 N, half highlightMask, inout SourceEyeLight sum)
{
	half3 radiance = light.color * (light.distanceAttenuation * light.shadowAttenuation);
	float3 L = light.direction;          // toward the light, as PixelShaderGetLightVector's is

	// CosineTermInternal: half-Lambert when the material asks for it, which every eye in the game does
	float nDotL = dot(bentNormal, L);
	half cosine = (_HalfLambert > 0.5h) ? (nDotL * 0.5 + 0.5) * (nDotL * 0.5 + 0.5) : saturate(nDotL);
	sum.vertexLight += radiance * cosine;

	// The iris caustic, line for line
	float3 tangentLight = float3(dot(L, T), dot(L, B), dot(L, N));
	float3 flattened = -tangentLight;
	flattened.xy *= -0.5;                // "Flatten tangent view"
	flattened.z = max(flattened.z, 0.5); // "Clamp z of tangent view to help maintain highlight"
	flattened = normalize(flattened);
	float irisFacing = pow(abs(dot(irisTangentNormal, flattened)), 6.0) * 0.5;       // "6.0 and 0.5 are magic numbers"
	float coneOfDarkness = pow(1.0 - saturate((-tangentLight.z - 0.25) / 0.75), 4.0); // light behind the eyeball
	sum.irisLighting += irisFacing * highlightMask * coneOfDarkness * radiance;

	// The glint. Not masked by the highlight mask: only the iris caustic is (:337 against :413).
	sum.glints += pow(saturate(dot(corneaReflection, L)), 128.0) * radiance;
}

half4 SourceEyeFragment(Varyings input) : SV_Target
{
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

	// Source's SPHERE uv. The importer measures it per vertex from the model's own eyeball - centre, axes and
	// iris size, from mstudioeyeball_t - into uv2 (ModelImporter.EyeProjection). A mesh that carries none hands
	// zero, and a projection about the mesh's own origin stands in, spanning a unit primitive: right for a
	// material preview's sphere and for nothing else. It does not read _EyeballRadius, which in Source is the
	// ray-traced sphere's world radius and has nothing to do with the projection.
	float2 sphereUv = any(input.eyeUv) ? input.eyeUv : (input.positionOS.xy + 0.5);

	// The cornea texture is a CROPPED version of the iris texture: vSphereUv = vCorneaUv * 0.5 + 0.25 (:205).
	float2 corneaUv = (sphereUv - 0.25) * 2.0;

	// Valve's tangent frame (eye_refract_vs20.fxc:126, :139-142): N out from the eye's centre, T across the iris,
	// B = N x T. The importer bakes N (EyeProjection.SphereNormal). T is the direction the iris uv's u increases
	// in, which a pixel reads off its own derivatives: the uv is linear in position, so its surface gradient IS
	// the projection's axis, projected onto the tangent plane - exactly what Valve's cross products do to it.
	float3 N = normalize(input.normalWS);
	float3 dpdx = ddx(input.positionWS);
	float3 dpdy = ddy(input.positionWS);
	float3 r1 = cross(dpdy, N);
	float3 r2 = cross(N, dpdx);
	float  inverseDeterminant = 1.0 / (abs(dot(dpdx, r1)) > 1e-30 ? dot(dpdx, r1) : 1e-30);
	float3 gradientU = (r1 * ddx(sphereUv.x) + r2 * ddy(sphereUv.x)) * inverseDeterminant;
	float3 gradientV = (r1 * ddx(sphereUv.y) + r2 * ddy(sphereUv.y)) * inverseDeterminant;
	float3 T = gradientU - dot(gradientU, N) * N;
	T = (dot(T, T) > 1e-20) ? normalize(T) : normalize(cross(float3(0, 1, 0), N) + float3(1e-5, 0, 0));
	// B is orthonormal as Valve's is and runs along INCREASING v, which on the importer's uv is UP the eye - where
	// Valve's own B points. Valve's is cross(N, T) (vs20:142), but the same cross product in Unity's left-handed
	// world comes out mirrored, pointing down, so the direction is taken from the uv instead of trusted. The cornea
	// texture agrees: across eye-cornea R rises toward larger u (25 at the left, 230 at the right) and G toward the
	// file's top row, larger v (24 at the bottom, 232 at the top) - a convex dome in this frame.
	float3 B = cross(N, T);
	B *= (dot(B, gradientV) < 0.0) ? -1.0 : 1.0;

	// The view vector runs from the camera TO the surface, as Valve's does (vs20:145)
	float3 viewVector = normalize(input.positionWS - GetCameraPositionWS());
	float3 tangentView = float3(dot(viewVector, T), dot(viewVector, B), dot(viewVector, N));

	half4 cornea = SAMPLE_TEXTURE2D(_CorneaTexture, sampler_CorneaTexture, corneaUv);
	half  highlightMask = cornea.a;

	// Hacked parallax mapping on the iris (:214-229): the cornea's B holds how deep the iris sits under it (255 at
	// the centre, ~16 at the edge). Valve's frame is (T, B) = (+u, -v): its B points up while its v runs down the
	// texture. That mirror is the "Need to flip x...not sure why" - with the flip both axes agree, and what they
	// agree on is to move the iris sample ALONG the view ray's tangential direction, away from the viewer. The iris
	// reads as a layer recessed under the cornea, and its deepest part, the pupil, slides toward whoever is looking.
	// This frame is (+u, +v), so the same result is sphereUv + parallax on both axes and no flip. (Subtracting it
	// here, as this port first did, slid the pupil away from the viewer; subtracting with Valve's flip squashed it
	// into a vertical slit, one axis each way.)
	float2 parallax = (tangentView.xy * cornea.b * _ParallaxStrength) / (1.0 - tangentView.z);
	float2 irisUv = sphereUv + parallax;
	// The noise moves half as far the OTHER way - Valve's vSphereUv + parallax * 0.5, in its mirrored frame - which
	// puts it above the surface: "to help define raised cornea layer"
	half   corneaNoise = SAMPLE_TEXTURE2D(_Iris, sampler_Iris, sphereUv - parallax * 0.5).a;

	// Dilate the pupil (:307-312). 0.2 is the iris's uv radius - Valve's constant, not a tuned one.
	irisUv -= 0.5;
	half pupilCentreToBorder = saturate(length(irisUv) / 0.2h);
	irisUv *= lerp(1.0h, pupilCentreToBorder, saturate(_Dilation) * 2.5h - 1.25h);
	irisUv += 0.5;
	half4 iris = SAMPLE_TEXTURE2D(_Iris, sampler_Iris, irisUv);

	// The cornea normal (:236-251). rg - 0.5, not rg * 2 - 1: "This scales the bump to 50% strength". The noise
	// goes into the NORMAL here as well as into the colour below.
	float3 corneaTangentNormal = float3((cornea.rg - 0.5) * _CorneaBumpStrength, 1.0);
	corneaTangentNormal.xy += corneaNoise * 0.1;
	corneaTangentNormal = normalize(corneaTangentNormal);
	float3 corneaNormalWS = normalize(T * corneaTangentNormal.x + B * corneaTangentNormal.y + N * corneaTangentNormal.z);
	float3 corneaReflection = reflect(viewVector, corneaNormalWS);

	// "I'm not normalizing on purpose" (:330)
	float3 irisTangentNormal = corneaTangentNormal;
	irisTangentNormal.xy *= -2.5;

	// The diffuse light takes a BENT normal: "make the eyeball less rounded left to right to help vertex lighting
	// quality" (vs20:152-153). Half of N's component along the iris's horizontal axis is taken out, which does
	// not care which way that axis points.
	float3 bentNormal = normalize(N - 0.5 * dot(N, T) * T);

	float3 viewDirWS = -viewVector;
	float2 positionSS = input.positionCS.xy;
	SourceEyeLight light = (SourceEyeLight)0;
	light.vertexLight = SourceEyeAmbient(input.positionWS, bentNormal, viewDirWS, positionSS);

	AccumulateSourceEyeLight(GetMainLight(input.shadowCoord), bentNormal, irisTangentNormal, corneaReflection,
							 T, B, N, highlightMask, light);

#ifdef _ADDITIONAL_LIGHTS
	// URP's clustered LIGHT_LOOP_BEGIN reads a local called "inputData" by name to find which cluster the pixel
	// is in, so one has to exist even though this pass builds its lighting by hand. It is needed in variants
	// that list _ADDITIONAL_LIGHTS as DISABLED too: Core.hlsl force-defines that keyword whenever the cluster
	// loop is on (Core.hlsl:20), which is how the _CLUSTER_LIGHT_LOOP variant reached this block at all.
	InputData inputData = (InputData)0;
	inputData.positionWS = input.positionWS;
	inputData.normalWS = N;
	inputData.viewDirectionWS = viewDirWS;
	inputData.shadowCoord = input.shadowCoord;
	inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

	uint lightCount = GetAdditionalLightsCount();
#if USE_CLUSTER_LIGHT_LOOP
	// The cluster loop walks the PUNCTUAL lights only. Under Forward+ and Deferred+ every additional directional
	// light sits at the front of the list and is looped on its own, as URP's UniversalFragmentPBR does
	// (Lighting.hlsl) - without this, a directional light that was not the main one never reached the eye.
	[loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
	{
		CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
		AccumulateSourceEyeLight(GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1)), bentNormal,
								 irisTangentNormal, corneaReflection, T, B, N, highlightMask, light);
	}
#endif
	LIGHT_LOOP_BEGIN(lightCount)
		AccumulateSourceEyeLight(GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1)), bentNormal,
								 irisTangentNormal, corneaReflection, T, B, N, highlightMask, light);
	LIGHT_LOOP_END
#endif

	// A little view-dependent iris light from the ambient alone, for scenes with no local lights (:377).
	// g_flAverageAmbient is the ambient cube's mean; the ambient either side of the eye averages to exactly the
	// probes' constant term for L1, which is that mean.
	half averageAmbient = Luminance(0.5 * (SourceEyeAmbient(input.positionWS, N, viewDirWS, positionSS) +
										   SourceEyeAmbient(input.positionWS, -N, viewDirWS, positionSS)));
	light.irisLighting += saturate(dot(irisTangentNormal, -tangentView)) * averageAmbient * highlightMask * 0.5;

	// Ambient occlusion colours the VERTEX light only (:385-388), after the iris caustic is done
	half3 occlusionTexture = SAMPLE_TEXTURE2D(_AmbientOcclTexture, sampler_AmbientOcclTexture, input.uv).rgb;
	light.vertexLight *= lerp(_AmbientOcclColor.rgb, half3(1, 1, 1), occlusionTexture);

	// The combine (:430-446), term for term
	half3 colour = iris.rgb;                               // unlit iris, pupil and sclera
	colour += corneaNoise * 0.1h;                          // "to help define raised cornea layer for close-ups"
	colour *= light.vertexLight + light.irisLighting;      // vertex lighting + the iris caustic
#ifdef _ENVMAP
	colour += _Glossiness * SAMPLE_TEXTURECUBE(_EnvMap, sampler_EnvMap, corneaReflection).rgb * light.vertexLight;
#endif
	colour += light.glints;

	return half4(colour, 1.0h);
}

#endif

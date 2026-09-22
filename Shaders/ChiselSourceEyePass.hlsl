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
// WHAT IS APPROXIMATED, and why it cannot be exact here. Source projects the iris onto the eyeball with
// $IrisU/$IrisV and takes the surface normal from the sphere centre $EyeOrigin - and the client rewrites
// all three every frame from the eye attachment, which is how an eye tracks what it is looking at. An
// imported scene has no such runtime, so the projection is done in OBJECT space instead: the eye is taken
// to be centred on the mesh's origin and looking along z, with x across and y up. A static eye is the
// honest result, not a bug.
//
// WHAT IS STILL WRONG, so nobody reads the paragraph above as a claim that this is finished. The two axes
// and the centre here are a DEFAULT, and Source keeps the real ones per eyeball in the model file:
// mstudioeyeball_t (studio.h:1240) carries org, up, forward, radius and iris_scale, and baseanimating.cpp
// puts them in world space with the eye bone's matrix. Nothing reads them yet. On a material preview, whose
// mesh is centred and one unit across, the default is right. On an actual character it is not: an eyeball
// is a submesh of the whole body, so object space is centred between the character's feet and the
// projection lands far outside [0,1] - every pixel clamps to the edge of the iris texture, which is the
// sclera, and the eye renders as a white blob. Fixing that needs the eyeball structs, not another constant.

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
CBUFFER_END

struct Attributes
{
	float4 positionOS : POSITION;
	float3 normalOS   : NORMAL;
	float2 uv         : TEXCOORD0;
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
	// Source takes the normal from the eyeball's CENTRE rather than the mesh (eye_refract_vs20.fxc:126),
	// which keeps the eye reading as a sphere even where the mesh is coarse.
	float3 normalOS    = (dot(input.positionOS.xyz, input.positionOS.xyz) > 1e-8) ? normalize(input.positionOS.xyz)
																				 : input.normalOS;
	output.normalWS    = TransformObjectToWorldNormal(normalOS);
	output.shadowCoord = TransformWorldToShadowCoord(positions.positionWS);
	return output;
}

half4 SourceEyeFragment(Varyings input) : SV_Target
{
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

	// Iris projection. $IrisU/$IrisV default to [0 1 0 0] and [0 0 1 0]: u from the eye's LEFT axis, v from
	// its UP axis, with the eye facing FORWARD, the axis the projection drops. Those names are the point -
	// the numbers spell them in Source's frame (x forward, y left, z up), and a mesh here is in Unity's,
	// so the three have to be named again rather than copied across. This reads u from x and v from y with
	// the eye facing z. Copying the literal yz is what put the eye on its side in the material preview.
	float radius = max(_EyeballRadius, 1e-4h);
	float2 sphereUv = (input.positionOS.xy / (2.0 * radius)) + 0.5;

	// The cornea texture is a CROPPED version of the iris texture, and Source relates the two by
	// vSphereUv = vCorneaUv * 0.5 + 0.25 (eye_refract_ps2x.fxc:205). The projection above spans the whole
	// eyeball across [0,1], which is Source's SPHERE uv, so the cornea's is that relation inverted. Sampling
	// the cornea at the sphere's uv stretched a texture that covers only the middle of the eye over all of
	// it, dome normal, highlight and all.
	float2 corneaUv = (sphereUv - 0.25) * 2.0;

	// Dilate the pupil, exactly as the original does it (eye_refract_ps2x.fxc:307-312). 0.2 is the iris's
	// uv radius and is Valve's constant, not a tuned one. It moves the IRIS sample alone: the cornea's uv is
	// the projection itself, which does not dilate.
	float2 irisUv = sphereUv - 0.5;
	half pupilCentreToBorder = saturate(length(irisUv) / 0.2h);
	irisUv *= lerp(1.0h, pupilCentreToBorder, saturate(_Dilation) * 2.5h - 1.25h);
	irisUv += 0.5;

	half4 iris = SAMPLE_TEXTURE2D(_Iris, sampler_Iris, irisUv);

	// The cornea holds a 2D normal in RG, the iris's parallax height in B and the highlight mask in A.
	// Valve takes rg - 0.5 rather than the usual rg * 2 - 1 and says why: "Note: This scales the bump to
	// 50% strength" (eye_refract_ps2x.fxc:236-237). The cornea NOISE is not in this texture at all - it is
	// the IRIS texture's alpha (:229), which is what B was standing in for here.
	half4 cornea = SAMPLE_TEXTURE2D(_CorneaTexture, sampler_CorneaTexture, corneaUv);
	half3 corneaTangentNormal = half3((cornea.rg - 0.5h) * _CorneaBumpStrength, 1.0h);
	// Valve offsets this by half the parallax vector; with no parallax that is the sphere uv, which is
	// exactly what the ps_2_0 path does (:222, :228) - the same path _ParallaxStrength is unused on here.
	half  corneaNoise = SAMPLE_TEXTURE2D(_Iris, sampler_Iris, sphereUv).a;
	half  highlightMask = cornea.a;

	half3 normalWS = normalize(input.normalWS);
	// The cornea's bump perturbs the sphere normal. Building a full tangent frame for an eyeball is not
	// worth it - the surface IS a sphere - so the 2D normal is applied across the view plane.
	float3 viewWS = normalize(GetWorldSpaceViewDir(input.positionWS));
	float3 right  = normalize(cross(float3(0, 1, 0), normalWS) + float3(1e-5, 0, 0));
	float3 up     = cross(normalWS, right);
	half3 corneaNormalWS = normalize(normalWS + right * corneaTangentNormal.x + up * corneaTangentNormal.y);

	// Ambient occlusion, sampled with the mesh's own uvs and tinted by $AmbientOcclColor
	// (eye_refract_ps2x.fxc:386-387).
	half3 occlusionTexture = SAMPLE_TEXTURE2D(_AmbientOcclTexture, sampler_AmbientOcclTexture, input.uv).rgb;
	half3 occlusion = lerp(_AmbientOcclColor.rgb, half3(1, 1, 1), occlusionTexture);

	// Diffuse light, plus the glints Source adds per light off the cornea's reflection vector.
	half3 reflectWS = reflect(-viewWS, corneaNormalWS);
	Light mainLight = GetMainLight(input.shadowCoord);
	half3 diffuse = mainLight.color * (mainLight.distanceAttenuation * mainLight.shadowAttenuation)
				  * saturate(dot(normalWS, mainLight.direction));
	half3 glints  = mainLight.color * pow(saturate(dot(reflectWS, mainLight.direction)), 128.0h) * highlightMask;

#ifdef _ADDITIONAL_LIGHTS
	// URP's clustered LIGHT_LOOP_BEGIN reads a local called "inputData" by name to find which cluster
	// the pixel is in, so one has to exist even though this pass builds its lighting by hand. It is
	// needed in variants that list _ADDITIONAL_LIGHTS as DISABLED too: Core.hlsl force-defines that
	// keyword whenever the cluster loop is on (Core.hlsl:20), which is how the _CLUSTER_LIGHT_LOOP
	// variant reached this block at all.
	InputData inputData = (InputData)0;
	inputData.positionWS = input.positionWS;
	inputData.normalWS = normalWS;
	inputData.viewDirectionWS = viewWS;
	inputData.shadowCoord = input.shadowCoord;
	inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

	uint lightCount = GetAdditionalLightsCount();
	LIGHT_LOOP_BEGIN(lightCount)
		Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
		half attenuation = light.distanceAttenuation * light.shadowAttenuation;
		diffuse += light.color * attenuation * saturate(dot(normalWS, light.direction));
		glints  += light.color * attenuation * pow(saturate(dot(reflectWS, light.direction)), 128.0h) * highlightMask;
	LIGHT_LOOP_END
#endif

	half3 ambient = SampleSH(normalWS);
	half3 light = (diffuse + ambient) * occlusion;

	half3 colour = iris.rgb;
	colour += corneaNoise * 0.1h;           // defines the raised cornea layer in close-ups
	colour *= light;
#ifdef _ENVMAP
	half3 reflection = _Glossiness * SAMPLE_TEXTURECUBE(_EnvMap, sampler_EnvMap, reflectWS).rgb;
	colour += reflection * light;
#endif
	colour += glints;

	return half4(colour, 1.0h);
}

#endif

#ifndef CHISEL_SOURCE_GENERIC_INCLUDED
#define CHISEL_SOURCE_GENERIC_INCLUDED

// The surface of Source's *Generic shader family, shared by LightmappedGeneric and VertexLitGeneric.
//
// Source's two shaders differ in how the lighting reaches the surface (baked lightmap vs per-vertex/dynamic), which in
// Unity the renderer decides for itself, so both use this same body and differ only in their name, their properties
// and LightmappedGeneric's second layer. What matters is reproducing the *parameters*, because those are what a VMT
// sets and what URP's Lit shader has no place for:
//
//   $basetexture/$color        base colour
//   $bumpmap                   tangent space normal map
//   $selfillum                 the lit surface replaced by its own colour where the BASE TEXTURE'S ALPHA (or
//                              $selfillummask) says so - lit signage and screens
//   $detail/$detailscale/...   second texture blended over the base at its own tiling
//   $envmap/$envmapmask/...    cubemap reflection, added to the lit surface as Source adds it: from the reflection
//                              probe nearest to the pixel (ChiselSourceProbes.hlsl), or from the cubemap the material names
//   $phong/$phongtint/...      Source's specular highlight, masked, tinted and shaped per pixel, and handed to URP's
//                              lighting as the specular colour and smoothness that give its highlight the same width
//                              and peak (SourcePhongToSpecular)
//   $alphatest/$translucent    the material's blend state + _ALPHATEST_ON
//   $nocull                    _Cull - Source uses it constantly for glass, grates and foliage
//
// Features are shader_feature_local toggles so a material that uses none of them costs nothing.

#include "ChiselSourceGenericInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#ifdef _ENVMAP
	#include "ChiselSourceProbes.hlsl"
#endif

// What a pass hands the surface
struct SourceSurfaceInput
{
	float2 uv;          // the base texture's coordinates, which every texture but the detail texture is read at
	float2 detailUV;
	float3 positionWS;
	half3  normalWS;    // the vertex normal
	half4  tangentWS;   // xyz: the vertex tangent, w: the bitangent's sign
	half3  viewDirWS;   // towards the camera, normalized
	half4  vertexColor;
};

// What the surface hands URP's lighting
struct SourceSurface
{
	half3 albedo;       // the part of the base colour that light falls on
	half  alpha;
	half3 normalTS;
	half3 normalWS;
	half3 specular;
	half  smoothness;
	half3 emission;     // what the surface adds by itself: self-illumination, envmap reflection and rim light
};

half3 SourceNormalWS(half3 normalTS, SourceSurfaceInput input)
{
#ifdef _NORMALMAP
	float3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
	return NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent, input.normalWS)));
#else
	return NormalizeNormalPerPixel(input.normalWS);
#endif
}

#ifdef _PHONG
	// Source: Fresnel(N,V) = (1 - dot(N,V))^2, then remapped through the three ranges, linearly between [x,y] over the
	// first half and [y,z] over the second.
	half SourcePhongFresnel(half3 normal, half3 viewDir, float3 ranges)
	{
		half f = 1.0h - saturate(dot(normal, viewDir));
		f = f * f;
		return (f > 0.5h) ? lerp(ranges.y, ranges.z, (2.0h * f) - 1.0h)
						  : lerp(ranges.x, ranges.y, 2.0h * f);
	}

	// Source adds, per light, scale * (R.L)^n * N.L * light (SpecularAndRimTerms in common_vertexlitgeneric_dx9.h), where
	// scale is the tint, mask, $phongboost and fresnel. URP's lobe is instead set by a specular colour and a smoothness, so
	// this picks the two that make it the same highlight:
	//  - width: a Phong lobe (R.L)^n is a Blinn-Phong lobe (N.H)^4n, and Blinn-Phong m corresponds to roughness
	//    a = sqrt(2 / (m + 2)) (Walter et al. 2007), so a^2 = 1 / (2n + 1). URP's roughness is that a, which is
	//    (1 - smoothness)^2;
	//  - peak: at N.H = L.H = 1, URP's DirectBRDFSpecular is 1 / (a^2 (4a + 2)), and Source's is 1, so
	//    specular = scale * a^2 (4a + 2).
	// What that cannot carry over: URP's visibility term still brightens the lobe toward grazing angles, up to 10 times.
	void SourcePhongToSpecular(half3 scale, half exponent, inout SourceSurface surface)
	{
		half roughness2 = 1.0h / (2.0h * max(exponent, 0.0h) + 1.0h);
		half roughness  = sqrt(roughness2);
		surface.smoothness = 1.0h - sqrt(roughness);
		// The G-buffer stores the specular colour in 0..1, so the forward path is held to the same
		surface.specular   = saturate(scale * (roughness2 * (4.0h * roughness + 2.0h)));
	}
#endif

void SourceSurfaceFunction(SourceSurfaceInput input, out SourceSurface surface)
{
	SourceBase base = SourceSampleBase(input.uv, input.vertexColor);

	surface.alpha      = SourceAlphaClip(SourceOpacity(base, input.vertexColor));
	SourceAlbedo(base, input.uv, input.detailUV, input.vertexColor, surface.albedo, surface.emission);
	surface.normalTS   = SourceNormalTS(input.uv, base);
	surface.normalWS   = SourceNormalWS(surface.normalTS, input);
	surface.specular   = _SpecColorTint.rgb * _SpecularScale;
	surface.smoothness = _Smoothness;

	// Everything in the highlight that can be decided per pixel: its mask, exponent, tint and fresnel. Source does NOT
	// mask the highlight with $envmapmask - that texture gates the cubemap reflection only.
#ifdef _PHONG
	#ifdef _PHONGMASK
		half phongMask = SAMPLE_TEXTURE2D(_PhongMask, sampler_BaseMap, input.uv).r;
	#else
		half phongMask = lerp(1.0h, base.color.a, _PhongMaskFromBaseAlpha);
	#endif

	// White is what Source samples when a material names no exponent texture, so it is what the two expressions below
	// fall back to: exponent 1 + 149, and a fully albedo-coloured tint.
	half4 exponentMap = half4(1, 1, 1, 1);
	#ifdef _PHONGEXPONENTMAP
		exponentMap = SAMPLE_TEXTURE2D(_PhongExponentMap, sampler_BaseMap, input.uv);
	#endif
	half exponent = (_PhongExponentFactor > 0) ? (1.0h + _PhongExponentFactor * exponentMap.r)
				  : (_PhongExponent >= 0)      ? _PhongExponent
											   : (1.0h + 149.0h * exponentMap.r);
	// Source tints with the BASE texture, before $color/$color2 modulates it (skin_ps20b.fxc computes vSpecularTint from
	// baseColor.rgb, and modulates albedo only afterwards).
	half3 tint = lerp(_SpecColorTint.rgb, lerp(half3(1, 1, 1), base.color.rgb, exponentMap.g), _PhongAlbedoTint);

	half fresnel = SourcePhongFresnel(surface.normalWS, input.viewDirWS, _PhongFresnelRanges.xyz);
	#ifdef _PHONGWARP
		// With a warp texture the fresnel is the lookup's second coordinate rather than a multiplier: skin_ps20b.fxc only
		// folds it into the mask "if we didn't already apply Fresnel to specular warp". The lookup's first coordinate is
		// the lobe, which only a light knows, so the table is read where the highlight is brightest - its last column -
		// and the colour found there tints the whole lobe. The fresnel is NOT clamped (ranges like [.4 .6 1.6] run past
		// 1) and the warp textures set no CLAMP flag, so it wraps around the table, as it does in the game.
		half3 highlight = SAMPLE_TEXTURE2D(_PhongWarp, sampler_PhongWarp, float2(1.0 - 0.5 * _PhongWarp_TexelSize.x, fresnel)).rgb;
	#else
		half3 highlight = fresnel;
	#endif
	SourcePhongToSpecular(tint * highlight * (phongMask * _PhongBoost), exponent, surface);

	// Source's rim light, the part a surface draws by itself (skin_ps20b.fxc): the ambient light seen along the view ray,
	// where the surface faces up (world Z in Source, Y here) and is seen edge-on, tinted like the highlight. The other part
	// is a lobe per light that the game folds into the highlight with a max() over all lights, which a G-buffer has no
	// room for.
	// $rimlightexponent, which is how wide the rim is. It was hardcoded to 4 - Source's default when the key is
	// absent - by squaring twice, so a material asking for anything else got a rim twice as broad as it wanted, and
	// since the term is ADDITIVE emission of the ambient probe, a too-broad rim washes the albedo out across whole
	// faces rather than edging them. agave.vmt asks for 8 and its upward-facing leaves turned pale grey.
	half rimEdge = 1.0h - saturate(dot(surface.normalWS, input.viewDirWS));
	rimEdge = pow(max(rimEdge, HALF_MIN), max(_RimLightExponent, 1.0h));
	half rimWeight = saturate(lerp(1.0h, exponentMap.a, _RimMask) * rimEdge * surface.normalWS.y);
	surface.emission += SampleSH(input.viewDirWS) * (_RimLight * _RimLightBoost * rimWeight) * tint;
#endif

#ifdef _ENVMAP
	// Source adds the reflection to the lit surface (lightmappedgeneric_ps2_3_x.h,
	// vertexlit_and_unlit_generic(_bump)_ps2x.fxc, skin_ps20b.fxc): the cubemap along the reflected view ray, masked,
	// tinted, squared by $envmapcontrast, desaturated by $envmapsaturation and shaped by a Fresnel term. The importer sets
	// up each shader's own subset of that (MaterialImporter.EnvMapOf).
	float3 reflection = reflect(-input.viewDirWS, surface.normalWS);
	half3 environment = (_EnvMapFromProbes > 0.5h) ? SourceProbeReflection(input.positionWS, reflection)
												   : SAMPLE_TEXTURECUBE(_EnvMap, sampler_SourceProbes, reflection).rgb;
	half normalAlpha = SAMPLE_TEXTURE2D(_PhongMask, sampler_BaseMap, input.uv).r;
	half envMask = (1.0h - dot(_EnvMapMaskWeights, half4(1, 1, 1, 1))) + _EnvMapMaskWeights.x * normalAlpha
				 + _EnvMapMaskWeights.y * base.layerAlpha + _EnvMapMaskWeights.z * (1.0h - base.layerAlpha);
	#ifdef _PHONG
		envMask += _EnvMapMaskWeights.w * phongMask;
	#endif
	envMask = lerp(envMask, 1.0h - envMask, _EnvMapInvertMask);
	half3 reflected = environment * envMask
					* lerp(half3(1, 1, 1), SAMPLE_TEXTURE2D(_EnvMapMask, sampler_BaseMap, input.uv).rgb, _EnvMapMaskTexture);
	reflected *= _EnvMapTint.rgb;
	reflected = lerp(reflected, reflected * reflected, _EnvMapContrast.rgb);
	reflected = lerp(dot(reflected, half3(0.299h, 0.587h, 0.114h)).xxx, reflected, _EnvMapSaturation.rgb);
	half facing = 1.0h - saturate(dot(surface.normalWS, input.viewDirWS));
	half envFresnel = pow(facing, 5.0h) * (1.0h - _FresnelReflection) + _FresnelReflection;
	#ifdef _PHONG
		envFresnel *= lerp(1.0h, fresnel, _EnvMapPhongFresnel);
	#endif
	surface.emission += reflected * envFresnel;
#endif
}

// Source adds its highlight to the lit surface and takes nothing out of the diffuse for it, where URP's
// InitializeBRDFData would (albedo * (1 - reflectivity)). Nor does a Source surface reflect the reflection probes or the
// sky through its specular inputs: it reflects only through $envmap, which the surface function adds itself, so the
// passes never call URP's GlobalIllumination.
void SourceBRDFData(SourceSurface surface, out BRDFData brdfData)
{
	half reflectivity = ReflectivitySpecular(surface.specular);
	half alpha = surface.alpha;
	InitializeBRDFDataDirect(surface.albedo, surface.albedo, surface.specular, reflectivity, 1.0h - reflectivity, surface.smoothness, alpha, brdfData);
}

// The light a Source surface gets from the lightmap or the light probes
half3 SourceGlobalIllumination(BRDFData brdfData, half3 bakedGI, half occlusion)
{
	// URP's "ambient occlusion only" debug view shows white under the occlusion
	half3 color = IsOnlyAOLightingFeatureEnabled() ? half3(1, 1, 1) : bakedGI * brdfData.diffuse;
	return color * occlusion;
}

#endif // CHISEL_SOURCE_GENERIC_INCLUDED

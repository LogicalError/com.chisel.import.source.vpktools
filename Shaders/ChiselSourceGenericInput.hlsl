#ifndef CHISEL_SOURCE_GENERIC_INPUT_INCLUDED
#define CHISEL_SOURCE_GENERIC_INPUT_INCLUDED

// The material of Source's *Generic shaders (ChiselSourceGeneric.hlsl), and the parts of their surface that every pass
// needs: the base colour, opacity and normal. Everything in UnityPerMaterial is declared by both shaders' passes, whatever
// their keywords, which is what URP's SRP batcher needs, and every variable in it is a property of both shaders, which
// is what the GPU Resident Drawer needs: each declares the other's (the second layer and vertex colour, the rim light)
// hidden, and leaves them off.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"

CBUFFER_START(UnityPerMaterial)
	float4 _BaseMap_ST;
	float4 _DetailTex_ST;
	float4 _PhongWarp_TexelSize;
	half4  _Color;
	half4  _SpecColorTint;
	half4  _EnvMapTint;
	half4  _EnvMapContrast;
	half4  _EnvMapSaturation;
	half4  _EnvMapMaskWeights;
	half4  _SelfIllumTint;
	float4 _PhongFresnelRanges;
	float4 _BlendMaskTransformU;
	float4 _BlendMaskTransformV;
	half   _Cutoff;
	half   _Smoothness;
	half   _SpecularScale;
	half   _BumpScale;
	half   _DetailBlendFactor;
	half   _DetailBlendMode;
	half   _VertexColor;
	half   _VertexAlpha;
	half   _BlendBase2;
	half   _BlendBump2;
	half   _BlendModulate;
	half   _MaskedBlending;
	half   _EnvMapFromProbes;
	half   _FresnelReflection;
	half   _EnvMapPhongFresnel;
	half   _EnvMapInvertMask;
	half   _EnvMapMaskTexture;
	half   _PhongBoost;
	half   _PhongExponent;
	half   _PhongExponentFactor;
	half   _PhongAlbedoTint;
	half   _PhongMaskFromBaseAlpha;
	half   _RimLight;
	half   _RimLightBoost;
	half   _RimLightExponent;
	half   _RimMask;
	half   _SelfIllumFromBaseAlpha;
	// What URP's rendering debugger shows of the mip streaming
	UNITY_TEXTURE_STREAMING_DEBUG_VARS;
CBUFFER_END

// What the material's parameters mean:
//  _VertexColor             1: the vertex colour multiplies the base colour ($vertexcolor, LightmappedGeneric only)
//  _VertexAlpha             1: the vertex alpha multiplies opacity
//  _BlendBase2/_BlendBump2  LightmappedGeneric's second layer ($basetexture2, $bumpmap2), blended in by the vertex ALPHA -
//                           which on a displacement is what Hammer's alpha painting wrote - and sharpened by
//                           $blendmodulatetexture when _BlendModulate is 1
//  _MaskedBlending          $maskedblending: blend by the modulate texture's green channel alone (0.5 without that
//                           texture), not by the vertex alpha
//  _BlendMaskTransformU/V   $blendmasktransform as the two rows Source's vertex shader takes: u' = dot(uv, row.xy) + row.w
//  _EnvMapFromProbes        1: $envmap is "env_cubemap", the probe nearest to the pixel; 0: the cubemap in _EnvMap
//  _FresnelReflection       LightmappedGeneric's reflection head-on, $fresnelreflection; 1 is no falloff, which every
//                           other shader has
//  _EnvMapPhongFresnel      the skin shader's $envmapfresnel: how much $phongfresnelranges shapes the reflection
//  _EnvMapMaskWeights       what masks the reflection, one weight at most: x the normal map's alpha, y the base alpha,
//                           z one minus the base alpha ($basealphaenvmapmask), w the phong mask. With none, nothing does.
//  _EnvMapMaskTexture       1: $envmapmask multiplies it too
//  _PhongExponent           negative: the exponent comes from _PhongExponentMap's red channel instead. Source's own
//                           constant defaults to -1, and it binds a WHITE texture when a material names no exponent
//                           texture, so "$phong with no $phongexponent" is 1 + 149 = 150 - a tight hotspot, not a sheen
//  _PhongExponentFactor     $phongexponentfactor replaces that 149 when it is non-zero
//  _PhongAlbedoTint         $phongalbedotint: the highlight takes the surface's own colour, mixed in by the exponent
//                           map's green channel
//  _PhongFresnelRanges      [facing, middle, grazing] - Source interpolates between these over the fresnel term
//  _PhongMaskFromBaseAlpha  1: the phong mask is the BASE texture's alpha, which is what Source reads when the material
//                           says so ($basemapalphaphongmask) or when it has no normal map at all
//  _RimLight/_RimLightBoost $rimlight (VertexLitGeneric's skin shader only) and its boost; _RimMask 1 masks it by the
//                           exponent map's alpha
//  _SelfIllumFromBaseAlpha  1: the self-illum mask is the base texture's alpha (Source's default), 0: $selfillummask

// A pixel shader has 16 samplers, and URP's lightmaps, shadows, cookies and occlusion take several of them. So the
// textures read at the base texture's coordinates share its sampler, which is what a material's textures share in Source
// too: the same wrap and filtering. Unity binds sampler_X by texture X, which a program using it must read as well: the
// depth-normals pass reads the normal maps without the base texture, so they share a sampler of their own. The detail
// texture, the blend modulation and the phong warp table are read at coordinates of their own and keep theirs.
TEXTURE2D(_BaseMap);                SAMPLER(sampler_BaseMap);
TEXTURE2D(_BumpMap);                SAMPLER(sampler_BumpMap);
TEXTURE2D(_BaseTexture2);
TEXTURE2D(_BumpMap2);
TEXTURE2D(_BlendModulateTexture);   SAMPLER(sampler_BlendModulateTexture);
TEXTURE2D(_DetailTex);              SAMPLER(sampler_DetailTex);
TEXTURE2D(_EnvMapMask);
// The normal map's alpha, written beside it by the importer because Unity re-encodes normal maps and that alpha does not
// survive. Source's usual phong mask (skin_ps20b.fxc: fSpecMask = lerp(normalTexel.a, baseColor.a,
// $basemapalphaphongmask)), and one of the envmap's masks.
TEXTURE2D(_PhongMask);
// $phongexponenttexture: red is the exponent, green how much of the albedo tints the highlight, alpha the rim mask
TEXTURE2D(_PhongExponentMap);
// $phongwarptexture: a colour table indexed by { lobe, fresnel }, which is how Source makes a surface iridescent - the
// highlight shifts hue with the viewing angle instead of scaling
TEXTURE2D(_PhongWarp);              SAMPLER(sampler_PhongWarp);
TEXTURE2D(_SelfIllumMask);
TEXTURECUBE(_EnvMap);

// The base colour, with LightmappedGeneric's second layer blended in
struct SourceBase
{
	half4 color;       // the blended colour, and the FIRST layer's alpha, which Source goes on reading as opacity and
	                   // as the self-illum mask
	half  layerAlpha;  // the two layers' alphas blended, which the envmap mask reads (blendedAlpha)
	half  layerBlend;  // how much of the second layer there is
};

SourceBase SourceSampleBase(float2 uv, half4 vertexColor)
{
	SourceBase result;
	result.color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
#ifdef _VERTEXBLEND
	// The weight the second layer comes in by is the vertex alpha (lightmappedgeneric_vs20.fxc). With a
	// $blendmodulatetexture its green channel moves the transition and its red channel widens it - except on a
	// self-illuminated material, where Source compiles that step out (lightmappedgeneric_ps2_3_x.h). The default
	// modulation texture is white, which keeps smoothstep's two edges apart. $maskedblending replaces the vertex alpha
	// with 0.5, and the modulate texture's green channel then decides the blend outright (MASKEDBLENDING).
	half layerBlend = lerp(vertexColor.a, 0.5h, _MaskedBlending);
	#ifndef _SELFILLUM
		float2 maskCoord = float2(dot(uv, _BlendMaskTransformU.xy) + _BlendMaskTransformU.w,
								  dot(uv, _BlendMaskTransformV.xy) + _BlendMaskTransformV.w);
		half4 modulation = SAMPLE_TEXTURE2D(_BlendModulateTexture, sampler_BlendModulateTexture, maskCoord);
		half  sharpened  = smoothstep(saturate(modulation.g - modulation.r), saturate(modulation.g + modulation.r), layerBlend);
		layerBlend = lerp(layerBlend, lerp(sharpened, modulation.g, _MaskedBlending), _BlendModulate);
	#endif
	half4 color2 = SAMPLE_TEXTURE2D(_BaseTexture2, sampler_BaseMap, uv);
	result.layerBlend = layerBlend;
	result.layerAlpha = lerp(result.color.a, color2.a, layerBlend * _BlendBase2);
	result.color.rgb  = lerp(result.color.rgb, color2.rgb, layerBlend * _BlendBase2);
#else
	result.layerBlend = 0;
	result.layerAlpha = result.color.a;
#endif
	return result;
}

// Opacity, before the alpha test: Source tests the alpha it outputs, vertex alpha included
half SourceOpacity(SourceBase base, half4 vertexColor)
{
	half alpha = base.color.a * _Color.a;
#ifdef _ENVMAP
	// Where the base alpha masks the reflection ($basealphaenvmapmask) it is not opacity
	// (lightmappedgeneric_ps2_3_x.h, vertexlit_and_unlit_generic_ps2x.fxc)
	alpha = lerp(alpha, _Color.a, step(0.5h, _EnvMapMaskWeights.z));
#endif
#ifdef _SELFILLUM
	// Nor on a self-illuminated material, whether its mask is the base alpha or $selfillummask - taking it as opacity is
	// what makes these surfaces come out see-through or unlit
	alpha = _Color.a;
#endif
	return alpha * lerp(1.0h, vertexColor.a, _VertexAlpha);
}

half SourceAlphaClip(half alpha)
{
#ifdef _ALPHATEST_ON
	clip(alpha - _Cutoff);
#endif
	return alpha;
}

// The base colour with the detail texture and vertex colour applied, split into the part light falls on and the light
// the surface gives off by itself
void SourceAlbedo(SourceBase base, float2 uv, float2 detailUV, half4 vertexColor, out half3 lit, out half3 selfIllumination)
{
	half3 albedo = base.color.rgb * _Color.rgb;

#ifdef _DETAIL
	half4 detail = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, detailUV);
	// Source's $detailblendmode 0 (DecalModulate) doubles the product so mid-grey leaves the base unchanged; mode 1
	// (Additive) adds. Anything else falls back to modulate, which is the overwhelmingly common case in the shipped
	// materials.
	half3 modulated = albedo * detail.rgb * 2;
	half3 additive  = albedo + detail.rgb;
	half3 blended   = lerp(modulated, additive, step(0.5h, _DetailBlendMode));
	albedo = lerp(albedo, blended, _DetailBlendFactor);
#endif

	// After the detail texture, as Source does it. LightmappedGeneric - the only shader here that honours $vertexcolor -
	// takes the colour as it is, without the gamma decode UnlitGeneric applies.
	albedo *= lerp(half3(1, 1, 1), vertexColor.rgb, _VertexColor);

#ifdef _SELFILLUM
	// Self-illumination replaces the lit surface by the mask rather than adding to it
	// (vertexlit_and_unlit_generic_ps2x.fxc, lightmappedgeneric_ps2_3_x.h):
	//   diffuseComponent = lerp(diffuseComponent, selfIllumTint * albedo, mask)
	// Specular and the envmap are added afterwards.
	half3 illumMask  = lerp(SAMPLE_TEXTURE2D(_SelfIllumMask, sampler_BaseMap, uv).rgb, base.color.aaa, _SelfIllumFromBaseAlpha);
	lit              = albedo * (1.0h - illumMask);
	selfIllumination = albedo * _SelfIllumTint.rgb * illumMask;
#else
	lit              = albedo;
	selfIllumination = 0;
#endif
}

// The tangent space normal
half3 SourceNormalTS(float2 uv, SourceBase base)
{
#ifdef _NORMALMAP
	half3 normal = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), _BumpScale);
	#ifdef _VERTEXBLEND
		// Source only blends a second normal map into a first one
		half3 normal2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap2, sampler_BumpMap, uv), _BumpScale);
		normal = lerp(normal, normal2, base.layerBlend * _BlendBump2);
	#endif
	return normalize(normal);
#else
	return half3(0, 0, 1);
#endif
}

#endif // CHISEL_SOURCE_GENERIC_INPUT_INCLUDED

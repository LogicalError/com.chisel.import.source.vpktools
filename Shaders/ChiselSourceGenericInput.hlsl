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
	half   _NoFog;
	half   _OpaqueAlpha;
	half   _SelfIllumFromBaseAlpha;
	// What URP's rendering debugger shows of the mip streaming
	UNITY_TEXTURE_STREAMING_DEBUG_VARS;
CBUFFER_END

// Under a BatchRendererGroup - the GPU Resident Drawer, or Entities Graphics - UnityPerMaterial is not per
// draw, so a whole batch would shade with one material's values unless these come from the instance data. A
// pass with no DOTS_INSTANCING_ON variant is refused outright, at draw submission rather than at compile,
// which is why the symptom is silence and nothing drawn.
//
// The cached statics are URP's own shape (LitInput.hlsl): reading each property once instead of at every use
// stops the compiler regenerating the load, which URP measured at about 10% of GPU time on a Quest 2. The _ST
// and _TexelSize members are left out for the same reason URP leaves _BaseMap_ST out - a texture's tiling is
// not a per-instance value, and a batch only mixes materials that share the texture.
//
// NOTE: hlslc.py cannot check these variants. URP's own Lit.shader fails in it identically, on
// unity_SpecCube0_HDR, because the offline compiler does not provide Unity's instancing constants. Verify
// DOTS variants with Shader_GetErrors in the editor.
#ifdef UNITY_DOTS_INSTANCING_ENABLED
UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
	UNITY_DOTS_INSTANCED_PROP(float4, _Color)
	UNITY_DOTS_INSTANCED_PROP(float4, _SpecColorTint)
	UNITY_DOTS_INSTANCED_PROP(float4, _EnvMapTint)
	UNITY_DOTS_INSTANCED_PROP(float4, _EnvMapContrast)
	UNITY_DOTS_INSTANCED_PROP(float4, _EnvMapSaturation)
	UNITY_DOTS_INSTANCED_PROP(float4, _EnvMapMaskWeights)
	UNITY_DOTS_INSTANCED_PROP(float4, _SelfIllumTint)
	UNITY_DOTS_INSTANCED_PROP(float4, _PhongFresnelRanges)
	UNITY_DOTS_INSTANCED_PROP(float4, _BlendMaskTransformU)
	UNITY_DOTS_INSTANCED_PROP(float4, _BlendMaskTransformV)
	UNITY_DOTS_INSTANCED_PROP(float , _Cutoff)
	UNITY_DOTS_INSTANCED_PROP(float , _Smoothness)
	UNITY_DOTS_INSTANCED_PROP(float , _SpecularScale)
	UNITY_DOTS_INSTANCED_PROP(float , _BumpScale)
	UNITY_DOTS_INSTANCED_PROP(float , _DetailBlendFactor)
	UNITY_DOTS_INSTANCED_PROP(float , _DetailBlendMode)
	UNITY_DOTS_INSTANCED_PROP(float , _VertexColor)
	UNITY_DOTS_INSTANCED_PROP(float , _VertexAlpha)
	UNITY_DOTS_INSTANCED_PROP(float , _BlendBase2)
	UNITY_DOTS_INSTANCED_PROP(float , _BlendBump2)
	UNITY_DOTS_INSTANCED_PROP(float , _BlendModulate)
	UNITY_DOTS_INSTANCED_PROP(float , _MaskedBlending)
	UNITY_DOTS_INSTANCED_PROP(float , _EnvMapFromProbes)
	UNITY_DOTS_INSTANCED_PROP(float , _FresnelReflection)
	UNITY_DOTS_INSTANCED_PROP(float , _EnvMapPhongFresnel)
	UNITY_DOTS_INSTANCED_PROP(float , _EnvMapInvertMask)
	UNITY_DOTS_INSTANCED_PROP(float , _EnvMapMaskTexture)
	UNITY_DOTS_INSTANCED_PROP(float , _PhongBoost)
	UNITY_DOTS_INSTANCED_PROP(float , _PhongExponent)
	UNITY_DOTS_INSTANCED_PROP(float , _PhongExponentFactor)
	UNITY_DOTS_INSTANCED_PROP(float , _PhongAlbedoTint)
	UNITY_DOTS_INSTANCED_PROP(float , _PhongMaskFromBaseAlpha)
	UNITY_DOTS_INSTANCED_PROP(float , _RimLight)
	UNITY_DOTS_INSTANCED_PROP(float , _RimLightBoost)
	UNITY_DOTS_INSTANCED_PROP(float , _RimLightExponent)
	UNITY_DOTS_INSTANCED_PROP(float , _RimMask)
	UNITY_DOTS_INSTANCED_PROP(float , _NoFog)
	UNITY_DOTS_INSTANCED_PROP(float , _OpaqueAlpha)
	UNITY_DOTS_INSTANCED_PROP(float , _SelfIllumFromBaseAlpha)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

static float4 unity_DOTS_Sampled_Color;
static float4 unity_DOTS_Sampled_SpecColorTint;
static float4 unity_DOTS_Sampled_EnvMapTint;
static float4 unity_DOTS_Sampled_EnvMapContrast;
static float4 unity_DOTS_Sampled_EnvMapSaturation;
static float4 unity_DOTS_Sampled_EnvMapMaskWeights;
static float4 unity_DOTS_Sampled_SelfIllumTint;
static float4 unity_DOTS_Sampled_PhongFresnelRanges;
static float4 unity_DOTS_Sampled_BlendMaskTransformU;
static float4 unity_DOTS_Sampled_BlendMaskTransformV;
static float  unity_DOTS_Sampled_Cutoff;
static float  unity_DOTS_Sampled_Smoothness;
static float  unity_DOTS_Sampled_SpecularScale;
static float  unity_DOTS_Sampled_BumpScale;
static float  unity_DOTS_Sampled_DetailBlendFactor;
static float  unity_DOTS_Sampled_DetailBlendMode;
static float  unity_DOTS_Sampled_VertexColor;
static float  unity_DOTS_Sampled_VertexAlpha;
static float  unity_DOTS_Sampled_BlendBase2;
static float  unity_DOTS_Sampled_BlendBump2;
static float  unity_DOTS_Sampled_BlendModulate;
static float  unity_DOTS_Sampled_MaskedBlending;
static float  unity_DOTS_Sampled_EnvMapFromProbes;
static float  unity_DOTS_Sampled_FresnelReflection;
static float  unity_DOTS_Sampled_EnvMapPhongFresnel;
static float  unity_DOTS_Sampled_EnvMapInvertMask;
static float  unity_DOTS_Sampled_EnvMapMaskTexture;
static float  unity_DOTS_Sampled_PhongBoost;
static float  unity_DOTS_Sampled_PhongExponent;
static float  unity_DOTS_Sampled_PhongExponentFactor;
static float  unity_DOTS_Sampled_PhongAlbedoTint;
static float  unity_DOTS_Sampled_PhongMaskFromBaseAlpha;
static float  unity_DOTS_Sampled_RimLight;
static float  unity_DOTS_Sampled_RimLightBoost;
static float  unity_DOTS_Sampled_RimLightExponent;
static float  unity_DOTS_Sampled_RimMask;
static float  unity_DOTS_Sampled_NoFog;
static float  unity_DOTS_Sampled_OpaqueAlpha;
static float  unity_DOTS_Sampled_SelfIllumFromBaseAlpha;

void SetupDOTSSourceGenericMaterialPropertyCaches()
{
	unity_DOTS_Sampled_Color                  = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _Color);
	unity_DOTS_Sampled_SpecColorTint          = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _SpecColorTint);
	unity_DOTS_Sampled_EnvMapTint             = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _EnvMapTint);
	unity_DOTS_Sampled_EnvMapContrast         = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _EnvMapContrast);
	unity_DOTS_Sampled_EnvMapSaturation       = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _EnvMapSaturation);
	unity_DOTS_Sampled_EnvMapMaskWeights      = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _EnvMapMaskWeights);
	unity_DOTS_Sampled_SelfIllumTint          = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _SelfIllumTint);
	unity_DOTS_Sampled_PhongFresnelRanges     = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _PhongFresnelRanges);
	unity_DOTS_Sampled_BlendMaskTransformU    = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BlendMaskTransformU);
	unity_DOTS_Sampled_BlendMaskTransformV    = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BlendMaskTransformV);
	unity_DOTS_Sampled_Cutoff                 = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Cutoff);
	unity_DOTS_Sampled_Smoothness             = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Smoothness);
	unity_DOTS_Sampled_SpecularScale          = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _SpecularScale);
	unity_DOTS_Sampled_BumpScale              = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _BumpScale);
	unity_DOTS_Sampled_DetailBlendFactor      = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _DetailBlendFactor);
	unity_DOTS_Sampled_DetailBlendMode        = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _DetailBlendMode);
	unity_DOTS_Sampled_VertexColor            = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _VertexColor);
	unity_DOTS_Sampled_VertexAlpha            = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _VertexAlpha);
	unity_DOTS_Sampled_BlendBase2             = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _BlendBase2);
	unity_DOTS_Sampled_BlendBump2             = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _BlendBump2);
	unity_DOTS_Sampled_BlendModulate          = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _BlendModulate);
	unity_DOTS_Sampled_MaskedBlending         = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _MaskedBlending);
	unity_DOTS_Sampled_EnvMapFromProbes       = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _EnvMapFromProbes);
	unity_DOTS_Sampled_FresnelReflection      = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _FresnelReflection);
	unity_DOTS_Sampled_EnvMapPhongFresnel     = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _EnvMapPhongFresnel);
	unity_DOTS_Sampled_EnvMapInvertMask       = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _EnvMapInvertMask);
	unity_DOTS_Sampled_EnvMapMaskTexture      = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _EnvMapMaskTexture);
	unity_DOTS_Sampled_PhongBoost             = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _PhongBoost);
	unity_DOTS_Sampled_PhongExponent          = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _PhongExponent);
	unity_DOTS_Sampled_PhongExponentFactor    = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _PhongExponentFactor);
	unity_DOTS_Sampled_PhongAlbedoTint        = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _PhongAlbedoTint);
	unity_DOTS_Sampled_PhongMaskFromBaseAlpha = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _PhongMaskFromBaseAlpha);
	unity_DOTS_Sampled_RimLight               = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _RimLight);
	unity_DOTS_Sampled_RimLightBoost          = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _RimLightBoost);
	unity_DOTS_Sampled_RimLightExponent       = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _RimLightExponent);
	unity_DOTS_Sampled_RimMask                = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _RimMask);
	unity_DOTS_Sampled_NoFog                  = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _NoFog);
	unity_DOTS_Sampled_OpaqueAlpha            = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _OpaqueAlpha);
	unity_DOTS_Sampled_SelfIllumFromBaseAlpha = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _SelfIllumFromBaseAlpha);
}

#undef UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES
#define UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES() SetupDOTSSourceGenericMaterialPropertyCaches()

#define _Color                  unity_DOTS_Sampled_Color
#define _SpecColorTint          unity_DOTS_Sampled_SpecColorTint
#define _EnvMapTint             unity_DOTS_Sampled_EnvMapTint
#define _EnvMapContrast         unity_DOTS_Sampled_EnvMapContrast
#define _EnvMapSaturation       unity_DOTS_Sampled_EnvMapSaturation
#define _EnvMapMaskWeights      unity_DOTS_Sampled_EnvMapMaskWeights
#define _SelfIllumTint          unity_DOTS_Sampled_SelfIllumTint
#define _PhongFresnelRanges     unity_DOTS_Sampled_PhongFresnelRanges
#define _BlendMaskTransformU    unity_DOTS_Sampled_BlendMaskTransformU
#define _BlendMaskTransformV    unity_DOTS_Sampled_BlendMaskTransformV
#define _Cutoff                 unity_DOTS_Sampled_Cutoff
#define _Smoothness             unity_DOTS_Sampled_Smoothness
#define _SpecularScale          unity_DOTS_Sampled_SpecularScale
#define _BumpScale              unity_DOTS_Sampled_BumpScale
#define _DetailBlendFactor      unity_DOTS_Sampled_DetailBlendFactor
#define _DetailBlendMode        unity_DOTS_Sampled_DetailBlendMode
#define _VertexColor            unity_DOTS_Sampled_VertexColor
#define _VertexAlpha            unity_DOTS_Sampled_VertexAlpha
#define _BlendBase2             unity_DOTS_Sampled_BlendBase2
#define _BlendBump2             unity_DOTS_Sampled_BlendBump2
#define _BlendModulate          unity_DOTS_Sampled_BlendModulate
#define _MaskedBlending         unity_DOTS_Sampled_MaskedBlending
#define _EnvMapFromProbes       unity_DOTS_Sampled_EnvMapFromProbes
#define _FresnelReflection      unity_DOTS_Sampled_FresnelReflection
#define _EnvMapPhongFresnel     unity_DOTS_Sampled_EnvMapPhongFresnel
#define _EnvMapInvertMask       unity_DOTS_Sampled_EnvMapInvertMask
#define _EnvMapMaskTexture      unity_DOTS_Sampled_EnvMapMaskTexture
#define _PhongBoost             unity_DOTS_Sampled_PhongBoost
#define _PhongExponent          unity_DOTS_Sampled_PhongExponent
#define _PhongExponentFactor    unity_DOTS_Sampled_PhongExponentFactor
#define _PhongAlbedoTint        unity_DOTS_Sampled_PhongAlbedoTint
#define _PhongMaskFromBaseAlpha unity_DOTS_Sampled_PhongMaskFromBaseAlpha
#define _RimLight               unity_DOTS_Sampled_RimLight
#define _RimLightBoost          unity_DOTS_Sampled_RimLightBoost
#define _RimLightExponent       unity_DOTS_Sampled_RimLightExponent
#define _RimMask                unity_DOTS_Sampled_RimMask
#define _NoFog                  unity_DOTS_Sampled_NoFog
#define _OpaqueAlpha            unity_DOTS_Sampled_OpaqueAlpha
#define _SelfIllumFromBaseAlpha unity_DOTS_Sampled_SelfIllumFromBaseAlpha
#endif


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

// The alpha test, and the alpha the fragment must then EXPORT. URP's AlphaClip clips exactly as the line below it used
// to, but when alpha-to-coverage is available - an opaque pass with MSAA, which URP signals through
// _AlphaToMaskAvailable - it returns a coverage alpha instead: the cutoff remapped to the half-covered sample, one
// screen-space derivative wide (SharpenAlphaStrict). Exporting that is the whole of the effect. Handing the hardware
// the raw alpha would not antialias the cutout edge, it would fade the entire surface by its opacity.
half SourceAlphaClip(half alpha)
{
#ifdef _ALPHATEST_ON
	alpha = AlphaClip(alpha, _Cutoff);
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

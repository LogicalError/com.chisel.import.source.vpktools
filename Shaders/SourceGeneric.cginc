#ifndef CHISEL_SOURCE_GENERIC_INCLUDED
#define CHISEL_SOURCE_GENERIC_INCLUDED

// Shared surface shader body for the Source engine's *Generic shader family.
//
// Source's LightmappedGeneric and VertexLitGeneric differ in how the lighting reaches the surface
// (baked lightmap vs per-vertex/dynamic), which in Unity a surface shader already decides for
// itself, so both use this same body and differ only in their name and defaults. What matters is
// reproducing the *parameters*, because those are what a VMT sets and what the Standard shader has
// no place for:
//
//   $basetexture/$color        base colour
//   $bumpmap                   tangent space normal map
//   $selfillum                 emission masked by the BASE TEXTURE'S ALPHA (or $selfillummask) -
//                              this is the one that cannot be expressed with Unity's Standard
//                              shader at all, and is why lit signage/screens imported flat
//   $detail/$detailscale/...   second texture blended over the base at its own tiling
//   $envmap/$envmapmask/$tint  cubemap reflection, masked and tinted
//   $phongexponent/$phongtint  specular
//   $alphatest/$translucent    handled by the material's blend state + _ALPHATEST_ON, as the
//                              Standard shader does, so one shader covers every blend mode
//   $nocull                    _Cull, which the Standard shader does not expose - Source uses it
//                              constantly for glass, grates and foliage
//
// Features are shader_feature_local toggles so a material that uses none of them costs nothing.

#include "UnityPBSLighting.cginc"

// NOTE: do NOT declare _MainTex_ST / _DetailTex_ST here. Naming a field uv_MainTex in the Input
//       struct makes the surface shader compiler declare the matching _ST itself and apply the
//       tiling/offset before surf() runs; declaring it again is a redefinition error.
sampler2D _MainTex;
fixed4    _Color;
half      _Cutoff;

#ifdef _NORMALMAP
	sampler2D _BumpMap;
	half      _BumpScale;
#endif

#ifdef _DETAIL
	sampler2D _DetailTex;
	half      _DetailBlendFactor;
	half      _DetailBlendMode;
#endif

// $envmap is nearly always the literal "env_cubemap", meaning "use the cubemap baked for this
// spot", which is Unity's reflection probes - so reflection is left to the Standard lighting model
// rather than sampled here, and $envmapmask/$envmaptint modulate how reflective the surface is.
#ifdef _ENVMAPMASK
	sampler2D _EnvMapMask;
#endif

#ifdef _SELFILLUM
	sampler2D _SelfIllumMask;
	fixed4    _SelfIllumTint;
	// 1 when the mask lives in the base texture's alpha channel (Source's default when no
	// $selfillummask is given), 0 when a separate mask texture was supplied.
	half      _SelfIllumFromBaseAlpha;
#endif

half _Smoothness;
half _SpecularScale;
fixed4 _SpecColorTint;

// Source's $phong is NOT a PBR reflectance - it is an additive highlight, masked per-pixel and
// shaped by $phongfresnelranges so that it is weak head-on and blooms at grazing angles. Feeding it
// into o.Specular instead makes every phong material read as polished metal, so it is computed here
// and added by the lighting function below, leaving o.Specular for $phongtint-less PBR use.
#ifdef _PHONG
	half   _PhongBoost;
	half   _PhongExponent;
	// [facing, middle, grazing] - Source interpolates between these over the fresnel term.
	float3 _PhongFresnelRanges;
	// 1 = the phong mask is the BASE texture's alpha ($basemapalphaphongmask), 0 = no mask.
	// The normal map's alpha (Source's other default) is not reachable: Unity re-encodes normal
	// maps and the original alpha does not survive the import.
	half   _PhongMaskFromBaseAlpha;
#endif

// NOTE: every field here is UNCONDITIONAL, and must stay that way. The surface shader generator
//       reads this struct textually, once, to decide the interpolator packing and where the
//       tangent basis that INTERNAL_DATA stands for ends up - it does not evaluate the feature
//       keywords, so it cannot see an #ifdef'd field. Putting uv_DetailTex behind #ifdef _DETAIL
//       therefore shifted worldPos/worldNormal/INTERNAL_DATA by one float2 in exactly the variants
//       that defined it: every _DETAIL material read its tangent basis out of the wrong
//       interpolators and came out with a constant shading normal (flat, unlit-looking surfaces),
//       whether or not it had a normal map at all. One unused float2 interpolator is the price.
struct Input
{
	float2 uv_MainTex;
	float2 uv_DetailTex;
	// World space, so the phong highlight below can be computed without a custom lighting
	// model - Unity's surface shader generator does not follow #include when resolving one,
	// so neither the lighting function nor a custom output struct can live in this file.
	float3 worldPos;
	float3 worldNormal;
	INTERNAL_DATA
};

#ifdef _PHONG
	// Source: Fresnel(N,V) = (1 - dot(N,V))^2, then remapped through the three ranges, linearly
	// between [x,y] over the first half and [y,z] over the second.
	half SourcePhongFresnel(half3 normal, half3 viewDir, float3 ranges)
	{
		half f = 1.0h - saturate(dot(normal, viewDir));
		f = f * f;
		return (f > 0.5h) ? lerp(ranges.y, ranges.z, (2.0h * f) - 1.0h)
						  : lerp(ranges.x, ranges.y, 2.0h * f);
	}
#endif

void surf(Input IN, inout SurfaceOutputStandardSpecular o)
{
	fixed4 base = tex2D(_MainTex, IN.uv_MainTex);
	fixed3 albedo = base.rgb * _Color.rgb;
	half alpha = base.a * _Color.a;

#ifdef _SELFILLUM
	// Source reads the self-illum mask from the base texture's alpha, which means that alpha is
	// NOT transparency on a $selfillum material - taking it as such is what makes these surfaces
	// come out see-through or unlit.
	half illumMask = lerp(tex2D(_SelfIllumMask, IN.uv_MainTex).r, base.a, _SelfIllumFromBaseAlpha);
	alpha = lerp(alpha, _Color.a, _SelfIllumFromBaseAlpha);
#endif

#ifdef _ALPHATEST_ON
	clip(alpha - _Cutoff);
#endif

#ifdef _DETAIL
	fixed4 detail = tex2D(_DetailTex, IN.uv_DetailTex);
	// Source's $detailblendmode 0 (DecalModulate) doubles the product so mid-grey leaves the base
	// unchanged; mode 1 (Additive) adds. Anything else falls back to modulate, which is the
	// overwhelmingly common case in the shipped materials.
	fixed3 modulated = albedo * detail.rgb * 2;
	fixed3 additive  = albedo + detail.rgb;
	fixed3 blended   = lerp(modulated, additive, step(0.5, _DetailBlendMode));
	albedo = lerp(albedo, blended, _DetailBlendFactor);
#endif

	o.Albedo = albedo;
	o.Alpha  = alpha;
	o.Smoothness = _Smoothness;

	fixed3 specular = _SpecColorTint.rgb * _SpecularScale;
#ifdef _ENVMAPMASK
	specular *= tex2D(_EnvMapMask, IN.uv_MainTex).rgb;
#endif
	o.Specular = specular;

	// o.Normal is written in BOTH branches on purpose. A surface shader only supplies Input.viewDir
	// in tangent space when the shader writes o.Normal somewhere; writing the identity normal in the
	// no-normal-map case keeps viewDir in the same space for every variant, so the fresnel below has
	// a normal and a view vector it can actually dot together.
#ifdef _NORMALMAP
	fixed3 normal = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
	normal.xy *= _BumpScale;
	o.Normal = normalize(normal);
#else
	o.Normal = fixed3(0, 0, 1);
#endif

#ifdef _PHONG
	// Source's phong = tint * mask * boost * fresnel(N,V) * pow(dot(R,V), exponent) * lightColor.
	// Added as emission rather than fed into o.Specular: it is an ADDITIVE highlight, and putting it
	// in o.Specular makes the surface behave like polished metal from every angle. Uses the pass's
	// light, so the forward-add pass contributes the other lights too.
	half phongMask = lerp(1.0h, base.a, _PhongMaskFromBaseAlpha);
	#ifdef _ENVMAPMASK
		// $normalmapalphaenvmapmask-style masks also gate the highlight in Source.
		phongMask *= tex2D(_EnvMapMask, IN.uv_MainTex).r;
	#endif

	float3 worldNormal = WorldNormalVector(IN, o.Normal);
	float3 viewWorld   = normalize(_WorldSpaceCameraPos - IN.worldPos);
	float3 lightWorld  = normalize(_WorldSpaceLightPos0.xyz);
	half   fresnel     = SourcePhongFresnel(worldNormal, viewWorld, _PhongFresnelRanges);
	half   lobe        = pow(saturate(dot(reflect(-lightWorld, worldNormal), viewWorld)),
							 max(_PhongExponent, 1.0h));
	o.Emission += _SpecColorTint.rgb * (_PhongBoost * phongMask * fresnel * lobe) * _LightColor0.rgb;
#endif

#ifdef _SELFILLUM
	o.Emission += albedo * illumMask * _SelfIllumTint.rgb;
#endif
}

#endif // CHISEL_SOURCE_GENERIC_INCLUDED

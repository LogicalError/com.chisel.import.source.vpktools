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
//   $phong/$phongtint/...      Source's specular highlight, masked, tinted and shaped per pixel, and
//                              handed to Unity's lighting as the specular colour and smoothness that
//                              give its highlight the same width and peak (SourcePhongToStandard)
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

// What the mesh's vertex colour does, as 0/1 switches rather than keywords: a surface shader's keywords are
// preprocessed combination by combination, and five more of them took LightmappedGeneric past the shader
// compiler's timeout. A lerp by a constant costs nothing next to that.
//  _VertexColor  1: the colour multiplies the base colour ($vertexcolor)
//  _VertexAlpha  1: the alpha multiplies opacity
// Both stay 0 on a VertexLitGeneric material, which ignores vertex colour.
half _VertexColor;
half _VertexAlpha;

// LightmappedGeneric's second layer: $basetexture2 and $bumpmap2, blended in by the mesh's vertex ALPHA -
// which on a displacement is what Hammer's alpha painting wrote - and sharpened by $blendmodulatetexture.
// One keyword for "there is a second layer"; which parts of it there are is again a set of 0/1 switches,
// the textures defaulting to ones that leave the result alone.
#ifdef _VERTEXBLEND
	sampler2D _BaseTexture2;
	sampler2D _BumpMap2;
	sampler2D _BlendModulateTexture;
	half      _BlendBase2;
	half      _BlendBump2;
	half      _BlendModulate;
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

// Source's $phong is an additive highlight, masked per pixel and shaped by $phongfresnelranges so that
// it is weak head-on and blooms at grazing angles, and Source adds it once per light. Unity's deferred
// path has no place to add a term per light - one shared pass lights every surface with the Standard
// BRDF - so the highlight goes in as o.Specular and o.Smoothness instead, scaled so that Unity's lobe
// has the same width and the same peak (SourcePhongToStandard). What that cannot carry over: Unity's
// own Fresnel still brightens the lobe at grazing light angles, and the smoothness lets reflection
// probes show at grazing view angles.
#ifdef _PHONG
	half   _PhongBoost;
	// Negative means the exponent comes from _PhongExponentMap's red channel instead. Source's own
	// constant defaults to -1, and it binds a WHITE texture when a material names no exponent texture,
	// so "$phong with no $phongexponent" is 1 + 149 = 150 - a tight hotspot, not a soft sheen.
	half   _PhongExponent;
	// $phongexponentfactor replaces that 149 when it is non-zero.
	half   _PhongExponentFactor;
	// 1 when $phongalbedotint applies: the highlight takes the surface's own colour, mixed in by the
	// exponent map's green channel.
	half   _PhongAlbedoTint;
	// [facing, middle, grazing] - Source interpolates between these over the fresnel term.
	float3 _PhongFresnelRanges;
	// 1 = the mask is the BASE texture's alpha, which is what Source reads when the material says so
	// ($basemapalphaphongmask) or when it has no normal map at all.
	half   _PhongMaskFromBaseAlpha;
	#ifdef _PHONGMASK
		// Source's usual mask is the NORMAL MAP's alpha (skin_ps20b.fxc:
		// fSpecMask = lerp(normalTexel.a, baseColor.a, $basemapalphaphongmask)). Unity re-encodes normal
		// maps and that alpha does not survive, so the importer writes it as a texture of its own.
		sampler2D _PhongMask;
	#endif
	#ifdef _PHONGEXPONENTMAP
		// $phongexponenttexture: red is the exponent, green how much of the albedo tints the highlight.
		// (Alpha is a rim-light mask, and rim lighting is not imported.)
		sampler2D _PhongExponentMap;
	#endif
	#ifdef _PHONGWARP
		// $phongwarptexture: a colour table indexed by { lobe, fresnel }, which is how Source makes a
		// surface iridescent - the highlight shifts hue with the viewing angle instead of scaling.
		sampler2D _PhongWarp;
		float4    _PhongWarp_TexelSize;
	#endif
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
	// World space, for the phong fresnel, which Source takes from the view angle alone.
	float3 worldPos;
	float3 worldNormal;
	// The mesh's vertex colour: a tint with $vertexcolor, opacity with $vertexalpha, and the weight of the
	// second layer on a blending material.
	float4 color : COLOR;
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

	// Source adds, per light, scale * (R.L)^n * N.L * light (SpecularAndRimTerms in
	// common_vertexlitgeneric_dx9.h), where scale is the tint, mask, $phongboost and fresnel. Unity's
	// StandardSpecular lobe is instead set by a specular colour and a smoothness, so this picks the two
	// that make it the same highlight:
	//  - width: a Phong lobe (R.L)^n is a Blinn-Phong lobe (N.H)^4n, and Blinn-Phong m corresponds to
	//    roughness alpha = sqrt(2 / (m + 2)) (Walter et al. 2007), so alpha^2 = 1 / (2n + 1), and Unity's
	//    smoothness is 1 - sqrt(alpha);
	//  - peak: at N.H = N.L = N.V = 1 Unity's specular term is specColor * V * D * pi = specColor / (4
	//    alpha^2), and Source's is scale, so specColor = scale * 4 alpha^2.
	void SourcePhongToStandard(half3 scale, half exponent, inout SurfaceOutputStandardSpecular o)
	{
		half alphaSquared = 1.0h / (2.0h * max(exponent, 0.0h) + 1.0h);
		o.Smoothness = 1.0h - sqrt(sqrt(alphaSquared));
		o.Specular   = min(scale * (4.0h * alphaSquared), 0.99h);

		// Source ADDS its highlight to the lit surface, while Unity takes the specular's share out of the
		// diffuse (EnergyConservationBetweenDiffuseAndSpecular). Dividing that share back in beforehand
		// cancels it exactly, which is why the specular stays below 1.
		o.Albedo /= 1.0h - SpecularStrength(o.Specular);
	}
#endif

void surf(Input IN, inout SurfaceOutputStandardSpecular o)
{
	fixed4 base = tex2D(_MainTex, IN.uv_MainTex);

#ifdef _VERTEXBLEND
	// The weight the second layer comes in by is the vertex alpha (lightmappedgeneric_vs20.fxc). With a
	// $blendmodulatetexture its green channel moves the transition and its red channel widens it - except
	// on a self-illuminated material, where Source compiles that step out (lightmappedgeneric_ps2_3_x.h).
	// The default modulation texture is white, which keeps smoothstep's two edges apart.
	half layerBlend = IN.color.a;
	#ifndef _SELFILLUM
		half4 modulation = tex2D(_BlendModulateTexture, IN.uv_MainTex);
		half  sharpened  = smoothstep(saturate(modulation.g - modulation.r), saturate(modulation.g + modulation.r), layerBlend);
		layerBlend = lerp(layerBlend, sharpened, _BlendModulate);
	#endif
	// Only the colour blends. The alpha Source goes on reading as opacity and as the self-illum mask is
	// still the first layer's.
	base.rgb = lerp(base.rgb, tex2D(_BaseTexture2, IN.uv_MainTex).rgb, layerBlend * _BlendBase2);
#endif

	fixed3 albedo = base.rgb * _Color.rgb;
	half alpha = base.a * _Color.a;

#ifdef _SELFILLUM
	// Source reads the self-illum mask from the base texture's alpha, which means that alpha is
	// NOT transparency on a $selfillum material - taking it as such is what makes these surfaces
	// come out see-through or unlit.
	half illumMask = lerp(tex2D(_SelfIllumMask, IN.uv_MainTex).r, base.a, _SelfIllumFromBaseAlpha);
	alpha = lerp(alpha, _Color.a, _SelfIllumFromBaseAlpha);
#endif

	// Before the alpha test: Source tests the alpha it outputs, vertex alpha included.
	alpha *= lerp(1.0h, IN.color.a, _VertexAlpha);

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

	// After the detail texture, as Source does it. LightmappedGeneric - the only shader here that honours
	// $vertexcolor - takes the colour as it is, without the gamma decode UnlitGeneric applies.
	albedo *= lerp(half3(1, 1, 1), IN.color.rgb, _VertexColor);

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
	#ifdef _VERTEXBLEND
		// Source only blends a second normal map into a first one.
		fixed3 normal2 = UnpackNormal(tex2D(_BumpMap2, IN.uv_MainTex));
		normal2.xy *= _BumpScale;
		normal = lerp(normal, normal2, layerBlend * _BlendBump2);
	#endif
	o.Normal = normalize(normal);
#else
	o.Normal = fixed3(0, 0, 1);
#endif

	// Everything in the highlight that can be decided per pixel: its mask, exponent, tint and fresnel.
	// Note that Source does NOT mask the highlight with $envmapmask - that texture gates the cubemap
	// reflection only.
#ifdef _PHONG
	#ifdef _PHONGMASK
		half phongMask = tex2D(_PhongMask, IN.uv_MainTex).r;
	#else
		half phongMask = lerp(1.0h, base.a, _PhongMaskFromBaseAlpha);
	#endif

	// White is what Source samples when a material names no exponent texture, so it is what the two
	// expressions below have to fall back to: exponent 1 + 149, and a fully albedo-coloured tint.
	half4 exponentMap = half4(1, 1, 1, 1);
	#ifdef _PHONGEXPONENTMAP
		exponentMap = tex2D(_PhongExponentMap, IN.uv_MainTex);
	#endif
	half exponent = (_PhongExponentFactor > 0) ? (1.0h + _PhongExponentFactor * exponentMap.r)
				  : (_PhongExponent >= 0)      ? _PhongExponent
											   : (1.0h + 149.0h * exponentMap.r);
	// Source tints with the BASE texture, before $color/$color2 modulates it (skin_ps20b.fxc computes
	// vSpecularTint from baseColor.rgb, and modulates albedo only afterwards).
	half3 tint = lerp(_SpecColorTint.rgb, lerp(half3(1, 1, 1), base.rgb, exponentMap.g), _PhongAlbedoTint);

	float3 worldNormal = WorldNormalVector(IN, o.Normal);
	float3 viewWorld   = normalize(_WorldSpaceCameraPos - IN.worldPos);
	half   fresnel     = SourcePhongFresnel(worldNormal, viewWorld, _PhongFresnelRanges);
	#ifdef _PHONGWARP
		// With a warp texture the fresnel is the lookup's second coordinate rather than a multiplier:
		// skin_ps20b.fxc only folds it into the mask "if we didn't already apply Fresnel to specular
		// warp". The lookup's first coordinate is the lobe, which only a light knows, so the table is
		// read where the highlight is brightest - its last column - and the colour found there tints
		// the whole lobe. The fresnel is NOT clamped (ranges like [.4 .6 1.6] run past 1) and the warp
		// textures set no CLAMP flag, so it wraps around the table, as it does in the game.
		half3 highlight = tex2D(_PhongWarp, float2(1.0 - 0.5 * _PhongWarp_TexelSize.x, fresnel)).rgb;
	#else
		half3 highlight = fresnel;
	#endif
	SourcePhongToStandard(tint * highlight * (phongMask * _PhongBoost), exponent, o);
#endif

#ifdef _SELFILLUM
	o.Emission += albedo * illumMask * _SelfIllumTint.rgb;
#endif
}

#endif // CHISEL_SOURCE_GENERIC_INCLUDED

#ifndef CHISEL_SOURCE_GENERIC_URP_INCLUDED
#define CHISEL_SOURCE_GENERIC_URP_INCLUDED

// Shared forward pass for Source's lit Generic materials.  This deliberately keeps the
// importer-facing property names/keywords from the built-in implementation.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

TEXTURE2D(_BaseMap);                SAMPLER(sampler_BaseMap);
TEXTURE2D(_BumpMap);                SAMPLER(sampler_BumpMap);
TEXTURE2D(_DetailTex);              SAMPLER(sampler_DetailTex);
TEXTURE2D(_BaseTexture2);           SAMPLER(sampler_BaseTexture2);
TEXTURE2D(_BumpMap2);               SAMPLER(sampler_BumpMap2);
TEXTURE2D(_BlendModulateTexture);   SAMPLER(sampler_BlendModulateTexture);
TEXTURE2D(_EnvMapMask);             SAMPLER(sampler_EnvMapMask);
TEXTURE2D(_PhongMask);              SAMPLER(sampler_PhongMask);
TEXTURE2D(_PhongExponentMap);       SAMPLER(sampler_PhongExponentMap);
TEXTURE2D(_SelfIllumMask);          SAMPLER(sampler_SelfIllumMask);
TEXTURECUBE(_EnvMap);               SAMPLER(sampler_EnvMap);

CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST, _DetailTex_ST;
half4 _Color, _SpecColorTint, _EnvMapTint, _SelfIllumTint;
half _Cutoff, _BumpScale, _DetailBlendFactor, _DetailBlendMode;
half _VertexColor, _VertexAlpha, _BlendBase2, _BlendBump2, _BlendModulate, _MaskedBlending;
float4 _BlendMaskTransformU, _BlendMaskTransformV;
half3 _EnvMapContrast, _EnvMapSaturation;
half4 _EnvMapMaskWeights;
half _EnvMapFromProbes, _EnvMapInvertMask, _EnvMapMaskTexture, _FresnelReflection, _EnvMapPhongFresnel;
half _Smoothness, _SpecularScale, _PhongBoost, _PhongExponent, _PhongExponentFactor;
half3 _PhongFresnelRanges;
half _PhongMaskFromBaseAlpha, _PhongAlbedoTint, _SelfIllumFromBaseAlpha;
CBUFFER_END

struct SourceAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    float2 staticLightmapUV : TEXCOORD1;
    float2 dynamicLightmapUV : TEXCOORD2;
    float4 color : COLOR;
};
struct SourceVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float2 detailUV : TEXCOORD1;
    float3 positionWS : TEXCOORD2;
    half3 normalWS : TEXCOORD3;
    half4 tangentWS : TEXCOORD4;
    half4 color : COLOR;
    half fog : TEXCOORD5;
    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 6);
#ifdef DYNAMICLIGHTMAP_ON
    float2 dynamicLightmapUV : TEXCOORD7;
#endif
};

SourceVaryings SourceLitVert(SourceAttributes input)
{
    SourceVaryings output;
    VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
    output.positionCS = p.positionCS; output.positionWS = p.positionWS;
    output.normalWS = n.normalWS; output.tangentWS = half4(n.tangentWS, input.tangentOS.w * GetOddNegativeScale());
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap); output.detailUV = TRANSFORM_TEX(input.uv, _DetailTex);
    output.color = input.color; output.fog = ComputeFogFactor(p.positionCS.z);
    OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
#ifdef DYNAMICLIGHTMAP_ON
    output.dynamicLightmapUV = input.dynamicLightmapUV * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
#endif
    OUTPUT_SH(output.normalWS, output.vertexSH);
    return output;
}

half3 SourceSampleBakedGI(SourceVaryings input, half3 normalWS)
{
#ifdef DYNAMICLIGHTMAP_ON
    return SAMPLE_GI(input.staticLightmapUV, input.dynamicLightmapUV, input.vertexSH, normalWS);
#else
    return SAMPLE_GI(input.staticLightmapUV, input.vertexSH, normalWS);
#endif
}

half SourceFresnel(half3 n, half3 v)
{
    half f = 1.0h - saturate(dot(n, v)); f *= f;
    return f > .5h ? lerp(_PhongFresnelRanges.y, _PhongFresnelRanges.z, 2.0h * f - 1.0h) : lerp(_PhongFresnelRanges.x, _PhongFresnelRanges.y, 2.0h * f);
}

half4 SourceLitFrag(SourceVaryings input) : SV_Target
{
    half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
    half layerBlend = lerp(input.color.a, .5h, _MaskedBlending);
#ifdef _VERTEXBLEND
    half4 layer = SAMPLE_TEXTURE2D(_BaseTexture2, sampler_BaseTexture2, input.uv);
    half4 mod = SAMPLE_TEXTURE2D(_BlendModulateTexture, sampler_BlendModulateTexture, input.uv);
    layerBlend = lerp(layerBlend, smoothstep(saturate(mod.g-mod.r), saturate(mod.g+mod.r), layerBlend), _BlendModulate);
    base.rgb = lerp(base.rgb, layer.rgb, layerBlend * _BlendBase2);
#endif
    half alpha = base.a * _Color.a;
#ifdef _SELFILLUM
    half illum = lerp(SAMPLE_TEXTURE2D(_SelfIllumMask, sampler_SelfIllumMask, input.uv).r, base.a, _SelfIllumFromBaseAlpha);
    alpha = lerp(alpha, _Color.a, _SelfIllumFromBaseAlpha);
#endif
    alpha *= lerp(1.0h, input.color.a, _VertexAlpha);
#ifdef _ALPHATEST_ON
    clip(alpha - _Cutoff);
#endif
    half3 albedo = base.rgb * _Color.rgb;
#ifdef _DETAIL
    half3 detail = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, input.detailUV).rgb;
    albedo = lerp(albedo, lerp(albedo * detail * 2.0h, albedo + detail, step(.5h, _DetailBlendMode)), _DetailBlendFactor);
#endif
    albedo *= lerp(half3(1,1,1), input.color.rgb, _VertexColor);
    half3 normalWS = normalize(input.normalWS);
#ifdef _NORMALMAP
    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
    #ifdef _VERTEXBLEND
    normalTS = normalize(lerp(normalTS, UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap2, sampler_BumpMap2, input.uv), _BumpScale), layerBlend * _BlendBump2));
    #endif
    half3 bitangent = input.tangentWS.w * cross(normalWS, input.tangentWS.xyz);
    normalWS = normalize(TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent, normalWS)));
#endif
    half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
    half3 bakedGI = SourceSampleBakedGI(input, normalWS);
    half4 shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
    Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS), input.positionWS, shadowMask);
    MixRealtimeAndBakedGI(light, normalWS, bakedGI, shadowMask);
    half ndotl = saturate(dot(normalWS, light.direction));
    half3 colour = albedo * (bakedGI + light.color * (ndotl * light.distanceAttenuation * light.shadowAttenuation));
#ifdef _ADDITIONAL_LIGHTS
    // LIGHT_LOOP_BEGIN uses this conventional name internally to look up the
    // clustered tile/cluster containing the current pixel.
    InputData inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.positionCS = input.positionCS;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    uint count = GetAdditionalLightsCount();
    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); ++lightIndex)
    {
        Light l = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);
        colour += albedo * l.color * saturate(dot(normalWS,l.direction)) * l.distanceAttenuation * l.shadowAttenuation;
    }
    #endif
    LIGHT_LOOP_BEGIN(count)
        Light l = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);
        colour += albedo * l.color * saturate(dot(normalWS,l.direction)) * l.distanceAttenuation * l.shadowAttenuation;
    LIGHT_LOOP_END
#endif
#ifdef _PHONG
    half exponent = _PhongExponentFactor > 0 ? 1 + _PhongExponentFactor * SAMPLE_TEXTURE2D(_PhongExponentMap, sampler_PhongExponentMap, input.uv).r : max(_PhongExponent, 1.0h);
    half mask = lerp(1.0h, base.a, _PhongMaskFromBaseAlpha);
    #ifdef _PHONGMASK
    mask = SAMPLE_TEXTURE2D(_PhongMask, sampler_PhongMask, input.uv).r;
    #endif
    half3 h = SafeNormalize(light.direction + viewDir);
    colour += _SpecColorTint.rgb * _PhongBoost * mask * SourceFresnel(normalWS, viewDir) * pow(saturate(dot(normalWS,h)), exponent) * light.color * ndotl;
#endif
#ifdef _ENVMAP
    half3 reflection = reflect(-viewDir, normalWS);
    half3 namedEnv = SAMPLE_TEXTURECUBE(_EnvMap, sampler_EnvMap, reflection).rgb;
    half3 probeEnv = GlossyEnvironmentReflection(reflection, input.positionWS, 0.0h, 1.0h,
                                                  GetNormalizedScreenSpaceUV(input.positionCS));
    half3 env = lerp(namedEnv, probeEnv, _EnvMapFromProbes);
    half envMask = _EnvMapMaskWeights.y * base.a + _EnvMapMaskWeights.z * (1-base.a) + (1-dot(_EnvMapMaskWeights, half4(1,1,1,1)));
    envMask = lerp(envMask, 1-envMask, _EnvMapInvertMask) * lerp(1, SAMPLE_TEXTURE2D(_EnvMapMask, sampler_EnvMapMask, input.uv).r, _EnvMapMaskTexture);
    half fresnel = lerp(_FresnelReflection, 1.0h, Pow4(1.0h - saturate(dot(normalWS, viewDir))));
    env *= envMask * fresnel * _EnvMapTint.rgb;
    env = lerp(env, env * env, _EnvMapContrast);
    env = lerp(dot(env, half3(.299h,.587h,.114h)).xxx, env, _EnvMapSaturation);
    colour += env;
#endif
#ifdef _SELFILLUM
    colour += albedo * illum * _SelfIllumTint.rgb;
#endif
    return half4(MixFog(colour, input.fog), alpha);
}

// URP's deferred renderer consumes this pass instead of UniversalForward.  Source-specific
// emission is retained; direct lights, shadows, baked GI and the specular lobe are then evaluated
// by URP's deferred lighting pass from these buffers.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferOutput.hlsl"
GBufferFragOutput SourceLitGBufferFrag(SourceVaryings input)
{
    half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
#ifdef _VERTEXBLEND
    base.rgb = lerp(base.rgb, SAMPLE_TEXTURE2D(_BaseTexture2, sampler_BaseTexture2, input.uv).rgb, input.color.a * _BlendBase2);
#endif
    half alpha = base.a * _Color.a * lerp(1.0h, input.color.a, _VertexAlpha);
#ifdef _ALPHATEST_ON
    clip(alpha - _Cutoff);
#endif
    half3 normalWS = normalize(input.normalWS);
#ifdef _NORMALMAP
    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
    half3 bitangent = input.tangentWS.w * cross(normalWS, input.tangentWS.xyz);
    normalWS = normalize(TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent, normalWS)));
#endif
    SurfaceData surface = (SurfaceData)0;
    surface.albedo = base.rgb * _Color.rgb * lerp(half3(1,1,1), input.color.rgb, _VertexColor);
    surface.specular = _SpecColorTint.rgb * _SpecularScale;
    surface.smoothness = _Smoothness; surface.occlusion = 1; surface.alpha = alpha;
    InputData data = (InputData)0;
    data.positionWS = input.positionWS; data.positionCS = input.positionCS; data.normalWS = normalWS;
    data.viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
    data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    data.bakedGI = SourceSampleBakedGI(input, normalWS); data.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
    data.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
    BRDFData brdf; InitializeBRDFData(surface.albedo, 0, surface.specular, surface.smoothness, alpha, brdf);
    Light mainLight = GetMainLight(data.shadowCoord, data.positionWS, data.shadowMask);
    MixRealtimeAndBakedGI(mainLight, normalWS, data.bakedGI, data.shadowMask);
    half3 gi = GlobalIllumination(brdf, (BRDFData)0, 0, data.bakedGI, 1, data.positionWS, normalWS, data.viewDirectionWS, data.normalizedScreenSpaceUV);
#ifdef _SELFILLUM
    half illum = lerp(SAMPLE_TEXTURE2D(_SelfIllumMask, sampler_SelfIllumMask, input.uv).r, base.a, _SelfIllumFromBaseAlpha);
    surface.emission = surface.albedo * illum * _SelfIllumTint.rgb;
#endif
    return PackGBuffersBRDFData(brdf, data, surface.smoothness, gi + surface.emission, surface.occlusion);
}

struct SourceShadowVaryings
{
	float4 positionCS : SV_POSITION;
	float2 uv : TEXCOORD0;
	half vertexAlpha : TEXCOORD1;
};

float3 _LightDirection;
float3 _LightPosition;

SourceShadowVaryings SourceShadowVert(SourceAttributes input)
{
	SourceShadowVaryings output;

	VertexPositionInputs positionInputs =
		GetVertexPositionInputs(input.positionOS.xyz);

	VertexNormalInputs normalInputs =
		GetVertexNormalInputs(input.normalOS, input.tangentOS);

	float3 positionWS = positionInputs.positionWS;
	float3 normalWS = normalize(normalInputs.normalWS);

	#if _CASTING_PUNCTUAL_LIGHT_SHADOW
		float3 lightDirectionWS =
			normalize(_LightPosition - positionWS);
	#else
		float3 lightDirectionWS = _LightDirection;
	#endif

	float4 positionCS = TransformWorldToHClip(
		ApplyShadowBias(
			positionWS,
			normalWS,
			lightDirectionWS
		)
	);

	positionCS = ApplyShadowClamping(positionCS);

	output.positionCS = positionCS;
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
	output.vertexAlpha = input.color.a;

	return output;
}

half4 SourceShadowFrag(SourceShadowVaryings input) : SV_Target
{
	#ifdef _ALPHATEST_ON

		half4 base = SAMPLE_TEXTURE2D(
			_BaseMap,
			sampler_BaseMap,
			input.uv
		);

		half alpha = base.a * _Color.a;

		#ifdef _SELFILLUM
			alpha = lerp(
				alpha,
				_Color.a,
				_SelfIllumFromBaseAlpha
			);
		#endif

		alpha *= lerp(
			1.0h,
			input.vertexAlpha,
			_VertexAlpha
		);

		clip(alpha - _Cutoff);

	#endif

	return 0;
}


#endif



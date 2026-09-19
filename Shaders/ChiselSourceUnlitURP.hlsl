#ifndef CHISEL_SOURCE_UNLIT_URP_INCLUDED
#define CHISEL_SOURCE_UNLIT_URP_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
TEXTURE2D(_Texture2); SAMPLER(sampler_Texture2);
TEXTURE2D(_EnvMapMask); SAMPLER(sampler_EnvMapMask);
TEXTURECUBE(_EnvMap); SAMPLER(sampler_EnvMap);
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST, _Texture2_ST, _BaseTextureScroll, _Texture2Scroll;
half4 _Color; half _Cutoff, _VertexColor, _VertexAlpha;
half4 _EnvMapTint, _EnvMapMaskWeights;
half3 _EnvMapContrast, _EnvMapSaturation;
half _EnvMapFromProbes, _EnvMapMaskTexture;
CBUFFER_END
struct SourceUnlitAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 color : COLOR; };
struct SourceUnlitVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; half4 color : COLOR; half fog : TEXCOORD2; float3 positionWS : TEXCOORD3; half3 normalWS : TEXCOORD4; };
SourceUnlitVaryings SourceUnlitVert(SourceUnlitAttributes input)
{
    SourceUnlitVaryings output; VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
    output.positionCS=p.positionCS; output.uv=TRANSFORM_TEX(input.uv,_BaseMap)+frac(_BaseTextureScroll.xy*_Time.y);
    output.uv2=TRANSFORM_TEX(input.uv,_Texture2)+frac(_Texture2Scroll.xy*_Time.y); output.color=input.color; output.fog=ComputeFogFactor(p.positionCS.z); output.positionWS=p.positionWS; output.normalWS=TransformObjectToWorldNormal(input.normalOS); return output;
}
half4 SourceUnlitFrag(SourceUnlitVaryings input) : SV_Target
{
    half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _Color;
#ifdef CHISEL_TWO_TEXTURE
    col*=SAMPLE_TEXTURE2D(_Texture2,sampler_Texture2,input.uv2);
#else
    col.rgb*=lerp(1.0h,pow(saturate(input.color.rgb),2.2h),_VertexColor); col.a*=lerp(1.0h,input.color.a,_VertexAlpha);
    #ifdef _ENVMAP
    half3 viewDir=SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
    half3 reflection=reflect(-viewDir,normalize(input.normalWS));
    half3 namedEnv=SAMPLE_TEXTURECUBE(_EnvMap,sampler_EnvMap,reflection).rgb;
    half3 probeEnv=GlossyEnvironmentReflection(reflection,input.positionWS,0.0h,1.0h,GetNormalizedScreenSpaceUV(input.positionCS));
    half3 reflected=lerp(namedEnv,probeEnv,_EnvMapFromProbes);
    half mask=(1-dot(_EnvMapMaskWeights,half4(1,1,1,1)))+_EnvMapMaskWeights.y*col.a+_EnvMapMaskWeights.z*(1-col.a);
    reflected*=mask*lerp(1,SAMPLE_TEXTURE2D(_EnvMapMask,sampler_EnvMapMask,input.uv).r,_EnvMapMaskTexture)*_EnvMapTint.rgb;
    reflected=lerp(reflected,reflected*reflected,_EnvMapContrast);
    col.rgb+=lerp(dot(reflected,half3(.299h,.587h,.114h)).xxx,reflected,_EnvMapSaturation);
    #endif
#endif
#ifdef _ALPHATEST_ON
    clip(col.a-_Cutoff);
#endif
    col.rgb=MixFog(col.rgb,input.fog); return col;
}
#endif

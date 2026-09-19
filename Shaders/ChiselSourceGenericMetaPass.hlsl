#ifndef CHISEL_SOURCE_GENERIC_META_PASS_INCLUDED
#define CHISEL_SOURCE_GENERIC_META_PASS_INCLUDED

// The Meta pass, which Unity's lightmapper renders a Source surface with: the colour light bounces off, and the light the
// surface gives off by itself. That is its self-illumination only; its reflection depends on where it is seen from.

#include "ChiselSourceGenericInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

struct SourceMetaAttributes
{
	float4 positionOS : POSITION;
	half4  color      : COLOR;
	float2 uv0        : TEXCOORD0;
	float2 uv1        : TEXCOORD1;
	float2 uv2        : TEXCOORD2;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct SourceMetaVaryings
{
	float4 positionCS : SV_POSITION;
	float4 uv         : TEXCOORD0;    // xy: the base texture's, zw: the detail texture's
	half4  color      : TEXCOORD1;
#ifdef EDITOR_VISUALIZATION
	float2 VizUV      : TEXCOORD2;
	float4 LightCoord : TEXCOORD3;
#endif
};

SourceMetaVaryings SourceMetaVertex(SourceMetaAttributes input)
{
	SourceMetaVaryings output = (SourceMetaVaryings)0;
	output.positionCS = UnityMetaVertexPosition(input.positionOS.xyz, input.uv1, input.uv2);
	output.uv.xy      = TRANSFORM_TEX(input.uv0, _BaseMap);
	output.uv.zw      = TRANSFORM_TEX(input.uv0, _DetailTex);
	output.color      = input.color;
#ifdef EDITOR_VISUALIZATION
	UnityEditorVizData(input.positionOS.xyz, input.uv0, input.uv1, input.uv2, output.VizUV, output.LightCoord);
#endif
	return output;
}

half4 SourceMetaFragment(SourceMetaVaryings input) : SV_Target
{
	SourceBase base = SourceSampleBase(input.uv.xy, input.color);
	SourceAlphaClip(SourceOpacity(base, input.color));

	MetaInput metaInput = (MetaInput)0;
	SourceAlbedo(base, input.uv.xy, input.uv.zw, input.color, metaInput.Albedo, metaInput.Emission);
#ifdef EDITOR_VISUALIZATION
	metaInput.VizUV      = input.VizUV;
	metaInput.LightCoord = input.LightCoord;
#endif
	return UnityMetaFragment(metaInput);
}

#endif // CHISEL_SOURCE_GENERIC_META_PASS_INCLUDED

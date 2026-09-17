#ifndef CHISEL_SOURCE_PROBES_INCLUDED
#define CHISEL_SOURCE_PROBES_INCLUDED

// The reflection probes a Source level has at its env_cubemap entities, baked by Unity and packed by the map importer's
// SourceReflectionProbes component: one cubemap array, and a volume of cells each holding the index of the probe nearest
// to it. Source gives a surface the env_cubemap nearest to it - vbsp per brush side, the engine per model - and looking the
// nearest probe up per pixel is what comes closest on renderers that span a whole map, where Unity picks one probe for all.
//
// Shaders sampling these need `#pragma require cubearray`.

#include "UnityCG.cginc"

// xyz: the volume's corner, in world space; w: 1 once the component has set the probes up
float4 _SourceProbeVolumeMin;
// xyz: the volume's size in cells; w: cells per world unit
float4 _SourceProbeVolumeCells;
// Unity's decode values for the array's texels, and each probe's intensity
half4  _SourceProbes_HDR;
float  _SourceProbeIntensity[256];

#if defined(SHADER_TARGET_SURFACE_ANALYSIS_MOJOSHADER)
// The half of Unity's surface shader analysis that only parses DX9 syntax knows neither cube arrays nor Load. All it has
// to learn is which inputs the lookup reads, so it sees a stand-in that reads the same ones.
half3 SourceProbeReflection(float3 worldPos, float3 direction)
{
	return UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, direction + worldPos).rgb;
}
#else
UNITY_DECLARE_TEXCUBEARRAY(_SourceProbes);
UNITY_DECLARE_TEX3D_NOSAMPLER(_SourceProbeIndex);

// What the probe nearest to worldPos reflects along the direction, or Unity's own reflection (the sky) without probes.
half3 SourceProbeReflection(float3 worldPos, float3 direction)
{
	if (_SourceProbeVolumeMin.w < 0.5)
		return DecodeHDR(UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, direction), unity_SpecCube0_HDR);

	int3 cells = (int3)_SourceProbeVolumeCells.xyz;
	int3 cell  = clamp((int3)floor((worldPos - _SourceProbeVolumeMin.xyz) * _SourceProbeVolumeCells.w), (int3)0, cells - 1);
	int  probe = (int)(_SourceProbeIndex.Load(int4(cell, 0)).r * 65535.0 + 0.5);
	half4 texel = UNITY_SAMPLE_TEXCUBEARRAY(_SourceProbes, float4(direction, probe));
	return DecodeHDR(texel, half4(_SourceProbes_HDR.x * _SourceProbeIntensity[probe], _SourceProbes_HDR.yzw));
}
#endif

#endif // CHISEL_SOURCE_PROBES_INCLUDED

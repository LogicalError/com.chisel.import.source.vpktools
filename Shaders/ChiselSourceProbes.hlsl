#ifndef CHISEL_SOURCE_PROBES_INCLUDED
#define CHISEL_SOURCE_PROBES_INCLUDED

// The reflection probes a Source level has at its env_cubemap entities, baked by Unity and packed by the map importer's
// SourceReflectionProbes component: one cubemap array, and a volume of cells each holding the index of the probe nearest
// to it. Source gives a surface the env_cubemap nearest to it - vbsp per brush side, the engine per model - and looking the
// nearest probe up per pixel is what comes closest on renderers that span a whole map. URP's own probe lookup blends the
// probes whose boxes hold the pixel, and a level has more probes than it takes.
//
// Include after URP's Core.hlsl. Shaders sampling these need `#pragma require cubearray`.

// DecodeHDREnvironment
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"

// xyz: the volume's corner, in world space; w: 1 once the component has set the probes up
float4 _SourceProbeVolumeMin;
// xyz: the volume's size in cells; w: cells per world unit
float4 _SourceProbeVolumeCells;
// Unity's decode values for the array's texels, and each probe's intensity
half4  _SourceProbes_HDR;
float  _SourceProbeIntensity[256];

TEXTURECUBE_ARRAY(_SourceProbes);
SAMPLER(sampler_SourceProbes);
TEXTURE3D(_SourceProbeIndex);

// What the probe nearest to positionWS reflects along the direction, or what the sky reflects without probes
half3 SourceProbeReflection(float3 positionWS, float3 direction)
{
	UNITY_BRANCH
	if (_SourceProbeVolumeMin.w < 0.5)
		return DecodeHDREnvironment(SAMPLE_TEXTURECUBE(_GlossyEnvironmentCubeMap, sampler_GlossyEnvironmentCubeMap, direction), _GlossyEnvironmentCubeMap_HDR);
	else
	{
		int3 cells = (int3)_SourceProbeVolumeCells.xyz;
		int3 cell  = clamp((int3)floor((positionWS - _SourceProbeVolumeMin.xyz) * _SourceProbeVolumeCells.w), (int3)0, cells - 1);
		int  probe = (int)(LOAD_TEXTURE3D(_SourceProbeIndex, cell).r * 65535.0 + 0.5);
		half4 texel = SAMPLE_TEXTURECUBE_ARRAY(_SourceProbes, sampler_SourceProbes, direction, probe);
		return DecodeHDREnvironment(texel, half4(_SourceProbes_HDR.x * _SourceProbeIntensity[probe], _SourceProbes_HDR.yzw));
	}
}

#endif // CHISEL_SOURCE_PROBES_INCLUDED

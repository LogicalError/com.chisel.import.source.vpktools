// Unity equivalent of Source's "Cable" shader, which the game draws its ropes with (move_rope and keyframe_rope, see
// VmfRope in the map importer). Source colours a rope with its base texture, lit by the light the client found at the
// rope's nodes, and shapes that across the rope's width with the normal map: (0.5 * N.z + 0.5)^2, a half-Lambert
// against a light straight out of the strip (cable_ps2x.fxc). The shader reads nothing else - $color, $minlight and
// $maxlight do nothing there, and so nothing here.
//
// The importer draws a rope with a LineRenderer, which lays a texture out along the line in U and across it in V.
// Source's ropes run along V and across U, so the two are swapped when sampling.
Shader "Chisel/Source/Cable"
{
	Properties
	{
		[MainTexture] _MainTex ("$basetexture", 2D) = "white" {}
		[Normal] _BumpMap ("$bumpmap", 2D) = "bump" {}
		_Cutoff ("$alphatestreference", Range(0,1)) = 0.5

		[HideInInspector] _SrcBlend ("__src", Float) = 1.0
		[HideInInspector] _DstBlend ("__dst", Float) = 0.0
		[HideInInspector] _ZWrite ("__zw", Float) = 1.0
		[Enum(UnityEngine.Rendering.CullMode)] _Cull ("$nocull", Float) = 2
	}

	SubShader
	{
		Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
		LOD 300

		Cull [_Cull]
		Blend [_SrcBlend] [_DstBlend]
		ZWrite [_ZWrite]

		CGPROGRAM
		#pragma target 3.0
		#pragma shader_feature_local _ALPHATEST_ON
		#pragma surface surf SourceCable fullforwardshadows keepalpha nolightmap nodynlightmap nodirlightmap nometa

		#include "UnityPBSLighting.cginc"

		sampler2D _MainTex;
		sampler2D _BumpMap;
		half      _Cutoff;

		struct Input
		{
			float2 uv_MainTex;
		};

		// C_RopeKeyframe::CalcLightValues takes the light at each node from whichever side it is brightest, and the
		// shader only shapes it across the width, so a light's direction plays no part here. A G-buffer can only be lit
		// through a normal, which is why this lighting model has no deferred pass: the rope is drawn in forward, lit by
		// the lights Unity picks for it.
		inline half4 LightingSourceCable(SurfaceOutput s, half3 viewDir, UnityGI gi)
		{
			return half4(s.Albedo * (gi.light.color + gi.indirect.diffuse), s.Alpha);
		}

		inline void LightingSourceCable_GI(SurfaceOutput s, UnityGIInput data, inout UnityGI gi)
		{
			// The ambient light and light probes, and the light's attenuation
			gi = UnityGlobalIllumination(data, 1.0, s.Normal);
		}

		void surf(Input IN, inout SurfaceOutput o)
		{
			// Across the rope in U and along it in V, as Source lays the texture out
			float2 coord = IN.uv_MainTex.yx;
			fixed4 base = tex2D(_MainTex, coord);
		#ifdef _ALPHATEST_ON
			clip(base.a - _Cutoff);
		#endif
			half facing = UnpackNormal(tex2D(_BumpMap, coord)).z * 0.5h + 0.5h;
			o.Albedo = base.rgb * (facing * facing);
			o.Alpha  = base.a;
		}
		ENDCG
	}

	FallBack Off
}

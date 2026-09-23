// The particles of func_dustmotes and func_dustcloud, drawn the way the SDK's client draws them (CDustEffect::RenderParticles
// in c_func_dust.cpp). A Unity ParticleSystem runs them (VmfDustMotes in the map importer). This does the per-camera part:
// - a quad facing the camera, a size across either side of the particle, and for func_dustmotes that size times the
//   distance, so a mote stays as large on screen;
// - the alpha over its life, sin(t * 2pi - pi/2) * 0.5 + 0.5, fading in and out again, and 1 for frozen particles;
// - the alpha times 1 - distance / DistMax, and nothing drawn beyond DistMax.
// The distance is along the view direction, as the client measures it.
Shader "Chisel/Import/DustMotes"
{
	Properties
	{
		[MainTexture] _BaseMap ("Particle Texture", 2D) = "white" {}
	}

	SubShader
	{
		Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "RenderPipeline" = "UniversalPipeline" }

		// particle/sparkles is UnlitGeneric with $translucent, $vertexcolor and $vertexalpha
		Blend SrcAlpha OneMinusSrcAlpha
		ZWrite Off
		Cull Off

		Pass
		{
			Name "DustMotes"

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex SourceDustVertex
			#pragma fragment SourceDustFragment
			// A BatchRendererGroup - the GPU Resident Drawer, or Entities Graphics - refuses a pass with no
			// DOTS_INSTANCING_ON variant, at draw submission rather than at compile, so the symptom is silence.
			#pragma multi_compile _ DOTS_INSTANCING_ON

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float4 _BaseMap_ST;
			CBUFFER_END

			TEXTURE2D(_BaseMap);
			SAMPLER(sampler_BaseMap);

			// The ParticleSystemRenderer's vertex streams, as VmfDustMotes.kVertexStreams orders them
			struct Attributes
			{
				float3 position  : POSITION;   // Unity's own corner, not used: the quad is built from the centre
				half4  color     : COLOR;      // Color and Alpha as the map writes them
				float4 texcoord0 : TEXCOORD0;  // xy: the corner's UV; z: the size; w: the age, 0 to 1
				float4 custom1   : TEXCOORD1;  // x: DistMax (meters); y: frozen; z: size follows the distance
				float3 center    : TEXCOORD2;  // the particle, in world space
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv         : TEXCOORD0;
				half4  color      : TEXCOORD1;
			};

			Varyings SourceDustVertex(Attributes input)
			{
				UNITY_SETUP_INSTANCE_ID(input);
				Varyings output = (Varyings)0;
				// Nothing to draw leaves the quad a point, which is never rasterized
				output.positionCS = float4(0, 0, 0, 1);

				float3 centerVS = TransformWorldToView(input.center);
				float viewDistance = -centerVS.z;
				float distMax = input.custom1.x;
				if (viewDistance <= 0 || viewDistance > distMax)
					return output;

				float alpha = 1;
				if (input.custom1.y < 0.5)
					alpha = sin(input.texcoord0.w * TWO_PI - HALF_PI) * 0.5 + 0.5;
				alpha *= 1 - viewDistance / distMax;

				float size = input.texcoord0.z;
				if (input.custom1.z > 0.5)
					size *= viewDistance;

				float2 corner = input.texcoord0.xy * 2 - 1;
				output.positionCS = TransformWViewToHClip(centerVS + float3(corner * size, 0));
				output.uv = TRANSFORM_TEX(input.texcoord0.xy, _BaseMap);
				// The colour is written in gamma space; URP blends in linear
				output.color = half4(SRGBToLinear(input.color.rgb), input.color.a * alpha);
				return output;
			}

			half4 SourceDustFragment(Varyings input) : SV_Target
			{
				half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
				return texel * input.color;
			}
			ENDHLSL
		}
	}
}

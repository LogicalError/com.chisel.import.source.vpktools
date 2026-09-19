// The beam point_spotlight draws from its lamp: a flat strip along the object's +Z that turns about its
// own axis to face the camera, which is how Source draws beams. Unlike the glows it is depth tested, so
// walls and floors hide the parts of it they should.
//
// The transform carries the beam: its position is the lamp, +Z the direction, the x scale the width and
// the z scale the length, both in meters.
Shader "Chisel/Import/SpotlightBeam"
{
	Properties
	{
		// [MainTexture]: the importer hands the texture over as Material.mainTexture
		[MainTexture] _BaseMap ("Beam Texture", 2D) = "white" {}
		_TintColor ("Tint Color", Color) = (1,1,1,1)
		_Intensity ("Intensity", Float) = 1
	}

	SubShader
	{
		Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "DisableBatching" = "True" "RenderPipeline" = "UniversalPipeline" }

		Blend One One
		ZWrite Off
		Cull Off

		Pass
		{
			Name "SpotlightBeam"

			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex vert
			#pragma fragment frag
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float4 _BaseMap_ST;
				float4 _TintColor;
				float  _Intensity;
			CBUFFER_END

			TEXTURE2D(_BaseMap);
			SAMPLER(sampler_BaseMap);

			struct Attributes
			{
				// x in [-0.5, 0.5] across the beam, z in [0, 1] from the lamp to the far end.
				float4 positionOS : POSITION;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv         : TEXCOORD0;
				float3 color      : TEXCOORD1;
				float3 positionWS : TEXCOORD2;
				half   fogFactor  : TEXCOORD3;
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;
				float4x4 objectToWorld = GetObjectToWorldMatrix();
				float3 lamp = TransformObjectToWorld(float3(0, 0, 0));
				float3 axis = objectToWorld._m02_m12_m22;
				float beamLength = length(axis);
				axis /= max(beamLength, 1e-5);
				float beamWidth = length(objectToWorld._m00_m10_m20);

				float along = input.positionOS.z;
				float3 position = lamp + axis * (along * beamLength);
				float3 toCamera = GetCameraPositionWS() - position;

				float3 side = cross(axis, toCamera);
				float sideLength = length(side);
				// Seen exactly end-on there is no sideways to speak of. Any perpendicular will do, since the
				// fade below has the beam gone by then.
				side = (sideLength > 1e-5) ? side / sideLength
										   : normalize(cross(axis, (abs(axis.y) < 0.99) ? float3(0, 1, 0) : float3(1, 0, 0)));
				position += side * (input.positionOS.x * beamWidth);

				output.positionCS = TransformWorldToHClip(position);
				output.positionWS = position;
				// Stretched once along the length (FBEAM_NOTILE), starting at the lamp.
				output.uv = TRANSFORM_TEX(float2(input.positionOS.x + 0.5, 1 - along), _BaseMap);

				// FBEAM_SHADEOUT: full strength at the lamp, nothing at the far end.
				float fade = 1 - along;
				// Stand-in for the lampbeam material proxy. A strip that turns about its own axis thins to
				// nothing when seen end-on, so it fades out as the view lines up with the beam.
				float endOn = abs(dot(axis, normalize(toCamera)));
				fade *= 1 - smoothstep(0.7, 0.98, endOn);

				output.color = _TintColor.rgb * (_Intensity * fade);
				output.fogFactor = ComputeFogFactor(output.positionCS.z);
				return output;
			}

			half4 frag(Varyings input) : SV_Target
			{
				half4 beam = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
				half3 color = beam.rgb * beam.a * input.color;
				// Additive: fog takes it toward black rather than toward the fog colour.
				color = MixFogColor(color, half3(0, 0, 0), InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor));
				return half4(color, 0);
			}
			ENDHLSL
		}
	}
}

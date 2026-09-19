// Source's sprites (env_sprite and friends): an additive quad that turns to face the camera, faded where it meets the
// scene behind it. The fade reads the camera depth texture, which URP makes when the pipeline asset asks for it.
Shader "Particles/Additive (Soft) Camera Aligned"
{
	Properties
	{
		// [MainTexture]: the importer hands the texture over as Material.mainTexture
		[MainTexture] _BaseMap ("Particle Texture", 2D) = "white" {}
		_TintColor ("Tint Color", Color) = (1,1,1,1)
		_InvFade ("Soft Particles Factor", Range(0.01,3.0)) = 1.0
		_Alpha ("Alpha", Range(0.01,1.0)) = 1.0
	}

	SubShader
	{
		Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "DisableBatching" = "True" "RenderPipeline" = "UniversalPipeline" }

		Blend One OneMinusSrcColor
		ColorMask RGB
		Cull Off
		ZWrite Off

		Pass
		{
			Name "Sprite"

			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex vert
			#pragma fragment frag
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float4 _BaseMap_ST;
				half4  _TintColor;
				float  _InvFade;
				half   _Alpha;
			CBUFFER_END

			TEXTURE2D(_BaseMap);
			SAMPLER(sampler_BaseMap);

			struct Attributes
			{
				float4 positionOS : POSITION;
				half4  color      : COLOR;
				float2 uv         : TEXCOORD0;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				half4  color      : COLOR;
				float2 uv         : TEXCOORD0;
				float3 positionWS : TEXCOORD1;
				float  eyeDepth   : TEXCOORD2;
				half   fogFactor  : TEXCOORD3;
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;

				// The quad is built in view space around the object's origin so it always faces
				// the camera, which means the model matrix cannot size it the way it sizes an
				// ordinary mesh. Take the scale off the matrix by hand - the length of each
				// basis column - so a shared unit quad can still be sized by its transform.
				float4x4 objectToWorld = GetObjectToWorldMatrix();
				float2 objectScale = float2(length(objectToWorld._m00_m10_m20), length(objectToWorld._m01_m11_m21));

				float3 originView = TransformWorldToView(TransformObjectToWorld(float3(0, 0, 0)));
				float3 positionView = originView - float3(input.positionOS.x * objectScale.x, input.positionOS.y * objectScale.y, -0.5);
				output.positionCS = TransformWViewToHClip(positionView);
				output.positionWS = mul(UNITY_MATRIX_I_V, float4(positionView, 1.0)).xyz;
				output.eyeDepth = -positionView.z;

				output.color = input.color;
				output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
				output.fogFactor = ComputeFogFactor(output.positionCS.z);
				return output;
			}

			half4 frag(Varyings input) : SV_Target
			{
				// Soft edge where the quad meets the scene behind it. Built-in Unity did this under SOFTPARTICLES_ON,
				// which the quality settings turned on; URP has no such global switch.
				float rawDepth = SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS));
				float sceneDepth = IsPerspectiveProjection() ? LinearEyeDepth(rawDepth, _ZBufferParams) : LinearDepthToEyeDepth(rawDepth);
				half fade = saturate(_InvFade * (sceneDepth - input.eyeDepth));

				// _TintColor carries the per-sprite colour: a Source env_sprite tints and dims a
				// shared glow material with its own rendercolor/renderamt, and the alpha rides
				// through the premultiply below as the brightness.
				half4 color = input.color;
				color.a *= fade * _Alpha;
				color *= _TintColor * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
				color.rgb *= color.a;
				// Fog towards black, because of the additive blend
				color.rgb = MixFogColor(color.rgb, half3(0, 0, 0), InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor));
				return color;
			}
			ENDHLSL
		}
	}
}

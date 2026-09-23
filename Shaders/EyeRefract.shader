// Unity equivalent of Source's "EyeRefract" shader, used by eye materials on characters.
//
// An eye material has NO $basetexture - its colour is $Iris, which Valve's own shader comments call the
// "Unlit iris, pupil, and sclera color". Without this shader such a material fell through to URP/Lit,
// which found no base texture and drew the eye pure white on every character in the game.
//
// The port and what it approximates are documented in ChiselSourceEyePass.hlsl.
Shader "Chisel/Source/EyeRefract"
{
	Properties
	{
		[MainTexture] _Iris ("$iris", 2D) = "white" {}
		_CorneaTexture ("$corneatexture", 2D) = "black" {}
		_AmbientOcclTexture ("$ambientoccltexture", 2D) = "white" {}
		[NoScaleOffset] _EnvMap ("$envmap", Cube) = "" {}

		_AmbientOcclColor ("$ambientocclcolor", Color) = (0.33, 0.33, 0.33, 1)
		_EyeballRadius ("$eyeballradius", Float) = 0.5
		_Dilation ("$dilation", Range(0,1)) = 0.5
		_Glossiness ("$glossiness", Float) = 1.0
		_CorneaBumpStrength ("$corneabumpstrength", Float) = 1.0
		_ParallaxStrength ("$parallaxstrength", Float) = 0.25

		// An eye is always opaque and always two-sided-free; these exist so the importer's shared
		// render-state code has somewhere to write, and so the inspector shows what is actually in force.
		[HideInInspector] _Cull ("__cull", Float) = 2
	}

	SubShader
	{
		Tags
		{
			"RenderType" = "Opaque"
			"Queue" = "Geometry"
			"RenderPipeline" = "UniversalPipeline"
			"UniversalMaterialType" = "Lit"
			"IgnoreProjector" = "True"
		}
		LOD 300

		Pass
		{
			Name "ForwardLit"
			// UniversalForwardOnly, not UniversalForward. Under Deferred or Deferred+ an OPAQUE object is drawn
			// through the GBuffer, and URP simply skips a UniversalForward pass for it - no error, no warning,
			// nothing drawn. This shader has no GBuffer pass and should not have one: it lights the eye itself,
			// unlit iris plus cornea glints, and a GBuffer would hand it to URP's deferred lighting and lose
			// exactly what the port is for. UniversalForwardOnly is the tag for that case, and it is drawn in
			// every rendering mode - URP's forward pass takes SRPDefaultUnlit, UniversalForward AND
			// UniversalForwardOnly.
			//
			// This is why the eyes vanished when the renderer moved to Deferred+ (m_RenderingMode 3) while every
			// other Source shader kept working: the other opaque ones carry a GBuffer pass.
			Tags { "LightMode" = "UniversalForwardOnly" }

			ZWrite On
			Cull [_Cull]

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex SourceEyeVertex
			#pragma fragment SourceEyeFragment

			#pragma shader_feature_local_fragment _ENVMAP

			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
			#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
			#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
			#pragma multi_compile _ _CLUSTER_LIGHT_LOOP
			#pragma multi_compile_instancing
			// A BatchRendererGroup refuses any pass with no DOTS_INSTANCING_ON variant, at draw submission and
			// not at compile - so the symptom is silence and nothing drawn. See ChiselSourceEyePass.hlsl for
			// which of this material's properties are read per instance.
			#pragma multi_compile _ DOTS_INSTANCING_ON

			#include "ChiselSourceEyePass.hlsl"
			ENDHLSL
		}

		Pass
		{
			Name "ShadowCaster"
			Tags { "LightMode" = "ShadowCaster" }

			ZWrite On
			ZTest LEqual
			ColorMask 0
			Cull [_Cull]

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex ShadowVertex
			#pragma fragment ShadowFragment
			#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
			#pragma multi_compile_instancing
			// A BatchRendererGroup refuses any pass with no DOTS_INSTANCING_ON variant, at draw submission and
			// not at compile - so the symptom is silence and nothing drawn. See ChiselSourceEyePass.hlsl for
			// which of this material's properties are read per instance.
			#pragma multi_compile _ DOTS_INSTANCING_ON

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

			float3 _LightDirection;
			float3 _LightPosition;

			struct ShadowAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
			struct ShadowVaryings { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

			ShadowVaryings ShadowVertex(ShadowAttributes input)
			{
				ShadowVaryings output = (ShadowVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
				float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
			#if _CASTING_PUNCTUAL_LIGHT_SHADOW
				float3 lightDirectionWS = normalize(_LightPosition - positionWS);
			#else
				float3 lightDirectionWS = _LightDirection;
			#endif
				output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
			#if UNITY_REVERSED_Z
				output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
			#else
				output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
			#endif
				return output;
			}

			half4 ShadowFragment(ShadowVaryings input) : SV_Target { return 0; }
			ENDHLSL
		}

		Pass
		{
			// Without this the eye contributes NOTHING to _CameraDepthTexture. URP picks a depth-NORMALS
			// prepass whenever anything wants normals - the Decal feature alone is enough - and DepthOnly
			// is not a fallback for it. A shader missing this pass is invisible to fog, decals and every
			// other depth reader, which reads as a sorting bug rather than a missing pass.
			Name "DepthNormalsOnly"
			Tags { "LightMode" = "DepthNormalsOnly" }

			ZWrite On
			Cull [_Cull]

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex DepthNormalsVertex
			#pragma fragment DepthNormalsFragment
			#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
			#pragma multi_compile_instancing
			// A BatchRendererGroup refuses any pass with no DOTS_INSTANCING_ON variant, at draw submission and
			// not at compile - so the symptom is silence and nothing drawn. See ChiselSourceEyePass.hlsl for
			// which of this material's properties are read per instance.
			#pragma multi_compile _ DOTS_INSTANCING_ON

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"

			struct DepthNormalsAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
			struct DepthNormalsVaryings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

			DepthNormalsVaryings DepthNormalsVertex(DepthNormalsAttributes input)
			{
				DepthNormalsVaryings output = (DepthNormalsVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				// The same sphere normal the lit pass uses, so depth-normals agree with what is drawn.
				float3 normalOS = (dot(input.positionOS.xyz, input.positionOS.xyz) > 1e-8) ? normalize(input.positionOS.xyz)
																						  : input.normalOS;
				output.normalWS = TransformObjectToWorldNormal(normalOS);
				return output;
			}

			half4 DepthNormalsFragment(DepthNormalsVaryings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				#if defined(_GBUFFER_NORMALS_OCT)
					float2 octNormalWS = PackNormalOctQuadEncode(normalize(input.normalWS));
					float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
					half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
					return half4(packedNormalWS, 0.0);
				#else
					return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
				#endif
			}
			ENDHLSL
		}
	}

	Fallback Off
}

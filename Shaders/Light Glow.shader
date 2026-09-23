// Source's glow overlays: env_lightglow, and the halo point_spotlight draws at its lamp.
//
// Source draws these on top of everything ($ignorez) and gets away with it because every glow runs an
// occlusion query against a small sphere around itself (GlowProxySize) and fades by how much of that
// sphere is in view. Drawing on top without the query is exactly what makes a glow show through walls,
// so the query is reproduced here instead: the vertex stage samples the camera depth texture across the
// sphere's footprint on screen. URP makes that texture when the pipeline asset asks for it (Depth Texture),
// and always for the scene view.
//
// The transform carries the glow: its position is the centre, x/y scale the full width and height,
// and +Z the direction it faces for the one-sided and halo fades.
Shader "Chisel/Import/LightGlow"
{
	Properties
	{
		// [MainTexture]: the importer hands the texture over as Material.mainTexture
		[MainTexture] _BaseMap ("Glow Texture", 2D) = "white" {}
		_TintColor ("Tint Color", Color) = (1,1,1,1)
		_Intensity ("Intensity", Float) = 1
		// x = MinDist, y = MaxDist, z = OuterMaxDist, in meters.
		_FadeDistances ("Fade Distances", Vector) = (0,0,0,0)
		_ProxyRadius ("Proxy Radius", Float) = 0.04
		// 0 = visible from every side. Otherwise the view has to be in front of +Z, and this multiplies
		// the dot product before it is clamped: large for a hard cut, 2 for a spotlight halo.
		_DirectionalFade ("Directional Fade", Float) = 0
	}

	SubShader
	{
		Tags { "Queue" = "Transparent+1" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "DisableBatching" = "True" "RenderPipeline" = "UniversalPipeline" }

		Blend One One
		ZTest Always
		ZWrite Off
		Cull Off

		Pass
		{
			Name "LightGlow"

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag
			// A BatchRendererGroup - the GPU Resident Drawer, or Entities Graphics - refuses a pass with no
			// DOTS_INSTANCING_ON variant, at draw submission rather than at compile, so the symptom is silence.
			#pragma multi_compile _ DOTS_INSTANCING_ON

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float4 _BaseMap_ST;
				float4 _TintColor;
				float  _Intensity;
				float4 _FadeDistances;
				float  _ProxyRadius;
				float  _DirectionalFade;
			CBUFFER_END

			TEXTURE2D(_BaseMap);
			SAMPLER(sampler_BaseMap);

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv         : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv         : TEXCOORD0;
				float3 color      : TEXCOORD1;
			};

			// The centre and two rings across the proxy sphere's footprint, in units of its radius.
			static const float2 kProxyTaps[9] =
			{
				float2( 0.0,   0.0),
				float2( 0.7,   0.0),  float2( 0.0,   0.7),  float2(-0.7,   0.0),  float2( 0.0,  -0.7),
				float2( 0.35,  0.35), float2(-0.35,  0.35), float2(-0.35, -0.35), float2( 0.35, -0.35)
			};

			// Source's RemapValClamped, including what it does with an empty range.
			float RemapValClamped(float value, float a, float b, float c, float d)
			{
				if (a == b)
					return (value >= b) ? d : c;
				return c + (d - c) * saturate((value - a) / (b - a));
			}

			// How much of the proxy sphere is in front of the scene, from 0 to 1.
			float ProxyVisibility(float3 originView)
			{
				float eyeDepth = -originView.z;
				if (eyeDepth <= _ProxyRadius)
					return 1;

				// Screen uv the way URP's own passes read the depth texture with (VertexPositionInputs.positionNDC)
				float4 originCS = TransformWViewToHClip(originView);
				float4 ndc = originCS * 0.5;
				float2 centre = (float2(ndc.x, ndc.y * _ProjectionParams.x) + ndc.w) / originCS.w;

				bool perspective = IsPerspectiveProjection();
				float2 radius = 0.5 * (perspective ? _ProxyRadius / eyeDepth : _ProxyRadius) *
								float2(UNITY_MATRIX_P[0][0], UNITY_MATRIX_P[1][1]);
				float sphereFront = eyeDepth - _ProxyRadius;

				float visible = 0;
				float tested = 0;
				for (int i = 0; i < 9; i++)
				{
					float2 uv = centre + kProxyTaps[i] * radius;
					// Only what is on screen can be tested, so off-screen taps are left out of the count.
					float onScreen = step(0, uv.x) * step(uv.x, 1) * step(0, uv.y) * step(uv.y, 1);
					// SampleSceneDepth loads the texel rather than filtering it, which a vertex stage can do.
					float rawDepth = SampleSceneDepth(saturate(uv));
					float sceneDepth = perspective ? LinearEyeDepth(rawDepth, _ZBufferParams) : LinearDepthToEyeDepth(rawDepth);
					visible += onScreen * step(sphereFront, sceneDepth);
					tested += onScreen;
				}
				return (tested > 0) ? (visible / tested) : 1;
			}

			Varyings vert(Attributes input)
			{
				UNITY_SETUP_INSTANCE_ID(input);
				Varyings output = (Varyings)0;
				float4x4 objectToWorld = GetObjectToWorldMatrix();
				float3 originWorld = TransformObjectToWorld(float3(0, 0, 0));
				float2 size = float2(length(objectToWorld._m00_m10_m20), length(objectToWorld._m01_m11_m21));
				float3 facing = normalize(objectToWorld._m02_m12_m22);

				float3 originView = TransformWorldToView(originWorld);
				output.positionCS = TransformWViewToHClip(originView + float3(input.positionOS.xy * size, 0));
				output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

				float3 toViewer = GetCameraPositionWS() - originWorld;
				float viewerDistance = length(toViewer);
				toViewer /= max(viewerDistance, 1e-5);

				// C_LightGlowOverlay: fade in between MinDist and MaxDist; past MaxDist, fade back out by
				// OuterMaxDist when that is further away.
				float fade = (_FadeDistances.z > _FadeDistances.y && viewerDistance > _FadeDistances.y)
							? RemapValClamped(viewerDistance, _FadeDistances.y, _FadeDistances.z, 1, 0)
							: RemapValClamped(viewerDistance, _FadeDistances.x, _FadeDistances.y, 0, 1);

				if (_DirectionalFade > 0)
					fade *= saturate(dot(toViewer, facing) * _DirectionalFade);

				if (fade > 0)
					fade *= ProxyVisibility(originView);

				output.color = _TintColor.rgb * (_Intensity * fade);

				// Nothing to draw: collapse the quad to a point so it is never rasterized.
				if (fade <= 0)
					output.positionCS = float4(0, 0, 0, 1);
				return output;
			}

			half4 frag(Varyings input) : SV_Target
			{
				half4 glow = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
				return half4(glow.rgb * glow.a * input.color, 0);
			}
			ENDHLSL
		}
	}
}

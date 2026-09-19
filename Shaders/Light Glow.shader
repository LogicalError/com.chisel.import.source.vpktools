// Source's glow overlays: env_lightglow, and the halo point_spotlight draws at its lamp.
//
// Source draws these on top of everything ($ignorez) and gets away with it because every glow runs an
// occlusion query against a small sphere around itself (GlowProxySize) and fades by how much of that
// sphere is in view. Drawing on top without the query is exactly what makes a glow show through walls,
// so the query is reproduced here instead: the vertex stage samples the camera depth texture across the
// sphere's footprint on screen. A Deferred camera always has that texture; a Forward camera has to ask
// for it with DepthTextureMode.Depth.
//
// The transform carries the glow: its position is the centre, x/y scale the full width and height,
// and +Z the direction it faces for the one-sided and halo fades.
Shader "Chisel/Import/LightGlow"
{
	Properties
	{
		_BaseMap ("Glow Texture", 2D) = "white" {}
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
		Tags { "Queue" = "Transparent+1" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "DisableBatching" = "True" }

		Blend One One
		ZTest Always
		ZWrite Off
		Cull Off

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.5

			#include "UnityCG.cginc"

			sampler2D _BaseMap;
			float4 _BaseMap_ST;
			float4 _TintColor;
			float _Intensity;
			float4 _FadeDistances;
			float _ProxyRadius;
			float _DirectionalFade;
			UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 color : TEXCOORD1;
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
			float ProxyVisibility(float4 originView)
			{
				float eyeDepth = -originView.z;
				if (eyeDepth <= _ProxyRadius)
					return 1;

				float4 screen = ComputeScreenPos(mul(UNITY_MATRIX_P, originView));
				float2 centre = screen.xy / screen.w;
				float2 radius = 0.5 * (_ProxyRadius / eyeDepth) * float2(UNITY_MATRIX_P[0][0], UNITY_MATRIX_P[1][1]);
				float sphereFront = eyeDepth - _ProxyRadius;

				float visible = 0;
				float tested = 0;
				for (int i = 0; i < 9; i++)
				{
					float2 uv = centre + kProxyTaps[i] * radius;
					// Only what is on screen can be tested, so off-screen taps are left out of the count.
					float onScreen = step(0, uv.x) * step(uv.x, 1) * step(0, uv.y) * step(uv.y, 1);
					float sceneDepth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uv, 0, 0)));
					visible += onScreen * step(sphereFront, sceneDepth);
					tested += onScreen;
				}
				return (tested > 0) ? (visible / tested) : 1;
			}

			v2f vert(appdata v)
			{
				v2f o;
				float3 originWorld = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
				float2 size = float2(length(unity_ObjectToWorld._m00_m10_m20), length(unity_ObjectToWorld._m01_m11_m21));
				float3 facing = normalize(unity_ObjectToWorld._m02_m12_m22);

				float4 originView = mul(UNITY_MATRIX_V, float4(originWorld, 1));
				o.vertex = mul(UNITY_MATRIX_P, originView + float4(v.vertex.xy * size, 0, 0));
				o.uv = TRANSFORM_TEX(v.uv, _BaseMap);

				float3 toViewer = _WorldSpaceCameraPos - originWorld;
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

				o.color = _TintColor.rgb * (_Intensity * fade);

				// Nothing to draw: collapse the quad to a point so it is never rasterized.
				if (fade <= 0)
					o.vertex = float4(0, 0, 0, 1);
				return o;
			}

			float4 frag(v2f i) : SV_Target
			{
				float4 glow = tex2D(_BaseMap, i.uv);
				return float4(glow.rgb * glow.a * i.color, 0);
			}
			ENDCG
		}
	}
}

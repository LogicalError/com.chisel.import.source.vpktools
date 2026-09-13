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
		_MainTex ("Beam Texture", 2D) = "white" {}
		_TintColor ("Tint Color", Color) = (1,1,1,1)
		_Intensity ("Intensity", Float) = 1
	}

	SubShader
	{
		Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "DisableBatching" = "True" }

		Blend One One
		ZWrite Off
		Cull Off

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fog

			#include "UnityCG.cginc"

			sampler2D _MainTex;
			float4 _MainTex_ST;
			float4 _TintColor;
			float _Intensity;

			struct appdata
			{
				// x in [-0.5, 0.5] across the beam, z in [0, 1] from the lamp to the far end.
				float4 vertex : POSITION;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 color : TEXCOORD1;
				UNITY_FOG_COORDS(2)
			};

			v2f vert(appdata v)
			{
				v2f o;
				float3 lamp = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
				float3 axis = unity_ObjectToWorld._m02_m12_m22;
				float beamLength = length(axis);
				axis /= max(beamLength, 1e-5);
				float beamWidth = length(unity_ObjectToWorld._m00_m10_m20);

				float along = v.vertex.z;
				float3 position = lamp + axis * (along * beamLength);
				float3 toCamera = _WorldSpaceCameraPos - position;

				float3 side = cross(axis, toCamera);
				float sideLength = length(side);
				// Seen exactly end-on there is no sideways to speak of. Any perpendicular will do, since the
				// fade below has the beam gone by then.
				side = (sideLength > 1e-5) ? side / sideLength
										   : normalize(cross(axis, (abs(axis.y) < 0.99) ? float3(0, 1, 0) : float3(1, 0, 0)));
				position += side * (v.vertex.x * beamWidth);

				o.vertex = mul(UNITY_MATRIX_VP, float4(position, 1));
				// Stretched once along the length (FBEAM_NOTILE), starting at the lamp.
				o.uv = TRANSFORM_TEX(float2(v.vertex.x + 0.5, 1 - along), _MainTex);

				// FBEAM_SHADEOUT: full strength at the lamp, nothing at the far end.
				float fade = 1 - along;
				// Stand-in for the lampbeam material proxy. A strip that turns about its own axis thins to
				// nothing when seen end-on, so it fades out as the view lines up with the beam.
				float endOn = abs(dot(axis, normalize(toCamera)));
				fade *= 1 - smoothstep(0.7, 0.98, endOn);

				o.color = _TintColor.rgb * (_Intensity * fade);
				UNITY_TRANSFER_FOG(o, o.vertex);
				return o;
			}

			float4 frag(v2f i) : SV_Target
			{
				float4 beam = tex2D(_MainTex, i.uv);
				float4 col = float4(beam.rgb * beam.a * i.color, 0);
				// Additive: fog takes it toward black rather than toward the fog colour.
				UNITY_APPLY_FOG_COLOR(i.fogCoord, col, fixed4(0, 0, 0, 0));
				return col;
			}
			ENDCG
		}
	}
}

// Black Mesa's lens flares (env_lensflare), drawn the way its client draws them (C_Lensflare in bms/bin/client.dll): up
// to twenty elements, each a textured quad in screen space along the line from the light through the centre of the
// screen. Each is brightened, sized and turned by where on the screen the light is, and faded by distance, by a light
// style and by how much of the GlowProxySize sphere around the light is in view.
//
// The importer (VmfLensFlare) builds one quad per element and hands each its parameters in the vertex data, so a material
// is only a texture, and every flare in a map shares them.
Shader "Chisel/Import/LensFlare"
{
	Properties
	{
		[MainTexture] _BaseMap ("Flare Texture", 2D) = "white" {}
	}

	SubShader
	{
		Tags { "Queue" = "Transparent+2" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "DisableBatching" = "True" "RenderPipeline" = "UniversalPipeline" }

		// The flare materials (effects/lensflare/*) are UnlitGeneric with $additive, $translucent, $vertexcolor,
		// $vertexalpha and $ignorez
		Blend SrcAlpha One
		ZTest Always
		ZWrite Off
		Cull Off

		Pass
		{
			Name "LensFlare"

			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex SourceLensFlareVertex
			#pragma fragment SourceLensFlareFragment

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "ChiselSourceProxyVisibility.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float4 _BaseMap_ST;
			CBUFFER_END

			TEXTURE2D(_BaseMap);
			SAMPLER(sampler_BaseMap);

			// The element types C_Lensflare parses flareNN_params into
			#define FLARE_DISK  1   // "DISK position width height [1]", and "CLA" the same way
			#define FLARE_BOW   2   // "BOW size"
			#define FLARE_RAY   3   // "RAY width height"
			#define FLARE_DISKH 4   // "DISKH position width height offset"

			struct Attributes
			{
				float3 corner : POSITION;     // x: 0 left, 1 right; y: 0 top, 1 bottom
				half4  color  : COLOR;        // flareNN_color as the map writes it
				float2 uv     : TEXCOORD0;
				float4 shape  : TEXCOORD1;    // x: type; yzw: the parameters after it
				float4 ramp   : TEXCOORD2;    // x: DISKH's offset, or DISK's 1 to turn it; yzw: flareNN_intensity
				float4 sizes  : TEXCOORD3;    // xyz: flareNN_sizes; w: 32 units, in meters
				float4 flare  : TEXCOORD4;    // FlareAttenuation (meters, 0 for none), GlowProxySize (meters), FlareStyle, FlareType
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv         : TEXCOORD0;
				half4  color      : TEXCOORD1;
			};

			// The engine's light styles 0-12, stepped through ten times a second: 'a' is off, 'm' normal, 'z' double
			static const uint kStyleStart[13]  = { 0, 1, 24, 75, 109, 121, 154, 171, 199, 241, 257, 282, 318 };
			static const uint kStyleLength[13] = { 1, 23, 51, 34, 12, 33, 17, 28, 42, 16, 25, 36, 13 };
			static const uint kStyleLetters[331] =
			{
				12, 12, 12, 13, 12, 12, 14, 12, 12, 14, 12, 12, 13, 14, 13, 12, 12, 14, 13, 16, 13, 12, 12, 14, 0, 1, 2, 3, 4, 5, 6, 7,
				8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 24, 23, 22, 21, 20, 19, 18, 17, 16, 15, 14, 13, 12, 11,
				10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 12, 12, 12, 12, 12, 0, 0, 0, 0, 0, 12, 12, 12, 12, 12, 0, 0, 0, 0, 0, 0,
				1, 2, 3, 4, 5, 6, 0, 1, 2, 3, 4, 5, 6, 12, 0, 12, 0, 12, 0, 12, 0, 12, 0, 12, 0, 9, 10, 11, 12, 13, 14, 15,
				16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 24, 23, 22, 21, 20, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 13, 12, 14, 13, 16, 13,
				12, 14, 12, 13, 12, 14, 12, 14, 12, 13, 14, 12, 12, 12, 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 12, 12, 12, 12, 0, 0, 0, 0,
				12, 12, 12, 0, 0, 12, 12, 12, 12, 12, 0, 0, 0, 12, 12, 12, 0, 0, 0, 12, 12, 12, 0, 1, 2, 3, 4, 5, 0, 0, 0, 0,
				12, 12, 12, 12, 0, 1, 2, 3, 4, 5, 12, 12, 12, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 25, 25, 25, 25, 25, 25, 25,
				25, 12, 12, 0, 12, 0, 12, 12, 12, 12, 0, 12, 12, 0, 12, 0, 12, 0, 0, 0, 12, 0, 12, 12, 12, 0, 0, 1, 2, 3, 4, 5,
				6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 12, 12,
				13, 13, 12, 12, 13, 13, 13, 12, 12, 13, 13
			};

			float SourceLightStyle(float style)
			{
				uint index = (uint)clamp(style, 0, 12);
				uint frame = (uint)(_Time.y * 10.0) % kStyleLength[index];
				return kStyleLetters[kStyleStart[index] + frame] / 12.0;
			}

			// flareNN_intensity and flareNN_sizes: the first value with the light at the centre of the screen, the second a
			// quarter of the screen's height away from it, the third half its height and further
			float SourceFlareRamp(float distance, float quarter, float halfHeight, float3 values)
			{
				if (quarter > distance)
					return values.x + (values.y - values.x) / quarter * distance;
				if (distance >= quarter && halfHeight >= distance)
					return values.y + (values.z - values.y) / quarter * (distance - quarter);
				return values.z;
			}

			Varyings SourceLensFlareVertex(Attributes input)
			{
				Varyings output = (Varyings)0;
				// Nothing to draw leaves the quad a point, which is never rasterized
				output.positionCS = float4(0, 0, 0, 1);

				// Where the light is: the entity, or for a sun flare the sun, as far along its direction as the view
				// reaches (the object's +Z points at it)
				float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
				if (input.flare.w > 0.5)
				{
					float far = _ProjectionParams.z;
					originWS = GetCameraPositionWS() + TransformObjectToWorldDir(float3(0, 0, 1)) * max(far - input.sizes.w, far * 0.875);
				}

				float4 originCS = TransformWorldToHClip(originWS);
				if (originCS.w <= 0)
					return output;

				// Faded by distance past FlareAttenuation, by the light style, and by how much of the proxy sphere is in view
				float strength = SourceLightStyle(input.flare.z);
				if (input.flare.x > 0)
					strength *= 1 - distance(GetCameraPositionWS(), originWS) / input.flare.x;
				if (strength <= 0)
					return output;
				strength *= ProxyVisibility(TransformWorldToView(originWS), input.flare.y);
				if (strength <= 0)
					return output;

				// The client's screen: pixels, y down
				float2 screen = _ScreenParams.xy;
				float2 ndc = originCS.xy / originCS.w;
				ndc.y *= _ProjectionParams.x;
				float2 light  = float2((ndc.x + 1) * 0.5 * screen.x, screen.y - (ndc.y + 1) * 0.5 * screen.y);
				float2 centre = 0.5 * screen;
				float2 toCentre = centre - light;
				float fromCentre = length(toCentre);
				float quarter = 0.25 * screen.y;
				float intensity = max(SourceFlareRamp(fromCentre, quarter, centre.y, input.ramp.yzw), 0);
				float size = SourceFlareRamp(fromCentre, quarter, centre.y, input.sizes.xyz);

				// The element's point on the line through the centre: 0 at the light, 1 at the centre, 2 mirrored
				int type = (int)round(input.shape.x);
				float along = (type == FLARE_DISK || type == FLARE_DISKH) ? input.shape.y : 0;
				float2 position = light + toCentre * along;

				float2 extent;
				float2 topLeft;
				float angle = 0;
				if (type == FLARE_BOW)
				{
					// As wide as the light is far from the centre, standing on the light and turned to face the centre
					float bow = input.shape.y * fromCentre;
					extent = float2(bow, bow);
					topLeft = float2(position.x - bow * 0.5, position.y - bow);
					angle = -(atan2(light.y - centre.y, light.x - centre.x) - 1.5708);
				}
				else
				{
					// A hundredth of the screen's height per unit, times the size
					float2 factors = (type == FLARE_RAY) ? input.shape.yz : input.shape.zw;
					extent = screen.y * factors * 0.01 * size;
					if (type == FLARE_DISKH)
					{
						// Along the line horizontally only: it stays at the light's height, offset upwards
						topLeft = float2(position.x - extent.x * 0.5, light.y - input.ramp.x * screen.y * 0.01);
					}
					else if (type == FLARE_DISK && input.ramp.x > 0.5)
					{
						// Standing on its point and turned along the line
						float lineAngle = atan2(light.y - centre.y, light.x - centre.x) + ((input.shape.y >= 0) ? -1.5708 : 1.5708);
						angle = -lineAngle;
						topLeft = float2(position.x - extent.x * 0.5, position.y - extent.y);
					}
					else
					{
						topLeft = position - extent * 0.5;
					}
				}
				// Whole pixels, as the client hands the quad over
				topLeft = trunc(topLeft);
				extent = trunc(extent);

				// The quad in clip space, with Direct3D 9's half pixel, turned about the middle of its bottom edge
				float left   = (topLeft.x - 0.5) * 2 / screen.x - 1;
				float right  = (topLeft.x + extent.x - 0.5) * 2 / screen.x - 1;
				float top    = 1 - (topLeft.y - 0.5) * 2 / screen.y;
				float bottom = 1 - (topLeft.y + extent.y - 0.5) * 2 / screen.y;
				float2 pivot = float2((left + right) * 0.5, bottom);
				float2 corner = float2(input.corner.x > 0.5 ? right : left, input.corner.y > 0.5 ? bottom : top) - pivot;
				float sine, cosine;
				sincos(angle, sine, cosine);
				float2 turned = float2(corner.x * cosine - corner.y * sine, corner.x * sine + corner.y * cosine) + pivot;

				output.positionCS = float4(turned.x, turned.y * _ProjectionParams.x, 0.5, 1);
				output.uv = input.uv;
				// The colour times the intensity, clamped as the client clamps it to a byte, in the linear space URP blends in
				output.color = half4(SRGBToLinear(saturate(input.color.rgb * (intensity * strength))), 1);
				return output;
			}

			half4 SourceLensFlareFragment(Varyings input) : SV_Target
			{
				half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
				return half4(texel.rgb * input.color.rgb, texel.a * input.color.a);
			}
			ENDHLSL
		}
	}
}

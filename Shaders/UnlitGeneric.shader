// Unity equivalent of Source's "UnlitGeneric" shader: no lighting at all, the base texture is the
// final colour. Source uses it for screens, sprites, light glows, HUD-like decals and anything that
// is meant to be fullbright, so routing these through a lit shader is what makes them look dull.
Shader "Chisel/Source/UnlitGeneric"
{
	Properties
	{
		[MainColor] _Color ("$color", Color) = (1,1,1,1)
		[MainTexture] _MainTex ("$basetexture", 2D) = "white" {}
		_Cutoff ("$alphatestreference", Range(0,1)) = 0.5
		[Toggle] _VertexColor ("$vertexcolor", Float) = 0
		[Toggle] _VertexAlpha ("$vertexalpha", Float) = 0

		_EnvMapMask ("$envmapmask", 2D) = "white" {}
		[NoScaleOffset] _EnvMap ("$envmap (a named cubemap)", Cube) = "black" {}
		[Toggle] _EnvMapFromProbes ("$envmap is env_cubemap: the nearest probe", Float) = 1
		_EnvMapTint ("$envmaptint", Color) = (1,1,1,1)
		_EnvMapContrast ("$envmapcontrast", Vector) = (0,0,0,0)
		_EnvMapSaturation ("$envmapsaturation", Vector) = (1,1,1,0)
		_EnvMapMaskWeights ("Envmap mask: -, base alpha, 1 - base alpha, -", Vector) = (0,0,0,0)
		[Toggle] _EnvMapMaskTexture ("Envmap masked by $envmapmask", Float) = 0

		[HideInInspector] _SrcBlend ("__src", Float) = 1.0
		[HideInInspector] _DstBlend ("__dst", Float) = 0.0
		[HideInInspector] _ZWrite ("__zw", Float) = 1.0
		[Enum(UnityEngine.Rendering.CullMode)] _Cull ("$nocull", Float) = 2
	}

	SubShader
	{
		Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
		LOD 100

		Cull [_Cull]
		Blend [_SrcBlend] [_DstBlend]
		ZWrite [_ZWrite]

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fog
			#pragma shader_feature_local _ALPHATEST_ON
			#pragma shader_feature_local _ENVMAP
			// The reflection probes come as a cubemap array (ChiselSourceProbes.cginc)
			#pragma require cubearray

			#include "UnityCG.cginc"
		#ifdef _ENVMAP
			#include "ChiselSourceProbes.cginc"
		#endif

			struct appdata
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
				float2 uv : TEXCOORD0;
				fixed4 color : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				UNITY_FOG_COORDS(1)
				fixed4 color : COLOR;
				float4 vertex : SV_POSITION;
			#ifdef _ENVMAP
				float3 worldPos : TEXCOORD2;
				float3 worldNormal : TEXCOORD3;
			#endif
				UNITY_VERTEX_OUTPUT_STEREO
			};

			sampler2D _MainTex;
			float4 _MainTex_ST;
			fixed4 _Color;
			half _Cutoff;
			half _VertexColor;
			half _VertexAlpha;
		#ifdef _ENVMAP
			UNITY_DECLARE_TEXCUBE_NOSAMPLER(_EnvMap);
			half      _EnvMapFromProbes;
			half3     _EnvMapTint;
			half3     _EnvMapContrast;
			half3     _EnvMapSaturation;
			half4     _EnvMapMaskWeights;
			half      _EnvMapMaskTexture;
			sampler2D _EnvMapMask;
		#endif

			v2f vert(appdata v)
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.color = v.color;
			#ifdef _ENVMAP
				o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
				o.worldNormal = UnityObjectToWorldNormal(v.normal);
			#endif
				UNITY_TRANSFER_FOG(o, o.vertex);
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				fixed4 base = tex2D(_MainTex, i.uv);
				fixed4 col = base * _Color;
				// $vertexcolor: UnlitGeneric takes the vertex colour out of gamma space first
				// (vertexlit_and_unlit_generic_vs20.fxc: GammaToLinear, which is pow(x, 2.2)).
				col.rgb *= lerp(fixed3(1, 1, 1), pow(i.color.rgb, 2.2), _VertexColor);
				col.a   *= lerp(1, i.color.a, _VertexAlpha);
			#ifdef _ALPHATEST_ON
				clip(col.a - _Cutoff);
			#endif
			#ifdef _ENVMAP
				// Source adds the reflection to the unlit colour (vertexlit_and_unlit_generic_ps2x.fxc): masked by
				// $envmapmask or one minus the base alpha, tinted, squared by $envmapcontrast and desaturated by
				// $envmapsaturation, without a Fresnel term.
				float3 viewWorld = normalize(_WorldSpaceCameraPos - i.worldPos);
				float3 reflection = reflect(-viewWorld, normalize(i.worldNormal));
				half3 environment = (_EnvMapFromProbes > 0.5h) ? SourceProbeReflection(i.worldPos, reflection)
															   : UNITY_SAMPLE_TEXCUBE_SAMPLER(_EnvMap, _SourceProbes, reflection).rgb;
				half envMask = (1.0h - dot(_EnvMapMaskWeights, half4(1, 1, 1, 1))) + _EnvMapMaskWeights.y * base.a
							 + _EnvMapMaskWeights.z * (1.0h - base.a);
				half3 reflected = environment * envMask * lerp(half3(1, 1, 1), tex2D(_EnvMapMask, i.uv).rgb, _EnvMapMaskTexture);
				reflected *= _EnvMapTint;
				reflected = lerp(reflected, reflected * reflected, _EnvMapContrast);
				reflected = lerp(dot(reflected, half3(0.299h, 0.587h, 0.114h)).xxx, reflected, _EnvMapSaturation);
				col.rgb += reflected;
			#endif
				UNITY_APPLY_FOG(i.fogCoord, col);
				return col;
			}
			ENDCG
		}
	}

	FallBack "Unlit/Texture"
}

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

			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
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
				UNITY_VERTEX_OUTPUT_STEREO
			};

			sampler2D _MainTex;
			float4 _MainTex_ST;
			fixed4 _Color;
			half _Cutoff;
			half _VertexColor;
			half _VertexAlpha;

			v2f vert(appdata v)
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.color = v.color;
				UNITY_TRANSFER_FOG(o, o.vertex);
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				fixed4 col = tex2D(_MainTex, i.uv) * _Color;
				// $vertexcolor: UnlitGeneric takes the vertex colour out of gamma space first
				// (vertexlit_and_unlit_generic_vs20.fxc: GammaToLinear, which is pow(x, 2.2)).
				col.rgb *= lerp(fixed3(1, 1, 1), pow(i.color.rgb, 2.2), _VertexColor);
				col.a   *= lerp(1, i.color.a, _VertexAlpha);
			#ifdef _ALPHATEST_ON
				clip(col.a - _Cutoff);
			#endif
				UNITY_APPLY_FOG(i.fogCoord, col);
				return col;
			}
			ENDCG
		}
	}

	FallBack "Unlit/Texture"
}

// Unity equivalent of Source's "UnlitTwoTexture": unlit, the base texture multiplied by a second
// texture that carries a transform of its own. Source uses it for screens, fake light shafts and energy
// effects, and the second texture is usually the moving part - 88 of the game's 117 UnlitTwoTexture
// materials scroll it with a TextureScroll proxy.
Shader "Chisel/Source/UnlitTwoTexture"
{
	Properties
	{
		[MainColor] _Color ("$color", Color) = (1,1,1,1)
		[MainTexture] _MainTex ("$basetexture", 2D) = "white" {}
		_Texture2 ("$texture2", 2D) = "white" {}
		// TextureScroll proxies, as a uv offset per second. Set by the importer from the VMT.
		_BaseTextureScroll ("$basetexturetransform scroll", Vector) = (0,0,0,0)
		_Texture2Scroll ("$texture2transform scroll", Vector) = (0,0,0,0)
		_Cutoff ("$alphatestreference", Range(0,1)) = 0.5

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
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				float2 uv2 : TEXCOORD1;
				UNITY_FOG_COORDS(2)
				float4 vertex : SV_POSITION;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			sampler2D _MainTex;
			float4 _MainTex_ST;
			sampler2D _Texture2;
			float4 _Texture2_ST;
			float4 _BaseTextureScroll;
			float4 _Texture2Scroll;
			fixed4 _Color;
			half _Cutoff;

			v2f vert(appdata v)
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				o.vertex = UnityObjectToClipPos(v.vertex);
				// frac() keeps the offset in [0,1) however long the level has been running, which is
				// also what the proxy does - an ever-growing offset loses precision.
				o.uv = TRANSFORM_TEX(v.uv, _MainTex) + frac(_BaseTextureScroll.xy * _Time.y);
				o.uv2 = TRANSFORM_TEX(v.uv, _Texture2) + frac(_Texture2Scroll.xy * _Time.y);
				UNITY_TRANSFER_FOG(o, o.vertex);
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				fixed4 col = tex2D(_MainTex, i.uv) * tex2D(_Texture2, i.uv2) * _Color;
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

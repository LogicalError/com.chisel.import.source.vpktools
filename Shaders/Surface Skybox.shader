Shader "Skybox/Surface Skybox"
{
    Properties
    {
        _Tint("Tint Color", Color) = (.5, .5, .5, .5)
        [Gamma] _Exposure("Exposure", Range(0, 8)) = 1.0
        _Rotation("Rotation", Range(0, 360)) = 0
        _BaseMap("Skybox Cubemap", Cube) = "" {}
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
        }

        ZWrite Off
        ZTest LEqual

        CGINCLUDE

        #pragma target 5.0
        #pragma only_renderers d3d11
        #include "UnityCG.cginc"

        ENDCG

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            samplerCUBE _BaseMap;

            float4 _Tint;
            float _Exposure;
            float _Rotation;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            float4 RotateAroundYInDegrees(float4 vPositionOs, float flDegrees)
            {
                float flRadians = flDegrees * UNITY_PI / 180.0;

                float flSin;
                float flCos;

                sincos(flRadians, flSin, flCos);

                float2x2 m = float2x2(
                    flCos, -flSin,
                    flSin,  flCos
                );

                return float4(
                    mul(m, vPositionOs.xz),
                    vPositionOs.yw
                ).xzyw;
            }

            v2f vert(appdata v)
            {
                v2f o;

                // Get the world position of the skybox vertex.
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

                // Normal transformation.
                float4 clipPos = UnityObjectToClipPos(v.vertex);

                // Force the skybox to the far depth plane.
                // This makes it behave as if it is infinitely far away.
                #if defined(UNITY_REVERSED_Z)
                    clipPos.z = 0.0;
                #else
                    clipPos.z = clipPos.w;
                #endif

                o.pos = clipPos;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 viewDir = normalize(
                    RotateAroundYInDegrees(
                        float4(
                            i.worldPos - _WorldSpaceCameraPos,
                            1
                        ),
                        _Rotation
                    ).xyz
                );

                float3 skyboxColor = texCUBE(_BaseMap, viewDir);

                float4 color;

                color.rgb = saturate(
                    skyboxColor *
                    _Tint.rgb *
                    unity_ColorSpaceDouble.rgb *
                    _Exposure
                );

                color.a = 1.0;

                return color;
            }

            ENDCG
        }
    }
}
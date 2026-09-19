Shader "Skybox/Cubemap Skybox"
{
    Properties
    {
        _Tex ("Cubemap", Cube) = "" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _Exposure ("Exposure", Range(0,8)) = 1
        _Rotation ("Horizontal Rotation", Range(0,360)) = 0
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

            samplerCUBE _Tex;

            float4 _Tint;
            float _Exposure;
            float _Rotation;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 direction : TEXCOORD0;
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
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

                // Normal transformation.
                float4 clipPos = UnityObjectToClipPos(v.vertex);

                // Force the skybox to the far depth plane.
                // This makes it behave as if it is infinitely far away.
                #if defined(UNITY_REVERSED_Z)
                    clipPos.z = 0.0;
                #else
                    clipPos.z = clipPos.w;
                #endif

                o.vertex = clipPos;

                o.direction =
                    worldPos - _WorldSpaceCameraPos;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 direction = normalize(i.direction);

                // Convert degrees to radians.
                float angle = radians(_Rotation);

                float sinAngle = sin(angle);
                float cosAngle = cos(angle);

                // Rotate around world Y.
                float3 rotatedDirection;

                rotatedDirection.x =
                    direction.x * cosAngle -
                    direction.z * sinAngle;

                rotatedDirection.y =
                    direction.y;

                rotatedDirection.z =
                    direction.x * sinAngle +
                    direction.z * cosAngle;

                fixed4 color =
                    texCUBE(_Tex, rotatedDirection);

                color.rgb *= _Tint.rgb;
                color.rgb *= _Exposure;

                return color;
            }

            ENDCG
        }
    }

    FallBack Off
}

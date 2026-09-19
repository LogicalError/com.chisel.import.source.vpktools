// A URP approximation of Source's Water shader. It carries across the VMT's normal map,
// refraction/fog tint and reflection amount and provides the animated reflective surface that a
// func_water_analog brush expects. Screen-space refraction remains a future rendering feature.
Shader "Chisel/Source/Water"
{
    Properties
    {
        [Normal] _BumpMap ("$normalmap", 2D) = "bump" {}
        _WaterTint ("$refracttint / $fogcolor", Color) = (0.2,0.5,0.4,1)
        _Opacity ("Opacity", Range(0,1)) = 0.65
        _ReflectionStrength ("$reflectamount", Range(0,1)) = 0.2
        _NormalStrength ("Normal strength", Range(0,2)) = 0.7
        _ScrollSpeed ("Scroll speed", Vector) = (0.025,0.035,-0.02,0.015)
        [HideInInspector] _SrcBlend ("__src", Float) = 5
        [HideInInspector] _DstBlend ("__dst", Float) = 10
        [HideInInspector] _ZWrite ("__zw", Float) = 0
        [HideInInspector] _Cull ("__cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "Water"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BumpMap_ST;
                half4 _WaterTint;
                half _Opacity, _ReflectionStrength, _NormalStrength;
                float4 _ScrollSpeed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; half fog : TEXCOORD3; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BumpMap);
                output.fog = ComputeFogFactor(p.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uvA = input.uv + frac(_ScrollSpeed.xy * _Time.y);
                float2 uvB = input.uv * 1.37 + frac(_ScrollSpeed.zw * _Time.y);
                half3 nA = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvA), _NormalStrength);
                half3 nB = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvB), _NormalStrength);
                half3 normalWS = normalize(input.normalWS + half3(nA.x + nB.x, 0, nA.y + nB.y) * 0.35h);
                half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half fresnel = pow(1.0h - saturate(dot(normalWS, viewDir)), 5.0h);
                half3 reflection = GlossyEnvironmentReflection(reflect(-viewDir, normalWS), input.positionWS, 0.05h, 1.0h, GetNormalizedScreenSpaceUV(input.positionCS));
                half3 color = lerp(_WaterTint.rgb, reflection, saturate(_ReflectionStrength + fresnel * 0.55h));
                color = MixFog(color, input.fog);
                return half4(color, saturate(_Opacity + fresnel * 0.2h));
            }
            ENDHLSL
        }
    }
    FallBack Off
}

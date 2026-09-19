// Source DecalModulate: the decal texture modulates the framebuffer as 2 * decal * destination.
// Mid-grey is neutral, darker texels stain and brighter texels lighten the surface underneath.
Shader "Chisel/Source/DecalModulate"
{
    Properties
    {
        [MainColor] _Color ("$color", Color) = (1,1,1,1)
        [MainTexture] _BaseMap ("$basetexture", 2D) = "gray" {}
        [HideInInspector] _SrcBlend ("__src", Float) = 2
        [HideInInspector] _DstBlend ("__dst", Float) = 3
        [HideInInspector] _ZWrite ("__zw", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("$nocull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "DecalModulate"
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

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; half fog : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _Color;
                color.rgb = MixFogColor(color.rgb, half3(0.5h, 0.5h, 0.5h), input.fog);
                return color;
            }
            ENDHLSL
        }
    }
    FallBack Off
}

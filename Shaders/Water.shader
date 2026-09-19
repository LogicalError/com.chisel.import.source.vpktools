// Source's Water shader under URP (SDK 2013: water.cpp, water_ps2x_helper.h DrawWater, WaterCheap_ps2x.fxc).
//
// Water that refracts or reflects ("expensive") shows lerp(refraction, reflection, fresnel):
//   - the refraction is the opaque scene behind the surface (URP's opaque texture stands in for _rt_WaterRefraction),
//     offset by the normal map ($refractamount), tinted ($refracttint) and fogged by how far the view runs under the
//     water ($fogcolor over $fogend - $fogstart). Source renders its refraction with height fog below the water plane
//     and keeps the fog amount in the texture's alpha (CalcWaterFogAlpha); here it comes from the depth texture.
//   - the reflection: Source renders the level mirrored in the water plane (_rt_WaterReflection). This takes the
//     env_cubemap nearest to the pixel ($envmap "env_cubemap", ChiselSourceProbes.hlsl) or the cubemap the material names.
// Water that does neither, or $forcecheap, is "cheap": the envmap alone, faded in by fresnel and by distance between
// $cheapwaterstartdistance and $cheapwaterenddistance, over the refraction when the water has one.
Shader "Chisel/Source/Water"
{
    Properties
    {
        [Normal] _BumpMap ("$normalmap", 2D) = "bump" {}
        _BumpTransformU ("$bumptransform u row", Vector) = (1,0,0,0)
        _BumpTransformV ("$bumptransform v row", Vector) = (0,1,0,0)
        _BumpScroll ("TextureScroll on $bumptransform", Vector) = (0,0,0,0)
        [NoScaleOffset] _BumpFrames ("$normalmap frames (AnimatedTexture)", 2DArray) = "" {}
        _BumpFrameRate ("AnimatedTexture frame rate", Float) = 0
        _BumpFrameCount ("Frames", Float) = 0
        _Scroll1 ("$scroll1", Vector) = (0,0,0,0)
        _Scroll2 ("$scroll2", Vector) = (0,0,0,0)
        _RefractTint ("$refracttint", Color) = (1,1,1,1)
        _RefractAmount ("$refractamount", Float) = 0
        _ReflectTint ("$reflecttint", Color) = (1,1,1,1)
        _ReflectAmount ("$reflectamount", Float) = 0.8
        _WaterFogColor ("$fogcolor", Color) = (1,0,0,1)
        _WaterFogRange ("$fogend - $fogstart, in units", Float) = 1
        [NoScaleOffset] _EnvMap ("$envmap", Cube) = "black" {}
        [ToggleUI] _EnvMapFromProbes ("$envmap is env_cubemap", Float) = 1
        [ToggleUI] _Refract ("Refracts ($refracttexture)", Float) = 1
        [ToggleUI] _Reflect ("Reflects ($reflecttexture, expensive)", Float) = 0
        [ToggleUI] _Cheap ("Draws the cheap water", Float) = 1
        [ToggleUI] _CheapBlend ("Cheap water blends in (no $forcecheap)", Float) = 1
        [ToggleUI] _CheapFresnel ("Cheap water fresnel (no $nofresnel)", Float) = 1
        _ReflectBlendFactor ("$reflectblendfactor", Float) = 1
        _CheapWaterDistances ("$cheapwaterstartdistance, $cheapwaterenddistance, in units", Vector) = (9.53, 19.06, 0, 0)
        [HideInInspector] _SrcBlend ("__src", Float) = 5
        [HideInInspector] _DstBlend ("__dst", Float) = 10
        [HideInInspector] _ZWrite ("__zw", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("$nocull", Float) = 2
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
            #pragma require cubearray
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "ChiselSourceProbes.hlsl"

            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D_ARRAY(_BumpFrames); SAMPLER(sampler_BumpFrames);
            TEXTURECUBE(_EnvMap);

            // Source's planar reflection (SourceWaterReflection): the level mirrored in the water plane at
            // _SourceWaterReflectionHeight, rendered for this camera, flipped left to right
            TEXTURE2D(_SourceWaterReflection); SAMPLER(sampler_SourceWaterReflection);
            float _SourceWaterReflectionEnabled;
            float _SourceWaterReflectionHeight;
            CBUFFER_START(UnityPerMaterial)
                float4 _BumpMap_ST;
                float4 _BumpTransformU;
                float4 _BumpTransformV;
                float4 _BumpScroll;
                float4 _Scroll1;
                float4 _Scroll2;
                half4 _RefractTint;
                half4 _ReflectTint;
                half4 _WaterFogColor;
                float4 _CheapWaterDistances;
                float _BumpFrameRate;
                float _BumpFrameCount;
                float _RefractAmount;
                float _ReflectAmount;
                float _WaterFogRange;
                half _EnvMapFromProbes;
                half _Refract;
                half _Reflect;
                half _Cheap;
                half _CheapBlend;
                half _CheapFresnel;
                half _ReflectBlendFactor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half4  tangentWS  : TEXCOORD2; // w: the bitangent's sign
                float2 uv         : TEXCOORD3;
                half   fog        : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = normal.normalWS;
                output.tangentWS = half4(normal.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.uv = input.uv;
                output.fog = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            // The normal map, or with an AnimatedTexture proxy the frame it is at: frame rate times time, from the first frame
            // (CAnimatedTextureProxy)
            half4 SampleNormalMap(float2 uv)
            {
                half4 texel;
                UNITY_BRANCH
                if (_BumpFrameCount > 1.5)
                    texel = SAMPLE_TEXTURE2D_ARRAY(_BumpFrames, sampler_BumpFrames, uv, fmod(floor(_Time.y * _BumpFrameRate), _BumpFrameCount));
                else
                    texel = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv);
                return texel;
            }

            // The normal map, decoded. water_vs20.fxc transforms the coordinates by $bumptransform, which a TextureScroll
            // proxy replaces by a scrolling offset. With $scroll1 (MULTITEXTURE) Source averages two more samples: the
            // coordinates turned 45 degrees at a tenth of their scale and swapped at 0.45, each scrolling by its $scroll.
            half3 WaterNormalTS(float2 uv)
            {
                float2 bumpUV = float2(dot(uv, _BumpTransformU.xy) + _BumpTransformU.w,
                                       dot(uv, _BumpTransformV.xy) + _BumpTransformV.w) + frac(_BumpScroll.xy * _Time.y);
                half4 texel = SampleNormalMap(bumpUV);
                UNITY_BRANCH
                if (_Scroll1.x != 0)
                {
                    float2 uv1 = float2(uv.x + uv.y, uv.y - uv.x) * 0.1 + _Scroll1.xy * _Time.y;
                    float2 uv2 = uv.yx * 0.45 + _Scroll2.xy * _Time.y;
                    texel = 0.33h * (texel + SampleNormalMap(uv1) + SampleNormalMap(uv2));
                }
                return UnpackNormal(texel);
            }

            // The scene behind the water at a screen position, in world space, and its distance along the view
            float3 ScenePosition(float2 screenUV, out float viewDepth)
            {
                float3 positionWS = ComputeWorldSpacePosition(screenUV, SampleSceneDepth(screenUV), UNITY_MATRIX_I_VP);
                viewDepth = LinearEyeDepth(positionWS, GetWorldToViewMatrix());
                return positionWS;
            }

            // How much water fog lies in front of the scene at a screen position: CalcWaterFogAlpha (common_ps_fxc.h), the
            // part of the view that runs under the water plane over the fog range. Source's refraction is rendered with this
            // height fog, and keeps the amount in its alpha for the water shader.
            half WaterFog(float2 screenUV, float waterHeight)
            {
                float viewDepth;
                float3 scene = ScenePosition(screenUV, viewDepth);
                float depthFromWater = waterHeight - scene.y;
                float depthFromEye = max(_WorldSpaceCameraPos.y - scene.y, 1e-4);
                return saturate(saturate(depthFromWater / depthFromEye) * viewDepth / _WaterFogRange);
            }

            // What the envmap reflects: the env_cubemap nearest to the pixel, or the cubemap the material names
            half3 EnvMapReflection(float3 positionWS, float3 direction)
            {
                return (_EnvMapFromProbes > 0.5h) ? SourceProbeReflection(positionWS, direction)
                                                  : SAMPLE_TEXTURECUBE(_EnvMap, sampler_SourceProbes, direction).rgb;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalTS = WaterNormalTS(input.uv);
                float sgn = input.tangentWS.w;
                float3 bitangentWS = sgn * cross(input.normalWS, input.tangentWS.xyz);
                // Not normalized: Source takes N.V with the decoded normal as it is
                half3 normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS));
                float3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 reflectWS = reflect(-viewWS, normalize(normalWS));
                half3 reflection = EnvMapReflection(input.positionWS, reflectWS) * _ReflectTint.rgb;

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                half fogBehind = WaterFog(screenUV, input.positionWS.y);

                // The planar reflection where this camera has one for the plane of this surface, flipped back and offset by
                // the normal times $reflectamount and the fog behind (DrawWater); the envmap elsewhere
                half3 mirrored = reflection;
                UNITY_BRANCH
                if (_SourceWaterReflectionEnabled > 0.5 && abs(input.positionWS.y - _SourceWaterReflectionHeight) < 0.05)
                {
                    float2 reflectUV = float2(1.0 - screenUV.x, screenUV.y) + normalTS.xy * (_ReflectAmount * fogBehind);
                    mirrored = SAMPLE_TEXTURE2D(_SourceWaterReflection, sampler_SourceWaterReflection, reflectUV).rgb * _ReflectTint.rgb;
                }

                half3 color = half3(0, 0, 0);
                half alpha = 1.0h;
                bool expensive = (_Refract > 0.5h) || (_Reflect > 0.5h);
                if (expensive)
                {
                    half3 refraction = half3(0, 0, 0);
                    half fog = fogBehind;
                    if (_Refract > 0.5h)
                    {
                        // DrawWater: the normal offsets the lookup by $refractamount, times the fog behind it, so shallow
                        // water bends the view less. Source's refraction holds only what lies under the water; the opaque
                        // texture holds everything, so an offset that lands on something in front of the surface stays
                        // with the pixel right behind it.
                        float2 refractUV = screenUV + normalTS.xy * (_RefractAmount * fogBehind);
                        float sceneDepth;
                        ScenePosition(refractUV, sceneDepth);
                        if (sceneDepth < LinearEyeDepth(input.positionWS, GetWorldToViewMatrix()))
                            refractUV = screenUV;
                        fog = WaterFog(refractUV, input.positionWS.y);

                        // The refraction's own height fog, then $refracttint, then DrawWater's blend toward the fog colour
                        refraction = lerp(SampleSceneColor(refractUV), _WaterFogColor.rgb, fog);
                        refraction *= _RefractTint.rgb;
                        refraction = lerp(refraction, _WaterFogColor.rgb, saturate(fog - 0.05h));
                    }

                    // Fresnel, less over shallow water
                    half fresnel = pow(1.0h - saturate(dot(viewWS, normalWS)), 5.0h) * saturate((fog - 0.05h) * 20.0h);
                    if (_Refract > 0.5h && _Reflect > 0.5h)
                        color = lerp(refraction, mirrored, fresnel);
                    else
                        color = (_Reflect > 0.5h) ? mirrored : refraction;
                }

                if (_Cheap > 0.5h)
                {
                    half facing = 1.0h - max(0.0h, dot(viewWS, normalize(normalWS)));
                    half fresnel = (_CheapFresnel > 0.5h) ? facing * facing * facing * facing * facing : _ReflectBlendFactor;
                    half3 cheapColor = reflection;
                    half cheapAlpha = 1.0h;
                    if (_CheapBlend > 0.5h)
                    {
                        // Fades in by fresnel and with distance, and out along the shore
                        float distance = length(_WorldSpaceCameraPos - input.positionWS);
                        half far = saturate((distance - _CheapWaterDistances.x) / max(_CheapWaterDistances.y - _CheapWaterDistances.x, 1e-4));
                        cheapAlpha = saturate(fresnel + far);
                        if (_Refract > 0.5h)
                            cheapAlpha *= saturate((fogBehind - 0.05h) * 20.0h);
                    }
                    else
                        cheapColor = lerp(_WaterFogColor.rgb, cheapColor, fresnel);

                    // Source draws it as a second pass, blended over the expensive water
                    if (expensive)
                        color = lerp(color, cheapColor, cheapAlpha);
                    else
                    {
                        color = cheapColor;
                        alpha = cheapAlpha;
                    }
                }

                color = MixFog(color, input.fog);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}

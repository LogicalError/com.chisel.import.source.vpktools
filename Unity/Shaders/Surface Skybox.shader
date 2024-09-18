Shader "Skybox/Surface Skybox"
{
    //*
    Properties
    {
        _Tint( "Tint Color", Color ) = ( .5, .5, .5, .5 )
        [Gamma] _Exposure( "Exposure", Range( 0, 8 ) ) = 1.0
        _Rotation( "Rotation", Range( 0, 360 ) ) = 0
        _MainTex("Skybox Cubemap", Cube) = "" {}
    }
    SubShader
    {
        Tags
        {
            "Queue"="Background"
            "RenderType"="Background"
            "PreviewType"="Skybox"
        }
        Cull Off
        ZWrite Off
        //doesn't change anything, messed with Zero and One in both spots, no fix
        //Blend SrcAlpha OneMinusSrcAlpha
        CGINCLUDE
        #pragma target 5.0
        #pragma only_renderers d3d11
        #pragma exclude_renderers gles
        #pragma exclude_renderers gles
        //-------------------------------------------------------------------------------------------------------------------------------------------------------------
        #include "UnityCG.cginc"
        //#include "vr_utils.cginc"
        //-------------------------------------------------------------------------------------------------------------------------------------------------------------
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            samplerCUBE _MainTex;
            
            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            //-------------------------------------------------------------------------------------------------------------------------------------------------------------
            float4 RotateAroundYInDegrees( float4 vPositionOs, float flDegrees )
            {
                float flRadians = flDegrees * UNITY_PI / 180.0;
                float flSin, flCos;
                sincos( flRadians, flSin, flCos );
                float2x2 m = float2x2( flCos, -flSin, flSin, flCos );
                return float4( mul( m, vPositionOs.xz ), vPositionOs.yw ).xzyw;
            }
            //-------------------------------------------------------------------------------------------------------------------------------------------------------------
        
            float4 _Tint;
            float _Exposure;
            float _Rotation;
        
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                 
                // Calculate view direction from the world position to the camera
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 viewDir = normalize(RotateAroundYInDegrees( float4(i.worldPos - _WorldSpaceCameraPos,1 ), _Rotation).xyz);
                
                float3 vSkyboxLinearColor = texCUBE(_MainTex, viewDir);
                //float3 vSkyboxLinearColor = DecodeHDR( vSkyboxTexel.rgba, faceSamplerDecode.rgba );
                
                float4 color;
                color.rgb = saturate( (vSkyboxLinearColor.rgb ) * _Tint.rgb * unity_ColorSpaceDouble.rgb * _Exposure );
                color.a = 1.0;
                return color;
            }
            ENDCG
        }
    }
    /*/
    Properties
    {
        _Tint( "Tint Color", Color ) = ( .5, .5, .5, .5 )
        [Gamma] _Exposure( "Exposure", Range( 0, 8 ) ) = 1.0
        _Rotation( "Rotation", Range( 0, 360 ) ) = 0
        [NoScaleOffset] _FrontTex( "Front [+Z] (HDR)", 2D) = "grey"
        [NoScaleOffset] _BackTex( "Back [-Z] (HDR)", 2D) = "grey" 
        [NoScaleOffset] _LeftTex( "Left [+X] (HDR)", 2D) = "grey" 
        [NoScaleOffset] _RightTex( "Right [-X] (HDR)", 2D) = "grey" 
        [NoScaleOffset] _UpTex( "Up [+Y] (HDR)", 2D) = "grey"
        [NoScaleOffset] _DownTex( "Down [-Y] (HDR)", 2D) = "grey"
        //_SunIntensity ("Sun Intensity", Range(0,4)) = 1
        //_SunSize ("Sun Size", Range(0,8)) = 3
        //_SunPos ("Sun Direction", Vector) = (0,1,0,0)
    }
    SubShader
    {
        Tags
        {
            "Queue"="Background"
            "RenderType"="Background"
            "PreviewType"="Skybox"
        }
        Cull Off
        ZWrite Off
        //doesn't change anything, messed with Zero and One in both spots, no fix
        //Blend SrcAlpha OneMinusSrcAlpha
        CGINCLUDE
        #pragma target 5.0
        #pragma only_renderers d3d11
        #pragma exclude_renderers gles
        //-------------------------------------------------------------------------------------------------------------------------------------------------------------
        #include "UnityCG.cginc"
        //#include "vr_utils.cginc"
        //-------------------------------------------------------------------------------------------------------------------------------------------------------------
        float4 _Tint;
        float _Exposure;
        float _Rotation;
        //float _SunIntensity;
        //float _SunSize;
        //float4 _SunPos;
        //-------------------------------------------------------------------------------------------------------------------------------------------------------------
        float4 RotateAroundYInDegrees( float4 vPositionOs, float flDegrees )
        {
            float flRadians = flDegrees * UNITY_PI / 180.0;
            float flSin, flCos;
            sincos( flRadians, flSin, flCos );
            float2x2 m = float2x2( flCos, -flSin, flSin, flCos );
            return float4( mul( m, vPositionOs.xz ), vPositionOs.yw ).xzyw;
        }
        //-------------------------------------------------------------------------------------------------------------------------------------------------------------
        struct VS_INPUT
        {
            float4 vPositionOs : POSITION;
            float2 vTexcoord : TEXCOORD0;
        };
        struct PS_INPUT
        {
            float4 vPositionPs : SV_POSITION;
            float2 vTexcoord : TEXCOORD0;
            //float3 viewDir : TEXCOORD1;
            float3 vPositionOs : TEXCOORD1;
        };
        struct PS_OUTPUT
        {
            float4 vColor : SV_Target0;
        };
        //-------------------------------------------------------------------------------------------------------------------------------------------------------------
        PS_INPUT SkyboxVs( VS_INPUT v )
        {
            PS_INPUT o;
            o.vPositionPs.xyzw = UnityObjectToClipPos( v.vPositionOs.xyzw );
            o.vTexcoord.xy = v.vTexcoord.xy;
            //o.viewDir = _WorldSpaceCameraPos.xyz - mul (unity_ObjectToWorld, v.vPositionOs).xyz;
            o.vPositionOs = mul(unity_ObjectToWorld, v.vPositionOs).xyz;
            return o;
        }
        //-------------------------------------------------------------------------------------------------------------------------------------------------------------
        PS_OUTPUT SkyboxPs( PS_INPUT i, sampler2D faceSampler, float4 faceSamplerDecode )
        {
            float3 viewDir = normalize(RotateAroundYInDegrees( float4(_WorldSpaceCameraPos.xyz - i.vPositionOs, 1), _Rotation ).xyz);

            float4 vSkyboxTexel = tex2D( faceSampler, i.vTexcoord.xy ).rgba;
            float3 vSkyboxLinearColor = DecodeHDR( vSkyboxTexel.rgba, faceSamplerDecode.rgba );
            PS_OUTPUT o;
            //float3 sun = pow (dot (normalize (_SunPos), normalize (i.viewDir)), 8 - _SunSize) * _SunIntensity;
            o.vColor.rgb = saturate( (vSkyboxLinearColor.rgb ) * _Tint.rgb * unity_ColorSpaceDouble.rgb * _Exposure );
            o.vColor.a = 1.0;
            // Dither to fix banding artifacts
            //o.vColor.rgb += ScreenSpaceDither( i.vPositionPs.xy );
            return o;
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma vertex SkyboxVs
            #pragma fragment MainPs
            sampler2D _FrontTex;
            float4 _FrontTex_HDR;
            PS_OUTPUT MainPs( PS_INPUT i ) { return SkyboxPs( i, _FrontTex, _FrontTex_HDR ); }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex SkyboxVs
            #pragma fragment MainPs
            sampler2D _BackTex;
            float4 _BackTex_HDR;
            PS_OUTPUT MainPs( PS_INPUT i ) { return SkyboxPs( i, _BackTex, _BackTex_HDR ); }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex SkyboxVs
            #pragma fragment MainPs
            sampler2D _LeftTex;
            float4 _LeftTex_HDR;
            PS_OUTPUT MainPs( PS_INPUT i ) { return SkyboxPs( i, _LeftTex, _LeftTex_HDR ); }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex SkyboxVs
            #pragma fragment MainPs
            sampler2D _RightTex;
            float4 _RightTex_HDR;
            PS_OUTPUT MainPs( PS_INPUT i ) { return SkyboxPs( i, _RightTex, _RightTex_HDR ); }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex SkyboxVs
            #pragma fragment MainPs
            sampler2D _UpTex;
            float4 _UpTex_HDR;
            PS_OUTPUT MainPs( PS_INPUT i ) { return SkyboxPs( i, _UpTex, _UpTex_HDR ); }
            ENDCG
        }
        Pass 
        {
            CGPROGRAM
            #pragma vertex SkyboxVs
            #pragma fragment MainPs
            sampler2D _DownTex;
            float4 _DownTex_HDR;
            PS_OUTPUT MainPs( PS_INPUT i ) { return SkyboxPs( i, _DownTex, _DownTex_HDR ); }
            ENDCG
        }
    }
    //*/
}
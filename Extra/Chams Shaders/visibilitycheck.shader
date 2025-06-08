Shader "Custom/visibilitycheck"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (0, 0, 0, 1)
        _ColorInvisible ("Invisible Color", Color) = (1, 1, 1, 1)
        _ColorVisible ("Visible Color", Color) = (1, 0, 0, 1)
        _FresnelBias ("Fresnel Bias", Float) = -1
        _FresnelScale ("Fresnel Scale", Float) = 1
        _FresnelPower ("Fresnel Power", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Geometry"
            "RenderType"="Opaque"
        }

        Blend Off
        Lighting Off
        Cull Back

        Pass
        {
            ZTest Greater
            ZWrite Off

            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 pos : POSITION;
                float2 uv : TEXCOORD0;
                half3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                half2 uv : TEXCOORD0;
                float fresnel : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _ColorInvisible;
            fixed _FresnelBias;
            fixed _FresnelScale;
            fixed _FresnelPower;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.pos);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                float3 i = normalize(ObjSpaceViewDir(v.pos));
                o.fresnel = _FresnelBias + _FresnelScale * pow(1 + dot(i, v.normal), _FresnelPower);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                return lerp(c, _ColorInvisible, 1 - i.fresnel);
            }

            ENDCG
        }

        Pass
        {
            ZTest LEqual
            ZWrite On

            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 pos : POSITION;
                float2 uv : TEXCOORD0;
                half3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                half2 uv : TEXCOORD0;
                float fresnel : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _ColorVisible;
            fixed _FresnelBias;
            fixed _FresnelScale;
            fixed _FresnelPower;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.pos);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                float3 i = normalize(ObjSpaceViewDir(v.pos));
                o.fresnel = _FresnelBias + _FresnelScale * pow(1 + dot(i, v.normal), _FresnelPower);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                return lerp(c, _ColorVisible, 1 - i.fresnel);
            }

            ENDCG
        }
    }
}
Shader "custom/visibilitycheck"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}  
        _ColorInvisible ("Invisible Color", Color) = (1, 0, 0, 1)
        _ColorVisible ("Visible Color", Color) = (0, 1, 0, 1)
    }
    SubShader
    {
        Tags
        {
            "Queue"="Geometry"
            "RenderType"="Opaque"
        }
        Cull Back
        Lighting Off    // Disable lighting for a completely flat look.
        Blend Off

        // --- Pass 1: Render parts hidden behind other geometry ---
        Pass
        {
            ZTest Greater   // Render only where the depth is greater (behind geometry)
            ZWrite Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragInvisible
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _ColorInvisible;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 fragInvisible(v2f i) : SV_Target
            {
                // Sample the texture and multiply by the invisible color.
                fixed4 texColor = tex2D(_MainTex, i.uv);
                return texColor * _ColorInvisible;
            }
            ENDCG
        }

        // --- Pass 2: Render normally visible parts ---
        Pass
        {
            ZTest LEqual   // Render where depth is less or equal (normally visible)
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragVisible
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _ColorVisible;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 fragVisible(v2f i) : SV_Target
            {
                // Sample the texture and multiply by the visible color.
                fixed4 texColor = tex2D(_MainTex, i.uv);
                return texColor * _ColorVisible;
            }
            ENDCG
        }
    }
}
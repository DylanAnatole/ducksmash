// Same cartoon lighting as DuckSmash/Toon but WITHOUT the outline pass.
// Used for everything that has no outline: one draw per object instead of two (big win on mobile).
Shader "DuckSmash/ToonFlat"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        _Gloss ("Gloss", Range(0,1)) = 0.5
        _ShadowTint ("Shadow Tint", Color) = (0.62,0.58,0.78,1)
        _Emission ("Emission", Color) = (0,0,0,0)
        _Outline ("Outline Width", Range(0,0.03)) = 0
        _OutlineColor ("Outline Color", Color) = (0.2,0.13,0.28,1)
        _UseVC ("Use Vertex Color", Float) = 0
        _Ground ("Water contact darkening", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            fixed4 _Color, _ShadowTint, _Emission;
            half _Gloss, _UseVC, _Ground;

            struct v2f
            {
                float4 pos : SV_POSITION;
                half3 n : TEXCOORD0;
                float3 wp : TEXCOORD1;
                float2 uv : TEXCOORD2;
                SHADOW_COORDS(3)
                fixed4 vc : COLOR;
            };

            v2f vert (appdata_full v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.vc = lerp(fixed4(1,1,1,1), v.color, _UseVC);
                TRANSFER_SHADOW(o)
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                half3 n = normalize(i.n);
                half3 l = normalize(_WorldSpaceLightPos0.xyz);
                half3 v = normalize(_WorldSpaceCameraPos - i.wp);
                fixed4 col = tex2D(_MainTex, i.uv) * _Color * i.vc;

                half ndl = dot(n, l);
                half atten = SHADOW_ATTENUATION(i);
                half lit = smoothstep(0.0, 0.06, ndl) * smoothstep(0.3, 0.6, atten);

                half3 c = col.rgb * lerp(_ShadowTint.rgb, half3(1,1,1), lit) * (0.55 + 0.5 * _LightColor0.rgb);
                half3 h = normalize(l + v);
                c += smoothstep(0.965 - _Gloss * 0.02, 0.985 - _Gloss * 0.02, dot(n, h)) * lit * _Gloss * 0.75;
                half rim = 1.0 - saturate(dot(n, v)); rim = rim * rim * rim;
                c += rim * 0.18 * col.rgb + rim * 0.06;
                c *= lerp(1.0, lerp(0.68, 1.0, saturate(i.wp.y * 3.2 + 0.08)), _Ground);   // grounded look without a shadow pass
                c += _Emission.rgb;
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}

// Cartoon shading for Duck Smash: 2-band lighting, soft rim, glossy highlight,
// received shadows, and a constant-width inverted-hull outline.
Shader "DuckSmash/Toon"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        _Gloss ("Gloss", Range(0,1)) = 0.5
        _ShadowTint ("Shadow Tint", Color) = (0.62,0.58,0.78,1)
        _Emission ("Emission", Color) = (0,0,0,0)
        _Outline ("Outline Width", Range(0,0.03)) = 0.008
        _OutlineColor ("Outline Color", Color) = (0.2,0.13,0.28,1)
        _UseVC ("Use Vertex Color", Float) = 0
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
            half _Gloss, _UseVC;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 n : TEXCOORD0;
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
                o.vc = lerp(fixed4(1,1,1,1), v.color, _UseVC);   // baked models carry part colours per vertex
                TRANSFER_SHADOW(o)
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.n);
                float3 l = normalize(_WorldSpaceLightPos0.xyz);
                float3 v = normalize(_WorldSpaceCameraPos - i.wp);
                fixed4 col = tex2D(_MainTex, i.uv) * _Color * i.vc;

                float ndl = dot(n, l);
                float atten = SHADOW_ATTENUATION(i);
                float lit = smoothstep(0.0, 0.06, ndl) * smoothstep(0.3, 0.6, atten);

                float3 shade = lerp(_ShadowTint.rgb, float3(1,1,1), lit);
                float3 c = col.rgb * shade * (0.55 + 0.5 * _LightColor0.rgb);

                // glossy toy highlight
                float3 h = normalize(l + v);
                float spec = smoothstep(0.965 - _Gloss * 0.02, 0.985 - _Gloss * 0.02, dot(n, h)) * lit * _Gloss;
                c += spec * 0.75;

                // soft rim
                float rim = pow(1.0 - saturate(dot(n, v)), 3.0);
                c += rim * 0.18 * col.rgb + rim * 0.06;

                c += _Emission.rgb;
                return fixed4(c, 1);
            }
            ENDCG
        }

        Pass
        {
            Name "OUTLINE"
            Cull Front
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local __ NO_OUTLINE
            #include "UnityCG.cginc"
            float _Outline; fixed4 _OutlineColor;

            float4 vert (appdata_base v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
            #if defined(NO_OUTLINE)
                return float4(2, 2, 2, 1);   // every vertex outside clip space -> whole outline culled
            #endif
                float4 p = UnityObjectToClipPos(v.vertex);
                float3 nv = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, v.normal));
                float2 off = TransformViewToProjection(nv.xy);
                p.xy += off * _Outline * p.w;
                return p;
            }
            fixed4 frag () : SV_Target { return _OutlineColor; }
            ENDCG
        }
    }
    FallBack "Diffuse"
}

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
                // glossy vinyl-toy shading: soft wrapped light (round, 3D) + sky/ground fill + broad sheen + sharp hotspot + white rim
                float wrap = saturate(ndl * 0.55 + 0.45);
                float lit = (wrap * wrap * (3.0 - 2.0 * wrap)) * lerp(0.55, 1.0, smoothstep(0.3, 0.6, atten));
                float sky = saturate(n.y * 0.5 + 0.5);
                float3 shade = lerp(_ShadowTint.rgb * 0.95, float3(1.06, 1.05, 1.02), lit) * lerp(0.9, 1.06, sky);
                float3 c = col.rgb * shade * (0.6 + 0.45 * _LightColor0.rgb);
                float3 h = normalize(l + v);
                float ndh = saturate(dot(n, h));
                c += (pow(ndh, 18.0) * 0.16 + smoothstep(0.972, 0.988, ndh) * 0.55) * _Gloss * saturate(lit + 0.2);
                float rim = pow(1.0 - saturate(dot(n, v)), 2.6);
                c += rim * (0.16 * col.rgb + 0.08) * sky;   // softer rim: less washed-out edges
                c *= lerp(1.0, lerp(0.68, 1.0, saturate(i.wp.y * 3.2 + 0.08)), _Ground);   // grounded look without a shadow pass
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

            float4 vert (appdata_full v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
            #if defined(NO_OUTLINE)
                return float4(2, 2, 2, 1);   // every vertex outside clip space -> whole outline culled
            #endif
                // baked meshes carry smoothed normals in uv3 (xyz) + a per-part on/off flag (w); plain meshes use their normals
                float3 sn = v.texcoord3.xyz; bool baked = dot(sn, sn) > 0.25;
                float3 nrm = baked ? sn : v.normal; float on = baked ? v.texcoord3.w : 1;
                float4 p = UnityObjectToClipPos(v.vertex);
                float3 nv = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, nrm));
                float2 off = TransformViewToProjection(nv.xy);
                p.xy += off * _Outline * p.w * on;
                p.z += (1 - on) * 0.0 ;
                return p;
            }
            fixed4 frag () : SV_Target { return _OutlineColor; }
            ENDCG
        }
    }
    FallBack "Diffuse"
}

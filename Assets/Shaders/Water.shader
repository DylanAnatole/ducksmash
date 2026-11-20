// Bath water: the caustic texture sampled twice, scrolling in different directions and scales,
// averaged, with a soft sparkle where both bright caustic lines cross. Unlit and animated on the GPU
// (no per-frame CPU work), 2 texture reads per pixel: cheap enough for phones.
Shader "DuckSmash/Water"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Caustics", 2D) = "white" {}
        _Speed ("Speed", Float) = 1
        _Sparkle ("Sparkle", Float) = 0.55
        _Shift ("Hue shift (rgb = target tint, a = amount)", Color) = (1,1,1,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color; half4 _Shift; float _Speed, _Sparkle;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = TRANSFORM_TEX(v.texcoord, _MainTex); return o; }
            fixed4 frag (v2f i) : SV_Target
            {
                float t = _Time.y * _Speed;
                fixed4 a = tex2D(_MainTex, i.uv + float2(0.012, 0.022) * t);
                fixed4 b = tex2D(_MainTex, i.uv * 1.31 + float2(-0.019, 0.011) * t + 0.37);
                fixed3 c = (a.rgb + b.rgb) * 0.5;
                fixed s = saturate((a.g + b.g) - 1.52) * 4.0;       // both caustic lines bright -> glint
                c += s * s * _Sparkle;
                half3 o = c * _Color.rgb;
                half l = dot(o, half3(0.3, 0.59, 0.11));
                o = lerp(o, l * _Shift.rgb * 2.2, _Shift.a);    // re-tint by brightness: keeps the caustics, changes the water colour
                return fixed4(o, 1);
            }
            ENDCG
        }
    }
}

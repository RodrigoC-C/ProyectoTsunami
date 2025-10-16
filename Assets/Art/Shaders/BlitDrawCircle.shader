Shader "Hidden/BlitDrawCircle"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            sampler2D _MainTex;
            float4 _Circle;   // xy = centerUV, z = radiusUV, w = softUV

            struct VIn { float4 pos:POSITION; float2 uv:TEXCOORD0; };
            struct VOut{ float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };

            VOut vert(VIn v){ VOut o; o.pos=v.pos; o.uv=v.uv; return o; }

            float4 frag(VOut i):SV_Target
            {
                float v = tex2D(_MainTex, i.uv).r;

                float2 d = i.uv - _Circle.xy;
                float dist = length(d);                       // distancia en UV
                float r = max(1e-5, _Circle.z);
                float s = max(1e-5, _Circle.w);

                // 1 dentro del círculo, 0 fuera, con borde suave 's'
                float add = 1.0 - smoothstep(r - s, r, dist);

                v = max(v, add);
                return float4(v, v, v, 1);
            }
            ENDHLSL
        }
    }
}

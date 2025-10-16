Shader "URP/TsunamiWater"
{
   Properties
{
    _BaseColor("Base Color", Color) = (0.1,0.25,0.35,0.85)
    _FloodMask("Flood Mask", 2D) = "black"{}
    _Threshold("Visibility Threshold", Range(0,1)) = 0.5
    _FoamBoost("Foam Boost", Range(0,3)) = 1.5
    _MinAlpha("Min Alpha (debug)", Range(0,1)) = 0.0

    // NUEVO: datos del plano en mundo
    _PlaneCenterWS("Plane Center WS", Vector) = (0,0,0,0)
    _PlaneRightWS("Plane Right WS", Vector) = (1,0,0,0)
    _PlaneForwardWS("Plane Forward WS", Vector) = (0,0,1,0)
    _PlaneHalfSize("Plane Half Size (X,Z world)", Vector) = (5000,0,5000,0)
}
SubShader
{
    Tags{"Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline"}
    Blend SrcAlpha OneMinusSrcAlpha
    ZWrite Off
    Cull Back

    Pass
    {
        HLSLPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        float4 _BaseColor;
        sampler2D _FloodMask; float4 _FloodMask_TexelSize;
        float _Threshold, _FoamBoost, _MinAlpha;

        // NUEVO
        float4 _PlaneCenterWS, _PlaneRightWS, _PlaneForwardWS, _PlaneHalfSize;

        struct VIn  { float4 posOS:POSITION; };
        struct VOut { float4 posHCS:SV_POSITION; float3 posWS:TEXCOORD0; };

        VOut vert(VIn v){
            VOut o;
            float3 ws = TransformObjectToWorld(v.posOS.xyz);
            o.posWS = ws;
            o.posHCS = TransformWorldToHClip(ws);
            return o;
        }

        half4 frag(VOut i):SV_Target
        {
            // UV calculado por proyección sobre los ejes del plano
            float3 to = i.posWS - _PlaneCenterWS.xyz;
            float halfW = max(1e-4, _PlaneHalfSize.x);
            float halfH = max(1e-4, _PlaneHalfSize.z);
            float u = dot(to, normalize(_PlaneRightWS.xyz))   / halfW * 0.5 + 0.5;
            float v = dot(to, normalize(_PlaneForwardWS.xyz)) / halfH * 0.5 + 0.5;
            float2 uv = saturate(float2(u,v));

            float m = tex2D(_FloodMask, uv).r;

            float alpha = max(_MinAlpha, smoothstep(_Threshold - 0.02, _Threshold + 0.02, m));
            float foam = saturate(1.0 - abs(m - _Threshold) * 20.0) * _FoamBoost;

            float3 col = _BaseColor.rgb + foam * 0.25;
            return float4(col, _BaseColor.a * alpha);
        }
        ENDHLSL
    }
}
}
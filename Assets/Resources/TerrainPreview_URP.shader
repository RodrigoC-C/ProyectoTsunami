Shader "Tsunami/TerrainPreview_URP"
{
    Properties
    {
        _ColorLow  ("Low Color",  Color) = (0.10, 0.35, 0.15, 1)
        _ColorHigh ("High Color", Color) = (0.85, 0.80, 0.60, 1)

        _MinH ("Min Height (m)", Float) = -20.0
        _MaxH ("Max Height (m)", Float) = 10.0

        [Header(Contours)]
        _ContourStep  ("Contour Step (m)", Float) = 1.0
        _ContourWidth ("Contour Width", Range(0.001,0.05)) = 0.01
        _ContourColor ("Contour Color", Color) = (0,0,0,0.7)

        [Header(Lighting)]
        _Metallic   ("Metallic",   Range(0,1)) = 0
        _Smoothness ("Smoothness", Range(0,1)) = 0.2
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalRenderPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200
        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorLow;
                float4 _ColorHigh;
                float  _MinH, _MaxH;
                float  _ContourStep, _ContourWidth;
                float4 _ContourColor;
                float  _Metallic, _Smoothness;
            CBUFFER_END

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                float  heightWS   : TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs posI = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs   nI   = GetVertexNormalInputs(v.normalOS);

                o.positionCS = posI.positionCS;
                o.positionWS = posI.positionWS;
                o.normalWS   = nI.normalWS;
                o.heightWS   = posI.positionWS.y;     // altura en mundo
                o.fogCoord   = ComputeFogFactor(posI.positionCS.z);
                return o;
            }

            float4 frag (Varyings i) : SV_Target
            {
                // Gradiente por altura
                float heightT = saturate( (i.heightWS - _MinH) / max(1e-5, (_MaxH - _MinH)) );
                float3 baseCol = lerp(_ColorLow.rgb, _ColorHigh.rgb, heightT);

                // Curvas de nivel
                float bands = (i.heightWS - _MinH) / max(_ContourStep, 1e-4);
                float edge  = abs(frac(bands) - 0.5) * 2.0;           // 0 en línea, 1 lejos
                float contourMask = smoothstep(0.0, _ContourWidth, 1.0 - edge);
                float3 colorWithContours = lerp(baseCol, _ContourColor.rgb, contourMask * _ContourColor.a);

                // Luz principal sencilla
                float3 N = normalize(i.normalWS);
                Light mainLight = GetMainLight();
                float3 L = normalize(mainLight.direction);
                float  ndl = saturate(dot(N, -L));
                float3 lit = colorWithContours * (0.25 + 0.75 * ndl);

                // Niebla
                lit = MixFog(lit, i.fogCoord);
                return float4(lit, 1);
            }
            ENDHLSL
        }
    }
}

Shader "Custom/TsunamiWater_PBR_URP_Compute"
{
    Properties
    {
        // Colores y óptica
        _WaterColor  ("Water Color", Color) = (0.08, 0.35, 0.85, 1)
        _DeepColor   ("Deep Color",  Color) = (0.02, 0.07, 0.12, 1)
        _FresnelPower("Fresnel Power", Range(1,8)) = 5
        _SpecularPow ("Spec Power", Range(8,256))  = 64
        _SpecularStr ("Spec Strength", Range(0,1)) = 0.4
        _AbsorbStr   ("Absorption", Range(0,3))    = 0.8

        // Rango físico de PNG16
        _EtaMin   ("Eta Min (m)", Float) = -2
        _EtaMax   ("Eta Max (m)", Float) =  2
        _InundMax ("Inund Max (m)", Float) = 3

        // Geometría
        _VerticalScale ("Vertical Scale (x)", Float) = 4
        _BaseY         ("Base Y (m)", Float) = 0

        // Suavizado
        _SmoothStrength ("Smooth Strength (0..1)", Range(0,1)) = 1
        _FrameLerp      ("Frame Lerp (0..1)", Range(0,1)) = 0

        // Espuma
        _FoamTex      ("Foam Noise (R)", 2D)   = "white" {}
        _FoamColor    ("Foam Color", Color)    = (1,1,1,1)
        _FoamScale    ("Foam Tiling", Float)   = 0.05
        _FoamIntensity("Foam Intensity", Range(0,3)) = 1.2
        _FoamThreshold("Foam Threshold", Range(0,2)) = 0.35
        
        // NUEVA propiedad para altura precalculada
        _DisplacementTex ("Displacement Texture", 2D) = "black" {}
        _DomainSize ("Domain Size", Vector) = (512, 512, 0, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "RenderType"="Opaque" }
        LOD 200
        Cull Back
        ZWrite On
        Blend Off

        Pass
        {
            Name "ForwardLit"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _FOAM_INUND

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // Texturas
            TEXTURE2D(_DisplacementTex); SAMPLER(sampler_DisplacementTex); // Desplazamiento precalculado
            TEXTURE2D_ARRAY(_EtaArray);   SAMPLER(sampler_EtaArray);
            TEXTURE2D_ARRAY(_InundArray); SAMPLER(sampler_InundArray);
            TEXTURE2D(_FoamTex);          SAMPLER(sampler_FoamTex);

            // Uniforms
            float4 _WaterColor, _DeepColor, _FoamColor;
            float  _FresnelPower, _SpecularPow, _SpecularStr, _AbsorbStr;
            float  _EtaMin, _EtaMax, _InundMax;
            float  _VerticalScale, _BaseY;
            int    _FrameIndex, _NextFrameIndex;
            float  _FrameLerp;
            float2 _EtaTexelSize;
            float2 _DomainMeters;
            float2 _DomainSize; // Tamaño de la textura de desplazamiento
            float  _SmoothStrength;
            float  _FoamScale, _FoamIntensity, _FoamThreshold;

            struct Attributes {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings {
                float4 positionHCS : SV_Position;
                float3 posWS       : TEXCOORD0;
                float2 uv          : TEXCOORD1;
                float  displacement : TEXCOORD2;
            };

            // VERTEX SHADER SIMPLIFICADO
            Varyings vert(Attributes v)
            {
                Varyings o;
                
                // Leer desplazamiento precalculado del compute shader
                float displacement = SAMPLE_TEXTURE2D_LOD(_DisplacementTex, sampler_DisplacementTex, v.uv, 0).r;
                
                // Aplicar desplazamiento
                float3 posOS = v.positionOS.xyz;
                posOS.y = displacement; // Ya incluye _BaseY y _VerticalScale
                
                o.positionHCS = TransformObjectToHClip(posOS);
                o.posWS = TransformObjectToWorld(posOS);
                o.uv = v.uv;
                o.displacement = displacement;
                
                return o;
            }

            // ---- utilidades para FRAGMENT SHADER ----
            float u16_to_eta  (float r01){ return lerp(_EtaMin,   _EtaMax,   saturate(r01)); }
            float u16_to_inund(float r01){ return lerp(0.0,       _InundMax, saturate(r01)); }

            float catmull(float x){
                x = abs(x);
                if (x < 1.0) return 1.0 + x*x*(-2.5 + 1.5*x);
                if (x < 2.0) return 2.0 + x*(-4.0 + x*(2.5 - 0.5*x));
                return 0.0;
            }

            float bilinearA(TEXTURE2D_ARRAY_PARAM(tex, samp), float2 uv, int slice){
                return SAMPLE_TEXTURE2D_ARRAY(tex, samp, uv, slice).r;
            }

            float bicubicA(TEXTURE2D_ARRAY_PARAM(tex, samp), float2 uv, int slice){
                float2 st = uv / _EtaTexelSize - 0.5;
                float2 i  = floor(st);
                float2 f  = st - i;

                float wx[4] = {catmull(1.0+f.x), catmull(f.x), catmull(1.0-f.x), catmull(2.0-f.x)};
                float wy[4] = {catmull(1.0+f.y), catmull(f.y), catmull(1.0-f.y), catmull(2.0-f.y)};

                float sum=0, norm=0;
                [unroll] for(int m=-1;m<=2;++m){
                    [unroll] for(int n=-1;n<=2;++n){
                        float2 uv2=(i+float2(m,n)+0.5)*_EtaTexelSize;
                        float  w = wx[m+1]*wy[n+1];
                        sum  += SAMPLE_TEXTURE2D_ARRAY(tex,samp,uv2,slice).r * w;
                        norm += w;
                    }
                }
                return sum/max(norm,1e-5);
            }

            float sampleA(TEXTURE2D_ARRAY_PARAM(tex, samp), float2 uv, int slice){
                float a = bilinearA(tex,samp,uv,slice);
                float b = bicubicA (tex,samp,uv,slice);
                return lerp(a,b,_SmoothStrength);
            }

            float etaAt (float2 uv, int slice){ return u16_to_eta  (sampleA(_EtaArray, sampler_EtaArray, uv, slice)); }
            float inundAt(float2 uv, int slice){ return u16_to_inund(sampleA(_InundArray, sampler_InundArray, uv, slice)); }

            half4 frag(Varyings i) : SV_Target
            {
                // --- derivadas en METROS (para normales físicas) -------------
                float dx_m = _DomainMeters.x * _EtaTexelSize.x;
                float dy_m = _DomainMeters.y * _EtaTexelSize.y;

                float2 du = float2(_EtaTexelSize.x, 0);
                float2 dv = float2(0, _EtaTexelSize.y);

                float eC = etaAt(i.uv, _FrameIndex);
                float eR = etaAt(i.uv+du, _FrameIndex);
                float eL = etaAt(i.uv-du, _FrameIndex);
                float eU = etaAt(i.uv+dv, _FrameIndex);
                float eD = etaAt(i.uv-dv, _FrameIndex);

                float dEdx = (eR-eL)/(2*dx_m);
                float dEdy = (eU-eD)/(2*dy_m);

                // normal geom. (x hacia +, z hacia +, y up)
                float3 n = normalize(float3(-dEdx*_VerticalScale, 1.0, -dEdy*_VerticalScale));

                // --- luz principal URP ---------------------------------------
                float3 V = normalize(GetWorldSpaceViewDir(i.posWS));
                Light mainL = GetMainLight();
                float3 L = normalize(mainL.direction);
                float3 H = normalize(L+V);

                float NdotL = saturate(dot(n,L));
                float NdotV = saturate(dot(n,V));
                float NdotH = saturate(dot(n,H));

                float diff = NdotL;

                float spec = pow(saturate(NdotH), _SpecularPow) * _SpecularStr * NdotL;

                // Fresnel Schlick
                float F = pow(1.0 - NdotV, _FresnelPower);

                // Color base + absorción simple con ángulo (NdotV)
                float3 waterCol = _WaterColor.rgb;
                float absorb = exp(-_AbsorbStr * (1.0 - NdotV));
                waterCol = lerp(_DeepColor.rgb, waterCol, absorb);

                // Espuma
                float slope = length(float2(dEdx,dEdy)); // |∇η|
                float etaA  = eC;
                float etaB  = etaAt(i.uv, _NextFrameIndex);
                float deta  = abs(etaB-etaA);           // |∂η/∂t|

                float foamCore = slope*2.0 + deta*0.5;

                #if _FOAM_INUND
                    float innA = inundAt(i.uv, _FrameIndex);
                    foamCore += saturate(innA/max(_InundMax,1e-5))*0.35;
                #endif

                float2 uvN = i.posWS.xz * _FoamScale;
                float foamN = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, uvN).r;

                float foam = saturate((foamCore + foamN*0.6 - _FoamThreshold)*_FoamIntensity);

                // Composición: difusa + espec + fresnel + espuma
                float3 col = waterCol * (diff * mainL.color.rgb);
                col += spec * mainL.color.rgb;
                col = lerp(col, _FoamColor.rgb, foam);
                col = lerp(col, 1.0.xxx, F*0.1); // un toque de fresnel a blanco

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
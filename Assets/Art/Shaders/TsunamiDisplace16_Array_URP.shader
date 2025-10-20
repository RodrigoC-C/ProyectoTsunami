Shader "Tsunami/TsunamiDisplace16_Array_URP_Textured"
{
    Properties
    {
        // --- Simulación (igual que tuyo) ---
        [Header(Tsunami Simulation)]
        _HeightArray ("Heightmap Array (R16)", 2DArray) = "" {}
        _Slice ("Frame Index", Float) = 0
        _DispScale ("Displacement (m)", Float) = 10.0

        // --- Apariencia base ---
        [Header(Water Appearance)]
        _WaterTex ("Water Albedo", 2D) = "gray" {}
        _WaterTint ("Water Tint", Color) = (0.08, 0.35, 0.8, 1)
        _Alpha ("Global Alpha", Range(0,1)) = 0.6

        // --- Normales superficiales (ya los usabas) ---
        [Header(Surface Waves)]
        _NormalMap ("Normal Map (Ondas)", 2D) = "bump" {}
        _NormalScale ("Normal Strength", Range(0, 3)) = 1.0
        _ScrollSpeedX ("Scroll Speed X", Float) = 0.05
        _ScrollSpeedY ("Scroll Speed Y", Float) = 0.03
        _Tiling ("Normal/Albedo Tiling", Float) = 10.0

        // --- Fresnel y espuma por altura (opcional) ---
        [Header(Effects)]
        _FresnelPow("Fresnel Power", Range(0, 10)) = 5.0

        // --- Alineación grilla (igual que tuyo) ---
        [Header(Grid Alignment)]
        _FlipV ("Flip V (0/1)", Float) = 1
        _Rotate90 ("Rotate 90 (0/1)", Float) = 0

        // --- (Opcional) Depth Fade ---
        //[Header(Depth Fade)]
        //_DepthFadeDistance ("Fade Distance", Float) = 3.0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalRenderPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        // Para que se vea el terreno bajo el agua:
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off          // si prefieres que “corte” con el terreno, pon On
        Cull Back

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            // (Opcional) para depth fade
            //#pragma multi_compile _ USE_DEPTH_FADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            //#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl" // si activas depth fade

            // --- Texturas y Samplers ---
            TEXTURE2D_ARRAY(_HeightArray);
            SAMPLER(sampler_HeightArray);

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            TEXTURE2D(_WaterTex);
            SAMPLER(sampler_WaterTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _WaterTint;                         // rgba (usamos .rgb y .a opcional)
                float _Slice, _DispScale, _FlipV, _Rotate90;
                float _NormalScale, _ScrollSpeedX, _ScrollSpeedY, _Tiling;
                float _FresnelPow, _Alpha;
                // float _DepthFadeDistance;               // si usas depth fade
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float4 tangentOS    : TANGENT;
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                half3  normalWS     : TEXCOORD1;
                half3  tangentWS    : TEXCOORD2;
                half3  bitangentWS  : TEXCOORD3;
                float2 uv_heightmap : TEXCOORD4;
                float2 uv_surf      : TEXCOORD5;   // para albedo/normal pequeños
                float  height_norm  : TEXCOORD6;   // 0..1
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float2 FixUV(float2 uv)
            {
                if (_Rotate90 > 0.5) uv = float2(uv.y, 1.0 - uv.x);
                if (_FlipV   > 0.5) uv.y = 1.0 - uv.y;
                return uv;
            }

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float2 uvH = FixUV(v.uv);

                // Altura 0..1
                float h  = _HeightArray.SampleLevel(sampler_HeightArray, float3(uvH, _Slice), 0).r;
                o.height_norm = h;

                // Desplazamiento en metros
                float dy = (h - 0.5) * 2.0 * _DispScale;

                // Posición desplazada (eje Y del objeto)
                float3 posOS = v.positionOS.xyz;
                posOS.y += dy;

                // Normales “macro” por derivada simple
                uint w,hgt,depth;
                _HeightArray.GetDimensions(w, hgt, depth);
                float2 px = 1.0 / max(float2(w, hgt), 1.0);

                float hR = _HeightArray.SampleLevel(sampler_HeightArray, float3(uvH + float2(px.x,0), _Slice), 0).r;
                float hU = _HeightArray.SampleLevel(sampler_HeightArray, float3(uvH + float2(0,px.y), _Slice), 0).r;
                float dyR = (hR - 0.5) * 2.0 * _DispScale;
                float dyU = (hU - 0.5) * 2.0 * _DispScale;

                float3 tOS = float3(1, dyR - dy, 0);
                float3 bOS = float3(0, dyU - dy, 1);
                float3 nOS = normalize(cross(bOS, tOS));

                // Transformaciones
                VertexPositionInputs posI = GetVertexPositionInputs(posOS);
                o.positionHCS = posI.positionCS;
                o.positionWS  = posI.positionWS;

                VertexNormalInputs nI = GetVertexNormalInputs(nOS, v.tangentOS);
                o.normalWS    = nI.normalWS;
                o.tangentWS   = nI.tangentWS;
                o.bitangentWS = nI.bitangentWS;

                // UV para detalles superficiales (albedo+normal) con tiling
                o.uv_heightmap = uvH;
                o.uv_surf      = v.uv * _Tiling;

                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                // Normal map pequeño con scroll
                float2 scroll = float2(_ScrollSpeedX, _ScrollSpeedY) * _Time.y;
                float2 uvSurf = i.uv_surf + scroll;

                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvSurf), _NormalScale);
                half3x3 TBN = half3x3(i.tangentWS, i.bitangentWS, i.normalWS);
                half3 nWS = normalize(mul(nTS, TBN));

                // Fresnel (para borde brillante y algo más de transparencia)
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                float  NdotV = saturate(dot(nWS, V));
                float  fresnel = pow(1.0 - NdotV, _FresnelPow);

                // Albedo de agua (textura + tinte)
                half3 albedo = SAMPLE_TEXTURE2D(_WaterTex, sampler_WaterTex, uvSurf).rgb;
                albedo = albedo * _WaterTint.rgb;

                // Transparencia (constante + fresnel)
                // Más fresco en bordes: sube alfa en normal-incidencia
                float alpha = saturate(_Alpha * (0.6 + 0.4 * (1.0 - fresnel)));

                // (Opcional) Depth fade – habilita el depth texture en tu URP Asset y descomenta pragmas/includes
                /*
                #if defined(USE_DEPTH_FADE)
                    float sceneRaw = SampleSceneDepth(i.positionHCS.xy / i.positionHCS.w);
                    float sceneEye = LinearEyeDepth(sceneRaw, _ZBufferParams);
                    float  fragEye = i.positionHCS.w;
                    float fade = saturate((sceneEye - fragEye) / max(_DepthFadeDistance, 1e-3));
                    alpha *= fade;
                #endif
                */

                // Iluminación simple tipo lit “fake” (reflejo especular suave)
                // Si quieres algo más PBR, puedes añadir BRDF de URP aquí.
                half3 color = albedo;

                // toque de fresnel en color (ligeramente más claro en glancing angles)
                color = lerp(color, color * 1.1h, fresnel);

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}

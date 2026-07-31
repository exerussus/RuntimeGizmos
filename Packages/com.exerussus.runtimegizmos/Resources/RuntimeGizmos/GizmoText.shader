// Текстовые метки. Глиф приходит отрезками, каждый отрезок — 6 вершин (2 треугольника).
// В вершине лежит мировой якорь строки и смещение конца отрезка В ПИКСЕЛЯХ, поэтому
// размер текста не зависит от расстояния до камеры, а один и тот же меш корректен
// сразу для всех камер кадра.
//
// Разворот в ленту здесь проще, чем у толстых линий: направление отрезка известно
// прямо в пикселях, проецировать ничего не нужно.
Shader "Hidden/RuntimeGizmos/Text"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        [Enum(Off,0,On,1)] _ZWriteMode ("ZWrite", Float) = 0
        _DepthBias ("Depth Bias (NDC)", Float) = 0
        _Alpha ("Global Alpha", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"       = "UniversalPipeline"
            "RenderType"           = "Transparent"
            "Queue"                = "Transparent"
            "IgnoreProjector"      = "True"
            "ForceNoShadowCasting" = "True"
            "DisableBatching"      = "True"
        }

        Pass
        {
            Name "GIZMO_TEXT"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Off
            ZTest [_ZTest]
            ZWrite [_ZWriteMode]
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing

            #include "GizmoCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Alpha;
                float _DepthBias;
                float _ZTest;
                float _ZWriteMode;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;   // мировой якорь строки
                half4  color      : COLOR;
                float2 offset     : TEXCOORD0;  // смещение этого конца, пиксели
                float2 other      : TEXCOORD1;  // смещение другого конца, пиксели
                // x: знак = сторона квада, модуль = конец отрезка (1 — начало, 2 — конец).
                //    Оба конца обязаны считать ОДНУ И ТУ ЖЕ локальную систему, иначе
                //    интерполировать по кваду нечего — отсюда явный признак конца
                //    вместо перестановки offset/other местами.
                // y: толщина штриха; минус означает мировой режим.
                float2 sideWidth  : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 local      : TEXCOORD0;   // (вдоль, поперёк) в системе отрезка
                float2 metrics    : TEXCOORD1;   // (половина длины, половина толщины)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // Знак толщины несёт режим: минус — размер задан в мировых единицах.
                // Отдельный атрибут ради одного бита заводить незачем, толщина всегда
                // положительна. Тот же приём, что у билборда иконок.
                float halfW = 0.5 * abs(IN.sideWidth.y);
                bool worldMode = IN.sideWidth.y < 0.0;

                float sideSign = IN.sideWidth.x < 0.0 ? -1.0 : 1.0;
                bool atEnd = abs(IN.sideWidth.x) > 1.5;

                float2 d = IN.other - IN.offset;
                float len = length(d);
                float2 dir = len > 1e-5 ? d / len : float2(1.0, 0.0);
                float2 nrm = float2(-dir.y, dir.x);
                float2 mid = (IN.offset + IN.other) * 0.5;
                float halfLen = len * 0.5;

                // Квад — это bounding box капсулы: выходит за оба конца и за обе стороны
                // ровно на половину толщины. Саму капсулу вырезает фрагментный шейдер.
                float2 local = float2((atEnd ? 1.0 : -1.0) * (halfLen + halfW), sideSign * halfW);
                float2 p = mid + dir * local.x + nrm * local.y;

                OUT.local = local;
                OUT.metrics = float2(halfLen, halfW);

                float4 clip;
                if (worldMode)
                {
                    // Смещения — в мировых единицах, разворачиваем билбордом по осям камеры.
                    // Метка уменьшается с расстоянием, как обычная геометрия.
                    float3 right = UNITY_MATRIX_V._m00_m01_m02;
                    float3 up    = UNITY_MATRIX_V._m10_m11_m12;
                    clip = TransformObjectToHClip(IN.positionOS.xyz + right * p.x + up * p.y);
                }
                else
                {
                    clip = TransformObjectToHClip(IN.positionOS.xyz);

                    // Пиксели в NDC. Умножение на w компенсирует деление перспективы,
                    // поэтому смещение остаётся ровно тем же в пикселях на любой глубине.
                    // Складывать нужно именно в клип-пространстве: делить на w нельзя,
                    // иначе точки за камерой дадут мусор.
                    p.y *= _ProjectionParams.x;   // перевёрнутая проекция в RenderTexture
                    clip.xy += p * (2.0 / max(_ScreenParams.xy, float2(1.0, 1.0))) * clip.w;
                }

                OUT.positionCS = GizmoApplyDepthBias(clip, _DepthBias);

                half4 c = IN.color;
                c.rgb = GizmoDecodeColor(c.rgb);
                OUT.color = c;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // Расстояние до отрезка (SDF капсулы). Даёт круглые торцы — они мягче
                // квадратных и аккуратнее заполняют изломы букв — и, главное, позволяет
                // сгладить край аналитически. Без этого штрих остаётся голым
                // многоугольником с жёсткой границей, и мелкий текст выглядит рваным
                // независимо от того, включён ли MSAA.
                float2 q = float2(max(abs(IN.local.x) - IN.metrics.x, 0.0), IN.local.y);
                float dist = length(q) - IN.metrics.y;

                // fwidth — это размер пикселя в тех же единицах, что и dist, поэтому
                // переход занимает ровно один пиксель и в пиксельном режиме, и в мировом.
                float aa = max(fwidth(dist), 1e-6);
                float cov = saturate(0.5 - dist / aa);
                if (cov <= 0.0) discard;

                half4 c = IN.color;
                c.a *= _Alpha * cov;
                return c;
            }
            ENDHLSL
        }
    }

    Fallback Off
}

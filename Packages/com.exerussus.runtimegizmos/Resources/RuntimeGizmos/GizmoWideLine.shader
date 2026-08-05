// Толстые линии. Отрезок приходит как 6 вершин (2 треугольника), разворот в
// ленту делается в МИРОВОМ пространстве, а не в NDC: так корректно
// обрабатываются точки за камерой, где деление на w даёт мусор.
//
// Геометрических шейдеров здесь намеренно нет — их не поддерживают ни WebGL,
// ни Metal, ни большинство мобильных GPU.
Shader "Hidden/RuntimeGizmos/WideLine"
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
            Name "GIZMO_WIDE"
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
                float4 positionOS : POSITION;   // этот конец отрезка
                float3 other      : NORMAL;     // противоположный конец
                half4  color      : COLOR;
                float3 uv         : TEXCOORD0;  // x = сторона, y = ширина в пикселях, z = фаза пунктира
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float  dash       : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 wp = IN.positionOS.xyz;

                float3 toCam = GizmoCameraDir(wp);

                float3 dir = IN.other - wp;
                float dl = length(dir);
                dir = dl > 1e-6 ? dir / dl : float3(1, 0, 0);

                float3 side = cross(dir, toCam);
                float sl = length(side);
                if (sl < 1e-5)
                {
                    // Отрезок смотрит точно в камеру — берём любую перпендикулярную ось.
                    float3 axis = abs(dir.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0);
                    side = normalize(cross(dir, axis));
                }
                else
                {
                    side /= sl;
                }

                float halfWidth = 0.5 * IN.uv.y * GizmoWorldPerPixel(wp);
                wp += side * (IN.uv.x * halfWidth);

                OUT.positionCS = GizmoApplyDepthBias(
                    TransformObjectToHClip(wp), _DepthBias);

                half4 c = IN.color;
                c.rgb = GizmoDecodeColor(c.rgb);
                OUT.color = c;
                OUT.dash = IN.uv.z;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                if (IN.dash >= 0.0 && frac(IN.dash) > 0.5) discard;

                half4 c = IN.color;
                c.a *= _Alpha;
                return c;
            }
            ENDHLSL
        }
    }

    Fallback Off
}

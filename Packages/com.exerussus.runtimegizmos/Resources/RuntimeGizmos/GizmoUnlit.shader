// Тонкие линии (MeshTopology.Lines) и сплошная заливка.
// URP-only: пасс помечен LightMode = SRPDefaultUnlit, его подхватывает
// DrawObjectsPass как в опаковой, так и в прозрачной фазе.
Shader "Hidden/RuntimeGizmos/Unlit"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        [Enum(Off,0,On,1)] _ZWriteMode ("ZWrite", Float) = 0
        _DepthBias ("Depth Bias (NDC)", Float) = 0
        _Color ("Tint", Color) = (1,1,1,1)
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
            "PreviewType"          = "Plane"
            "ForceNoShadowCasting" = "True"
            "DisableBatching"      = "True"
        }

        Pass
        {
            Name "GIZMO"
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
                float4 _Color;
                float  _Alpha;
                float  _DepthBias;
                float  _ZTest;
                float  _ZWriteMode;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionCS = GizmoApplyDepthBias(
                    TransformObjectToHClip(IN.positionOS.xyz), _DepthBias);

                half4 c = IN.color;
                c.rgb = GizmoDecodeColor(c.rgb);
                OUT.color = c * _Color;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                half4 c = IN.color;
                c.a *= _Alpha;
                return c;
            }
            ENDHLSL
        }
    }

    Fallback Off
}

// DrawGUITexture. Позиции приходят прямо в пикселях экрана (начало координат —
// левый верхний угол, как в GUI); вершинный шейдер переводит их в клип-пространство
// минуя матрицу VP.
Shader "Hidden/RuntimeGizmos/Screen"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 8
        [Enum(Off,0,On,1)] _ZWriteMode ("ZWrite", Float) = 0
        _Alpha ("Global Alpha", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"       = "UniversalPipeline"
            "RenderType"           = "Transparent"
            "Queue"                = "Overlay"
            "IgnoreProjector"      = "True"
            "ForceNoShadowCasting" = "True"
            "DisableBatching"      = "True"
        }

        Pass
        {
            Name "GIZMO_SCREEN"
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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float  _Alpha;
                float  _ZTest;
                float  _ZWriteMode;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;   // xy — пиксели экрана
                half4  color      : COLOR;
                float4 corner     : TEXCOORD0;  // zw = uv
                float2 size       : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float2 px = IN.positionOS.xy;
                float2 ndc;
                ndc.x = px.x / max(_ScreenParams.x, 1.0) * 2.0 - 1.0;
                ndc.y = 1.0 - px.y / max(_ScreenParams.y, 1.0) * 2.0;
                // Компенсация перевёрнутой проекции при рендере в RenderTexture.
                ndc.y *= _ProjectionParams.x;

                OUT.positionCS = float4(ndc, UNITY_NEAR_CLIP_VALUE, 1.0);
                OUT.uv = IN.corner.zw;

                half4 c = IN.color;
                c.rgb = GizmoDecodeColor(c.rgb);
                OUT.color = c;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color;
                c.a *= _Alpha;
                return c;
            }
            ENDHLSL
        }
    }

    Fallback Off
}

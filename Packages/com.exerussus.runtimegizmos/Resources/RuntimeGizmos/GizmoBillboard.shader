// Иконки. Центр иконки продублирован на все 6 вершин квада, разворот в сторону
// камеры делает вершинный шейдер — поэтому один и тот же меш корректен сразу
// для всех камер кадра и для обоих глаз в XR.
Shader "Hidden/RuntimeGizmos/Billboard"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
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
            Name "GIZMO_ICON"
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
                float  _DepthBias;
                float  _ZTest;
                float  _ZWriteMode;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;   // мировой центр иконки
                half4  color      : COLOR;
                float4 corner     : TEXCOORD0;  // xy = угол (-1..1), zw = uv
                float2 size       : TEXCOORD1;  // <0 → мировые единицы, иначе пиксели
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

                float3 wp = IN.positionOS.xyz;

                float3 right = UNITY_MATRIX_V._m00_m01_m02;
                float3 up    = UNITY_MATRIX_V._m10_m11_m12;

                bool worldSize = IN.size.x < 0.0;
                float2 sz  = abs(IN.size);
                float2 wsz = worldSize ? sz : sz * GizmoWorldPerPixel(wp);

                wp += right * (IN.corner.x * wsz.x * 0.5)
                    + up    * (IN.corner.y * wsz.y * 0.5);

                OUT.positionCS = GizmoApplyDepthBias(TransformObjectToHClip(wp), _DepthBias);
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

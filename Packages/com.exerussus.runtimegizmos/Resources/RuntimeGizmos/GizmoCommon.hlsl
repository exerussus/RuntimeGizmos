#ifndef RUNTIME_GIZMOS_COMMON_INCLUDED
#define RUNTIME_GIZMOS_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Перевод sRGB в линейное. Считаем сами, а не берём SRGBToLinear из библиотеки URP:
// Core.hlsl не подключает Color.hlsl, где эта функция живёт, и полагаться на то,
// какие инклюды окажутся подтянуты в конкретной версии пакета, — лишний риск.
// Своё имя ещё и снимает вопрос коллизии, если Color.hlsl прилетит транзитивно.
float3 GizmoSRGBToLinear(float3 c)
{
    float3 lo = c / 12.92;
    float3 hi = pow(max(c + 0.055, 0.0) / 1.055, 2.4);
    return (c <= 0.04045) ? lo : hi;
}

// Цвета вершин лежат в буфере как sRGB (Color32). В линейном проекте переводим
// их здесь, чтобы Color.red выглядел красным, а не пересвеченным.
float3 GizmoDecodeColor(float3 c)
{
#ifdef UNITY_COLORSPACE_GAMMA
    return c;
#else
    return GizmoSRGBToLinear(c);
#endif
}

// Сдвиг глубины к камере, в единицах NDC.
//
// Почему не фиксированный Offset factor,units: в OpenGL ES / WebGL существует
// только GL_POLYGON_OFFSET_FILL — для GL_LINES полигональный офсет молча
// игнорируется. То есть на Android GLES и на WebGL тонкие линии, лежащие на
// поверхности, z-файтили бы. Ручной сдвиг в вершинном шейдере ведёт себя
// одинаково на всех графических API.
//
// Умножение на w делает сдвиг равномерным в NDC уже после деления перспективы.
float4 GizmoApplyDepthBias(float4 clipPos, float bias)
{
#if UNITY_REVERSED_Z
    clipPos.z += bias * clipPos.w;
#else
    clipPos.z -= bias * clipPos.w;
#endif
    return clipPos;
}

// Сколько мировых единиц приходится на один пиксель по вертикали в точке wp.
float GizmoWorldPerPixel(float3 wp)
{
    float screenH = max(_ScreenParams.y, 1.0);

    if (unity_OrthoParams.w > 0.5)
        return 2.0 * unity_OrthoParams.y / screenH;

    float viewDepth = -mul(UNITY_MATRIX_V, float4(wp, 1.0)).z;
    float projScale = max(abs(UNITY_MATRIX_P._m11), 1e-5);
    return 2.0 * max(viewDepth, 1e-4) / (projScale * screenH);
}

// Направление на камеру. Для ортографии оно едино для всей сцены — это третья
// строка матрицы вида.
//
// Здесь _WorldSpaceCameraPos, а не GetCameraPositionWS(): переменная объявлена в
// UnityInput.hlsl, который Core.hlsl тянет гарантированно, а функция живёт
// в ShaderVariablesFunctions.hlsl — на один уровень предположений больше.
// URP не использует camera-relative rendering, так что результат тот же.
float3 GizmoCameraDir(float3 wp)
{
    float3 toCam = unity_OrthoParams.w > 0.5
        ? UNITY_MATRIX_V._m20_m21_m22
        : (_WorldSpaceCameraPos - wp);

    float l = length(toCam);
    return l > 1e-6 ? toCam / l : float3(0, 0, 1);
}

#endif // RUNTIME_GIZMOS_COMMON_INCLUDED

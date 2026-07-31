using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace RuntimeGizmos.Internal
{
    // ВАЖНО: во всех вершинных структурах Position обязан лежать по смещению 0.
    // Компактор retained-буфера читает позицию как *(Vector3*)vertexPtr.

    /// <summary>16 байт. Тонкие линии (MeshTopology.Lines) и заливка (Triangles).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GizmoVertex
    {
        public Vector3 Position;
        public Color32 Color;
    }

    /// <summary>36 байт. Толстые линии: разворачиваются в квад в вершинном шейдере.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GizmoWideVertex
    {
        public Vector3 Position; // этот конец отрезка
        public Vector3 Other;    // противоположный конец (NORMAL)
        public Color32 Color;
        public Vector2 Params;   // x = сторона (-1/+1), y = ширина в пикселях (TEXCOORD0)
    }

    /// <summary>40 байт. Билборды-иконки и экранные квады.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GizmoQuadVertex
    {
        public Vector3 Position; // мировой центр, либо пиксельные координаты для screen-space
        public Color32 Color;
        public Vector4 Corner;   // xy = смещение угла (-1..1), zw = uv (TEXCOORD0)
        public Vector2 Size;     // x<0 → размер в мировых единицах, иначе в пикселях (TEXCOORD1)
    }

    /// <summary>
    /// Вершина текстовой метки. Position — мировой якорь строки (обязан лежать по смещению 0,
    /// компактор отложенной геометрии читает его как Vector3), остальное — смещения в пикселях.
    /// 40 байт.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GizmoTextVertex
    {
        public Vector3 Position;
        public Color32 Color;
        public Vector2 Offset;
        public Vector2 Other;
        public Vector2 SideWidth;
    }

    internal static class GizmoVertexLayouts
    {
        // Порядок атрибутов обязан быть каноническим:
        // Position, Normal, Tangent, Color, TexCoord0..7
        public static readonly VertexAttributeDescriptor[] Thin =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
        };

        public static readonly VertexAttributeDescriptor[] Wide =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
        };

        public static readonly VertexAttributeDescriptor[] Quad =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2),
        };

        public static readonly VertexAttributeDescriptor[] Text =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2),
        };
    }
}

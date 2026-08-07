using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using RuntimeGizmos.Internal;

namespace RuntimeGizmos.Tests
{
    /// <summary>
    /// То, что офлайн-харнесс проверить не может в принципе: настоящие шейдеры и
    /// настоящая разметка вершинного буфера Unity.
    ///
    /// Расхождение разметки со структурой Unity не диагностирует — оно проявляется
    /// мусором на экране, причём не на всех платформах сразу.
    /// </summary>
    public class ShaderAndLayoutTests
    {
        static readonly string[] Shaders =
        {
            "GizmoUnlit", "GizmoWideLine", "GizmoBillboard", "GizmoScreen", "GizmoText",
        };

        [Test]
        public void Шейдеры_лежат_в_Resources([ValueSource(nameof(Shaders))] string name)
        {
            var s = Resources.Load<Shader>("RuntimeGizmos/" + name);
            Assert.IsNotNull(s, $"шейдер RuntimeGizmos/{name} не найден в Resources");
        }

        [Test]
        public void Шейдеры_компилируются_на_этой_платформе([ValueSource(nameof(Shaders))] string name)
        {
            var s = Resources.Load<Shader>("RuntimeGizmos/" + name);
            if (s == null) Assert.Ignore("шейдер не найден — см. отдельный кейс");
            Assert.IsTrue(s.isSupported, $"шейдер {name} не скомпилировался; пакету нужен URP");
        }

        // Position обязан лежать по смещению 0: компактор retained-буфера читает
        // позицию как *(Vector3*)vertexPtr, не спрашивая разметку.
        [Test]
        public void Position_первым_атрибутом_во_всех_разметках()
        {
            AssertPositionFirst(GizmoVertexLayouts.Thin, nameof(GizmoVertexLayouts.Thin));
            AssertPositionFirst(GizmoVertexLayouts.Wide, nameof(GizmoVertexLayouts.Wide));
            AssertPositionFirst(GizmoVertexLayouts.Quad, nameof(GizmoVertexLayouts.Quad));
            AssertPositionFirst(GizmoVertexLayouts.Text, nameof(GizmoVertexLayouts.Text));
        }

        static void AssertPositionFirst(VertexAttributeDescriptor[] layout, string name)
        {
            Assert.Greater(layout.Length, 0, name + ": пустая разметка");
            Assert.AreEqual(VertexAttribute.Position, layout[0].attribute, name + ": Position не первый");
            Assert.AreEqual(VertexAttributeFormat.Float32, layout[0].format, name + ": Position не Float32");
            Assert.AreEqual(3, layout[0].dimension, name + ": Position не трёхкомпонентный");
        }

        // Порядок атрибутов в SetVertexBufferParams обязан быть каноническим:
        // Position, Normal, Tangent, Color, TexCoord0..7. Unity молча принимает
        // нарушение и отдаёт мусор.
        [Test]
        public void Порядок_атрибутов_канонический()
        {
            AssertOrdered(GizmoVertexLayouts.Thin, nameof(GizmoVertexLayouts.Thin));
            AssertOrdered(GizmoVertexLayouts.Wide, nameof(GizmoVertexLayouts.Wide));
            AssertOrdered(GizmoVertexLayouts.Quad, nameof(GizmoVertexLayouts.Quad));
            AssertOrdered(GizmoVertexLayouts.Text, nameof(GizmoVertexLayouts.Text));
        }

        static void AssertOrdered(VertexAttributeDescriptor[] layout, string name)
        {
            for (int i = 1; i < layout.Length; i++)
                Assert.Less((int)layout[i - 1].attribute, (int)layout[i].attribute,
                            $"{name}: атрибут {layout[i].attribute} стоит после {layout[i - 1].attribute}");
        }

        [Test]
        public void Размер_структуры_совпадает_с_разметкой()
        {
            AssertSize<GizmoVertex>(GizmoVertexLayouts.Thin, nameof(GizmoVertex));
            AssertSize<GizmoWideVertex>(GizmoVertexLayouts.Wide, nameof(GizmoWideVertex));
            AssertSize<GizmoQuadVertex>(GizmoVertexLayouts.Quad, nameof(GizmoQuadVertex));
            AssertSize<GizmoTextVertex>(GizmoVertexLayouts.Text, nameof(GizmoTextVertex));
        }

        static void AssertSize<T>(VertexAttributeDescriptor[] layout, string name) where T : struct
        {
            int declared = GizmoTestHarness.LayoutSize(layout);
            int actual = Marshal.SizeOf<T>();
            Assert.AreEqual(declared, actual,
                $"{name}: разметка обещает {declared} байт, структура занимает {actual}");
        }

        // Настоящий Mesh отказывается принимать разметку, которую заглушка проглотила бы.
        [Test]
        public void Unity_принимает_каждую_разметку()
        {
            AssertMeshAccepts(GizmoVertexLayouts.Thin, nameof(GizmoVertexLayouts.Thin));
            AssertMeshAccepts(GizmoVertexLayouts.Wide, nameof(GizmoVertexLayouts.Wide));
            AssertMeshAccepts(GizmoVertexLayouts.Quad, nameof(GizmoVertexLayouts.Quad));
            AssertMeshAccepts(GizmoVertexLayouts.Text, nameof(GizmoVertexLayouts.Text));
        }

        static void AssertMeshAccepts(VertexAttributeDescriptor[] layout, string name)
        {
            var m = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                m.SetVertexBufferParams(64, layout);
                var got = m.GetVertexAttributes();
                Assert.AreEqual(layout.Length, got.Length, name + ": Unity вернул другое число атрибутов");
                for (int i = 0; i < layout.Length; i++)
                {
                    Assert.AreEqual(layout[i].attribute, got[i].attribute, name + $": атрибут {i}");
                    Assert.AreEqual(layout[i].format, got[i].format, name + $": формат атрибута {i}");
                    Assert.AreEqual(layout[i].dimension, got[i].dimension, name + $": размерность атрибута {i}");
                }
            }
            finally { GizmoTestHarness.Destroy(m); }
        }
    }
}

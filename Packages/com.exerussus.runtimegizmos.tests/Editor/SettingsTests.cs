using NUnit.Framework;
using UnityEngine;
using RuntimeGizmos;
using RuntimeGizmos.Internal;

namespace RuntimeGizmos.Tests
{
    /// <summary>
    /// Разрешение настроек по слоям, клампы и платформенные профили.
    /// В Unity, в отличие от офлайн-харнесса, платформа определяется по-настоящему —
    /// по активному build target.
    /// </summary>
    public class SettingsTests
    {
        [SetUp] public void SetUp() => GizmoSettings.ResetSession();
        [TearDown] public void TearDown() => GizmoSettings.ResetSession();

        [Test]
        public void Оверрайд_из_кода_бьёт_платформенный_дефолт()
        {
            GizmoSettings.PlatformOverride = GizmoPlatform.Desktop;
            Assert.AreEqual(1f, GizmoSettings.DefaultLineWidth, 1e-5f);

            GizmoSettings.DefaultLineWidth = 9f;
            Assert.AreEqual(9f, GizmoSettings.DefaultLineWidth, 1e-5f);

            GizmoSettings.PlatformOverride = GizmoPlatform.Mobile;
            Assert.AreEqual(9f, GizmoSettings.DefaultLineWidth, 1e-5f,
                            "оверрайд не должен зависеть от платформы");

            GizmoSettings.Overrides.DefaultLineWidth = null;
            Assert.AreEqual(2f, GizmoSettings.DefaultLineWidth, 1e-5f,
                            "после снятия оверрайда возвращается дефолт мобилки");
        }

        [Test]
        public void Профили_платформ_различаются()
        {
            GizmoSettings.PlatformOverride = GizmoPlatform.Desktop;
            int desktopCap = GizmoSettings.MaxVerticesPerChannel;
            int desktopSegments = GizmoSettings.CircleSegments;

            GizmoSettings.PlatformOverride = GizmoPlatform.Web;
            Assert.Less(GizmoSettings.MaxVerticesPerChannel, desktopCap,
                        "в вебе куча фиксируется на старте — потолок обязан быть жёстче");
            Assert.Less(GizmoSettings.CircleSegments, desktopSegments);

            GizmoSettings.PlatformOverride = GizmoPlatform.XR;
            Assert.AreEqual(3f, GizmoSettings.DefaultLineWidth, 1e-5f, "в XR всё рисуется дважды");
        }

        [Test]
        public void Значения_зажимаются_в_допустимый_диапазон()
        {
            GizmoSettings.CircleSegments = 100000;
            Assert.AreEqual(256, GizmoSettings.CircleSegments);

            GizmoSettings.CircleSegments = -5;
            Assert.AreEqual(6, GizmoSettings.CircleSegments);

            GizmoSettings.GlobalAlpha = 55f;
            Assert.AreEqual(1f, GizmoSettings.GlobalAlpha, 1e-5f);

            GizmoSettings.GlobalAlpha = -3f;
            Assert.AreEqual(0f, GizmoSettings.GlobalAlpha, 1e-5f);

            GizmoSettings.Layer = 999;
            Assert.AreEqual(31, GizmoSettings.Layer, "слой не бывает больше 31");

            GizmoSettings.MaxVerticesPerChannel = -7;
            Assert.AreEqual(0, GizmoSettings.MaxVerticesPerChannel, "0 означает «без потолка»");
        }

        [Test]
        public void ResetSession_снимает_всё()
        {
            GizmoSettings.PlatformOverride = GizmoPlatform.Web;
            GizmoSettings.CircleSegments = 7;
            GizmoSettings.GlobalAlpha = 0.3f;

            GizmoSettings.ResetSession();

            Assert.IsNull(GizmoSettings.PlatformOverride);
            Assert.AreEqual(1f, GizmoSettings.GlobalAlpha, 1e-5f);
        }

        [Test]
        public void DepthBias_зависит_от_формы_буфера_глубины()
        {
            // Значение выбирается не по платформе, а по тому, обратный ли Z:
            // на прямом [0,1] в OpenGL/GLES точности заметно меньше.
            float bias = GizmoSettings.DepthBias;
            Assert.Greater(bias, 0f, "нулевой сдвиг означал бы z-файтинг линий на поверхностях");
            Assert.Less(bias, 1e-2f, "слишком большой сдвиг оторвёт линии от поверхности");
        }

        [Test]
        public void Платформа_определяется_без_падения()
        {
            GizmoSettings.PlatformOverride = null;
            var p = GizmoSettings.Platform;
            Assert.IsTrue(System.Enum.IsDefined(typeof(GizmoPlatform), p), "неизвестный класс платформы: " + p);
        }

        [Test]
        public void Дефолты_разумны_на_любом_профиле()
        {
            foreach (GizmoPlatform p in System.Enum.GetValues(typeof(GizmoPlatform)))
            {
                GizmoSettings.PlatformOverride = p;

                Assert.Greater(GizmoSettings.DefaultLineWidth, 0f, p + ": нулевая толщина линии");
                Assert.GreaterOrEqual(GizmoSettings.CircleSegments, 6, p + ": окружность из трёх отрезков");
                Assert.GreaterOrEqual(GizmoSettings.SphereRings, 2, p + ": слишком мало колец у сферы");
                Assert.GreaterOrEqual(GizmoSettings.SphereSegments, 4, p + ": слишком мало сегментов у сферы");
                Assert.Greater(GizmoSettings.MaxVerticesPerChannel, 0, p + ": потолок канала не задан");
            }
        }
    }
}

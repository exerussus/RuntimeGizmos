using System.Text;
using UnityEngine;
using RuntimeGizmos;

namespace RuntimeGizmosDemo
{
    /// <summary>
    /// Прогон всех возможностей пакета в одном месте.
    ///
    /// Повесить на пустой объект в сцене и нажать Play. Работает и без Play —
    /// компонент помечен [ExecuteAlways], и это отдельный кейс для проверки:
    /// в Edit Mode геометрия должна быть видна в Scene View и не мерцать.
    ///
    /// Раздел переключается кнопками слева. На что смотреть в каждом — написано
    /// прямо на экране и подробно в CHECKLIST.md рядом.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("RuntimeGizmos/Demo")]
    public class RuntimeGizmosDemo : MonoBehaviour
    {
        public enum Section
        {
            Примитивы,
            ТолщинаЛиний,
            ТестГлубины,
            Текст,
            Длительность,
            МатрицаИScope,
            МешиИИконки,
            Цвета,
            Паттерны,
            Настройки,
            Нагрузка,
        }

        [Header("Что показывать")]
        public Section Current = Section.Примитивы;

        [Header("Необязательные ссылки")]
        [Tooltip("Меш для DrawMesh / DrawWireMesh. Для каркаса нужен Read/Write Enabled.")]
        public Mesh TestMesh;

        [Tooltip("Текстура для DrawIcon и DrawGUITexture.")]
        public Texture TestIcon;

        [Header("Нагрузочный тест")]
        [Range(100, 20000)] public int StressLines = 4000;

        [Header("Заслонка для теста глубины")]
        [Tooltip("Создать куб, за который будет уходить геометрия.")]
        public bool SpawnOccluder = true;

        GameObject _occluder;
        float _durationStamp = -999f;
        readonly StringBuilder _sb = new StringBuilder();

        void OnEnable()
        {
            if (SpawnOccluder && _occluder == null)
            {
                _occluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _occluder.name = "~GizmoDemoOccluder";
                _occluder.hideFlags = HideFlags.DontSave;
                _occluder.transform.SetParent(transform, false);
                _occluder.transform.localPosition = new Vector3(0f, 0f, 1.5f);
                _occluder.transform.localScale = new Vector3(4f, 4f, 0.2f);
            }
        }

        void OnDisable()
        {
            if (_occluder == null) return;
            if (Application.isPlaying) Destroy(_occluder);
            else DestroyImmediate(_occluder);
            _occluder = null;
        }

        // Рисуем из LateUpdate: команды обязаны попасть в ТОТ ЖЕ кадр, без задержки.
        void LateUpdate()
        {
            Vector3 o = transform.position;

            switch (Current)
            {
                case Section.Примитивы:      Primitives(o); break;
                case Section.ТолщинаЛиний:   Widths(o); break;
                case Section.ТестГлубины:    Depth(o); break;
                case Section.Текст:          Text(o); break;
                case Section.Длительность:   Duration(o); break;
                case Section.МатрицаИScope:  Matrices(o); break;
                case Section.МешиИИконки:    Meshes(o); break;
                case Section.Цвета:          Colors(o); break;
                case Section.Паттерны:       Patterns(o); break;
                case Section.Настройки:      SettingsView(o); break;
                case Section.Нагрузка:       Stress(o); break;
            }

            Gizmo.Reset();
        }

        // ================================================================ разделы

        void Primitives(Vector3 o)
        {
            float s = 2.2f;
            Gizmo.lineWidth = 2f;

            Cell(o, -3, 1, "DrawLine",       p => Gizmo.DrawLine(p + Vector3.left * 0.6f, p + Vector3.right * 0.6f));
            Cell(o, -2, 1, "DrawRay",        p => Gizmo.DrawRay(p, Vector3.up));
            Cell(o, -1, 1, "DrawWireCube",   p => Gizmo.DrawWireCube(p, Vector3.one));
            Cell(o,  0, 1, "DrawCube",       p => Gizmo.DrawCube(p, Vector3.one * 0.8f));
            Cell(o,  1, 1, "DrawWireSphere", p => Gizmo.DrawWireSphere(p, 0.6f));
            Cell(o,  2, 1, "DrawSphere",     p => Gizmo.DrawSphere(p, 0.5f));
            Cell(o,  3, 1, "DrawWireDisc",   p => Gizmo.DrawWireDisc(p, Vector3.up, 0.6f));

            Cell(o, -3, 0, "DrawWireArc",    p => Gizmo.DrawWireArc(p, Vector3.up, Vector3.forward, 220f, 0.6f));
            Cell(o, -2, 0, "DrawWireCapsule",p => Gizmo.DrawWireCapsule(p - Vector3.up * 0.4f, p + Vector3.up * 0.4f, 0.35f));
            Cell(o, -1, 0, "DrawWireCone",   p => Gizmo.DrawWireCone(p, Vector3.up, 30f, 1f));
            Cell(o,  0, 0, "DrawArrow",      p => Gizmo.DrawArrow(p - Vector3.up * 0.5f, p + Vector3.up * 0.5f));
            Cell(o,  1, 0, "DrawAxes",       p => Gizmo.DrawAxes(p, Quaternion.identity, 0.7f));
            Cell(o,  2, 0, "DrawPoint",      p => Gizmo.DrawPoint(p, 0.4f));
            Cell(o,  3, 0, "DrawBounds",     p => Gizmo.DrawBounds(new Bounds(p, Vector3.one)));

            Cell(o, -3, -1, "DrawTriangle",  p => Gizmo.DrawTriangle(p + Vector3.up * 0.5f, p + Vector3.right * 0.5f, p - Vector3.right * 0.5f));
            Cell(o, -2, -1, "DrawQuad",      p => Gizmo.DrawQuad(
                p + new Vector3(-0.5f, -0.35f, 0f), p + new Vector3(0.5f, -0.35f, 0f),
                p + new Vector3( 0.5f,  0.35f, 0f), p + new Vector3(-0.5f, 0.35f, 0f)));
            Cell(o, -1, -1, "DrawWireQuad",  p => Gizmo.DrawWireQuad(
                p + new Vector3(-0.5f, -0.35f, 0f), p + new Vector3(0.5f, -0.35f, 0f),
                p + new Vector3( 0.5f,  0.35f, 0f), p + new Vector3(-0.5f, 0.35f, 0f)));
            Cell(o,  0, -1, "DrawFrustum",   p => Gizmo.DrawFrustum(p, 45f, 1.2f, 0.2f, 1.6f));
            Cell(o,  1, -1, "DrawLineStrip", p => Gizmo.DrawLineStrip(new[]
            {
                p + new Vector3(-0.5f, -0.4f, 0f), p + new Vector3(-0.2f, 0.4f, 0f),
                p + new Vector3( 0.2f, -0.4f, 0f), p + new Vector3( 0.5f, 0.4f, 0f),
            }, true));
            Cell(o,  2, -1, "DrawPolyLine",  p => Gizmo.DrawPolyLine(new[]
            {
                p + Vector3.left * 0.5f, p + Vector3.up * 0.4f, p + Vector3.right * 0.5f,
            }));
            Cell(o,  3, -1, "DrawLineList",  p => Gizmo.DrawLineList(new[]
            {
                p + Vector3.left * 0.5f, p + Vector3.right * 0.5f,
                p + Vector3.down * 0.5f, p + Vector3.up * 0.5f,
            }));

            void Cell(Vector3 origin, int cx, int cy, string label, System.Action<Vector3> draw)
            {
                var p = origin + new Vector3(cx * s, cy * s, 0f);
                Gizmo.color = new Color(0.4f, 0.9f, 1f);
                draw(p);
                Gizmo.color = Color.white;
                Gizmo.DrawText(label, p + Vector3.down * 0.95f, 11f);
            }
        }

        void Widths(Vector3 o)
        {
            // Толщина задана в ПИКСЕЛЯХ: отъезд камеры не должен её менять.
            for (int i = 0; i < 8; i++)
            {
                Gizmo.lineWidth = i + 1;
                Gizmo.color = Color.HSVToRGB(i / 8f, 0.8f, 1f);
                var a = o + new Vector3(-3f, i * 0.5f - 2f, 0f);
                Gizmo.DrawLine(a, a + Vector3.right * 6f);
                Gizmo.color = Color.white;
                Gizmo.DrawText((i + 1) + " px", a + Vector3.left * 0.1f, 12f, Vector2.zero, GizmoTextAlign.Right);
            }

            // Тот же набор, но уходящий вдаль — толщина обязана остаться прежней.
            Gizmo.color = Color.white;
            for (int i = 0; i < 8; i++)
            {
                Gizmo.lineWidth = 3f;
                var a = o + new Vector3(-3f, -3f, i * 4f);
                Gizmo.DrawLine(a, a + Vector3.right * 6f);
                Gizmo.DrawText("z=" + (i * 4), a + Vector3.left * 0.2f, 12f, Vector2.zero, GizmoTextAlign.Right);
            }
        }

        void Depth(Vector3 o)
        {
            Gizmo.lineWidth = 3f;

            Gizmo.depthTest = true;
            Gizmo.color = Color.green;
            Gizmo.DrawWireSphere(o + Vector3.left * 1.2f, 1.2f);
            Gizmo.DrawText("depthTest = true", o + Vector3.left * 1.2f + Vector3.down * 1.6f, 13f);

            Gizmo.depthTest = false;
            Gizmo.color = Color.red;
            Gizmo.DrawWireSphere(o + Vector3.right * 1.2f, 1.2f);
            Gizmo.DrawText("depthTest = false", o + Vector3.right * 1.2f + Vector3.down * 1.6f, 13f);

            // Линия ровно по поверхности заслонки — здесь ловится z-файтинг.
            Gizmo.depthTest = true;
            Gizmo.color = Color.yellow;
            Gizmo.lineWidth = 1f;
            for (int i = -3; i <= 3; i++)
            {
                var a = o + new Vector3(-2f, i * 0.4f, 1.39f);
                Gizmo.DrawLine(a, a + Vector3.right * 4f);
            }
            Gizmo.DrawText("линии на самой поверхности — не должно мерцать",
                           o + new Vector3(0f, -2.2f, 1.39f), 12f);
        }

        void Text(Vector3 o)
        {
            Gizmo.color = Color.white;
            Gizmo.lineWidth = 1f;

            Gizmo.DrawText("ABCDEFGHIJKLMNOPQRSTUVWXYZ", o + Vector3.up * 2.0f, 16f);
            Gizmo.DrawText("abcdefghijklmnopqrstuvwxyz", o + Vector3.up * 1.4f, 16f);
            Gizmo.DrawText("0123456789 gjpqy", o + Vector3.up * 0.8f, 16f);
            Gizmo.DrawText("!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~", o + Vector3.up * 0.2f, 16f);
            Gizmo.DrawText("АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ", o + Vector3.down * 0.4f, 16f);
            Gizmo.DrawText("абвгдеёжзийклмнопрстуфхцчшщъыьэюя", o + Vector3.down * 1.0f, 16f);
            Gizmo.DrawText("неизвестный символ: \u6F22\u4E2D — квадрат, а не пустота",
                           o + Vector3.down * 1.6f, 14f);

            float[] sizes = { 8f, 11f, 14f, 20f, 28f, 40f };
            for (int i = 0; i < sizes.Length; i++)
                Gizmo.DrawText("размер " + sizes[i], o + Vector3.down * (2.3f + i * 0.55f), sizes[i]);

            // Выравнивание относительно якоря
            var anchor = o + new Vector3(0f, -6.2f, 0f);
            Gizmo.color = Color.red;
            Gizmo.DrawPoint(anchor, 0.15f);
            Gizmo.color = Color.white;
            Gizmo.DrawText("Left",   anchor, 14f, new Vector2(6f, 14f),  GizmoTextAlign.Left);
            Gizmo.DrawText("Center", anchor, 14f, new Vector2(0f, 0f),   GizmoTextAlign.Center);
            Gizmo.DrawText("Right",  anchor, 14f, new Vector2(-6f, -14f), GizmoTextAlign.Right);

            // Пиксельный размер: одинаков на любой глубине.
            for (int i = 0; i < 6; i++)
                Gizmo.DrawText("пиксели: размер тот же", o + new Vector3(4f, 0.6f, i * 5f), 14f);

            // Мировой размер: уменьшается с расстоянием, как обычная геометрия.
            Gizmo.color = new Color(0.5f, 1f, 0.6f);
            for (int i = 0; i < 6; i++)
                Gizmo.DrawTextWorld("мир: уменьшается", o + new Vector3(4f, 0f, i * 5f), 0.35f);
            Gizmo.color = Color.white;

            // Толщина штриха
            for (int i = 1; i <= 4; i++)
            {
                Gizmo.lineWidth = i;
                Gizmo.DrawText("штрих " + i + " px", o + new Vector3(-5f, 1f - i * 0.6f, 0f), 18f);
            }
        }

        void Duration(Vector3 o)
        {
            Gizmo.lineWidth = 2f;
            Gizmo.color = Color.cyan;
            Gizmo.DrawText("кнопка слева ставит метку на 3 секунды", o + Vector3.up * 2f, 14f);

            float age = Time.realtimeSinceStartup - _durationStamp;
            if (age < 4f)
            {
                Gizmo.color = Color.white;
                Gizmo.DrawText("прошло " + age.ToString("0.0") + " с", o + Vector3.up * 1.4f, 14f);
            }

            // Каждый кадр рисуется след, живущий 2 секунды.
            Gizmo.duration = 2f;
            Gizmo.color = Color.magenta;
            float t = Time.realtimeSinceStartup;
            var p = o + new Vector3(Mathf.Cos(t) * 2f, Mathf.Sin(t * 1.3f) * 1.2f, 0f);
            Gizmo.DrawPoint(p, 0.12f);
            Gizmo.duration = 0f;

            Gizmo.color = Color.white;
            Gizmo.DrawText("след живёт 2 с", o + Vector3.down * 2f, 13f);
        }

        void Matrices(Vector3 o)
        {
            Gizmo.lineWidth = 2f;
            float t = Time.realtimeSinceStartup;

            // Вложенные Scope: и цвет, и матрица обязаны восстановиться.
            var outer = Matrix4x4.TRS(o, Quaternion.Euler(0f, t * 30f, 0f), Vector3.one);
            using (Gizmo.Scope(Color.green, outer))
            {
                Gizmo.DrawWireCube(Vector3.zero, Vector3.one * 2f);

                var inner = outer * Matrix4x4.TRS(new Vector3(2f, 0f, 0f), Quaternion.Euler(t * 90f, 0f, 0f), Vector3.one * 0.5f);
                using (Gizmo.Scope(Color.yellow, inner))
                {
                    Gizmo.DrawWireCube(Vector3.zero, Vector3.one * 2f);
                    Gizmo.DrawAxes(Vector3.zero, Quaternion.identity, 2f);
                }

                // Сюда мы обязаны вернуться с зелёным цветом и внешней матрицей.
                Gizmo.DrawLine(Vector3.zero, new Vector3(2f, 0f, 0f));
            }

            // А сюда — с белым и единичной.
            Gizmo.color = Color.white;
            Gizmo.DrawText("после Scope: белый, без вращения", o + Vector3.down * 2.5f, 13f);
            Gizmo.DrawLine(o + Vector3.left * 2f, o + Vector3.right * 2f);
        }

        void Meshes(Vector3 o)
        {
            Gizmo.lineWidth = 2f;

            if (TestMesh != null)
            {
                Gizmo.color = new Color(0.5f, 0.8f, 1f, 0.6f);
                Gizmo.DrawMesh(TestMesh, o + Vector3.left * 1.5f, Quaternion.identity, Vector3.one);
                Gizmo.color = Color.white;
                Gizmo.DrawText("DrawMesh", o + Vector3.left * 1.5f + Vector3.down * 1.2f, 12f);

                Gizmo.color = Color.green;
                Gizmo.DrawWireMesh(TestMesh, o + Vector3.right * 1.5f, Quaternion.identity, Vector3.one);
                Gizmo.color = Color.white;
                Gizmo.DrawText("DrawWireMesh (нужен Read/Write)", o + Vector3.right * 1.5f + Vector3.down * 1.2f, 12f);
            }
            else
            {
                Gizmo.color = Color.white;
                Gizmo.DrawText("назначь TestMesh в инспекторе", o, 14f);
            }

            if (TestIcon != null)
            {
                Gizmo.DrawIcon(o + Vector3.up * 1.5f, TestIcon);
                Gizmo.DrawText("DrawIcon", o + Vector3.up * 0.9f, 12f);
                Gizmo.DrawGUITexture(new Rect(20f, 20f, 64f, 64f), TestIcon);
            }

            // Встроенная иконка редактора. В билде её не будет — это ожидаемо.
            // Если имени нет, в консоль один раз упадёт понятное предупреждение.
            Gizmo.DrawIcon(o + Vector3.up * 2.5f, "console.infoicon");
            Gizmo.DrawText("console.infoicon (только в редакторе)", o + Vector3.up * 3.1f, 12f);
        }

        void Colors(Vector3 o)
        {
            // Чистые цвета: в линейном пространстве не должны выглядеть пересвеченными.
            Color[] cols = { Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.magenta, Color.white, Color.gray };
            string[] names = { "red", "green", "blue", "yellow", "cyan", "magenta", "white", "gray" };

            for (int i = 0; i < cols.Length; i++)
            {
                var p = o + new Vector3((i - 3.5f) * 1.1f, 0f, 0f);
                Gizmo.color = cols[i];
                Gizmo.DrawCube(p, new Vector3(0.9f, 0.9f, 0.1f));
                Gizmo.lineWidth = 4f;
                Gizmo.DrawLine(p + new Vector3(-0.45f, -0.8f, 0f), p + new Vector3(0.45f, -0.8f, 0f));
                Gizmo.color = Color.white;
                Gizmo.lineWidth = 1f;
                Gizmo.DrawText(names[i], p + Vector3.down * 1.2f, 11f);
            }

            // Полупрозрачность
            for (int i = 0; i < 5; i++)
            {
                Gizmo.color = new Color(1f, 0.5f, 0f, (i + 1) / 5f);
                Gizmo.DrawCube(o + new Vector3((i - 2f) * 1.1f, 2f, 0f), Vector3.one * 0.8f);
            }
            Gizmo.color = Color.white;
            Gizmo.DrawText("альфа 0.2 … 1.0", o + new Vector3(0f, 1f, 0f), 12f);
        }

        void Patterns(Vector3 o)
        {
            float t = Time.realtimeSinceStartup;
            Gizmo.lineWidth = 2f;

            // --- связь между двумя объектами
            var a = new Bounds(o + new Vector3(-4f, 0f, 0f), new Vector3(1.2f, 1.6f, 1.2f));
            var b = new Bounds(o + new Vector3(4f, Mathf.Sin(t) * 1.5f, 0f), Vector3.one);
            Gizmo.DrawLink(a, b, new Color(0.3f, 1f, 0.7f), 2f, "цель");
            Gizmo.color = Color.white;
            Gizmo.DrawText("DrawLink: объём + линия по границам + направление",
                           o + Vector3.down * 2.2f, 13f);

            // --- вынос размеров, как на чертеже
            var box = new Vector3(-4f, 0f, 0f) + o;
            var half = new Vector3(0.6f, 0.8f, 0.6f);
            Gizmo.color = new Color(1f, 0.55f, 0.1f);
            Gizmo.DrawDimension(box + new Vector3(-half.x, -half.y, -half.z),
                                box + new Vector3(half.x, -half.y, -half.z),
                                Vector3.down, 1.6f);                        // ширина
            Gizmo.DrawDimension(box + new Vector3(-half.x, -half.y, -half.z),
                                box + new Vector3(-half.x, half.y, -half.z),
                                Vector3.left, 1.2f);                        // высота
            Gizmo.color = Color.white;

            // --- простая двухсторонняя стрелка
            Gizmo.color = new Color(0.7f, 0.7f, 1f);
            Gizmo.DrawMeasure(o + new Vector3(-1f, -2.6f, 0f), o + new Vector3(3f, -2.6f, 0f));
            Gizmo.color = Color.white;
            Gizmo.DrawText("DrawMeasure", o + new Vector3(1f, -3.1f, 0f), 12f);

            // --- радиус на земле
            Gizmo.color = new Color(1f, 0.6f, 0.2f);
            Gizmo.DrawRange(o + new Vector3(-4f, -3.5f, 4f), 2f, 1f);
            Gizmo.color = Color.white;
            Gizmo.DrawText("DrawRange", o + new Vector3(-4f, -3.9f, 4f), 12f);

            // --- сектор обзора
            Gizmo.color = new Color(1f, 0.9f, 0.3f);
            Gizmo.DrawFieldOfView(o + new Vector3(1f, -3.5f, 4f),
                                  Quaternion.Euler(0f, t * 40f, 0f) * Vector3.forward, 70f, 3f);
            Gizmo.color = Color.white;
            Gizmo.DrawText("DrawFieldOfView", o + new Vector3(1f, -3.9f, 4f), 12f);

            // --- маршрут
            Gizmo.color = new Color(0.5f, 0.8f, 1f);
            var path = new[]
            {
                o + new Vector3(5f, -3.5f, 2f), o + new Vector3(7f, -3.5f, 4f),
                o + new Vector3(6f, -3.5f, 6f), o + new Vector3(4f, -3.5f, 5f),
            };
            Gizmo.DrawPath(path, 0.12f, 1, true);
            Gizmo.color = Color.white;
            Gizmo.DrawText("DrawPath", o + new Vector3(5.5f, -3.9f, 2f), 12f);

            // --- вектор с подписью
            Gizmo.color = Color.magenta;
            Gizmo.DrawVector(o + new Vector3(-1f, 1.5f, 0f),
                             new Vector3(Mathf.Cos(t), Mathf.Sin(t), 0f) * 2f, 1f, "");
            Gizmo.color = Color.white;

            // --- точка попадания
            Gizmo.color = Color.red;
            Gizmo.DrawHit(o + new Vector3(2f, 1.5f, 0f), Quaternion.Euler(0f, 0f, t * 60f) * Vector3.up, 0.2f);
            Gizmo.color = Color.white;
            Gizmo.DrawText("DrawHit", o + new Vector3(2f, 0.9f, 0f), 12f);
        }

        void SettingsView(Vector3 o)
        {
            Gizmo.color = Color.white;
            Gizmo.lineWidth = 1f;

            ref readonly var c = ref GizmoSettings.Current;
            string[] lines =
            {
                "платформа: " + GizmoSettings.Platform,
                "ассет настроек: " + (GizmoSettings.Asset != null ? GizmoSettings.Asset.name : "нет, работают дефолты"),
                "DefaultLineWidth: " + c.DefaultLineWidth,
                "DepthBias: " + c.DepthBias + "  (reversed-Z: " + SystemInfo.usesReversedZBuffer + ")",
                "MaxVerticesPerChannel: " + c.MaxVerticesPerChannel,
                "CircleSegments: " + c.CircleSegments,
                "SphereRings/Segments: " + c.SphereRings + "/" + c.SphereSegments,
                "GlobalAlpha: " + c.GlobalAlpha,
                "Layer: " + c.Layer,
            };

            for (int i = 0; i < lines.Length; i++)
                Gizmo.DrawText(lines[i], o + Vector3.up * (1.6f - i * 0.45f), 14f, Vector2.zero, GizmoTextAlign.Left);
        }

        void Stress(Vector3 o)
        {
            Gizmo.lineWidth = 1f;
            float t = Time.realtimeSinceStartup * 0.2f;

            for (int i = 0; i < StressLines; i++)
            {
                float a = i * 0.017f + t;
                float r = 1f + (i % 100) * 0.05f;
                Gizmo.color = Color.HSVToRGB((i % 360) / 360f, 0.7f, 1f);
                Gizmo.DrawLine(o + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f),
                               o + new Vector3(Mathf.Cos(a + 0.3f) * r, Mathf.Sin(a + 0.3f) * r, 0f));
            }

            Gizmo.color = Color.white;
            Gizmo.DrawText(StressLines + " линий в кадре", o + Vector3.up * 6.5f, 18f);
        }

        // ================================================================ интерфейс

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10f, 100f, 240f, 460f), GUI.skin.box);
            GUILayout.Label("<b>RuntimeGizmos — демо</b>");

            foreach (Section s in System.Enum.GetValues(typeof(Section)))
            {
                bool on = Current == s;
                if (GUILayout.Button((on ? "▸ " : "   ") + s)) Current = s;
            }

            GUILayout.Space(8f);
            if (Current == Section.Длительность && GUILayout.Button("Поставить метку на 3 с"))
            {
                _durationStamp = Time.realtimeSinceStartup;
                Gizmo.duration = 3f;
                Gizmo.color = Color.white;
                Gizmo.lineWidth = 4f;
                Gizmo.DrawWireSphere(transform.position, 2.5f);
                Gizmo.DrawText("МЕТКА", transform.position + Vector3.up * 2.8f, 20f);
                Gizmo.Reset();
            }

            if (GUILayout.Button("Gizmo.Clear()")) Gizmo.Clear();
            if (GUILayout.Button("Настройки: сбросить")) GizmoSettings.ResetOverrides();

            GUILayout.EndArea();

            _sb.Clear();
            _sb.Append("платформа настроек: ").Append(GizmoSettings.Platform);
            _sb.Append("   толщина по умолчанию: ").Append(GizmoSettings.DefaultLineWidth);
            _sb.Append("   экран: ").Append(Screen.width).Append('×').Append(Screen.height);
            GUI.Label(new Rect(10f, 70f, 900f, 22f), _sb.ToString());
            GUI.Label(new Rect(10f, 10f, 900f, 60f), Hint(Current));
        }

        static string Hint(Section s)
        {
            switch (s)
            {
                case Section.Примитивы:
                    return "Каждая фигура подписана. Проверь, что нарисованы все и ни одна не вывернута наизнанку.";
                case Section.ТолщинаЛиний:
                    return "Отъедь и подъедь камерой: толщина в пикселях НЕ должна меняться. Нижний ряд уходит вдаль — там то же самое.\nПереключи камеру в Orthographic — поведение обязано сохраниться.";
                case Section.ТестГлубины:
                    return "Зелёная сфера уходит за куб, красная — рисуется поверх. Жёлтые линии лежат на самой поверхности куба: мерцание = z-файтинг, поднимай GizmoSettings.DepthBias.";
                case Section.Текст:
                    return "Латиница, кириллица, цифры, знаки. Выносные элементы у g j p q y и у д ц щ у ф. Неизвестный символ обязан дать пустой квадрат.\nСправа два ряда вглубь: белый (пиксели) не меняет размер, зелёный (DrawTextWorld) уменьшается. Отъедь камерой и сравни.";
                case Section.Длительность:
                    return "Розовый след живёт 2 секунды. Кнопка ставит метку на 3 секунды. Поставь Time.timeScale = 0 — след обязан продолжать жить и исчезать.";
                case Section.МатрицаИScope:
                    return "Вложенные Scope. Белая линия внизу обязана быть белой и невращающейся — значит состояние восстановилось.";
                case Section.МешиИИконки:
                    return "Назначь TestMesh и TestIcon в инспекторе. Для DrawWireMesh мешу нужен Read/Write Enabled — иначе будет предупреждение и заливка.";
                case Section.Цвета:
                    return "Чистые цвета не должны выглядеть выцветшими или пересвеченными. Сравни с Color Space проекта в Player Settings.";
                case Section.Паттерны:
                    return "DrawLink соединяет два объекта: у каждого показан объём, линия обрезана по габаритам, на середине шеврон направления.\nОранжевым — DrawDimension: выносные линии и размерная со стрелками, как на чертеже. Остальное — DrawMeasure, DrawRange, DrawFieldOfView, DrawPath, DrawVector, DrawHit, DrawLabel.";
                case Section.Настройки:
                    return "Разрешённые значения. Переключи Build Target на Android — в редакторе платформа и толщина обязаны смениться сами.";
                case Section.Нагрузка:
                    return "Смотри Stats и Profiler: draw call'ов должно быть единицы, GC Alloc в кадре — ноль.";
                default:
                    return "";
            }
        }
    }
}

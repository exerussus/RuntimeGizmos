using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using RuntimeGizmos.Internal;

namespace RuntimeGizmos.EditorTools
{
    /// <summary>
    /// Project Settings → RuntimeGizmos.
    ///
    /// Страница правит тот же GizmoSettingsAsset, который пакет ищет в Resources.
    /// Своего хранилища у неё нет намеренно: иначе появилось бы два источника правды,
    /// и настройки в билде расходились бы с тем, что показано в окне.
    ///
    /// Сверх инспектора ассета здесь то, чего в инспекторе быть не может: профиль
    /// платформы на сессию редактора и живая диагностика эдит-мода.
    /// </summary>
    internal sealed class GizmoSettingsPage : SettingsProvider
    {
        internal const string PagePath = "Project/RuntimeGizmos";
        const string AssetFolder = "Assets/Resources";

        static readonly string[] PlatformNames = { "Авто", "Desktop", "Mobile", "Web", "Console", "XR" };

        SerializedObject _so;
        SerializedProperty _global;
        SerializedProperty _platforms;

        GizmoSettingsPage() : base(PagePath, SettingsScope.Project)
        {
            label = "RuntimeGizmos";
            keywords = new[]
            {
                "gizmo", "gizmos", "runtimegizmos", "гизмо", "отладка", "debug draw",
                "urp", "scene view", "edit mode", "line width", "толщина линии",
            };
        }

        [SettingsProvider]
        static SettingsProvider Create() => new GizmoSettingsPage();

        [MenuItem("Tools/RuntimeGizmos/Настройки", false, 0)]
        static void Open() => SettingsService.OpenProjectSettings(PagePath);

        [MenuItem("Tools/RuntimeGizmos/Создать ассет настроек", false, 20)]
        static void CreateAssetFromMenu() => PingOrCreate();

        /// <summary>
        /// Диагностика живая, поэтому окно надо перерисовывать само. Десять раз в секунду
        /// (столько зовут OnInspectorUpdate) — ровно то, что нужно, чтобы видеть, тикает
        /// продюсер или нет; чаще незачем, реже не поймать.
        /// </summary>
        public override void OnInspectorUpdate() => Repaint();

        public override void OnDeactivate()
        {
            _so = null;
            _global = null;
            _platforms = null;
        }

        public override void OnGUI(string searchContext)
        {
            // Подписи длинные («Edit Mode: сколько секунд держать…»), со стандартной
            // шириной метки от значения остаётся полоска в сорок пикселей.
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 280f;

            try
            {
                EditorGUILayout.Space();
                DrawAsset();
                EditorGUILayout.Space();
                DrawPlatform();
                EditorGUILayout.Space();
                DrawDiagnostics();
            }
            finally
            {
                EditorGUIUtility.labelWidth = labelWidth;
            }
        }

        // ==================================================================== ассет

        void DrawAsset()
        {
            var asset = GizmoSettings.Asset;

            if (asset == null)
            {
                _so = null;

                EditorGUILayout.HelpBox(
                    "Ассета настроек нет — работают платформенные дефолты GizmoConfig. " +
                    "Это нормальный режим: ассет нужен только чтобы что-то переопределить. " +
                    "Оверрайды из кода (GizmoSettings.*) работают и без него.",
                    MessageType.Info);

                if (GUILayout.Button("Создать ассет в " + AssetFolder)) PingOrCreate();
                return;
            }

            // Ассет могли удалить или заменить прямо во время работы окна.
            if (_so == null || _so.targetObject != asset)
            {
                _so = new SerializedObject(asset);
                _global = _so.FindProperty("_global");
                _platforms = _so.FindProperty("_platforms");
            }

            EditorGUILayout.LabelField("Ассет настроек", AssetDatabase.GetAssetPath(asset));
            if (GUILayout.Button("Показать в проекте"))
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }

            EditorGUILayout.Space();

            _so.Update();
            EditorGUILayout.PropertyField(_global, new GUIContent("Общие переопределения"), true);
            EditorGUILayout.PropertyField(_platforms, new GUIContent("Под конкретные платформы"), true);

            // Правка идёт мимо инспектора ассета, поэтому OnValidate здесь не позовут —
            // сбрасываем разрешённую конфигурацию сами, иначе окно показывает одно, а
            // рисуется по-старому до перезапуска.
            if (_so.ApplyModifiedProperties()) GizmoSettings.Invalidate();
        }

        static void PingOrCreate()
        {
            var existing = GizmoSettings.Asset;
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                return;
            }

            if (!AssetDatabase.IsValidFolder(AssetFolder))
                AssetDatabase.CreateFolder("Assets", "Resources");

            var asset = ScriptableObject.CreateInstance<GizmoSettingsAsset>();
            AssetDatabase.CreateAsset(asset, AssetFolder + "/" + GizmoSettings.AssetResourceName + ".asset");
            AssetDatabase.SaveAssets();

            GizmoSettings.ReloadAsset();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        // ==================================================================== платформа

        void DrawPlatform()
        {
            EditorGUILayout.LabelField("Профиль платформы", EditorStyles.boldLabel);

            var over = GizmoSettings.PlatformOverride;
            int index = over.HasValue ? (int)over.Value + 1 : 0;
            int next = EditorGUILayout.Popup("Смотреть как", index, PlatformNames);

            if (next != index)
                GizmoSettings.PlatformOverride = next == 0 ? (GizmoPlatform?)null : (GizmoPlatform)(next - 1);

            EditorGUILayout.LabelField("Применяется сейчас", GizmoSettings.Platform.ToString());

            if (over.HasValue)
                EditorGUILayout.HelpBox(
                    "Профиль подменён только для этой сессии редактора: на входе в Play Mode он сбрасывается, " +
                    "в билд не попадает. Без подмены платформа берётся по активному build target.",
                    MessageType.Info);
        }

        // ==================================================================== диагностика

        void DrawDiagnostics()
        {
            EditorGUILayout.LabelField("Диагностика Edit Mode", EditorStyles.boldLabel);

            var pipeline = GraphicsSettings.currentRenderPipeline;
            EditorGUILayout.LabelField("Конвейер", pipeline != null ? pipeline.name : "Built-in");
            if (pipeline == null)
                EditorGUILayout.HelpBox(
                    "Активен Built-in Render Pipeline — рисовать нечем. Пакету нужен URP: " +
                    "назначьте Universal Render Pipeline Asset в Project Settings → Graphics.",
                    MessageType.Warning);

            EditorGUILayout.LabelField("Рендерер",
                GizmoRenderer.Ready ? "поднят" : "не поднят — поднимется на первом Draw*");

            EditorGUILayout.LabelField("Установка", GizmoLoop.EditorInstalled
                ? "подписка на камеры и узел PlayerLoop на месте"
                : "снята — вернётся на следующем тике редактора");

            string ticks;
            if (!GizmoSettings.EditorDriveUpdate) ticks = "выключены настройкой EditorDriveUpdate";
            else if (GizmoLoop.EditorDriving) ticks = "просим каждый кадр — продюсер жив";
            else ticks = "только пробные: рисовать пока некому";
            EditorGUILayout.LabelField("Тики player loop", ticks);

            EditorGUILayout.LabelField("Часы эдит-мода", GizmoRenderer.EditorClockPaused
                ? "стоят: окно Unity неактивно, снимок не гаснет"
                : "идут");

            float since = GizmoRenderer.EditorSinceData;
            EditorGUILayout.LabelField("Последние команды Draw*", since < 0f
                ? "в этой сессии не было"
                : since.ToString("F2") + " с назад, таймаут " + GizmoSettings.EditorStaleTimeout.ToString("F2") + " с");

            EditorGUILayout.LabelField("Живых регистраций GizmoLazy", Registry.Count.ToString());
        }
    }
}

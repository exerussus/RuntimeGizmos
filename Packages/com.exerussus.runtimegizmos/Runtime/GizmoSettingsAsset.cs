using System;
using UnityEngine;

namespace RuntimeGizmos
{
    // Unity не сериализует Nullable<T>, поэтому «задано / не задано» приходится
    // выражать явной парой полей. Зато в инспекторе это читается как галочка.

    [Serializable]
    public struct GizmoOptionalBool
    {
        public bool Use;
        public bool Value;
    }

    [Serializable]
    public struct GizmoOptionalInt
    {
        public bool Use;
        public int Value;
    }

    [Serializable]
    public struct GizmoOptionalUInt
    {
        public bool Use;
        public uint Value;
    }

    [Serializable]
    public struct GizmoOptionalFloat
    {
        public bool Use;
        public float Value;
    }

    /// <summary>Набор переопределений. Пустые (Use = false) поля пропускаются.</summary>
    [Serializable]
    public class GizmoConfigLayer
    {
        [Tooltip("Рисовать в игровых камерах, в том числе в билде.")] public GizmoOptionalBool DrawInGameView;
        [Tooltip("Рисовать в Scene View.")] public GizmoOptionalBool DrawInSceneView;
        [Tooltip("Превью-камеры инспектора, отражения и прочее.")] public GizmoOptionalBool DrawInOtherCameras;
        [Tooltip("Слой геометрии. Учитывается culling mask камеры.")] public GizmoOptionalInt Layer;
        [Tooltip("Rendering layer mask, с которым уходит геометрия.")] public GizmoOptionalUInt RenderingLayerMask;
        [Tooltip("Множитель альфы для всей отрисовки.")] public GizmoOptionalFloat GlobalAlpha;
        [Tooltip("Толщина линии в пикселях, с которой стартует Gizmo.lineWidth и к которой возвращает Gizmo.Reset().")] public GizmoOptionalFloat DefaultLineWidth;
        [Tooltip("Сдвиг глубины к камере (в единицах NDC) для линий с тестом глубины.")] public GizmoOptionalFloat DepthBias;
        [Tooltip("Потолок роста вершинного буфера на канал. 0 — без ограничения.")] public GizmoOptionalInt MaxVerticesPerChannel;
        [Tooltip("Сегментов в окружности.")] public GizmoOptionalInt CircleSegments;
        [Tooltip("Колец в сплошной сфере.")] public GizmoOptionalInt SphereRings;
        [Tooltip("Сегментов в кольце сплошной сферы.")] public GizmoOptionalInt SphereSegments;
        [Tooltip("Edit Mode: сколько секунд держать последний снимок геометрии без новых команд.")] public GizmoOptionalFloat EditorStaleTimeout;
        [Tooltip("Edit Mode: запрашивать перерисовку Scene View при появлении новой геометрии.")] public GizmoOptionalBool EditorAutoRepaint;

        public void ApplyTo(ref GizmoConfig c)
        {
            if (DrawInGameView.Use) c.DrawInGameView = DrawInGameView.Value;
            if (DrawInSceneView.Use) c.DrawInSceneView = DrawInSceneView.Value;
            if (DrawInOtherCameras.Use) c.DrawInOtherCameras = DrawInOtherCameras.Value;
            if (Layer.Use) c.Layer = Layer.Value;
            if (RenderingLayerMask.Use) c.RenderingLayerMask = RenderingLayerMask.Value;
            if (GlobalAlpha.Use) c.GlobalAlpha = GlobalAlpha.Value;
            if (DefaultLineWidth.Use) c.DefaultLineWidth = DefaultLineWidth.Value;
            if (DepthBias.Use) c.DepthBias = DepthBias.Value;
            if (MaxVerticesPerChannel.Use) c.MaxVerticesPerChannel = MaxVerticesPerChannel.Value;
            if (CircleSegments.Use) c.CircleSegments = CircleSegments.Value;
            if (SphereRings.Use) c.SphereRings = SphereRings.Value;
            if (SphereSegments.Use) c.SphereSegments = SphereSegments.Value;
            if (EditorStaleTimeout.Use) c.EditorStaleTimeout = EditorStaleTimeout.Value;
            if (EditorAutoRepaint.Use) c.EditorAutoRepaint = EditorAutoRepaint.Value;
        }
    }

    /// <summary>
    /// Необязательный ассет настроек. Кладётся в любую папку Resources под именем
    /// RuntimeGizmosSettings — тогда он подхватится сам. Если ассета нет, работают
    /// платформенные дефолты из GizmoConfig.
    ///
    /// Порядок внутри ассета: сначала общая секция, поверх неё — секция текущей платформы.
    /// </summary>
    [CreateAssetMenu(menuName = "RuntimeGizmos/Settings", fileName = GizmoSettings.AssetResourceName)]
    public sealed class GizmoSettingsAsset : ScriptableObject
    {
        [Serializable]
        public class PlatformSection
        {
            public GizmoPlatform Platform;
            public GizmoConfigLayer Layer = new GizmoConfigLayer();
        }

        [Header("Общие переопределения")]
        [SerializeField] GizmoConfigLayer _global = new GizmoConfigLayer();

        [Header("Переопределения под конкретные платформы")]
        [SerializeField] PlatformSection[] _platforms = Array.Empty<PlatformSection>();

        internal void ApplyTo(ref GizmoConfig c, GizmoPlatform platform)
        {
            _global?.ApplyTo(ref c);

            if (_platforms == null) return;
            for (int i = 0; i < _platforms.Length; i++)
            {
                var s = _platforms[i];
                if (s != null && s.Platform == platform) s.Layer?.ApplyTo(ref c);
            }
        }

        void OnValidate()
        {
            // Правки в инспекторе должны быть видны сразу, без перезапуска.
            GizmoSettings.Invalidate();
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Tools/RuntimeGizmos/Создать ассет настроек")]
        static void CreateAsset()
        {
            const string dir = "Assets/Resources";
            string path = dir + "/" + GizmoSettings.AssetResourceName + ".asset";

            var existing = UnityEditor.AssetDatabase.LoadAssetAtPath<GizmoSettingsAsset>(path);
            if (existing != null)
            {
                UnityEditor.Selection.activeObject = existing;
                UnityEditor.EditorGUIUtility.PingObject(existing);
                return;
            }

            if (!UnityEditor.AssetDatabase.IsValidFolder(dir))
                UnityEditor.AssetDatabase.CreateFolder("Assets", "Resources");

            var asset = CreateInstance<GizmoSettingsAsset>();
            UnityEditor.AssetDatabase.CreateAsset(asset, path);
            UnityEditor.AssetDatabase.SaveAssets();

            GizmoSettings.ReloadAsset();
            UnityEditor.Selection.activeObject = asset;
        }
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// Рисует пару Use/Value одной строкой: галочка слева, значение справа и погашено,
    /// пока галочка снята. Иначе ассет превращается в четырнадцать раскрывающихся списков.
    /// </summary>
    [UnityEditor.CustomPropertyDrawer(typeof(GizmoOptionalBool))]
    [UnityEditor.CustomPropertyDrawer(typeof(GizmoOptionalInt))]
    [UnityEditor.CustomPropertyDrawer(typeof(GizmoOptionalUInt))]
    [UnityEditor.CustomPropertyDrawer(typeof(GizmoOptionalFloat))]
    internal sealed class GizmoOptionalDrawer : UnityEditor.PropertyDrawer
    {
        public override void OnGUI(Rect pos, UnityEditor.SerializedProperty prop, GUIContent label)
        {
            var use = prop.FindPropertyRelative("Use");
            var val = prop.FindPropertyRelative("Value");
            if (use == null || val == null)
            {
                UnityEditor.EditorGUI.PropertyField(pos, prop, label, true);
                return;
            }

            UnityEditor.EditorGUI.BeginProperty(pos, label, prop);

            float toggleW = UnityEditor.EditorGUIUtility.labelWidth;
            var togglePos = new Rect(pos.x, pos.y, toggleW, UnityEditor.EditorGUIUtility.singleLineHeight);
            use.boolValue = UnityEditor.EditorGUI.ToggleLeft(togglePos, label, use.boolValue);

            var valuePos = new Rect(togglePos.xMax + 4f, pos.y,
                Mathf.Max(40f, pos.xMax - togglePos.xMax - 4f),
                UnityEditor.EditorGUIUtility.singleLineHeight);

            using (new UnityEditor.EditorGUI.DisabledScope(!use.boolValue))
                UnityEditor.EditorGUI.PropertyField(valuePos, val, GUIContent.none);

            UnityEditor.EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(UnityEditor.SerializedProperty prop, GUIContent label)
            => UnityEditor.EditorGUIUtility.singleLineHeight;
    }
#endif
}

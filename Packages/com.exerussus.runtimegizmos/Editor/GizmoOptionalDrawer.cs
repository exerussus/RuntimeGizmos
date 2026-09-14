using UnityEditor;
using UnityEngine;

namespace RuntimeGizmos.EditorTools
{
    /// <summary>
    /// Рисует пару Use/Value одной строкой: галочка слева, значение справа и погашено,
    /// пока галочка снята. Иначе ассет превращается в двадцать раскрывающихся списков.
    /// </summary>
    [CustomPropertyDrawer(typeof(GizmoOptionalBool))]
    [CustomPropertyDrawer(typeof(GizmoOptionalInt))]
    [CustomPropertyDrawer(typeof(GizmoOptionalUInt))]
    [CustomPropertyDrawer(typeof(GizmoOptionalFloat))]
    internal sealed class GizmoOptionalDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect pos, SerializedProperty prop, GUIContent label)
        {
            var use = prop.FindPropertyRelative("Use");
            var val = prop.FindPropertyRelative("Value");
            if (use == null || val == null)
            {
                EditorGUI.PropertyField(pos, prop, label, true);
                return;
            }

            EditorGUI.BeginProperty(pos, label, prop);

            float toggleW = EditorGUIUtility.labelWidth;
            var togglePos = new Rect(pos.x, pos.y, toggleW, EditorGUIUtility.singleLineHeight);
            use.boolValue = EditorGUI.ToggleLeft(togglePos, label, use.boolValue);

            var valuePos = new Rect(togglePos.xMax + 4f, pos.y,
                Mathf.Max(40f, pos.xMax - togglePos.xMax - 4f),
                EditorGUIUtility.singleLineHeight);

            using (new EditorGUI.DisabledScope(!use.boolValue))
                EditorGUI.PropertyField(valuePos, val, GUIContent.none);

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty prop, GUIContent label)
            => EditorGUIUtility.singleLineHeight;
    }
}

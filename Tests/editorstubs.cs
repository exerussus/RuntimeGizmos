// ВНИМАНИЕ: заглушки Unity API. Компилируются ТОЛЬКО вне Unity — харнессом из run.sh.
//
// Директива ниже обязательна. Если репозиторий склонировать внутрь Assets/, Unity
// подхватит эти файлы и получит вторые определения Vector3, Color, Transform,
// MonoBehaviour и десятков других типов — проект перестанет собираться целиком.
// Внутри Unity символ определён всегда, поэтому файл схлопывается в пустой.
#if !UNITY_2020_3_OR_NEWER

using System; using UnityEngine;
namespace UnityEditor {
  public enum PlayModeStateChange { EnteredEditMode, ExitingEditMode, EnteredPlayMode, ExitingPlayMode }
  public static class EditorApplication { public static event Action update; public static event Action<PlayModeStateChange> playModeStateChanged;
    public static bool isPlaying=>false;
    public static bool isFocused=true;
    // Счётчик вместо самого тика: харнессу важно, просит ли пакет редактор крутить player loop.
    public static int QueuedPlayerLoopUpdates;
    public static void QueuePlayerLoopUpdate(){ QueuedPlayerLoopUpdates++; }
    static EditorApplication(){update=null;playModeStateChanged=null;} }
  public static class AssemblyReloadEvents { public static event Action beforeAssemblyReload; static AssemblyReloadEvents(){beforeAssemblyReload=null;} }
  [AttributeUsage(AttributeTargets.Method)] public class InitializeOnLoadMethodAttribute : Attribute {}
  [AttributeUsage(AttributeTargets.Method)] public class MenuItemAttribute : Attribute { public MenuItemAttribute(string s){} public MenuItemAttribute(string s,bool v,int p){} }
  public class SceneView { public static void RepaintAll(){} }
  public static class EditorGUIUtility { public static float labelWidth; public static float singleLineHeight=>18f;
    public static GUIContent IconContent(string n)=>null; public static void PingObject(UnityEngine.Object o){} }
  public enum BuildTarget { NoTarget=-2, StandaloneWindows=5, iOS=9, Android=13, StandaloneWindows64=19, WebGL=20, StandaloneLinux64=24, tvOS=37, StandaloneOSX=2 }
  public static class EditorUserBuildSettings { public static BuildTarget activeBuildTarget=>BuildTarget.StandaloneWindows64; }
  public static class AssetDatabase { public static T LoadAssetAtPath<T>(string p) where T : UnityEngine.Object => null;
    public static string GetAssetPath(UnityEngine.Object o)=>"";
    public static void CreateAsset(UnityEngine.Object o,string p){} public static void SaveAssets(){}
    public static bool IsValidFolder(string p)=>true; public static string CreateFolder(string a,string b)=>""; }
  public static class Selection { public static UnityEngine.Object activeObject; }
  public class SerializedProperty { public bool boolValue; public int intValue; public float floatValue;
    public SerializedProperty FindPropertyRelative(string n)=>null; }
  public abstract class PropertyDrawer { public virtual void OnGUI(Rect r, SerializedProperty p, GUIContent l){}
    public virtual float GetPropertyHeight(SerializedProperty p, GUIContent l)=>0f; }
  [AttributeUsage(AttributeTargets.Class, AllowMultiple=true)] public class CustomPropertyDrawer : Attribute { public CustomPropertyDrawer(Type t){} public CustomPropertyDrawer(Type t,bool c){} }
  public enum SettingsScope { User, Project }
  public abstract class SettingsProvider {
    protected SettingsProvider(string path, SettingsScope scope){ settingsPath=path; this.scope=scope; }
    public string settingsPath; public SettingsScope scope; public string label;
    public System.Collections.Generic.IEnumerable<string> keywords;
    public virtual void OnGUI(string searchContext){}
    public virtual void OnDeactivate(){}
    public virtual void OnInspectorUpdate(){}
    public void Repaint(){} }
  [AttributeUsage(AttributeTargets.Method)] public class SettingsProviderAttribute : Attribute {}
  public static class SettingsService { public static void OpenProjectSettings(string path){} }
  public class SerializedObject { public SerializedObject(UnityEngine.Object o){ targetObject=o; }
    public UnityEngine.Object targetObject; public void Update(){} public bool ApplyModifiedProperties()=>false;
    public SerializedProperty FindProperty(string n)=>null; }
  public static class EditorStyles { public static GUIStyle boldLabel=new GUIStyle(); }
  public enum MessageType { None, Info, Warning, Error }
  public static class EditorGUILayout {
    public static void Space(){}
    public static void LabelField(string a){}
    public static void LabelField(string a,string b){}
    public static void LabelField(string a,GUIStyle s){}
    public static void HelpBox(string m, MessageType t){}
    public static void PropertyField(SerializedProperty p, GUIContent l, bool inc=false){}
    public static int Popup(string label,int i,string[] opts)=>i; }
  public static class EditorGUI {
    public static void BeginProperty(Rect r, GUIContent l, SerializedProperty p){}
    public static void EndProperty(){}
    public static bool ToggleLeft(Rect r, GUIContent l, bool v)=>v;
    public static void PropertyField(Rect r, SerializedProperty p, GUIContent l, bool inc=false){}
    public struct DisabledScope : IDisposable { public DisabledScope(bool d){} public void Dispose(){} } }
}

#endif

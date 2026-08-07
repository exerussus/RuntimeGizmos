// ВНИМАНИЕ: заглушки Unity API. Компилируются ТОЛЬКО вне Unity — харнессом из run.sh.
//
// Директива ниже обязательна. Если репозиторий склонировать внутрь Assets/, Unity
// подхватит эти файлы и получит вторые определения Vector3, Color, Transform,
// MonoBehaviour и десятков других типов — проект перестанет собираться целиком.
// Внутри Unity символ определён всегда, поэтому файл схлопывается в пустой.
#if !UNITY_2020_3_OR_NEWER

// Заглушки Unity API — только сигнатуры, чтобы прогнать компилятор по пакету.
using System; using System.Collections.Generic;
namespace Unity.Collections {
  // Реестр живых аллокаций: NativeArray — структура, копии в списке хранили бы
  // уже освобождённые указатели, поэтому следим по адресу.
  public static unsafe class NativeGuard {
    public class Rec { public int Size; public string T; }
    public static readonly Dictionary<IntPtr, Rec> Live = new Dictionary<IntPtr, Rec>();
    public const int Canary = 64;
    public static void Register(IntPtr p, int sz, string t) { lock (Live) Live[p] = new Rec { Size = sz, T = t }; }
    public static void Unregister(IntPtr p) { lock (Live) Live.Remove(p); }
    public static List<string> Broken() {
      var bad = new List<string>();
      lock (Live) foreach (var kv in Live) {
        byte* b = (byte*)kv.Key + kv.Value.Size;
        for (int i = 0; i < Canary; i++) if (b[i] != 0xAB) { bad.Add(kv.Value.T + " size=" + kv.Value.Size); break; }
      }
      return bad;
    }
    public static int Count { get { lock (Live) return Live.Count; } }
  }
  public enum Allocator { Invalid, None, Temp, TempJob, Persistent }
  public enum NativeArrayOptions { UninitializedMemory, ClearMemory }
  public unsafe struct NativeArray<T> : IDisposable where T : struct {
    internal void* _p; internal int _len; internal int _sz;
    public const int Canary = 64;
    public static int Live;
    public bool CanaryIntact { get { if (_p == null) return true;
      for (int i = 0; i < Canary; i++) if (((byte*)_p)[_sz + i] != 0xAB) return false; return true; } }
    public NativeArray(int len, Allocator a, NativeArrayOptions o = NativeArrayOptions.ClearMemory) {
      _len = len;
      int sz = System.Runtime.CompilerServices.Unsafe.SizeOf<T>() * Math.Max(1, len);
      _sz = sz;
      _p = (void*)System.Runtime.InteropServices.Marshal.AllocHGlobal(sz + Canary);
      if (o == NativeArrayOptions.ClearMemory) new Span<byte>(_p, sz).Clear();
      new Span<byte>((byte*)_p + sz, Canary).Fill(0xAB);
      NativeGuard.Register((IntPtr)_p, sz, typeof(T).Name);
      Live++;
    }
    public int Length => _len;
    public bool IsCreated => _p != null;
    public T this[int i] {
      get { if ((uint)i >= (uint)_len) throw new IndexOutOfRangeException("NativeArray["+i+"] len="+_len);
            return System.Runtime.CompilerServices.Unsafe.Read<T>((byte*)_p + (long)i * System.Runtime.CompilerServices.Unsafe.SizeOf<T>()); }
      set { if ((uint)i >= (uint)_len) throw new IndexOutOfRangeException("NativeArray["+i+"] len="+_len);
            System.Runtime.CompilerServices.Unsafe.Write((byte*)_p + (long)i * System.Runtime.CompilerServices.Unsafe.SizeOf<T>(), value); }
    }
    public void Dispose() { if (_p != null) {
      if (!CanaryIntact) throw new InvalidOperationException("ЗАПИСЬ ЗА ГРАНИЦУ БУФЕРА NativeArray<" + typeof(T).Name + "> len=" + _len);
      NativeGuard.Unregister((IntPtr)_p);
      System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)_p); _p = null; _len = 0; Live--; } }
  }
}
namespace Unity.Collections.LowLevel.Unsafe {
  public static unsafe class UnsafeUtility {
    public static void MemCpy(void* d, void* s, long n) { Buffer.MemoryCopy(s, d, n, n); }
    public static int SizeOf<T>() where T : struct => System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
  }
  public static unsafe class NativeArrayUnsafeUtility {
    public static void* GetUnsafePtr<T>(Unity.Collections.NativeArray<T> a) where T : struct => a._p;
    public static void* GetUnsafeReadOnlyPtr<T>(Unity.Collections.NativeArray<T> a) where T : struct => a._p;
  }
}
namespace UnityEngine {
  public struct Vector2 { public float x,y; public Vector2(float x,float y){this.x=x;this.y=y;}
    public static Vector2 zero=>new Vector2(0,0); public static Vector2 one=>new Vector2(1,1); public float magnitude=>(float)Math.Sqrt(x*x+y*y);
    public static Vector2 operator*(Vector2 a,float b)=>new Vector2(a.x*b,a.y*b);
    public static Vector2 operator/(Vector2 a,float b)=>new Vector2(a.x/b,a.y/b);
    public static Vector2 operator+(Vector2 a,Vector2 b)=>new Vector2(a.x+b.x,a.y+b.y);
    public static Vector2 operator-(Vector2 a,Vector2 b)=>new Vector2(a.x-b.x,a.y-b.y);
    public static implicit operator Vector3(Vector2 v)=>new Vector3(v.x,v.y,0);
    public static implicit operator Vector2(Vector3 v)=>new Vector2(v.x,v.y);
    public override string ToString()=>$"({x},{y})"; }
  public struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
    public Vector3(float x,float y):this(x,y,0){}
    public static Vector3 zero=>new Vector3(0,0,0); public static Vector3 one=>new Vector3(1,1,1); public static Vector3 up=>new Vector3(0,1,0);
    public static Vector3 right=>new Vector3(1,0,0); public static Vector3 forward=>new Vector3(0,0,1);
    public static Vector3 left=>new Vector3(-1,0,0); public static Vector3 down=>new Vector3(0,-1,0); public static Vector3 back=>new Vector3(0,0,-1);
    public float sqrMagnitude=>x*x+y*y+z*z; public float magnitude=>(float)Math.Sqrt(x*x+y*y+z*z);
    public Vector3 normalized { get { float m=magnitude; return m>1e-9f? this/m : zero; } }
    public void Normalize(){ float m=magnitude; if(m>1e-9f){x/=m;y/=m;z/=m;} else {x=y=z=0;} }
    public static Vector3 Normalize(Vector3 v)=>v.normalized;
    public static Vector3 Cross(Vector3 a,Vector3 b)=>new Vector3(a.y*b.z-a.z*b.y, a.z*b.x-a.x*b.z, a.x*b.y-a.y*b.x);
    public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
    public static Vector3 Min(Vector3 a,Vector3 b)=>new Vector3(Math.Min(a.x,b.x),Math.Min(a.y,b.y),Math.Min(a.z,b.z));
    public static Vector3 Max(Vector3 a,Vector3 b)=>new Vector3(Math.Max(a.x,b.x),Math.Max(a.y,b.y),Math.Max(a.z,b.z));
    public static Vector3 operator+(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
    public static Vector3 operator-(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    public static Vector3 operator-(Vector3 a)=>new Vector3(-a.x,-a.y,-a.z);
    public static Vector3 operator*(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
    public static Vector3 operator*(float b,Vector3 a)=>new Vector3(a.x*b,a.y*b,a.z*b);
    public static Vector3 operator/(Vector3 a,float b)=>new Vector3(a.x/b,a.y/b,a.z/b);
    public static bool operator==(Vector3 a,Vector3 b)=>(a-b).sqrMagnitude<1e-10f; public static bool operator!=(Vector3 a,Vector3 b)=>!(a==b);
    public override bool Equals(object o)=>o is Vector3 v && this==v; public override int GetHashCode()=>0;
    public override string ToString()=>$"({x},{y},{z})"; }
  public struct Vector2Int { public int x,y; public Vector2Int(int x,int y){this.x=x;this.y=y;} }
  public struct Vector4 { public float x,y,z,w; public Vector4(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}
    public static implicit operator Vector4(Vector3 v)=>default; public static implicit operator Vector3(Vector4 v)=>default; }
  public struct Color { public float r,g,b,a; public Color(float r,float g,float b,float a=1){this.r=r;this.g=g;this.b=b;this.a=a;}
    public static Color gray=>new Color(.5f,.5f,.5f,1); public static Color grey=>new Color(.5f,.5f,.5f,1); public static Color yellow=>new Color(1,1,0,1); public static Color cyan=>new Color(0,1,1,1); public static Color magenta=>new Color(1,0,1,1);
    public static Color HSVToRGB(float h,float s,float v)=>white;
    public static Color white=>new Color(1,1,1,1); public static Color red=>new Color(1,0,0,1); public static Color green=>new Color(0,1,0,1); public static Color blue=>new Color(0,0,1,1);
    public Color linear=>default; public Color gamma=>default;
    public static bool operator==(Color a,Color b)=>a.r==b.r&&a.g==b.g&&a.b==b.b&&a.a==b.a; public static bool operator!=(Color a,Color b)=>!(a==b);
    public override bool Equals(object o)=>true; public override int GetHashCode()=>0;
    public static Color operator*(Color a,float b)=>a; public static Color operator*(Color a,Color b)=>a;
    public static implicit operator Color(Color32 c)=>new Color(c.r/255f,c.g/255f,c.b/255f,c.a/255f);
    public static implicit operator Color32(Color c)=>new Color32((byte)(Math.Clamp(c.r,0,1)*255),(byte)(Math.Clamp(c.g,0,1)*255),(byte)(Math.Clamp(c.b,0,1)*255),(byte)(Math.Clamp(c.a,0,1)*255));
    public override string ToString()=>$"({r},{g},{b},{a})"; }
  public struct Color32 { public byte r,g,b,a; public Color32(byte r,byte g,byte b,byte a){this.r=r;this.g=g;this.b=b;this.a=a;} }
  public struct Quaternion { public float x,y,z,w; public static Quaternion identity=>default;
    public static Quaternion AngleAxis(float a,Vector3 v)=>identity; public static Quaternion LookRotation(Vector3 f,Vector3 u)=>default;
    public static Quaternion Euler(float x,float y,float z)=>default;
    public static Vector3 operator*(Quaternion q,Vector3 v)=>v; public static Quaternion operator*(Quaternion a,Quaternion b)=>a; }
  public struct Matrix4x4 { public float m00,m01,m02,m03,m10,m11,m12,m13,m20,m21,m22,m23,m30,m31,m32,m33;
    public static Matrix4x4 identity { get { var m=new Matrix4x4(); m.m00=m.m11=m.m22=m.m33=1; return m; } }
    public static Matrix4x4 TRS(Vector3 p,Quaternion r,Vector3 s){ var m=identity; m.m00=s.x; m.m11=s.y; m.m22=s.z; m.m03=p.x; m.m13=p.y; m.m23=p.z; return m; }
    public Vector3 MultiplyPoint3x4(Vector3 p)=>new Vector3(m00*p.x+m01*p.y+m02*p.z+m03, m10*p.x+m11*p.y+m12*p.z+m13, m20*p.x+m21*p.y+m22*p.z+m23);
    public Vector3 MultiplyVector(Vector3 p)=>new Vector3(m00*p.x+m01*p.y+m02*p.z, m10*p.x+m11*p.y+m12*p.z, m20*p.x+m21*p.y+m22*p.z);
    public Matrix4x4 inverse=>default; public Vector3 lossyScale=>default;
    public static Matrix4x4 operator*(Matrix4x4 a,Matrix4x4 b)=>a;
    public static bool operator==(Matrix4x4 a,Matrix4x4 b)=>a.m00==b.m00&&a.m01==b.m01&&a.m02==b.m02&&a.m03==b.m03&&a.m10==b.m10&&a.m11==b.m11&&a.m12==b.m12&&a.m13==b.m13&&a.m20==b.m20&&a.m21==b.m21&&a.m22==b.m22&&a.m23==b.m23&&a.m30==b.m30&&a.m31==b.m31&&a.m32==b.m32&&a.m33==b.m33;
    public static bool operator!=(Matrix4x4 a,Matrix4x4 b)=>!(a==b);
    public override bool Equals(object o)=>true; public override int GetHashCode()=>0;
    public Vector4 GetColumn(int i)=>default; }
  public struct Bounds { public Bounds(Vector3 c,Vector3 s){center=c;size=s;} public Vector3 center,size;
    public Vector3 extents=>default; public Vector3 min=>default; public Vector3 max=>default;
    public void Encapsulate(Vector3 p){} public void Encapsulate(Bounds b){} public void SetMinMax(Vector3 a,Vector3 b){} public void Expand(float f){} }
  public struct Ray { public Ray(Vector3 o,Vector3 d){origin=o;direction=d;} public Vector3 origin,direction; }
  public struct Rect { public Rect(float x,float y,float w,float h){this.x=x;this.y=y;width=w;height=h;}
    public float x,y,width,height; public float xMax=>0; public float xMin=>0; public float yMax=>0; public float yMin=>0;
    public Vector2 center=>default; public Vector2 min=>default; public Vector2 max=>default; }
  public static class Mathf { public const float PI=3.14159265359f; public const float Deg2Rad=0.01745329252f; public const float Rad2Deg=57.2957795f; public const float Epsilon=1e-5f;
    public static float Abs(float v)=>Math.Abs(v); public static int Abs(int v)=>Math.Abs(v);
    public static float Max(float a,float b)=>Math.Max(a,b); public static int Max(int a,int b)=>Math.Max(a,b);
    public static float Min(float a,float b)=>Math.Min(a,b); public static int Min(int a,int b)=>Math.Min(a,b);
    public static float Clamp(float v,float a,float b)=>v<a?a:(v>b?b:v); public static int Clamp(int v,int a,int b)=>v<a?a:(v>b?b:v);
    public static float Clamp01(float v)=>v<0?0:(v>1?1:v);
    public static float Cos(float v)=>(float)Math.Cos(v); public static float Sin(float v)=>(float)Math.Sin(v); public static float Tan(float v)=>(float)Math.Tan(v);
    public static float Sqrt(float v)=>(float)Math.Sqrt(v);
    public static int CeilToInt(float v)=>(int)Math.Ceiling(v); public static int FloorToInt(float v)=>(int)Math.Floor(v);
    public static int NextPowerOfTwo(int v){ if(v<=0)return 0; v--; v|=v>>1; v|=v>>2; v|=v>>4; v|=v>>8; v|=v>>16; return v+1; }
    public static float Repeat(float a,float b)=>a-(float)Math.Floor(a/b)*b;
    public static float Sign(float v)=>v<0?-1f:1f;
    public static float PingPong(float t,float l){ t=Repeat(t,l*2f); return l-Math.Abs(t-l); } }
  public static class Debug { public static void Log(object o){} public static void LogWarning(object o){} public static void LogError(object o){} }
  // 64-битный идентификатор из Unity 6.5. Заглушка повторяет только то, на что
  // опирается пакет: сравнение, хеш и получение у объекта.
  public readonly struct EntityId : System.IEquatable<EntityId> {
    readonly ulong _v;
    public EntityId(ulong v) { _v = v; }
    public bool IsValid() => _v != 0;
    public static ulong ToULong(EntityId e) => e._v;
    public bool Equals(EntityId o) => _v == o._v;
    public override bool Equals(object o) => o is EntityId e && Equals(e);
    public override int GetHashCode() => _v.GetHashCode();
    public override string ToString() => _v.ToString();
  }
  public class Object { public string name; public HideFlags hideFlags; internal bool _dead;
    public static void Destroy(Object o){ if(o!=null) o._dead=true; } public static void DestroyImmediate(Object o){ if(o!=null) o._dead=true; }
    static bool Nul(Object o)=>o is null || o._dead;
    static int _nextId = 1000; int _id;
    public int GetInstanceID(){ if(_id==0) _id=System.Threading.Interlocked.Increment(ref _nextId); return _id; }
    public EntityId GetEntityId() => new EntityId((ulong)GetInstanceID());
    public static bool operator==(Object a,Object b){ bool x=Nul(a), y=Nul(b); if(x||y) return x&&y; return ReferenceEquals(a,b); }
    public static bool operator!=(Object a,Object b)=>!(a==b);
    public override bool Equals(object o)=>false; public override int GetHashCode()=>0; }
  [Flags] public enum HideFlags { None=0, DontSave=52, HideAndDontSave=61 }
  public enum ColorSpace { Gamma, Linear }
  public class Texture : Object { public int width, height; }
  public class Texture2D : Texture { }
  public class Shader : Object { public bool isSupported=>Supported; public static bool Supported=true; public static bool Available=true;
    public static Shader Find(string n)=>Available?new Shader{name=n}:null; public static int PropertyToID(string n)=>0; }
  public class Material : Object { public Material(Shader s){} public int renderQueue;
    // Счётчик нужен кейсам про глобальную альфу: она раскладывается по материалам
    // только при изменении, и проверить это можно лишь числом вызовов.
    public static int SetFloatCalls;
    public void SetFloat(int id,float v){ SetFloatCalls++; } public void SetColor(int id,Color c){} public void SetTexture(int id,Texture t){} }
  public class MaterialPropertyBlock { public void SetColor(int id,Color c){} public void SetTexture(int id,Texture t){} public void SetFloat(int id,float v){} public void Clear(){} }
  public class Mesh : Object { public Bounds bounds; public int subMeshCount; public int vertexCount;
    public Vector3[] vertices; public int[] triangles;
    public void MarkDynamic(){} public void Clear(){}
    public int[] GetIndices(int s)=>null; public Rendering.MeshTopology GetTopology(int s)=>default;
    public bool isReadable=>true;
    public int VBCap=-1, IBCap=-1, IBFilled=-1, VertStride;
    public Rendering.SubMeshDescriptor Sub; public bool SubSet;
    public readonly List<(int dst,int cnt)> Writes = new List<(int,int)>(); public int Covered;

    // Счётчики трафика в GPU-буферы. Нужны бенчмарку: главная цена статики —
    // это перезаливка одних и тех же вершин каждый кадр, и её надо видеть числом,
    // а не на глаз по профайлеру.
    public static long UpVerts, UpBytes, IdxBytes; public static int UpCalls, IdxCalls, ParamCalls;
    public static void ResetCounters(){ UpVerts=UpBytes=IdxBytes=0; UpCalls=IdxCalls=ParamCalls=0; }

    public void SetVertexBufferParams(int c, params Rendering.VertexAttributeDescriptor[] a){
      VBCap=c; VertStride=0; foreach(var x in a) VertStride+=x.ByteSize; Writes.Clear(); ParamCalls++; }
    public void SetVertexBufferData<T>(Unity.Collections.NativeArray<T> d,int s,int o,int c,int stream=0,Rendering.MeshUpdateFlags f=Rendering.MeshUpdateFlags.Default) where T:struct {
      if (VBCap>=0 && o+c > VBCap) throw new InvalidOperationException("ЗАЛИВКА ЗА ЁМКОСТЬ ВЕРШИННОГО БУФЕРА: dst="+o+" cnt="+c+" cap="+VBCap);
      if (s+c > d.Length) throw new InvalidOperationException("ЧТЕНИЕ ЗА ГРАНИЦУ ИСТОЧНИКА: src="+s+" cnt="+c+" len="+d.Length);
      if (System.Runtime.CompilerServices.Unsafe.SizeOf<T>() != VertStride) throw new InvalidOperationException("РАЗМЕР ВЕРШИНЫ НЕ СОВПАЛ С РАЗМЕТКОЙ: struct="+System.Runtime.CompilerServices.Unsafe.SizeOf<T>()+" layout="+VertStride);
      UpCalls++; UpVerts+=c; UpBytes+=(long)c*VertStride;
      Writes.Add((o,c)); }
    public void SetIndexBufferParams(int c, Rendering.IndexFormat f){ IBCap=c; IBFilled=0; }
    public void SetIndexBufferData<T>(Unity.Collections.NativeArray<T> d,int s,int o,int c,Rendering.MeshUpdateFlags f=Rendering.MeshUpdateFlags.Default) where T:struct {
      if (s+c > d.Length) throw new InvalidOperationException("ИНДЕКСОВ В ПУЛЕ МЕНЬШЕ, ЧЕМ ЗАЛИВАЕТСЯ: cnt="+c+" пул="+d.Length);
      if (o+c > IBCap) throw new InvalidOperationException("ЗАЛИВКА ЗА ЁМКОСТЬ ИНДЕКСНОГО БУФЕРА");
      IdxCalls++; IdxBytes+=(long)c*4;
      IBFilled=o+c; }
    public void SetSubMesh(int i, Rendering.SubMeshDescriptor d, Rendering.MeshUpdateFlags f=Rendering.MeshUpdateFlags.Default){
      if (d.indexStart+d.indexCount > IBFilled) throw new InvalidOperationException("SUBMESH ССЫЛАЕТСЯ НА НЕЗАЛИТЫЕ ИНДЕКСЫ: нужно "+(d.indexStart+d.indexCount)+", залито "+IBFilled);
      if (d.firstVertex+d.vertexCount > VBCap) throw new InvalidOperationException("SUBMESH ССЫЛАЕТСЯ ЗА ЁМКОСТЬ ВЕРШИН");
      Covered=0; foreach(var w in Writes) Covered+=w.cnt; Writes.Clear();
      Sub=d; SubSet=true; }
    public void SetVertices(List<Vector3> v){} public void GetVertices(List<Vector3> v){} public void GetNormals(List<Vector3> v){}
    public int[] GetTriangles(int s, bool applyBase=true)=>null; public void GetTriangles(List<int> t,int s,bool applyBase=true){} public Rendering.IndexFormat indexFormat;
    public void SetIndices(int[] i,Rendering.MeshTopology t,int s,bool calc=true,int baseVertex=0){}
    public void SetIndices(System.Collections.Generic.List<int> i,Rendering.MeshTopology t,int s,bool calc=true,int baseVertex=0){} }
  public class Transform : Component { public Vector3 position; public Quaternion rotation; public Matrix4x4 localToWorldMatrix; public Vector3 lossyScale;
    public Vector3 localPosition, localScale, forward; public void SetParent(Transform p,bool w){}
    public T[] GetComponentsInChildren<T>() where T : Component => new T[0];
    public void GetComponentsInChildren<T>(List<T> r) where T : Component {}
    public int childCount => 0; public Transform GetChild(int i) => null;
    public Vector3 TransformPoint(Vector3 p) => p; public Vector3 TransformDirection(Vector3 p) => p;
    public Transform parent; public Vector3 up, right; }
  public class Component : Object { public Transform transform; public GameObject gameObject; }
  public class Behaviour : Component { public bool enabled = true; }
  public class Rigidbody : Component { public Vector3 position, linearVelocity, velocity, angularVelocity, worldCenterOfMass; }
  public struct RaycastHit { public Vector3 point, normal; public float distance; public Collider collider; }
  public class Renderer : Component { public bool enabled = true; public Bounds bounds; }
  public class MonoBehaviour : Behaviour { }
  public class Camera : Behaviour { public CameraType cameraType; public float fieldOfView, aspect, nearClipPlane, farClipPlane, orthographicSize;
    public bool orthographic; public int cullingMask; public static Camera main=>null; }
  public enum CameraType { Game=1, SceneView=2, Preview=4, VR=8, Reflection=16 }
  public static class Application { public static bool isPlaying=false; public static RuntimePlatform platform=RuntimePlatform.WindowsPlayer;
    public static event Action quitting; static Application(){ quitting=null; } }
  public enum RuntimePlatform { OSXEditor, OSXPlayer, WindowsPlayer, WindowsEditor, IPhonePlayer, Android, LinuxPlayer, LinuxEditor, WebGLPlayer, PS4, PS5, XboxOne, GameCoreXboxOne, GameCoreXboxSeries, Switch, tvOS }
  public static class Time { public static float realtimeSinceStartup=0f; public static float deltaTime=0.016f; public static float timeScale=1f; public static float time=0f; public static float unscaledTime=0f; }
  public static class QualitySettings { public static ColorSpace activeColorSpace=ColorSpace.Linear; }
  public static class Resources { public static T Load<T>(string p) where T : Object => null; }
  public static class Screen { public static int width=>1920; public static int height=>1080; public static float dpi=>0; }
  public static class SystemInfo { public static bool usesReversedZBuffer=true; public static int systemMemorySize=>0; }
  public static class Graphics { public static int Calls; public static readonly List<Mesh> Last = new List<Mesh>();
    // Бенчмарк гоняет тысячи кадров: без выключателя список сабмитов вырос бы
    // до сотен тысяч записей и мерил бы уже сам себя.
    public static bool Record = true;
    public static void RenderMesh(in Rendering.RenderParams rp, Mesh m, int sub, Matrix4x4 mtx){
      if (m == null) throw new ArgumentNullException("mesh");
      if (sub < 0 || sub >= Math.Max(1,m.subMeshCount)) throw new ArgumentOutOfRangeException("submesh="+sub+" count="+m.subMeshCount);
      Calls++; if (Record) Last.Add(m); } }
  public class ScriptableObject : Object { public static T CreateInstance<T>() where T : ScriptableObject => null; }
  public class GUIContent { public static GUIContent none=>null; public Texture image; public string text; }
  [AttributeUsage(AttributeTargets.Field)] public class SerializeField : Attribute {}
  [AttributeUsage(AttributeTargets.Field)] public class HideInInspector : Attribute {}
  [AttributeUsage(AttributeTargets.Field)] public class HeaderAttribute : Attribute { public HeaderAttribute(string h){} }
  [AttributeUsage(AttributeTargets.Field)] public class TooltipAttribute : Attribute { public TooltipAttribute(string t){} }
  [AttributeUsage(AttributeTargets.Class)] public class CreateAssetMenuAttribute : Attribute { public string menuName, fileName; public int order; }
  [AttributeUsage(AttributeTargets.Method)] public class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t){} }
  public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad, SubsystemRegistration, AfterAssembliesLoaded, BeforeSplashScreen }
}
namespace UnityEngine.Rendering {
  public enum MeshTopology { Triangles=0, Quads=2, Lines=3, LineStrip=4, Points=5 }
  public enum IndexFormat { UInt16, UInt32 }
  public enum VertexAttribute { Position, Normal, Tangent, Color, TexCoord0, TexCoord1, TexCoord2, TexCoord3 }
  public enum VertexAttributeFormat { Float32, Float16, UNorm8, SNorm8 }
  public struct VertexAttributeDescriptor { public VertexAttribute attribute; public VertexAttributeFormat format; public int dimension; public int stream;
    public VertexAttributeDescriptor(VertexAttribute a, VertexAttributeFormat f, int d, int st=0){attribute=a;format=f;dimension=d;stream=st;}
    public int ByteSize { get { int e = format==VertexAttributeFormat.Float32?4:(format==VertexAttributeFormat.Float16?2:1); return e*dimension; } } }
  public struct SubMeshDescriptor { public SubMeshDescriptor(int s,int c,MeshTopology t=MeshTopology.Triangles){indexStart=s;indexCount=c;topology=t;bounds=default;baseVertex=0;firstVertex=0;vertexCount=0;}
    public int indexStart,indexCount,baseVertex,firstVertex,vertexCount; public MeshTopology topology; public Bounds bounds; }
  [Flags] public enum MeshUpdateFlags { Default=0, DontValidateIndices=1, DontResetBoneBounds=2, DontNotifyMeshUsers=4, DontRecalculateBounds=8 }
  public enum CompareFunction { Disabled=0, Never=1, Less=2, Equal=3, LessEqual=4, Greater=5, NotEqual=6, GreaterEqual=7, Always=8 }
  public enum RenderQueue { Background=1000, Geometry=2000, AlphaTest=2450, Transparent=3000, Overlay=4000 }
  public enum ShadowCastingMode { Off, On, TwoSided, ShadowsOnly }
  public enum LightProbeUsage { Off, BlendProbes }
  public enum ReflectionProbeUsage { Off, BlendProbes, Simple }
  public enum MotionVectorGenerationMode { Camera, Object, ForceNoMotion }
  public struct RenderParams { public Material material; public int layer; public uint renderingLayerMask; public Camera camera;
    public MaterialPropertyBlock matProps; public ShadowCastingMode shadowCastingMode; public bool receiveShadows;
    public LightProbeUsage lightProbeUsage; public ReflectionProbeUsage reflectionProbeUsage;
    public MotionVectorGenerationMode motionVectorMode; public int rendererPriority; public Bounds worldBounds;
    public RenderParams(Material m){ this=default; material=m; } }
  public class RenderPipelineAsset : ScriptableObject {}
  public static class GraphicsSettings { public static RenderPipelineAsset currentRenderPipeline=>null; }
  public struct ScriptableRenderContext {}
  public static class RenderPipelineManager { public static event Action<ScriptableRenderContext,Camera> beginCameraRendering; }
}
namespace UnityEngine.LowLevel {
  public struct PlayerLoopSystem { public Type type; public PlayerLoopSystem[] subSystemList; public Action updateDelegate; }
  public static class PlayerLoop {
    public static PlayerLoopSystem Root;
    public static void Fresh() {
      Root = new PlayerLoopSystem { subSystemList = new[] {
        new PlayerLoopSystem { type = typeof(UnityEngine.PlayerLoop.Update), subSystemList = new PlayerLoopSystem[0] },
        new PlayerLoopSystem { type = typeof(UnityEngine.PlayerLoop.PostLateUpdate), subSystemList = new[] {
            new PlayerLoopSystem { type = typeof(int) } } } } };
    }
    static PlayerLoop(){ Fresh(); }
    public static PlayerLoopSystem GetCurrentPlayerLoop()=>Root;
    public static void SetPlayerLoop(PlayerLoopSystem s){ Root = s; }
  }
}
namespace UnityEngine.PlayerLoop { public struct PostLateUpdate {} public struct Update {} }
// --- дополнительно для демо-сцены
namespace UnityEngine {
  public class GameObject : Object { public Transform transform = new Transform();
    public GameObject(){} public GameObject(string n){name=n;}
    public static GameObject CreatePrimitive(PrimitiveType t)=>new GameObject(); }
  public enum PrimitiveType { Sphere, Capsule, Cylinder, Cube, Plane, Quad }
  [AttributeUsage(AttributeTargets.Class)] public class ExecuteAlwaysAttribute : Attribute {}
  [AttributeUsage(AttributeTargets.Class)] public class AddComponentMenuAttribute : Attribute { public AddComponentMenuAttribute(string s){} }
  [AttributeUsage(AttributeTargets.Field)] public class RangeAttribute : Attribute { public RangeAttribute(float a,float b){} }
  public class GUISkin { public GUIStyle box=new GUIStyle(); }
  public class GUIStyle {}
  public static class GUI { public static GUISkin skin=new GUISkin();
    public static void Label(Rect r,string s){} public static void Box(Rect r,string s){} }
  public static class GUILayout { public static void BeginArea(Rect r, GUIStyle s=null){} public static void EndArea(){}
    public static void Label(string s){} public static bool Button(string s)=>false; public static void Space(float f){} }
}

// --- для расширений
namespace UnityEngine {
  public class Light : Behaviour { public LightType type; public float range, spotAngle, innerSpotAngle; }
  public enum LightType { Spot, Directional, Point, Area, Rectangle, Disc }
  public class RectTransform : Transform { public void GetWorldCorners(Vector3[] c){} }
}

// --- физика и звук для расширений
namespace UnityEngine {
  public class Collider : Component { public Bounds bounds; public bool enabled = true; }
  public class BoxCollider : Collider { public Vector3 center, size; }
  public class SphereCollider : Collider { public Vector3 center; public float radius; }
  public class CapsuleCollider : Collider { public Vector3 center; public float radius, height; public int direction; }
  public class MeshCollider : Collider { public Mesh sharedMesh; }
  public class CharacterController : Collider { public Vector3 center; public float radius, height; }
  public class Joint : Component { public Vector3 anchor, connectedAnchor, axis; public Rigidbody connectedBody; }
  public class Collider2D : Component { public Bounds bounds; public Vector2 offset; }
  public class BoxCollider2D : Collider2D { public Vector2 size; public float edgeRadius; }
  public class CircleCollider2D : Collider2D { public float radius; }
  public enum CapsuleDirection2D { Vertical, Horizontal }
  public class CapsuleCollider2D : Collider2D { public Vector2 size; public CapsuleDirection2D direction; }
  public class PolygonCollider2D : Collider2D { public int pathCount; public int GetPath(int i, System.Collections.Generic.List<Vector2> p)=>0; }
  public class EdgeCollider2D : Collider2D { public void GetPoints(System.Collections.Generic.List<Vector2> p){} }
  public class Rigidbody2D : Component { public Vector2 linearVelocity, worldCenterOfMass; public float angularVelocity; }
  public struct RaycastHit2D { public Collider2D collider; public Vector2 point, normal; public float distance; }
  public class AudioSource : Behaviour { public float minDistance, maxDistance; }
}

#endif

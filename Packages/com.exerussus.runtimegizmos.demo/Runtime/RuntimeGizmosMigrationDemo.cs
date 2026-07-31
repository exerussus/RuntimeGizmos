// Проверка миграции: одна строка алиаса — и весь существующий код, написанный под
// UnityEngine.Gizmos, начинает рисоваться через рантайм-систему, в том числе в билде.
//
// Обрати внимание: OnDrawGizmos вызывается только в редакторе и только когда объект
// виден в Scene View. Чтобы то же самое работало в билде, вызов надо перенести
// в Update или LateUpdate — этим и занимается второй компонент ниже.
using UnityEngine;
using Gizmos = RuntimeGizmos.Gizmo;

namespace RuntimeGizmosDemo
{
    [AddComponentMenu("RuntimeGizmos/Демо миграции (OnDrawGizmos)")]
    public class RuntimeGizmosMigrationDemo : MonoBehaviour
    {
        public float Radius = 1f;
        public Color Tint = Color.yellow;

        // Ни одной правки внутри метода — поменялся только using сверху.
        void OnDrawGizmos()
        {
            Gizmos.color = Tint;
            Gizmos.DrawWireSphere(transform.position, Radius);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * Radius * 2f);

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }

    /// <summary>
    /// Тот же рисунок, но из LateUpdate — так он виден и в игровой камере, и в билде.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("RuntimeGizmos/Демо миграции (LateUpdate)")]
    public class RuntimeGizmosMigrationRuntime : MonoBehaviour
    {
        public float Radius = 1f;
        public Color Tint = Color.cyan;

        void LateUpdate()
        {
            Gizmos.color = Tint;
            Gizmos.DrawWireSphere(transform.position, Radius);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * Radius * 2f);
            RuntimeGizmos.Gizmo.Reset();
        }
    }
}

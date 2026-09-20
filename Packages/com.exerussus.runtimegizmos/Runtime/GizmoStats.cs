namespace RuntimeGizmos
{
    /// <summary>
    /// Чего стоила отрисовка гизмо в прошлом кадре. Снимается на границе кадра, уже после
    /// обмена буферов, поэтому описывает ровно ту геометрию, которую увидели камеры.
    ///
    /// Нужна, чтобы на вопрос «почему просело» отвечать числом, а не догадкой: профайлер
    /// покажет один общий Graphics.RenderMesh и ничего не скажет о том, кто его наполнил.
    ///
    /// <code>
    /// var s = Gizmo.Stats;
    /// if (s.Overflowed) Debug.LogWarning($"гизмо упёрлись в потолок: {s.PeakChannelVertices} верш. в канале");
    /// </code>
    /// </summary>
    public readonly struct GizmoStats
    {
        /// <summary>Вершин во всех каналах вместе.</summary>
        public readonly int Vertices;

        /// <summary>Из них отложенных — нарисованных с duration и живущих между кадрами.</summary>
        public readonly int RetainedVertices;

        /// <summary>
        /// Самый нагруженный канал: кадровая часть плюс отложенная.
        /// С MaxVerticesPerChannel напрямую не сравнивается — потолок проверяется при росте
        /// буфера и отдельно для кадрового и отложенного, — но растёт вместе с нагрузкой
        /// и показывает, какой канал смотреть.
        /// </summary>
        public readonly int PeakChannelVertices;

        /// <summary>
        /// Сабмитов на ОДНУ камеру. Камер в кадре может быть несколько, и каждая платит
        /// столько же; здесь — последняя, которая действительно рисовала (камеры,
        /// отсечённые настройками DrawIn*, счётчик не трогают).
        /// </summary>
        public readonly int DrawCalls;

        /// <summary>Команд DrawMesh и DrawWireMesh: каждая рисуется отдельно.</summary>
        public readonly int Meshes;

        /// <summary>Живых регистраций GizmoLazy.</summary>
        public readonly int Tracked;

        /// <summary>Какой-то канал упирался в потолок и отбрасывал геометрию В ЭТОМ кадре.</summary>
        public readonly bool Overflowed;

        internal GizmoStats(int vertices, int retained, int peak, int drawCalls, int meshes,
                            int tracked, bool overflowed)
        {
            Vertices = vertices;
            RetainedVertices = retained;
            PeakChannelVertices = peak;
            DrawCalls = drawCalls;
            Meshes = meshes;
            Tracked = tracked;
            Overflowed = overflowed;
        }
    }
}

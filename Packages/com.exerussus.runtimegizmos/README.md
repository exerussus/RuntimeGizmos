# RuntimeGizmos

Замена `UnityEngine.Gizmos`, которая рисует **и в билде, и в редакторе**, и видна **через игровую камеру**, а не только в Scene View. Unity 6 (6000.0+), **URP**.

Платформы: Windows / macOS / Linux, Android, iOS, **WebGL**, консоли, XR. Ограничивающая планка — WebGL 2.0 (это уровень OpenGL ES 3.0), под неё написано всё остальное.

История изменений — в [CHANGELOG.md](CHANGELOG.md).

## Установка

Требуется URP: пакет `com.unity.render-pipelines.universal` установлен, а в Project Settings → Graphics назначен Universal Render Pipeline Asset. Если активен Built-in RP, в консоль один раз упадёт предупреждение и рисоваться не будет ничего — поддержка Built-in убрана намеренно.

Window → Package Manager → **+** → **Add package from git URL**:

```
https://github.com/exerussus/RuntimeGizmos.git?path=/Packages/com.exerussus.runtimegizmos
```

Или в `Packages/manifest.json`, в раздел `dependencies`:

```json
"com.exerussus.runtimegizmos": "https://github.com/exerussus/RuntimeGizmos.git?path=/Packages/com.exerussus.runtimegizmos"
```

С привязкой к версии — тег после пути, именно в таком порядке:

```
https://github.com/exerussus/RuntimeGizmos.git?path=/Packages/com.exerussus.runtimegizmos#v1.2.1
```

Ничего вешать на сцену не нужно: система поднимается сама через `RuntimeInitializeOnLoadMethod` и `InitializeOnLoadMethod`.

Демо-сцена и чеклист ручной проверки — [отдельный пакет](https://github.com/exerussus/RuntimeGizmos/blob/main/Packages/com.exerussus.runtimegizmos.demo/README.md), ставится тем же способом по своему пути. Ставить только после этого пакета.

## Использование

```csharp
using RuntimeGizmos;

void Update()
{
    Gizmo.color = Color.cyan;
    Gizmo.lineWidth = 3f;               // в пикселях, постоянная на любой дистанции; дефолт зависит от платформы
    Gizmo.DrawWireSphere(transform.position, 1f);

    Gizmo.depthTest = false;            // поверх всей геометрии
    Gizmo.DrawArrow(transform.position, target.position);

    Gizmo.DrawText($"hp {hp}", transform.position, 14f);   // размер в пикселях

    Gizmo.duration = 2f;                // держать 2 секунды — удобно из FixedUpdate и колбэков
    Gizmo.DrawLine(hit.point, hit.point + hit.normal);
    Gizmo.Reset();
}
```

Перевод существующего кода без единой правки — алиасом в начале файла:

```csharp
using Gizmos = RuntimeGizmos.Gizmo;   // весь старый OnDrawGizmos поедет через рантайм-систему
```

Scope вместо ручного сохранения состояния (структура, без аллокаций):

```csharp
using (Gizmo.Scope(Color.red, transform.localToWorldMatrix))
    Gizmo.DrawWireCube(Vector3.zero, Vector3.one);
```

## Покрытие API

Полный паритет со стандартным `UnityEngine.Gizmos`: `color`, `matrix`, `DrawLine`, `DrawLineList`, `DrawLineStrip`, `DrawRay`, `DrawWireSphere`, `DrawSphere`, `DrawWireCube`, `DrawCube`, `DrawMesh` (все пять перегрузок), `DrawWireMesh` (все пять), `DrawFrustum`, `DrawIcon`, `DrawGUITexture`. Плюс текст, паттерны и управление толщиной, глубиной и временем жизни, которых в стандартном API нет.

Всё перечислено ниже с примерами вызова.

## Справочник по API

Все `Draw*` вырезаются из релизного билда вместе с вычислением аргументов. Все уважают текущие `color`, `matrix`, `depthTest`, `duration`, а линейные — ещё и `lineWidth`.

### Состояние

```csharp
Gizmo.enabled   = true;                  // общий выключатель
Gizmo.color     = Color.cyan;            // цвет последующих вызовов
Gizmo.matrix    = transform.localToWorldMatrix;   // локальная система координат
Gizmo.lineWidth = 3f;                    // толщина в пикселях; <=1 — быстрый путь
Gizmo.depthTest = false;                 // false — рисовать поверх всей геометрии
Gizmo.duration  = 2f;                    // держать 2 секунды; 0 — один кадр
Gizmo.dash      = 0.25f;                 // пунктир: штрих и пропуск по 0.25 юнита; 0 — сплошная
Gizmo.Reset();                           // вернуть всё к платформенным дефолтам
Gizmo.Clear();                           // стереть всю накопленную геометрию
```

`Scope` — структура, восстанавливает состояние на выходе, без аллокаций:

```csharp
using (Gizmo.Scope())                                   // сохранить всё
using (Gizmo.Scope(Color.red))                          // цвет
using (Gizmo.Scope(transform.localToWorldMatrix))       // матрица
using (Gizmo.Scope(Color.red, transform.localToWorldMatrix))
    Gizmo.DrawWireCube(Vector3.zero, Vector3.one);
```

### Линии

```csharp
Gizmo.DrawLine(from, to);
Gizmo.DrawLine(from, to, Color.red);                    // разовый цвет
Gizmo.DrawRay(from, direction);
Gizmo.DrawRay(ray);
Gizmo.DrawRay(ray, distance);
Gizmo.DrawLineList(points);                             // пары точек: 0-1, 2-3, ...
Gizmo.DrawLineStrip(points, looped: false);             // ReadOnlySpan<Vector3>
Gizmo.DrawPolyLine(points, looped: false);              // IReadOnlyList<Vector3>
Gizmo.DrawArrow(from, to, headSize: 0.25f, headAngle: 22f);
Gizmo.DrawAxes(position, rotation, size: 1f);           // RGB-триада осей
Gizmo.DrawPoint(position, size: 0.1f);                  // трёхосный крестик
```

### Каркасные фигуры

```csharp
Gizmo.DrawWireCube(center, size);
Gizmo.DrawWireCube(center, rotation, size);             // повёрнутый
Gizmo.DrawBounds(bounds);
Gizmo.DrawWireSphere(center, radius);
Gizmo.DrawWireDisc(center, normal, radius);
Gizmo.DrawWireArc(center, normal, from, angleDeg, radius);
Gizmo.DrawWireCapsule(start, end, radius);
Gizmo.DrawWireCapsule(center, rotation, height, radius);
Gizmo.DrawWireCone(apex, direction, angleDeg, length);
Gizmo.DrawWireQuad(a, b, c, d);
Gizmo.DrawFrustum(center, fov, maxRange, minRange, aspect);
Gizmo.DrawFrustum(camera);                              // по настройкам камеры
```

Пунктир задаётся `Gizmo.dash` в мировых единицах и работает и на тонком пути, и на толстом. Фаза накапливается вдоль ломаной, поэтому у путей и окружностей штрихи не рвутся на изломах. Заливки пунктир не берут.

### Сплошные фигуры

```csharp
Gizmo.DrawCube(center, size);
Gizmo.DrawSphere(center, radius);
Gizmo.DrawTriangle(a, b, c);
Gizmo.DrawQuad(a, b, c, d);
```

Полупрозрачные заливки между собой не сортируются — см. «Ограничения».

### Меши

```csharp
Gizmo.DrawMesh(mesh);
Gizmo.DrawMesh(mesh, position);
Gizmo.DrawMesh(mesh, position, rotation);
Gizmo.DrawMesh(mesh, position, rotation, scale);
Gizmo.DrawMesh(mesh, submeshIndex, position, rotation, scale);   // -1 — все сабмеши

Gizmo.DrawWireMesh(mesh, position, rotation, scale);             // те же пять перегрузок
```

`DrawWireMesh` строит каркас один раз и кэширует, но требует у меша галку **Read/Write Enabled**. Без неё будет одно предупреждение и отрисовка заливкой.

### Текст

```csharp
Gizmo.DrawText("hp 80", position);                       // 14 px по умолчанию
Gizmo.DrawText("hp 80", position, 20f);
Gizmo.DrawText("hp 80", position, Color.yellow, 20f);
Gizmo.DrawText("hp 80", position, 14f, new Vector2(8f, 0f), GizmoTextAlign.Left);

Gizmo.DrawTextWorld(player.name, head.position, 0.2f);   // размер в юнитах
Gizmo.DrawTextWorld(player.name, head.position, Color.cyan, 0.2f);
Gizmo.DrawTextWorld(name, pos, 0.2f, new Vector2(0f, 0.3f), GizmoTextAlign.Center);

Gizmo.DrawText("строка\nвторая\nтретья", position);      // перенос работает

Gizmo.DrawScreenText("fps 60", new Vector2(10f, 10f));   // пиксели экрана, начало слева сверху
Gizmo.DrawScreenText("state: idle", GizmoCorner.TopLeft); // угол; строки укладываются стопкой
```

`DrawText` задаёт размер **в пикселях** — метка одинакова на любой дистанции. `DrawTextWorld` — **в юнитах**, метка уменьшается с расстоянием. Обе развёрнуты к камере.

`Gizmo.lineWidth` — толщина штриха. В мировом режиме это доля высоты буквы, а не пиксели.

`\n` переносит строку, `\r\n` тоже. Многострочный блок центрируется по вертикали вокруг якоря, каждая строка выравнивается отдельно.

Угловые надписи отступают от краёв экрана на `GizmoSettings.ScreenSafeArea` (по умолчанию 12 px). Отступ считается **до края чернил**, а не до якоря строки, поэтому верхушки заглавных и выносные элементы за экран не уезжают. На телевизорах и телефонах с вырезом значение стоит поднять.

#### Поддерживаемые символы

Шрифт штриховой и собственный — системные шрифты недоступны на WebGL и консолях. Поэтому набор символов конечный:

| Блок | Символы |
|---|---|
| ASCII | `32`–`126`: пробел, `!"#$%&'()*+,-./`, цифры, `:;<=>?@`, `A`–`Z`, `[\]^_\``, `a`–`z`, `{\|}~` |
| Кириллица | `А`–`я`, `Ё`, `ё` |
| Типографика | `«` `»` `—` `–` `…` `№` `×` `°` `•` `→` `←` `↔` `±` |

Всё остальное — иероглифы, эмодзи, символы валют, греческий — рисуется **пустым квадратом**. Он заметен, но проявляется только на том экране, где такой текст встретился. Чтобы не узнавать об этом от игрока:

```csharp
Gizmo.IsRenderable('—');                       // true
Gizmo.FirstUnrenderable("цена 10 €");          // '€', либо '\0' если всё покрыто
```

Оба метода дёшевы и годятся для `Assert` рядом с локализацией. Список выше проверяется тестом `GlyphCoverageTests` — если он разойдётся с реальным покрытием, тест упадёт.

Частая засада: длинное тире `—` (U+2014) и кавычки-ёлочки `« »` умные редакторы подставляют автоматически вместо дефиса и `"`. Они поддержаны, но `‑` (неразрывный дефис), `’` (типографский апостроф) и `“ ”` (английские кавычки) — нет.

### Иконки и экран

```csharp
Gizmo.DrawIcon(center, "myicon.png");                    // Assets/Gizmos/ или Resources
Gizmo.DrawIcon(center, "console.infoicon");              // встроенная, только в редакторе
Gizmo.DrawIcon(center, "myicon", allowScaling: true, tint);
Gizmo.DrawIcon(center, texture, allowScaling: true, tint, size: 32f);

Gizmo.DrawGUITexture(new Rect(10, 10, 64, 64), texture); // в пикселях экрана
Gizmo.DrawGUITexture(rect, texture, tint);
Gizmo.DrawGUITexture(rect, texture, left, right, top, bottom);  // 9-slice игнорируется
```

### Паттерны

Готовые сборки поверх примитивов — то, что иначе пишется заново в каждом дебажном скрипте.

```csharp
// Габариты, читаемые с одного взгляда
Gizmo.DrawVolume(transform);
Gizmo.DrawVolume(bounds, cornerFraction: 0.22f, faint: 0.28f);
Gizmo.DrawVolume(center, rotation, size, cornerFraction, faint);

// Связь между объектами
Gizmo.DrawLink(agent.transform, target.transform, Color.green, 2f, "цель");
Gizmo.DrawLink(boundsA, boundsB, Color.green, width: 2f, label: null);

// Подпись над объектом
Gizmo.DrawLabel(player, player.name);                    // размер в пикселях
Gizmo.DrawLabel(player, player.name, worldHeight: 0.2f); // размер в юнитах

// Маршрут
Gizmo.DrawPath(waypoints, nodeSize: 0.08f, arrowEvery: 1, looped: true);

// Вектор со стрелкой
Gizmo.DrawVector(rb.position, rb.linearVelocity, scale: 0.3f);
Gizmo.DrawVector(origin, force, 1f, "");                 // "" — подписать длиной

// Игровой радиус на земле
Gizmo.DrawRange(transform.position, aggroRadius);
Gizmo.DrawRange(transform.position, aggroRadius, height: 1f);

// Сектор обзора
Gizmo.DrawFieldOfView(eyes.position, eyes.forward, 70f, viewDistance);

// Точка попадания рейкаста
Gizmo.DrawHit(hit.point, hit.normal, size: 0.15f);

// Замер: двухсторонняя стрелка
Gizmo.DrawMeasure(a, b);                                 // подпишет расстояние
Gizmo.DrawMeasure(a, b, "зазор", arrowSize: 0.4f);
Gizmo.DrawMeasure(a, b, null);                           // без подписи

// Вынос размера, как на чертеже
Gizmo.DrawDimension(a, b, Vector3.down, extensionLength: 1.6f);
Gizmo.DrawDimension(a, b, Vector3.down, 1.6f, "55", gap: 0.1f, overshoot: 0.3f, arrowSize: 0.2f);
Gizmo.DrawDimension(corner, Vector3.right, width: 55f, Vector3.down, 20f, "55");

// Полоса заполнения, развёрнутая к камере
Gizmo.DrawBar(head.position, hp / maxHp);
Gizmo.DrawBar(head.position, t, Color.green, Color.gray, 60f, 8f, 24f);
Gizmo.DrawBarWorld(head.position, t, 0.6f, 0.08f);

// Кривые и сетка
Gizmo.DrawTrajectory(muzzle.position, muzzle.forward * 12f, time: 3f);
Gizmo.DrawBezier(a, b, c, d, segments: 24);
Gizmo.DrawGrid(Vector3.zero, Quaternion.identity, new Vector2(1f, 1f), new Vector2Int(20, 20));

// Габариты объекта со всеми рендерерами в иерархии
Bounds worldBounds = Gizmo.WorldBounds(transform);
```

`DrawVolume` рисует в полную силу только **углы** габаритов — именно они читают размер. Контур и три сечения приглушены по альфе, поэтому десяток объёмов в кадре не превращается в кашу из рёбер.

`DrawLink` показывает объёмы обоих объектов и тянет линию, **обрезанную по границам габаритов**, с шевроном направления на середине.

`DrawDimension` — чертёжный вынос. Щёки стрелок разводятся вдоль выносного направления, то есть лежат в плоскости чертежа. Пустая подпись означает «подставить расстояние», `null` — не подписывать.

### Расширения для компонентов Unity

Отдельный namespace, чтобы без `using RuntimeGizmos.Extensions;` не всплывать в автодополнении у каждого `Transform`.

```csharp
// Transform
transform.DrawVolume(Color.cyan);                  // габариты со всеми рендерерами
transform.DrawLabel("hp 80");                      // подпись, размер в пикселях
transform.DrawLabel(name, worldHeight: 0.2f);      // подпись, размер в юнитах
transform.DrawLinkTo(target.transform, Color.green, 2f, "цель");
transform.DrawAxes(1f);
transform.DrawForward(2f);                         // только «вперёд», без шума трёх осей
transform.DrawBounds(Color.gray);                  // простой ящик, без приглушённых сечений
transform.DrawHierarchy(Color.gray, maxDepth: 8);  // дерево родитель→дети

// Bounds и коллекции
bounds.Draw(Color.red);
bounds.DrawVolume(Color.red);
waypoints.DrawPath(Color.yellow, nodeSize: 0.1f, arrowEvery: 1, looped: true);
enemies.DrawVolumes(Color.red);                    // IReadOnlyList<Transform>

// Меши и рендереры
mesh.DrawNormals(transform, Color.cyan, length: 0.1f, step: 4);
mesh.DrawWire(transform, Color.green);
renderer.DrawBounds(Color.gray);                   // AABB, по которому куллит камера

// Камера, свет, UI
camera.DrawFrustum(Color.white);
light.DrawRange(Color.yellow);                     // сфера / конус / стрелка по типу
rectTransform.DrawWorldCorners(Color.magenta);

// Коллайдеры — настоящая форма, а не габариты
collider.DrawShape(Color.green);                   // Box, Sphere, Capsule, CC, Mesh
collider.DrawBounds(Color.gray);
collider2D.DrawShape(Color.green);                 // Box, Circle, Capsule, Polygon, Edge
collider2D.DrawBounds(Color.gray);

// Rigidbody
rigidbody.DrawVelocity(Color.cyan, scale: 0.2f);
rigidbody.DrawAngularVelocity(Color.magenta);
rigidbody.DrawCenterOfMass(Color.red);
rigidbody2D.DrawVelocity(Color.cyan);
rigidbody2D.DrawAngularVelocity(Color.magenta);
rigidbody2D.DrawCenterOfMass(Color.red);

// Рейкасты
ray.Draw(Color.red, distance: 50f);
hit.Draw(Color.green);
ray.DrawTo(hit, maxDistance: 50f, Color.green);    // до попадания ярко, остаток приглушённо
hit2D.Draw(Color.green);

// Прочее
joint.DrawAnchors(Color.yellow);                   // свой anchor, чужой, ось
audioSource.DrawDistances(Color.cyan);             // min и max distance
```

`DrawShape` повторяет правила масштабирования движка: сфера берёт наибольшую ось, капсула — наибольшую из двух поперечных, высота не бывает меньше двух радиусов. На этом обычно и путаются, рисуя вручную. Неизвестные типы рисуются габаритным ящиком.

Файлы разделены по модулям движка — `Core`, `Physics`, `Physics2D`, `Audio`. Модуль выключен в проекте — удаляется один файл.

Скелета `SkinnedMeshRenderer` в наборе нет: `bones` отдаёт новый массив на каждое обращение.

## Ленивая отладка: GizmoLazy

Регистрация рисуется каждый кадр, пока жива цель. Команды читают цель заново, поэтому рисунок следует за объектом.

```csharp
GizmoLazy.Track(enemy).Volume(Color.red);
GizmoLazy.Track(enemy).For(5f).Label("агрится");
GizmoLazy.Track(agent).LinkTo(target.transform, Color.green);
GizmoLazy.Track(collider).Shape();
GizmoLazy.Track(rigidbody).Velocity();
GizmoLazy.Track(enemy).Draw(() => enemy.DrawVolume(Color.red));   // запасной выход

GizmoLazy.Untrack(enemy);
GizmoLazy.Clear();
```

Команды: `Volume`, `Bounds`, `Label`, `LinkTo`, `Axes`, `Forward`, `Range`, `FieldOfView`, `Hierarchy`, `Shape`, `Velocity`, `Draw`. Модификаторы перед командой: `.For(секунды)`, `.Key(имя)`.

Слой нужен там, где рисование надо **пересчитывать каждый кадр**. Статичную геометрию закрывает `Gizmo.duration` в базе: без аллокаций и, начиная с 1.3.0, вообще без стоимости — см. «Статика ничего не стоит».

Команды хранятся структурами, регистрация не мусорит — звать из `Update` безопасно. Аллоцирует только `Draw`: лямбда с захватом есть замыкание плюс делегат, её место в `Start`.

**В релизе исчезает целиком.** Терминал помечен `[Conditional]`, и компилятор выбрасывает всё выражение — приёмник `Track(...)`, модификаторы и вычисление аргументов. Харнесс это проверяет по длине IL-тела: 322 байта в редакторе против 2 в релизе.

Цепочка заканчивается одной командой: терминал возвращает void, иначе `[Conditional]` к нему не применить.

Ключ регистрации собирается сам: **цель + место вызова + вид команды**, файл и строку подставляет компилятор. Отсюда повтор из `Update` заменяет запись, а не копит, и разные команды с одной строки сосуществуют.

Ломается это в цикле: цели разные, а строка одна на всех, и записи схлопнулись бы в одну — отрисовался бы только последний. Для этого `.Key()`, он вытесняет автоматическое «файл + строка», а различает записи уже сама цель:

```csharp
foreach (var e in enemies)
    GizmoLazy.Track(e).Key("агро").Volume();   // ключ один, цели разные
```

Второе применение — адресная отмена: `Untrack(enemy, "агро")` снимет одну регистрацию, `Untrack(enemy)` — все по цели.

Общий префикс цепочки можно вынести в переменную, если команд на объект несколько:

```csharp
var g = GizmoLazy.Track(enemy).For(5f);
g.Volume(Color.red);
g.Label("агрится");
g.Range(8f);
```

Терминалы остаются `void` и вырезаются из релиза вместе с аргументами.

Записи снимаются сами: смерть цели, срок, границы Play Mode. Исключение в `Draw` снимает только виновника. Каждая запись начинает с чистого состояния `Gizmo`.

Выключатели: `GizmoLazy.Enabled`, `GizmoLazy.MaxTracked`, `GizmoLazy.Count`.

## Аллокации

На горячем пути (`Draw*` → буфер → меш) managed-аллокаций нет вообще:

* вершины пишутся в персистентные `NativeArray` через сырой указатель, рост — амортизированное удвоение;
* индексбуфер общий и единичный (`0,1,2,...`), геометрия неиндексированная — индексы уезжают на GPU только при росте ёмкости, а не каждый кадр;
* ёмкость вершинного буфера меша округляется вверх до степени двойки, диапазон отрисовки задаётся через `SubMeshDescriptor` — `SetVertexBufferParams` дёргается только при реальном росте;
* заливка через `SetVertexBufferData(NativeArray<T>, …)` с `DontValidateIndices | DontRecalculateBounds | DontNotifyMeshUsers | DontResetBoneBounds`;
* примитивы (куб, сфера, окружность) посчитаны один раз и лежат в нативной памяти — рисование это «трансформировать N точек и скопировать»;
* `RenderParams` — структура, `MaterialPropertyBlock` берутся из пула, `Dictionary` и списки не пересоздаются.

Через `[Conditional]` все `Draw*` вырезаются компилятором из релизных билдов **вместе с вычислением аргументов**. Оставить их в релизе — символ `RUNTIME_GIZMOS_ALWAYS`.

## Как это устроено

**Граница кадра.** `BeginFrame` меняет местами front/back-буферы и стоит после пользовательского кода, но до рендера: в плеймоде это вставка в `PostLateUpdate` через `PlayerLoop`, в эдит-моде — `EditorApplication.update`. Поэтому вызовы из `Update`/`LateUpdate` попадают в тот же кадр без задержки.

**Зависимости шейдеров.** Из библиотек URP вызывается ровно одна функция — `TransformObjectToHClip`; остальное это макросы платформенных заголовков и переменные `UnityInput.hlsl`, которые `Core.hlsl` тянет гарантированно. Перевод sRGB в линейное посчитан своей функцией `GizmoSRGBToLinear`, потому что `Core.hlsl` не подключает `Color.hlsl` с библиотечной `SRGBToLinear`.

**Отрисовка.** `Graphics.RenderMesh` из `RenderPipelineManager.beginCameraRendering`, по одному сабмиту на камеру с заполненным `RenderParams.camera`. URP вызывает этот хук до `context.Cull()`, поэтому геометрия успевает попасть в текущий кадр. Пассы помечены `LightMode = SRPDefaultUnlit` — этот тег URP отрисовывает в `DrawObjectsPass`, и в forward, и в deferred, и с Render Graph. Геометрия идёт обычным путём конвейера, поэтому корректно работает depth-тест и она одинаково видна и в Game View, и в Scene View, и в билде.

**Каналы.** Восемь каналов батчинга: {тонкие линии, толстые линии, треугольники, текст} × {с тестом глубины, поверх всего}. Вся кадровая геометрия в каждом канале — это один меш и один draw call.

Число draw call'ов зависит только от того, сколько каналов непустые, и никак не от количества геометрии: 786 тысяч вершин уходят в те же три сабмита, что и три вершины. Сверху добавляется по одному вызову на текстуру иконки, на экранную текстуру и на команду `DrawMesh`.

**Статика ничего не стоит.** Геометрия, нарисованная с `duration`, живёт в отдельном retained-буфере. Канал помнит, менялось ли его содержимое и когда истекает ближайший примитив: пока дописывать нечего и истекать нечему, компактор не проходит буфер, а `Prepare` переиспользует уже залитый меш вместо повторного `SetVertexBufferData`. Нарисованный один раз каркас уровня не тратит ни такта CPU, ни байта трафика в GPU — только свои три draw call'а.

Отсюда практический вывод: **если геометрия не меняется, рисуйте её один раз с большим `duration`, а не каждый кадр.**

```csharp
// один раз — например, из Start, а не из Update
Gizmo.duration = float.MaxValue;         // держать, пока не сотрут
foreach (var wp in waypoints) Gizmo.DrawWireSphere(wp, 0.3f);
Gizmo.Reset();
```

Стереть — `Gizmo.Clear()`. Отдельную группу выборочно снять нечем: retained выбывает только по истечении времени.

**Толстые линии** разворачиваются в квад в *вершинном* шейдере, в мировом пространстве (а не в NDC) — так корректно отрабатывают точки за камерой, где деление на `w` даёт мусор. Ширина в пикселях постоянна, ортографическая проекция учтена. Геометрических шейдеров нет нигде: их не поддерживают ни WebGL, ни Metal, ни большинство мобильных GPU.

**Смещение глубины** для линий с тестом глубины делается вручную в вершинном шейдере (`GizmoSettings.DepthBias`, в единицах NDC), а не через `Offset factor,units`. Причина конкретная: в OpenGL ES и WebGL существует только `GL_POLYGON_OFFSET_FILL`, для `GL_LINES` полигональный офсет молча игнорируется — то есть на Android GLES и в браузере линии, лежащие на поверхности, z-файтили бы. Ручной сдвиг ведёт себя одинаково на всех API, `UNITY_REVERSED_Z` учтён.

**Мерцание в эдит-моде.** `EditorApplication.update` и перерисовки вьюпорта идут с разной частотой, поэтому в эдит-моде последний снимок геометрии держится до `GizmoSettings.EditorStaleTimeout` секунд, а не сбрасывается на каждом тике. В плеймоде семантика строгая: не нарисовал в этом кадре — не видно.

## Настройки

Значение каждой настройки разрешается по слоям, сверху вниз:

1. **рантайм-оверрайд из кода** — `GizmoSettings.DefaultLineWidth = 2f`;
2. **ассет `GizmoSettingsAsset`** из любой папки `Resources` — сначала его платформенная секция, затем общая;
3. **дефолт под текущую платформу** из `GizmoConfig.DefaultsFor`.

Первый непустой слой выигрывает.

```csharp
GizmoSettings.DefaultLineWidth;              // прочитать разрешённое значение
GizmoSettings.DefaultLineWidth = 2f;         // поставить оверрайд
GizmoSettings.Overrides.DefaultLineWidth = null;  // снять оверрайд, вернуться к ассету/дефолту
GizmoSettings.ResetOverrides();       // снять все
GizmoSettings.Current;                // весь разрешённый набор одной структурой
```

Ассет создаётся через **Tools → RuntimeGizmos → Создать ассет настроек**: он ляжет в `Assets/Resources/RuntimeGizmosSettings.asset` под нужным именем. Внутри — общая секция и список секций под платформы; в каждой строке галочка «переопределять» и само значение, непомеченные поля просто пропускаются.

### Платформенное измерение есть только у данных

Оверрайдов «только для Android» в коде нет: код и так исполняется на целевой платформе, поэтому это обычный `if`:

```csharp
if (GizmoSettings.Platform == GizmoPlatform.Mobile) GizmoSettings.DefaultLineWidth = 3f;
```

Платформенное измерение есть только у данных — таблицы дефолтов и ассета. Что полезно — посмотреть чужой профиль, не собирая билд:

```csharp
GizmoSettings.PlatformOverride = GizmoPlatform.Web;   // Scene View как в браузере
GizmoSettings.PlatformOverride = null;                // обратно на автоопределение
```

По умолчанию в редакторе платформа берётся не из «редактор крутится на десктопе», а из активного build target — переключили проект на Android, и Scene View сразу показывает мобильные настройки.

### Классы платформ и дефолты

Платформа — это не Windows против Linux, а «экран в метре от лица» против «экран в очках»: `Desktop`, `Mobile`, `Web`, `Console`, `XR` (XR определяется первым и перекрывает Mobile — Quest это Android, но настройки ему нужны свои).

| | Desktop | Mobile | Web | Console | XR |
|---|---|---|---|---|---|
| `LineWidth` | 1 | 2 | 2 | 2 | 3 |
| `CircleSegments` | 32 | 20 | 20 | 32 | 24 |
| `SphereRings` / `SphereSegments` | 8 / 16 | 6 / 12 | 6 / 12 | 8 / 16 | 6 / 12 |
| `MaxVerticesPerChannel` | 1 048 576 | 262 144 | 131 072 | 1 048 576 | 131 072 |

Логика: линия в один физический пиксель почти не видна при DPI 2–3x, на консоли смотрят через комнату, в XR всё рисуется дважды. Куча WebGL фиксируется на старте — оттуда самый жёсткий потолок.

`DepthBias` вынесен из таблицы, потому что зависит не от платформы, а от буфера глубины под ногами: `SystemInfo.usesReversedZBuffer` даёт `1e-4` на reversed-Z с float (D3D, Metal, Vulkan) и `3e-4` на прямом `[0,1]` в OpenGL/GLES/WebGL, где точности заметно меньше.

### Статика и Play Mode

Оверрайды живут одну сессию. На обеих границах Play Mode сносится всё: оверрайды, кэш платформы, ссылка на ассет, материалы и нативные буферы. Важно при **выключенном Domain Reload**, где статика переживает переход.

### Все поля настроек

```csharp
GizmoSettings.DrawInGameView       = true;   // рисовать в игровых камерах, в том числе в билде
GizmoSettings.DrawInSceneView      = true;   // рисовать в Scene View
GizmoSettings.DrawInOtherCameras   = false;  // превью-камеры инспектора, отражения
GizmoSettings.Layer                = 0;      // слой геометрии, учитывается culling mask камеры
GizmoSettings.RenderingLayerMask   = uint.MaxValue;
GizmoSettings.GlobalAlpha          = 1f;     // множитель альфы для всей отрисовки
GizmoSettings.DefaultLineWidth     = 2f;     // стартовая толщина, к ней возвращает Gizmo.Reset()
GizmoSettings.DepthBias            = 1e-4f;  // сдвиг глубины в NDC против z-файтинга
GizmoSettings.MaxVerticesPerChannel = 1 << 18;  // потолок роста буфера; 0 — без ограничения
GizmoSettings.CircleSegments       = 32;
GizmoSettings.SphereRings          = 8;
GizmoSettings.SphereSegments       = 16;
GizmoSettings.EditorStaleTimeout   = 0.35f;  // Edit Mode: сколько держать последний снимок
GizmoSettings.EditorAutoRepaint    = true;   // Edit Mode: запрашивать перерисовку Scene View
```

Служебное:

```csharp
GizmoSettings.Current;                  // весь разрешённый набор одной структурой
GizmoSettings.Platform;                 // класс платформы, под который взяты дефолты
GizmoSettings.PlatformOverride = GizmoPlatform.Web;   // посмотреть чужой профиль
GizmoSettings.Asset;                    // подхваченный GizmoSettingsAsset или null
GizmoSettings.ReloadAsset();            // перечитать ассет с диска
GizmoSettings.ResetOverrides();         // снять все рантайм-оверрайды
GizmoSettings.Invalidate();             // пересобрать конфигурацию при следующем обращении
```

Детализацию (`CircleSegments`, `SphereRings`, `SphereSegments`) и `DepthBias` нужно задавать до первого вызова `Draw*` — примитивы и материалы кэшируются один раз.

## Проверка

В репозитории, в папке `Tests/`, лежит харнесс, который гоняет код пакета **без Unity**: реальный Roslyn компилирует `Runtime/` против заглушек Unity API, где `NativeArray` живёт в настоящей неуправляемой памяти с канарейками за концом буфера. Нужен .NET SDK 8, запуск — `Tests/run.sh` из корня репозитория, на Linux и в Git Bash на Windows одинаково. Питон нужен ровно одному шагу — проверке примеров из README; без него шаг пропускается с предупреждением. В сам пакет харнесс не входит: это оснастка разработки, и в проектах ей делать нечего.

Что он проверяет: компиляцию **всех примеров из этого README** против настоящего кода (документация врёт молча), компиляцию в семи конфигурациях и 248 кейсов — рост и потолок нативных буферов, разбор всех 162 глифов шрифта, раскладку текста и выравнивание, кадровую модель со строгим и мягким режимом, истечение `duration` и компактацию, пропуск лишней работы на неизменной статике, разрешение настроек по слоям и клампы, идемпотентность вставки в PlayerLoop, поведение при отсутствии шейдера, вызов из чужого потока, NaN и бесконечности, вырожденную геометрию, переполнение канала, освобождение памяти и — отдельно — что ни один буфер не переписан за границу при смешанной нагрузке во все каналы сразу.

Второй слой — тесты Unity Test Framework, они лежат уже внутри пакета, в `Tests/Editor` и `Tests/Runtime`, и проверяют то, что на заглушках проверить нельзя в принципе: компилируются ли шейдеры на этой платформе, принимает ли настоящий `Mesh` объявленную разметку вершин, встаёт ли граница кадра в `PlayerLoop` первой в `PostLateUpdate`, доходит ли геометрия до пикселей в `RenderTexture`, отбивается ли вызов из чужого потока, нет ли managed-аллокаций на горячем пути. Кейсы, которым нужен URP, пропускаются с объяснением, а не падают.

Запуск — Window → General → Test Runner, вкладки EditMode и PlayMode. Пакет здесь встроенный, поэтому тесты видны сразу; если ставить его из git, добавьте `com.exerussus.runtimegizmos` в `testables` в `Packages/manifest.json`.

Рядом лежит бенчмарк: `Tests/run.sh --bench [объектов] [кадров]`. Он меряет стоимость статической геометрии по трём статьям раздельно — запись вершин, компактор, заливка в меш — и считает трафик в вершинные буферы и managed-аллокации за прогон. Заглушка `SetVertexBufferData` только считает байты и ничего не копирует, поэтому колонка `Submit` не включает настоящую загрузку в GPU; смотреть надо на «заливка/кадр».

Математика текстового шейдера воспроизводится на CPU по тем же вершинам, что уходят на GPU, и буквы печатаются ASCII-графикой в отчёте.

Отдельно сверяются `sizeof` каждой вершинной структуры с суммой её `VertexAttributeDescriptor`, смещение `Position` = 0 и канонический порядок атрибутов. Расхождение здесь Unity не диагностирует — оно проявляется мусором на экране.

Дефекты, которые харнесс нашёл и которые исправлены:

* `Begin()` возвращал `true` после неудавшегося `Ensure()` — одного отсутствующего шейдера хватало, чтобы получить `NullReferenceException` на первом же `Draw*` вместо аккуратного отключения;
* `duration` короче кадра давал ноль кадров видимости, тогда как `duration = 0` давал один — немонотонно. Причина: команды штампуются временем прошлой границы кадра, а истечение проверялось уже обновлённым. Теперь и то и другое считается одним временем, и геометрия с `duration > 0` гарантированно переживает кадр, в котором нарисована;
* `DrawWireMesh` на меше с `subMeshCount == 0` вычислял `Mathf.Clamp(i, 0, -1)` = −1 и падал на `GetTopology(-1)`;
* `DrawMesh` с сабмешем вне диапазона ронял `Graphics.RenderMesh`;
* ключ кэша каркасов считался до клампа сабмеша, поэтому индексы 5 и 99 на односабмешевом меше создавали две одинаковые копии каркаса;
* кэш каркасов не сверял источник — после переиспользования instance ID мог отдать каркас другого меша;
* батчи иконок и GUI-текстур с уничтоженной текстурой не освобождались никогда, как и записи кэша каркасов с выгруженным источником.

## Совместимость версий

Unity 6000.0 и новее. В Unity 6.5 `Object.GetInstanceID` стал ошибкой компиляции, а идентичность объекта переехала на `EntityId` — 64-битное значение, которое в `int` не помещается.

Пакет обходит это через `GizmoObjectId` — обёртку, которая под `UNITY_6000_5_OR_NEWER` хранит `EntityId`, иначе `int`. Именно полное значение, а не хеш: по этим ключам ищутся регистрации `GizmoLazy` и кэш каркасных мешей, и коллизия дала бы чужую геометрию. Тот же приём в `GizmoMeshKey` для пары «меш + сабмеш».

Обе ветки компилируются в постоянном прогоне тестов, поэтому расхождение не пройдёт незамеченным.

## Ограничения, о которых стоит знать заранее

* **Только URP.** Built-in RP и HDRP не поддержаны. Вся C#-часть от конвейера не зависит — под HDRP достаточно заменить четыре шейдера на HDRP Unlit с `LightMode = ForwardOnly`.
* **Только главный поток.** Запись в буферы не потокобезопасна, из джобов вызывать нельзя. На WebGL это и так единственный вариант.
* **Тонкие линии — ровно один физический пиксель.** Это ограничение самого `GL_LINES`: ни WebGL, ни Metal, ни GLES не дают менять толщину линии. Поэтому на мобильных, в вебе, на консолях и в XR дефолтная толщина больше единицы и включается путь через квады. На десктопе дефолт остался волосяным — там он выглядит резче.
* **`DrawWireMesh`** строит каркасную версию меша один раз и кэширует её, но требует у исходного меша галку Read/Write Enabled. Без неё будет предупреждение и отрисовка заливкой.
* **`DrawGUITexture`** не поддерживает 9-slice бордеры — параметры принимаются и игнорируются.
* Полупрозрачные сплошные фигуры не сортируются между собой: фигуры с тестом глубины пишут в depth-буфер (чтобы корректно перекрывать друг друга), режим «поверх всего» — не пишет.
* Геометрия рисуется в очереди Transparent, то есть на неё влияет пост-обработка (bloom, color grading). Если это мешает — вынесите гизмо на отдельный слой и исключите его из post-processing volume.
* Истечение `duration` проверяется штампом прошлой границы кадра, поэтому геометрия живёт заданное время ±один кадр. Это плата за гарантию, что она не исчезнет в том же кадре, в котором нарисована.
* Текстовые метки не сортируются между собой по глубине и не переносятся по словам — это отладочные подписи, а не система вёрстки.
* Использование `MaterialPropertyBlock` для тинта мешей делает материалы несовместимыми с SRP Batcher. На практике это не важно: за кадр выходит порядка десяти draw call'ов.

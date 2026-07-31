# RuntimeGizmos

Замена `UnityEngine.Gizmos`, которая рисует **и в билде, и в редакторе**, и видна **через игровую камеру**, а не только в Scene View. Unity 6, URP.

Репозиторий — монорепа из двух UPM-пакетов. Ставится каждый отдельно, по своему пути внутри репозитория.

| Пакет | Путь | Назначение |
|---|---|---|
| [Runtime Gizmos](Packages/com.exerussus.runtimegizmos/README.md) | `Packages/com.exerussus.runtimegizmos` | сам плагин |
| [Демо](Packages/com.exerussus.runtimegizmos.demo/README.md) | `Packages/com.exerussus.runtimegizmos.demo` | демо-сцена и чеклист проверки |

Папка `Tests/` в корне — оснастка репозитория, а не пакет: она не устанавливается и в проекты не попадает.

---

## Установка плагина

**Требуется URP.** В Project Settings → Graphics должен быть назначен Universal Render Pipeline Asset, иначе пакет один раз напишет в консоль предупреждение и рисовать не будет.

### Через Package Manager

Window → Package Manager → **+** → **Add package from git URL** → вставить:

```
https://github.com/exerussus/RuntimeGizmos.git?path=/Packages/com.exerussus.runtimegizmos
```

### Через manifest.json

`Packages/manifest.json` вашего проекта, в раздел `dependencies`:

```json
"com.exerussus.runtimegizmos": "https://github.com/exerussus/RuntimeGizmos.git?path=/Packages/com.exerussus.runtimegizmos"
```

### С привязкой к версии

Без этого Unity подтянет текущее состояние ветки по умолчанию и зафиксирует его хеш в `packages-lock.json`. Чтобы обновляться осознанно, указывайте тег:

```
https://github.com/exerussus/RuntimeGizmos.git?path=/Packages/com.exerussus.runtimegizmos#v1.0.0
```

Порядок в строке именно такой: сначала `?path=`, потом `#тег`.

---

## Установка демо

Демо — отдельный пакет, чтобы не таскать его в релизные проекты. **Ставьте его только после основного пакета:** UPM не разрешает git-зависимости транзитивно, поэтому демо не может подтянуть плагин за собой, и без него просто не скомпилируется.

```
https://github.com/exerussus/RuntimeGizmos.git?path=/Packages/com.exerussus.runtimegizmos.demo
```

Дальше: пустой объект в сцене → **Add Component → RuntimeGizmos → Demo**.

Удаляется через Package Manager, следов в проекте не оставляет.

---

## Обновление и удаление

Обновить до нового тега — поменять `#v1.0.0` в `manifest.json` и вернуться в редактор. Если тег не указан, для повторного скачивания придётся удалить запись пакета из `packages-lock.json`: без этого Unity держится за ранее зафиксированный хеш.

Удалить — Package Manager → пакет → Remove.

---

## Что дальше

* [Документация плагина](Packages/com.exerussus.runtimegizmos/README.md) — справочник по API со всеми вызовами и примерами.
* [История изменений плагина](Packages/com.exerussus.runtimegizmos/CHANGELOG.md)
* [Чеклист ручной проверки в Unity](Packages/com.exerussus.runtimegizmos.demo/CHECKLIST.md)

## Разработка

Автотесты плагина гоняются без Unity: `Tests/run.sh`. Нужен .NET SDK 8. Прогоняются компиляция в пяти конфигурациях, компиляция всех примеров из README пакета и 170 кейсов по его логике.

Харнесс лежит в корне, а не внутри пакета, намеренно. Это .NET-код с заглушками Unity API — не часть плагина, а инструмент разработки. UPM копирует папку пакета целиком, включая `~`-папки, поэтому харнесс внутри пакета ехал бы мёртвым грузом в каждый проект, который его ставит. Из корня же он не попадает в установку вовсе.

Единственный генерируемый файл — сборка примеров из README — пишется во временную папку системы, поэтому в репозитории нет ни одного артефакта сборки и `.gitignore` не содержит исключений под них.

Весь C# внутри `Tests/` обёрнут в `#if !UNITY_2020_3_OR_NEWER`. Там лежат заглушки Unity API — вторые определения `Vector3`, `Color`, `Transform`, `MonoBehaviour` и ещё десятков типов. Обычно Unity их не видит: корневая папка вне `Assets/` и `Packages/` не импортируется. Но если репозиторий склонировать прямо внутрь `Assets/`, без директивы проект перестал бы собираться целиком. Внутри Unity символ определён всегда, поэтому файлы схлопываются в пустые. Что они действительно схлопываются, проверяет отдельный шаг в `run.sh`: сборка с этим символом не должна содержать ни одного типа.

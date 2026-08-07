#!/bin/bash
# Прогон набора кейсов по коду пакета без Unity.
#
# Лежит в корне репозитория, а не внутри пакета: это репозиторная оснастка,
# а не часть плагина. UPM копирует папку пакета целиком, включая ~-папки,
# поэтому харнесс внутри пакета ехал бы мёртвым грузом в каждый проект.
#
# Пакет компилируется настоящим Roslyn против заглушек Unity API, где NativeArray
# лежит в реальной неуправляемой памяти с канарейками — то есть выход за границу
# буфера при записи сырыми указателями будет пойман, а не «может быть, пронесёт».
#
# Нужен .NET SDK 8 и python3.
#   Linux:   apt-get install -y dotnet-sdk-8.0
#   Windows: winget install Microsoft.DotNet.SDK.8   (запускать из Git Bash)
#
#   ./run.sh                      только кейсы
#   ./run.sh --bench              кейсы + бенчмарк статической геометрии
#   ./run.sh --bench 5000 600     свой профиль: объектов, кадров
set -e
cd "$(dirname "$0")"

# ------------------------------------------------------------------ окружение
#
# Пути ищем через сам dotnet, а не по фиксированному /usr/lib/dotnet: расположение
# SDK отличается у каждого дистрибутива, а на Windows это ещё и «C:\Program Files\
# dotnet» с пробелом в пути. Отсюда же требование звать компилятор функцией, а не
# подставлять строку: неквотированный $CSC на пробеле развалился бы.

command -v dotnet >/dev/null 2>&1 || {
  echo "не найден dotnet. Нужен .NET SDK 8"; exit 1; }

# Питон ищем ЗАПУСКОМ, а не наличием файла. В Windows в WindowsApps лежат
# заглушки-алиасы python.exe и python3.exe: они существуют, command -v их находит,
# а на запуске печатают «Python was not found» и предлагают Microsoft Store.
PY=""
for cand in python3 python py; do
  command -v $cand >/dev/null 2>&1 || continue
  if [ "$cand" = py ]; then
    py -3 -c "pass" >/dev/null 2>&1 && { PY="py -3"; break; }
  else
    $cand -c "pass" >/dev/null 2>&1 && { PY="$cand"; break; }
  fi
done

# Windows-пути наружу отдаём в «смешанном» виде (C:/...): их понимают и bash, и csc.
native() { if command -v cygpath >/dev/null 2>&1; then cygpath -m "$1"; else printf '%s' "$1"; fi; }

# Git Bash по-своему переписывает аргументы, похожие на пути, и портит
# -define:"A;B", принимая точку с запятой за разделитель списка путей.
export MSYS2_ARG_CONV_EXCL='*'

SDKLINE=$(dotnet --list-sdks | grep -E '^8\.' | tail -1 || true)
[ -n "$SDKLINE" ] || { echo "не найден .NET SDK 8. Установлено:"; dotnet --list-sdks; exit 1; }
SDKVER=${SDKLINE%% *}
SDKDIR=${SDKLINE#*[}; SDKDIR=${SDKDIR%]}          # каталог со всеми версиями SDK
SDKDIR=${SDKDIR//\\//}                            # обратные слэши Windows — в прямые
DOTNET_HOME=$(dirname "$SDKDIR")

CSCDLL="$SDKDIR/$SDKVER/Roslyn/bincore/csc.dll"
[ -f "$CSCDLL" ] || { echo "не найден csc.dll: $CSCDLL"; exit 1; }
csc() { dotnet "$CSCDLL" "$@"; }

REFDIR=$(ls -d "$DOTNET_HOME"/packs/Microsoft.NETCore.App.Ref/8.*/ref/net8.0 2>/dev/null | tail -1 || true)
[ -n "$REFDIR" ] || { echo "не найдены референсные сборки net8.0 в $DOTNET_HOME/packs"; exit 1; }

REFS=()
for f in "$REFDIR"/*.dll; do REFS+=("-r:$(native "$f")"); done

# Собираем ВНЕ репозитория: репозиторий открывается как проект Unity, и .dll внутри
# него Unity потащит импортировать как плагины.
mkdir -p /tmp/rgbuild/il
BP=/tmp/rgbuild             # для утилит bash
B=$(native /tmp/rgbuild)    # для csc и dotnet, то есть нативных программ

RUNCFG='{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}'

SRC=$(find ../Packages/com.exerussus.runtimegizmos/Runtime -name '*.cs')
DEMO=$(find ../Packages/com.exerussus.runtimegizmos.demo/Runtime -name '*.cs')
NW=CS0067,CS0414,CS0169,CS0649,CS0436,CS0219,CS8321

echo "   SDK $SDKVER   $SDKDIR"

# ------------------------------------------------------------------ проверки

echo "-- компиляция во всех конфигурациях"
for D in NONE DEVELOPMENT_BUILD UNITY_EDITOR RUNTIME_GIZMOS_ALWAYS "UNITY_EDITOR;DEVELOPMENT_BUILD" UNITY_6000_5_OR_NEWER "UNITY_EDITOR;UNITY_6000_5_OR_NEWER"; do
  EX=""; case "$D" in *UNITY_EDITOR*) EX=editorstubs.cs;; esac
  printf "   %-40s" "$([ "$D" = NONE ] && echo "<плеер, release>" || echo "$D")"
  OUT=$(csc -nologo -target:library -unsafe+ -langversion:9.0 -nowarn:$NW \
        -define:"$D" "${REFS[@]}" -out:"$B/rgchk.dll" stubs.cs $EX $SRC 2>&1) || true
  if [ -z "$OUT" ]; then echo "ok"; else echo "ОШИБКА"; echo "$OUT"; exit 1; fi
done

# Заглушки обязаны схлопываться внутри Unity: если репозиторий окажется в Assets/,
# вторые определения Vector3, Transform и прочих положат сборку всего проекта.
echo "-- защита заглушек от компиляции в Unity"
csc -nologo -target:library -langversion:9.0 -define:UNITY_2020_3_OR_NEWER \
  "${REFS[@]}" -out:"$B/rgguard.dll" stubs.cs editorstubs.cs rast.cs tests.cs bench.cs 2>/dev/null
LEFT=$(grep -a -o -E 'Vector3|MonoBehaviour|Transform|NativeArray' "$BP/rgguard.dll" 2>/dev/null | sort -u | tr '\n' ' ')
if [ -z "$LEFT" ]; then echo "   ни одного типа не просочилось: ok"
else echo "   ПРОСОЧИЛИСЬ ТИПЫ: $LEFT"; exit 1; fi

echo "-- демо-пакет"
OUT=$(csc -nologo -target:library -unsafe+ -langversion:9.0 -nowarn:$NW,CS0108 \
      -define:UNITY_EDITOR "${REFS[@]}" -out:"$B/rgdemo.dll" stubs.cs editorstubs.cs $SRC $DEMO 2>&1 | grep -v warning) || true
if [ -z "$OUT" ]; then echo "   компилируется: ok"; else echo "   ОШИБКА В ДЕМО"; echo "$OUT"; exit 1; fi

# Обещание GizmoLazy: в релизе исчезает вся цепочка — приёмник Track(...),
# модификаторы и вычисление аргументов. Меряем длину IL-тела метода с вызовами:
# если всё вырезано, там остаётся один ret. Пробу собираем ОТДЕЛЬНО от пакета,
# иначе в сборке окажутся сами определения и по именам ничего не поймёшь.
echo "-- вырезаемость GizmoLazy из релиза"
printf '%s' "$RUNCFG" > /tmp/rgbuild/il/ilcheck.runtimeconfig.json
csc -nologo -target:exe -langversion:9.0 -nowarn:$NW "${REFS[@]}" -out:"$B/il/ilcheck.dll" ilcheck.cs
IL=()
for SYM in "" "UNITY_EDITOR"; do
  csc -nologo -target:library -unsafe+ -langversion:9.0 -nowarn:$NW ${SYM:+-define:$SYM} \
    "${REFS[@]}" -out:"$B/il/rgcore.dll" stubs.cs editorstubs.cs $SRC 2>/dev/null
  csc -nologo -target:library -unsafe+ -langversion:9.0 -nowarn:$NW ${SYM:+-define:$SYM} \
    "${REFS[@]}" -r:"$B/il/rgcore.dll" -out:"$B/il/probe.dll" striptest.cs
  IL+=("$(dotnet "$B/il/ilcheck.dll" "$B/il/probe.dll" StripProbe Use)")
done
if [ "${IL[0]}" -le 4 ] && [ "${IL[1]}" -gt 100 ]; then
  echo "   IL тела: релиз ${IL[0]} байт, редактор ${IL[1]} байт: ok"
else
  echo "   ЦЕПОЧКА НЕ ВЫРЕЗАЕТСЯ: релиз ${IL[0]} байт, редактор ${IL[1]}"; exit 1
fi

echo "-- примеры из README"
DOCSKIPPED=""
if [ -n "$PY" ]; then
  DOC=$($PY doccheck.py)
  OUT=$(csc -nologo -target:library -unsafe+ -langversion:9.0 -nowarn:$NW,CS0108,CS0168 \
        -define:UNITY_EDITOR "${REFS[@]}" -out:"$B/rgdoc.dll" stubs.cs editorstubs.cs "$DOC" $SRC 2>&1 | grep -v warning) || true
  if [ -z "$OUT" ]; then echo "   все компилируются: ok"; else echo "   ОШИБКА В ПРИМЕРАХ"; echo "$OUT"; exit 1; fi
else
  # Единственный шаг, которому нужен Python: он вытаскивает примеры из README
  # регулярками и генерирует из них компилируемый файл. Не блокируем из-за него
  # весь прогон, но и молчать нельзя — иначе README тихо разъедется с кодом.
  DOCSKIPPED=1
  echo "   ПРОПУЩЕНО: не найден рабочий Python (нужен для doccheck.py)"
  echo "   поставить: winget install Python.Python.3.12"
fi

# Бенчмарк собирается всегда, чтобы не сгнил незамеченным, но по умолчанию не
# запускается: это измерительный инструмент, а не критерий правильности.
# Собирается БЕЗ UNITY_EDITOR — с RUNTIME_GIZMOS_ALWAYS вызовы Draw* остаются,
# но отладочная проверка потока в Begin() в замер не попадает.
echo "-- бенчмарк"
csc -nologo -target:exe -unsafe+ -optimize+ -langversion:9.0 -nowarn:$NW \
  -define:"RUNTIME_GIZMOS_ALWAYS" "${REFS[@]}" -out:"$B/rgbench.dll" stubs.cs bench.cs $SRC
printf '%s' "$RUNCFG" > /tmp/rgbuild/rgbench.runtimeconfig.json
echo "   компилируется: ok"

echo "-- прогон кейсов"
csc -nologo -target:exe -unsafe+ -langversion:9.0 -nowarn:$NW \
  -define:"UNITY_EDITOR" "${REFS[@]}" -out:"$B/rgtests.dll" stubs.cs editorstubs.cs rast.cs tests.cs $SRC
printf '%s' "$RUNCFG" > /tmp/rgbuild/rgtests.runtimeconfig.json

set +e
dotnet "$B/rgtests.dll"
RC=$?
set -e

if [ $RC -eq 0 ] && [ "$1" = "--bench" ]; then
  echo
  dotnet "$B/rgbench.dll" "$2" "$3"
fi

if [ -n "$DOCSKIPPED" ]; then
  echo
  echo "!! примеры из README не проверялись: нет Python"
fi

exit $RC

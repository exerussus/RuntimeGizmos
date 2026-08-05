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
# Нужен .NET SDK 8:  apt-get install -y dotnet-sdk-8.0
set -e
cd "$(dirname "$0")"
SDK=$(ls -d /usr/lib/dotnet/sdk/8.* | head -1)
REF=$(ls -d /usr/lib/dotnet/packs/Microsoft.NETCore.App.Ref/8.*/ref/net8.0 | head -1)
CSC="dotnet $SDK/Roslyn/bincore/csc.dll"
REFS=$(for f in $REF/*.dll; do echo -n "-r:$f "; done)
SRC=$(find ../Packages/com.exerussus.runtimegizmos/Runtime -name '*.cs')
DEMO=$(find ../Packages/com.exerussus.runtimegizmos.demo/Runtime -name '*.cs')
NW=CS0067,CS0414,CS0169,CS0649,CS0436,CS0219,CS8321

echo "-- компиляция во всех конфигурациях"
for D in NONE DEVELOPMENT_BUILD UNITY_EDITOR RUNTIME_GIZMOS_ALWAYS "UNITY_EDITOR;DEVELOPMENT_BUILD" UNITY_6000_5_OR_NEWER "UNITY_EDITOR;UNITY_6000_5_OR_NEWER"; do
  EX=""; case "$D" in *UNITY_EDITOR*) EX=editorstubs.cs;; esac
  printf "   %-34s" "$([ "$D" = NONE ] && echo "<плеер, release>" || echo "$D")"
  OUT=$($CSC -nologo -target:library -unsafe+ -langversion:9.0 -nowarn:$NW \
        -define:"$D" $REFS -out:/tmp/rgchk.dll stubs.cs $EX $SRC 2>&1) || true
  if [ -z "$OUT" ]; then echo "ok"; else echo "ОШИБКА"; echo "$OUT"; exit 1; fi
done

# Заглушки обязаны схлопываться внутри Unity: если репозиторий окажется в Assets/,
# вторые определения Vector3, Transform и прочих положат сборку всего проекта.
echo "-- защита заглушек от компиляции в Unity"
$CSC -nologo -target:library -langversion:9.0 -define:UNITY_2020_3_OR_NEWER \
  $REFS -out:/tmp/rgguard.dll stubs.cs editorstubs.cs rast.cs tests.cs 2>/dev/null
LEFT=$(python3 -c "
d = open('/tmp/rgguard.dll','rb').read()
n = [x for x in [b'Vector3', b'MonoBehaviour', b'Transform', b'NativeArray'] if x in d]
print(' '.join(t.decode() for t in n))
")
if [ -z "$LEFT" ]; then echo "   ни одного типа не просочилось: ok"
else echo "   ПРОСОЧИЛИСЬ ТИПЫ: $LEFT"; exit 1; fi

# Обещание GizmoLazy: в релизе исчезает вся цепочка — приёмник Track(...),
# модификаторы и вычисление аргументов. Меряем длину IL-тела метода с вызовами:
# если всё вырезано, там остаётся один ret. Пробу собираем ОТДЕЛЬНО от пакета,
# иначе в сборке окажутся сами определения и по именам ничего не поймёшь.
echo "-- демо-пакет"
OUT=$($CSC -nologo -target:library -unsafe+ -langversion:9.0 -nowarn:$NW,CS0108 \
      -define:UNITY_EDITOR $REFS -out:/tmp/rgdemo.dll stubs.cs editorstubs.cs $SRC $DEMO 2>&1 | grep -v warning) || true
if [ -z "$OUT" ]; then echo "   компилируется: ok"; else echo "   ОШИБКА В ДЕМО"; echo "$OUT"; exit 1; fi

echo "-- вырезаемость GizmoLazy из релиза"
mkdir -p /tmp/rgil
printf '%s' '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}' > /tmp/rgil/ilcheck.runtimeconfig.json
$CSC -nologo -target:exe -langversion:9.0 -nowarn:$NW $REFS -out:/tmp/rgil/ilcheck.dll ilcheck.cs
IL=()
for SYM in "" "UNITY_EDITOR"; do
  $CSC -nologo -target:library -unsafe+ -langversion:9.0 -nowarn:$NW ${SYM:+-define:$SYM} \
    $REFS -out:/tmp/rgil/rgcore.dll stubs.cs editorstubs.cs $SRC 2>/dev/null
  $CSC -nologo -target:library -unsafe+ -langversion:9.0 -nowarn:$NW ${SYM:+-define:$SYM} \
    $REFS -r:/tmp/rgil/rgcore.dll -out:/tmp/rgil/probe.dll striptest.cs
  IL+=("$(dotnet /tmp/rgil/ilcheck.dll /tmp/rgil/probe.dll StripProbe Use)")
done
if [ "${IL[0]}" -le 4 ] && [ "${IL[1]}" -gt 100 ]; then
  echo "   IL тела: релиз ${IL[0]} байт, редактор ${IL[1]} байт: ok"
else
  echo "   ЦЕПОЧКА НЕ ВЫРЕЗАЕТСЯ: релиз ${IL[0]} байт, редактор ${IL[1]}"; exit 1
fi

echo "-- примеры из README"
DOC=$(python3 doccheck.py)
OUT=$($CSC -nologo -target:library -unsafe+ -langversion:9.0 -nowarn:$NW,CS0108,CS0168 \
      -define:UNITY_EDITOR $REFS -out:/tmp/rgdoc.dll stubs.cs editorstubs.cs "$DOC" $SRC 2>&1 | grep -v warning) || true
if [ -z "$OUT" ]; then echo "   все компилируются: ok"; else echo "   ОШИБКА В ПРИМЕРАХ"; echo "$OUT"; exit 1; fi

echo "-- прогон кейсов"
$CSC -nologo -target:exe -unsafe+ -langversion:9.0 -nowarn:$NW \
  -define:"UNITY_EDITOR" $REFS -out:/tmp/rgtests.dll stubs.cs editorstubs.cs rast.cs tests.cs $SRC
printf '%s' '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}' > /tmp/rgtests.runtimeconfig.json
exec dotnet /tmp/rgtests.dll

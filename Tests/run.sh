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
NW=CS0067,CS0414,CS0169,CS0649,CS0436,CS0219,CS8321

echo "-- компиляция во всех конфигурациях"
for D in NONE DEVELOPMENT_BUILD UNITY_EDITOR RUNTIME_GIZMOS_ALWAYS "UNITY_EDITOR;DEVELOPMENT_BUILD"; do
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

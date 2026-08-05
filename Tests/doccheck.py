# -*- coding: utf-8 -*-
"""Извлекает все примеры кода из README пакета и собирает из них компилируемый файл.

Смысл: документация врёт молча. Пока примеры не проходят через компилятор,
переименование поля рвёт README, и никто об этом не узнаёт.
"""
import re, sys, os, tempfile

here = os.path.dirname(os.path.abspath(__file__))
PACKAGE = os.path.join(here, '..', 'Packages', 'com.exerussus.runtimegizmos')

# Пишем ВНЕ репозитория: сгенерированному файлу нечего делать ни в истории,
# ни в .gitignore. Путь печатается, чтобы run.sh его подхватил.
OUT = os.path.join(tempfile.gettempdir(), 'rg_doccheck.cs')

md = open(os.path.join(PACKAGE, 'README.md'), encoding='utf-8').read()
blocks = re.findall(r'```csharp\n(.*?)```', md, re.S)
skip = ('using Gizmos =', 'void Update()', 'using RuntimeGizmos.Extensions;')   # файловый алиас и фрагмент MonoBehaviour

DECLS = '''        Transform transform = null, head = null, player = null, eyes = null, muzzle = null;
        GameObject agent = null, target = null;
        Vector3 from = default, to = default, direction = default, position = default,
                center = default, a = default, b = default, c = default, d = default,
                apex = default, start = default, end = default, corner = default,
                origin = default, size = default, normal = default, scale = default,
                pos = default, force = default;
        Quaternion rotation = default;
        Bounds bounds = default, boundsA = default, boundsB = default;
        Ray ray = default; Mesh mesh = null; Texture texture = null; Camera camera = null;
        Color tint = default; Rect rect = default; string name = null;
        float distance = 0, radius = 0, fov = 0, maxRange = 0, minRange = 0, aspect = 0,
              angleDeg = 0, length = 0, height = 0, width = 0, aggroRadius = 0,
              viewDistance = 0, cornerFraction = 0, faint = 0, hp = 0, maxHp = 1, t = 0;
        int submeshIndex = 0, left = 0, right = 0, top = 0, bottom = 0;
        Vector3[] points = null, waypoints = null;
        Rigidbody rb = null, rigidbody = null; RaycastHit hit = default;
        Collider collider = null; Collider2D collider2D = null; Rigidbody2D rigidbody2D = null;
        RaycastHit2D hit2D = default; Joint joint = null; AudioSource audioSource = null;
        Light light = null; RectTransform rectTransform = null; Renderer renderer = null;
        Transform[] enemies = null; Transform enemy = null;
'''

methods, kept = [], 0
for i, blk in enumerate(blocks):
    if any(m in blk for m in skip):
        continue
    kept += 1
    out = []
    for line in blk.rstrip().split('\n'):
        code = re.sub(r'//.*$', '', line).rstrip()
        t = code.strip()
        # иллюстративное чтение свойства — присваиваем, иначе это не оператор
        if t.endswith(';') and '=' not in t and '(' not in t:
            indent = code[:len(code) - len(code.lstrip())]
            line = indent + 'System.Object _' + str(abs(hash(t)) % 99999) + ' = ' + t
        out.append('        ' + line if line.strip() else '')
    methods.append('    static void Block%d()\n    {\n%s\n%s\n    }' % (i, DECLS, "\n".join(out)))

open(OUT, 'w', encoding='utf-8').write(
    '// Сгенерировано doccheck.py. Компилируется только вне Unity — см. заглушки рядом.\n'
    '#if !UNITY_2020_3_OR_NEWER\n\n'
    'using System.Collections.Generic;\nusing UnityEngine;\nusing RuntimeGizmos;\nusing RuntimeGizmos.Extensions;\n\n'
    '// Сгенерировано doccheck.py из README.md. Руками не править.\n'
    'public static class DocCheck\n{\n%s\n}\n\n#endif\n' % "\n\n".join(methods))
print('   примеров из README: %d' % kept, file=sys.stderr)
print(OUT)   # путь — в stdout, чтобы run.sh его подхватил

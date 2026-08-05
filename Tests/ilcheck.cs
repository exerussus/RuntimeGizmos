// Читает длину IL-тела метода: если вызовы вырезаны, там останется один ret.
#if !UNITY_2020_3_OR_NEWER
using System;
using System.Linq;
using System.Reflection;

public static class IlCheck
{
    public static int Main(string[] args)
    {
        var asm = Assembly.LoadFrom(args[0]);
        var m = asm.GetType(args[1]).GetMethod(args[2], BindingFlags.Public | BindingFlags.Static);
        var il = m.GetMethodBody().GetILAsByteArray();
        Console.WriteLine(il.Length);
        return 0;
    }
}
#endif

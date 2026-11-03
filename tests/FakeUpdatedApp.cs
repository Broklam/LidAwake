using System;
using System.IO;
using System.Reflection;
using System.Threading;

#if OLD
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
#else
[assembly: AssemblyVersion("1.2.3.0")]
[assembly: AssemblyFileVersion("1.2.3.0")]
#endif

internal static class FakeUpdatedApp
{
    private static void Main(string[] args)
    {
#if LEGACY
        Thread.Sleep(4500);
#elif !BROKEN
        if (args.Length == 2 && args[0] == "--update-ready")
        {
            File.WriteAllText(args[1], "ready");
            Thread.Sleep(3000);
        }
#endif
    }
}

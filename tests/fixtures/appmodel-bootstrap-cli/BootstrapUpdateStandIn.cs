// PORT-ONLY offline test fixture. No AI CLI or network is used.
using System;
using System.IO;
using System.Threading;

internal static class BootstrapUpdateStandIn
{
    private static int Main(string[] args)
    {
        string folder = AppDomain.CurrentDomain.BaseDirectory;
        if (args.Length == 1 && args[0] == "update")
        {
            File.WriteAllText(Path.Combine(folder, "update-started"), "");
            DateTime bound = DateTime.UtcNow.AddSeconds(120);
            while (!File.Exists(Path.Combine(folder, "release-update")))
            {
                if (DateTime.UtcNow >= bound) return 1;
                Thread.Sleep(5);
            }
            File.WriteAllText(Path.Combine(folder, "update-finished"), "");
        }
        else if (args.Length == 1 && args[0] == "--version") Console.WriteLine("1.0.0");
        else Console.WriteLine("{}");
        return 0;
    }
}

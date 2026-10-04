using System.Diagnostics;
using System.Globalization;
using System.Text;

// PORT-ONLY: SK-6 offline executable fixture; never invokes a real tool.
if (args[0] == "echo")
{
    await Console.OpenStandardInput().CopyToAsync(Console.OpenStandardOutput());
    await Console.OpenStandardError().WriteAsync(new byte[] { 0, 255, 10, 13, 128 });
    return 37;
}
if (args[0] == "tree")
{
    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
    start.ArgumentList.Add("wait");
    using var grandchild = Process.Start(start)!;
    File.WriteAllText(args[1], grandchild.Id.ToString(CultureInfo.InvariantCulture));
    await Task.Delay(Timeout.Infinite);
}
if (args[0] == "linger")
{
    // A detached helper that is meant to outlive this program, as a tool's background server does.
    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
    start.ArgumentList.Add("wait");
    using var helper = Process.Start(start)!;
    File.WriteAllText(args[1], helper.Id.ToString(CultureInfo.InvariantCulture));
    return 0;
}
if (args[0] == "hold-profile")
{
    // Leaves a grandchild running inside the profile folder (as a LibreOffice worker can), so the folder cannot be deleted when this exits.
    var held = new Uri(args.Single(a => a.StartsWith("-env:UserInstallation=", StringComparison.Ordinal))[22..]).LocalPath;
    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, WorkingDirectory = held };
    start.ArgumentList.Add("wait");
    using var grandchild = Process.Start(start)!;
    File.WriteAllText(args[1], grandchild.Id.ToString(CultureInfo.InvariantCulture));
    return 7;
}
if (args[0] == "armed") return File.Exists(args[1]) ? 0 : 91;
if (args[0] == "wait") await Task.Delay(Timeout.Infinite);
if (args[0] == "provider")
{
    Console.WriteLine("ready");
    string? line;
    while ((line = Console.ReadLine()) != null)
    {
        var found = Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator)
            .Select(p => Path.Combine(p, line)).FirstOrDefault(File.Exists);
        if (found == null) { Console.WriteLine("missing"); continue; }
        var start = new ProcessStartInfo(found) { UseShellExecute = false, RedirectStandardOutput = true };
        start.ArgumentList.Add("args");
        start.ArgumentList.Add("found");
        using var child = Process.Start(start)!;
        Console.Write(await child.StandardOutput.ReadToEndAsync());
        await child.WaitForExitAsync();
    }
    return 0;
}
if (args[0] == "python-paths")
{
    Console.WriteLine(Environment.GetEnvironmentVariable("PYTHONUTF8"));
    Console.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "python314._pth")))));
    return 0;
}
if (args[0] == "env")
{
    foreach (var key in args.Skip(1))
        Console.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable(key) ?? "<absent>")));
    return 0;
}
if (args[0] == "profile")
{
    var value = args.Single(a => a.StartsWith("-env:UserInstallation=", StringComparison.Ordinal))[22..];
    var path = new Uri(value).LocalPath;
    Directory.CreateDirectory(path);
    File.WriteAllText(Path.Combine(path, "used"), "fixture");
    using (var stdout = Console.OpenStandardOutput()) stdout.Write(new UTF8Encoding(false).GetBytes(path + "\n"));
    return args.Contains("fail", StringComparer.Ordinal) ? 42 : 0;
}
foreach (var arg in args.Skip(1)) Console.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(arg)));
return 0;

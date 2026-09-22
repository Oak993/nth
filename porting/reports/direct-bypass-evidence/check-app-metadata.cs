using System.Reflection;
foreach (var configuration in new[] { "Debug", "Release" })
{
    var path = $@"E:\Droppy\port\src\Marrow.App\bin\{configuration}\net10.0-windows\Marrow.App.dll";
    var assembly = Assembly.LoadFile(path);
    var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value);
    var name = configuration == "Debug" ? "Marrow Dev" : "Marrow";
    if (metadata["CFBundleDisplayName"] != name || metadata["CFBundleShortVersionString"] != "1.6.0" || metadata["CFBundleVersion"] != "19")
        throw new Exception("Assembly metadata differs from the pinned source and approved rename.");
    Console.WriteLine($"{configuration}: {name}, 1.6.0 (19), PASS; read metadata only, app not run");
}

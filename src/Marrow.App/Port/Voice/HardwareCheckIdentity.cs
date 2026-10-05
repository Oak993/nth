// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-14): the two version strings the hardware key is made of besides the hardware (voice-SPEC V-10.5).
using System.Reflection;

namespace Marrow.App.Port.Voice;

/// <summary>
/// V-10.5 keys a stored check by "app version and build" and "Whisper.net runtime version". ENGINEERING DECISION: the app is the assembly's CFBundleShortVersionString and CFBundleVersion
/// (the pinned 1.7.0 and build 21, AssemblyInfo.cs) written <c>1.7.0+21</c>; the runtime is the version of the Whisper.net assembly the engine is built on (pinned 1.9.1 in
/// Directory.Packages.props), written with three components. Both are read once.
/// </summary>
internal static class HardwareCheckIdentity
{
    private static readonly Lazy<string> AppText = new(ReadApp);
    private static readonly Lazy<string> RuntimeText = new(() => typeof(Whisper.net.WhisperFactory).Assembly.GetName().Version?.ToString(3) ?? "");

    public static string App => AppText.Value;

    public static string Runtime => RuntimeText.Value;

    private static string ReadApp()
    {
        var assembly = typeof(HardwareCheckIdentity).Assembly;
        string Metadata(string key) => assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value ?? "";
        return Metadata("CFBundleShortVersionString") + "+" + Metadata("CFBundleVersion");
    }
}

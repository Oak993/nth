// PORT-ONLY: SK-11b shared offline helpers. No process, network, real CLI or real data folder: a CodexSession here is only built, never started.
using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Port.Skills.Providers;

internal static class CodexWiringFixture
{
    public static SwiftURL Url(string path) => SwiftURL.Create("marrow-native-path:" + path.Replace('\\', '/'))!;

    public static CodexSession Session(Marrow.Services.Port.Skills.Providers.CodexSkillWiring? wiring = null, string? resumeId = null, string workingDirectory = "/tmp/marrow-sk11b-work")
    {
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Codex,
            WorkingDirectory = Url(workingDirectory),
            Environment = new ValueMap<string, string>(new Dictionary<string, string>()),
            ResumeID = resumeId,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        };
        return wiring is null ? new CodexSession(configuration) : new CodexSession(configuration, wiring);
    }

    public static object? Call(CodexSession session, string method, params object?[] args) =>
        typeof(CodexSession).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, args);

    public static T Property<T>(CodexSession session, string name) =>
        (T)typeof(CodexSession).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;

    public static T Field<T>(CodexSession session, string name) =>
        (T)typeof(CodexSession).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;

    public static object? CallStatic(string method, params object?[] args) =>
        typeof(CodexSession).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);

    public static T StaticProperty<T>(string name) =>
        (T)typeof(CodexSession).GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    public static JSONValue Json(string text) => JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(text))!;

    public static string Bytes(JSONValue value) => System.Text.Encoding.UTF8.GetString(value.Data());

    public static string FixturePath(string name, [System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", name);
}

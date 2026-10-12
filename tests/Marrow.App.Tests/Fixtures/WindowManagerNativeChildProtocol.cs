using System.Diagnostics;
using System.Reflection;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;
using Marrow.Testing;

namespace Marrow.App.Tests.Window.Native;

// Internal recursion protocol, never an outer opt-in. The selected Fact authenticates its
// parent and waits for job membership verification before creating an Avalonia application.
internal static class WindowManagerNativeChildProtocol
{
    internal const string NonceVariable = "MARROW_NATIVE_CHILD_NONCE";
    internal const string DirectoryVariable = "MARROW_NATIVE_CHILD_DIRECTORY";
    internal static readonly TimeSpan StartupBound = TimeSpan.FromSeconds(45);
    internal static readonly TimeSpan ScenarioBound = TimeSpan.FromSeconds(90);
    internal static readonly TimeSpan CleanupBound = TimeSpan.FromSeconds(10);

    internal static bool IsChild => Environment.GetEnvironmentVariable(NonceVariable) is not null ||
        Environment.GetEnvironmentVariable(DirectoryVariable) is not null;

    internal static string FactName(string member, Type? factType = null)
    {
        // CallerMemberName supplies a real, parameterless Fact in an explicitly permitted top-level type.
        // Do not accept arbitrary filter syntax or a class-wide fallback that could recurse.
        var type = factType ?? typeof(WindowManagerNativeTests);
        if (type != typeof(WindowManagerNativeTests) && type != typeof(ToolTipBackdropNativeTests) && type != typeof(WindowManagerFactoryStretchTests) &&
            type != typeof(EffortSliderNativeFrameTests) && type != typeof(GlassWindowNativeTests) && type != typeof(HandshakeNativeFrameTests) &&
            type != typeof(WindowManagerNativeCentringTests) && type != typeof(WindowManagerNativeChromeTests) &&
            type != typeof(GalleryNativeTests) && type != typeof(TimelineRowsNativePopoverTests) && type != typeof(TimelineRowsNativeReportTests) && type != typeof(TimelineRowsNativeReviewKeysTests) && type != typeof(TimelineRowsNativeReviewBadgeKeysTests) && type != typeof(DiffPopoverNativeTests) && type != typeof(VoiceOrbNativeTests) && type != typeof(OpenPanelNativeTests) && type != typeof(ComposerAttachPanelNativeTests) && type != typeof(SwiftShadowNativeTests) && type != typeof(RootViewDropNativeTests) && type != typeof(LinkPopoverNativeTests) && type != typeof(AboutUpdateViewsNativeTests) && type != typeof(MCPCustomEditorNativeTests) && type != typeof(ComposerWorkingDotsNativeTests) && type != typeof(GlassMaterialNativeTests) && type != typeof(ComposerSuggestionsNativeTests) && type != typeof(ThreadTimelineLiveResizeNativeTests) && type != typeof(VoiceGlowNativeTests) && type != typeof(MainWindowCornerWedgeNativeTests) && type != typeof(PopoverGlassNativeTests) && type != typeof(GlassPanelAndRimNativeTests) && type != typeof(AuroraBootNativeTests))
            throw new InvalidOperationException("Native child selection is not an allowed test type.");
        var method = type.GetMethod(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        if (method is null || method.ReturnType != typeof(void) || method.GetParameters().Length != 0 ||
            method.GetCustomAttribute<FactAttribute>() is null)
            throw new InvalidOperationException("Native child selection is not an existing parameterless Fact: " + member);
        return type.FullName + "." + member;
    }

    internal static WindowManagerNativeRequest CreateRequest(string fact)
    {
        var nonce = Guid.NewGuid().ToString("N");
        var ambient = TestDataDirectory.Read();
        var root = string.IsNullOrWhiteSpace(ambient) ? Path.GetTempPath() : ambient;
        var run = Path.GetFullPath(Path.Combine(root, "WindowManagerNative", nonce));
        if (Path.Exists(run)) throw new IOException("Native child scratch already exists: " + run);
        Directory.CreateDirectory(run);
        foreach (var name in new[] { "data", "temp", "dev", "home", "local", "roaming", "nuget", "results" })
            Directory.CreateDirectory(WindowManagerProcessProtocol.UnderRun(run, Path.Combine(run, name)));
        // The shell's known folders of the child's profile (USERPROFILE is <run>\home, ChildEnvironment): a shell dialog opens at the profile's
        // Desktop, and with none the shell puts a "Location is not available" box on top of it. Inside the run directory, never the real profile.
        foreach (var name in new[] { "Desktop", "Documents", "Downloads", "Music", "Pictures", "Videos" })
            Directory.CreateDirectory(WindowManagerProcessProtocol.UnderRun(run, Path.Combine(run, "home", name)));
        using var parent = Process.GetCurrentProcess();
        var request = new WindowManagerNativeRequest(nonce, fact, run, Environment.ProcessId,
            parent.StartTime.ToUniversalTime().Ticks, Path.GetFullPath(typeof(WindowManagerNativeTests).Assembly.Location),
            typeof(WindowManagerNativeTests).Module.ModuleVersionId, typeof(WindowManager).Module.ModuleVersionId,
            GalleryRun.RequestedDirectory());
        Write(run, "request.json", request);
        return request;
    }

    internal static IReadOnlyDictionary<string, string> ChildEnvironment(WindowManagerNativeRequest request, string dotnet)
    {
        // Replace the complete environment before test-module initializers run. No provider key,
        // token, existing native/process child marker or caller opt-in is inherited.
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[]
        {
            "SystemRoot", "WINDIR", "ComSpec", "PATH", "PATHEXT", "SystemDrive", "NUMBER_OF_PROCESSORS",
            "PROCESSOR_ARCHITECTURE", "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT(x86)",
        })
        {
            if (Environment.GetEnvironmentVariable(key) is { Length: > 0 } value) values[key] = value;
        }
        var run = request.RunDirectory;
        values[NonceVariable] = request.Nonce;
        values[DirectoryVariable] = run;
        values[TestDataDirectory.VariableName] = Path.Combine(run, "data");
        values["MARROW_DEV_DATA_ROOT"] = Path.Combine(run, "dev");
        values["TEMP"] = Path.Combine(run, "temp");
        values["TMP"] = Path.Combine(run, "temp");
        values["TMPDIR"] = Path.Combine(run, "temp");
        values["HOME"] = Path.Combine(run, "home");
        values["USERPROFILE"] = Path.Combine(run, "home");
        values["LOCALAPPDATA"] = Path.Combine(run, "local");
        values["APPDATA"] = Path.Combine(run, "roaming");
        values["NUGET_PACKAGES"] = Path.Combine(run, "nuget");
        values["DOTNET_HOST_PATH"] = dotnet;
        // ST-0: the test runtime starts before App.csproj can affect GC mode.
        values["DOTNET_gcServer"] = "1";
        values["DOTNET_gcConcurrent"] = "1";
        values["DOTNET_CLI_HOME"] = Path.Combine(run, "home");
        // SDK first-use must not create certificates or alter the user's tools PATH.
        values["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false";
        values["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "false";
        values["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        values["DOTNET_NOLOGO"] = "1";
        values["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "1";
        values["MSBUILDDISABLENODEREUSE"] = "1";
        return values;
    }

    internal static string ResolveDotnet() => WindowManagerProcessProtocol.ResolveDotnetHost();

    internal static WindowManagerNativeReady EnterChild(string fact)
    {
        var nonce = Environment.GetEnvironmentVariable(NonceVariable);
        var directory = Environment.GetEnvironmentVariable(DirectoryVariable);
        if (!Guid.TryParseExact(nonce, "N", out _) || string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            throw new InvalidOperationException("The internal native child marker is incomplete or invalid.");
        var run = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        run = WindowManagerProcessProtocol.UnderRun(run, run);
        if (!string.Equals(Path.GetFileName(run), nonce, StringComparison.Ordinal))
            throw new InvalidOperationException("Native child nonce does not identify its scratch directory.");
        var request = Read<WindowManagerNativeRequest>(run, "request.json");
        if (!string.Equals(request.Nonce, nonce, StringComparison.Ordinal) ||
            !string.Equals(request.FactName, fact, StringComparison.Ordinal) ||
            !string.Equals(request.RunDirectory, run, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.TestAssemblyPath, Path.GetFullPath(typeof(WindowManagerNativeTests).Assembly.Location), StringComparison.OrdinalIgnoreCase) ||
            request.TestModuleVersion != typeof(WindowManagerNativeTests).Module.ModuleVersionId ||
            request.AppModuleVersion != typeof(WindowManager).Module.ModuleVersionId ||
            !string.Equals(Assembly.GetEntryAssembly()?.GetName().Name, "testhost", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Native child request does not match this exact Fact, testhost and compiled modules.");
        if (!string.Equals(TestDataDirectory.Read(), Path.Combine(run, "data"), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetTempPath()), Path.Combine(run, "temp"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Native child data/temp were not isolated before loading the test assembly.");
        if (request.ParentProcessId == Environment.ProcessId)
            throw new InvalidOperationException("A native child cannot authorize itself.");
        using var parent = Process.GetProcessById(request.ParentProcessId);
        _ = parent.SafeHandle;
        if (parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != request.ParentStartedUtcTicks)
            throw new InvalidOperationException("The native child's original parent is no longer alive.");
        using var child = Process.GetCurrentProcess();
        var ready = new WindowManagerNativeReady(request, Environment.ProcessId, child.StartTime.ToUniversalTime().Ticks);
        Write(run, "ready.json", ready);
        Until(() =>
        {
            if (parent.HasExited) throw new InvalidOperationException("Native parent exited before granting child job ownership.");
            return File.Exists(Path.Combine(run, "permit.json"));
        }, StartupBound, "Native parent did not verify the child in its owned job.");
        var permit = Read<WindowManagerNativePermit>(run, "permit.json");
        if (permit.Identity != ready)
            throw new InvalidOperationException("Native child permit belongs to a different process or scenario.");
        Console.WriteLine($"Native child {ready.ChildProcessId}: {fact}; parent foreground grant={permit.ForegroundGranted}, error={permit.ForegroundGrantError}; artifacts={run}");
        return ready;
    }

    internal static void Write<T>(string run, string name, T value) => WindowManagerProcessProtocol.Write(run, name, value);

    internal static T Read<T>(string run, string name) =>
        WindowManagerProcessProtocol.Read<T>(run, name);

    // Infrastructure liveness only. Native input assertions keep their existing native-message
    // receipts; these deadlines never replace a production clock or manufacture input evidence.
    internal static void Until(Func<bool> condition, TimeSpan bound, string reason) =>
        WindowManagerProcessProtocol.Until(condition, bound, reason);
}

internal sealed record WindowManagerNativeRequest(string Nonce, string FactName, string RunDirectory,
    int ParentProcessId, long ParentStartedUtcTicks, string TestAssemblyPath, Guid TestModuleVersion, Guid AppModuleVersion,
    string? GalleryDirectory = null);

internal sealed record WindowManagerNativeReady(WindowManagerNativeRequest Request, int ChildProcessId, long ChildStartedUtcTicks);

internal sealed record WindowManagerNativePermit(WindowManagerNativeReady Identity, bool ForegroundGranted, int ForegroundGrantError);

internal sealed record WindowManagerNativeChildResult(WindowManagerNativeReady Identity, bool BodyPassed, bool NativeThreadJoined, string? Error);

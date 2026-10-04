using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// PA-0016, ED-020, ED-127: Command Code's bare executable name is "cmd" (Provider.swift:54), which on Windows can
// resolve to the operating system's own interpreter. The resolver (the port of ProviderRegistry.swift:219-226) rejects a
// candidate the version resource identifies as the Windows command interpreter, for Command Code only. A byte copy of
// cmd.exe in a scratch folder is used so the copy sits inside the test guard's allowed roots (ED-211); it is read for its
// version resource and never run. The %SystemRoot% directory rule cannot be observed here: the guard already refuses
// every System32 path in tests.
public sealed class ProviderRegistryCommandInterpreterTests
{
    [Fact]
    [Trait("Req", "PR-PROVCLI-260")] // windows_path: a candidate whose version resource identifies the command interpreter is rejected (PA-0016)
    [Trait("Decision", "ED-127")]
    [Trait("Decision", "ED-020")]
    public void A_copy_of_the_command_interpreter_is_not_Command_Code_but_another_provider_may_use_the_same_file()
    {
        using var runtime = new TestRuntime();
        var interpreter = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        Assert.True(File.Exists(interpreter));
        var folder = Path.Combine(Path.GetTempPath(), "marrow-cmd-reject-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var copy = Path.Combine(folder, "cmd.exe");
            File.Copy(interpreter, copy);
            var standIn = Path.Combine(folder, "commandcode.exe");
            File.WriteAllBytes(standIn, new byte[] { 0x4D, 0x5A });

            var settings = new FakeProviderSettings();
            settings.Paths[ProviderKind.Commandcode] = copy;
            settings.Paths[ProviderKind.Claude] = copy;
            var registry = new ProviderRegistry(settings);

            // The interpreter is never taken for Command Code, and a custom path does not fall back (:224).
            Assert.Null(registry.Executable(ProviderKind.Commandcode));
            // The rule is Command Code's alone: the same file is a valid path for any other provider.
            Assert.NotNull(registry.Executable(ProviderKind.Claude));

            // A Command Code binary that is not the interpreter resolves.
            settings.Paths[ProviderKind.Commandcode] = standIn;
            var resolved = new ProviderRegistry(settings).Executable(ProviderKind.Commandcode);
            Assert.NotNull(resolved);
            Assert.Equal(Path.GetFullPath(standIn), Path.GetFullPath(resolved!.Path.Replace('/', '\\').TrimStart('\\')), ignoreCase: true);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

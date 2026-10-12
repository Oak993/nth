using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-45; DESIGN.md 2.6 "Self-check (capability learning)"). What Tactus
// learned is kept in one file in the data folder: back after a restart, saved shortly after each change and when Tactus
// ends, cleared on disk when the user clears it, never lost in memory when the disk refuses; a missing or broken file
// loads nothing, and only well-formed entries load (the borrow's ways only with a version and a framework).
[Trait("Decision", "ED-NEW-pcctl-45")]
public sealed class PcCapabilityFileTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly PcProofTarget Notepad = new("notepad.exe", "10.0.26100.1", PcFramework.Win32);
    private static readonly PcCapabilityKey SaveInvoke = new("notepad.exe", "10.0.26100.1", PcRung.R1, "Invoke", "Button");

    private static PcCapabilities Fresh() => new(() => Start);

    private static JsonObject Entry(string exe, string version, int rung, string method, string control, string state, DateTimeOffset at) => new()
    {
        ["exe"] = exe,
        ["version"] = version,
        ["rung"] = rung,
        ["method"] = method,
        ["control"] = control,
        ["state"] = state,
        ["at"] = at.ToUnixTimeMilliseconds(),
    };

    private static string Keys(PcCapabilities capabilities) => string.Join(" | ", (JsonNode.Parse(capabilities.Export())!.AsArray())
        .Select(item => item!["exe"] + "/" + item["version"] + "/" + item["rung"] + "/" + item["method"] + "/" + item["control"] + "/" + item["state"])
        .Order(StringComparer.Ordinal));

    [Fact]
    public void What_was_learned_is_back_after_a_restart()
    {
        using var folder = new PcTestFolder("pccaps");
        var path = Path.Combine(folder.Path, PcCapabilityFile.FileName);
        var first = Fresh();
        using (PcCapabilityFile.Open(first, path, () => Start))
        {
            PcBorrowProofs.Learn(first, Notepad, PcActivation.Caption, new PcBorrowOutcome(PcBorrowStatus.Done));
            first.Refuse(SaveInvoke);
        }

        var second = Fresh();
        using var reopened = PcCapabilityFile.Open(second, path, () => Start);

        Assert.Equal(PcLearned.Passed, second.State(PcBorrowProofs.Key(Notepad, PcBorrowProofs.Caption)));
        Assert.Equal(PcLearned.Passed, second.State(PcBorrowProofs.Key(Notepad, PcBorrowProofs.Restore)));
        Assert.Equal(PcLearned.Refused, second.State(SaveInvoke));
        Assert.Equal(Keys(first), Keys(second));
    }

    [Fact]
    public async Task A_change_is_saved_shortly_without_waiting_for_Tactus_to_end()
    {
        using var folder = new PcTestFolder("pccaps");
        var path = Path.Combine(folder.Path, PcCapabilityFile.FileName);
        var capabilities = Fresh();
        using var file = PcCapabilityFile.Open(capabilities, path, () => Start);

        capabilities.Refuse(SaveInvoke);

        var deadline = Environment.TickCount64 + 20_000;
        while (!File.Exists(path) && Environment.TickCount64 < deadline) await Task.Delay(50);
        var saved = Fresh();
        saved.Import(await File.ReadAllTextAsync(path));
        Assert.Equal(PcLearned.Refused, saved.State(SaveInvoke));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Every_change_is_announced_once_and_a_repeat_that_changes_nothing_is_not()
    {
        var capabilities = Fresh();
        var changes = 0;
        capabilities.Changed += () => changes++;
        var key = PcBorrowProofs.Key(Notepad, PcBorrowProofs.Caption);

        capabilities.Passed(key);
        capabilities.Passed(key);
        capabilities.FocusMoving(SaveInvoke);
        capabilities.FocusMoving(SaveInvoke);
        capabilities.Refuse(SaveInvoke);
        capabilities.Import(capabilities.Export());
        capabilities.Clear();

        Assert.Equal(4, changes);
    }

    [Fact]
    public void Clearing_forgets_what_was_learned_on_disk_too()
    {
        using var folder = new PcTestFolder("pccaps");
        var path = Path.Combine(folder.Path, PcCapabilityFile.FileName);
        var capabilities = Fresh();
        using (var file = PcCapabilityFile.Open(capabilities, path, () => Start))
        {
            PcBorrowProofs.Learn(capabilities, Notepad, PcActivation.Caption, new PcBorrowOutcome(PcBorrowStatus.Done));
            file.Flush();
            Assert.NotEqual("[]", File.ReadAllText(path));
            capabilities.Clear();
            file.Flush();
        }

        Assert.Equal("[]", File.ReadAllText(path));
        var reopened = Fresh();
        using var again = PcCapabilityFile.Open(reopened, path, () => Start);
        Assert.Equal(PcActivation.Caption, PcBorrowProofs.Activation(reopened, Notepad, PcBorrowKind.Focus));
        Assert.Equal(PcLearned.Unknown, reopened.State(PcBorrowProofs.Key(Notepad, PcBorrowProofs.Caption)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json {")]
    [InlineData("{\"exe\":\"notepad.exe\"}")]
    [InlineData("[1, \"two\", null]")]
    public void A_missing_or_broken_file_loads_nothing(string? content)
    {
        using var folder = new PcTestFolder("pccaps");
        var path = Path.Combine(folder.Path, PcCapabilityFile.FileName);
        if (content is not null) File.WriteAllText(path, content);
        var capabilities = Fresh();

        using var file = PcCapabilityFile.Open(capabilities, path, () => Start);

        Assert.Equal("[]", capabilities.Export());
        Assert.Equal(PcLearned.Unknown, capabilities.State(PcBorrowProofs.Key(Notepad, PcBorrowProofs.Restore)));
        Assert.Equal(PcRealInputReason.ActivationUnproven, PcBorrowProofs.Unproven(capabilities, "notepad.exe", PcBorrowKind.Focus));
    }

    [Fact]
    public void Only_well_formed_entries_load_and_the_borrow_ways_only_with_a_version_and_a_framework()
    {
        var hour = Start - TimeSpan.FromHours(1);
        var items = new JsonArray
        {
            Entry("notepad.exe", "10.0.26100.1", (int)PcRung.R1, "Invoke", "Button", "Refused", hour),
            Entry("notepad.exe", "10.0.26100.1", (int)PcRung.R4, PcBorrowProofs.Caption, "Win32", "Passed", hour),
            Entry("notepad.exe", "", (int)PcRung.R2, "WM_SETTEXT", "Edit", "Passed", hour),
            Entry(PcBorrowProofs.AnyApp, "10.0.26100.1", (int)PcRung.R4, PcBorrowProofs.Caption, "Win32", "Passed", hour),
            Entry("", "10.0.26100.1", (int)PcRung.R1, "Invoke", "Button", "Passed", hour),
            Entry("notepad.exe", "10.0.26100.1", 99, "Invoke", "Button", "Passed", hour),
            Entry("notepad.exe", "10.0.26100.1", (int)PcRung.R1, "", "Button", "Passed", hour),
            Entry("notepad.exe", "10.0.26100.1", (int)PcRung.R1, "Toggle", "Button", "Maybe", hour),
            Entry("notepad.exe", "10.0.26100.1", (int)PcRung.R1, "Toggle", "Button", "Unknown", hour),
            Entry("notepad.exe", "10.0.26100.1", (int)PcRung.R1, "Expand", "Button", "Refused", Start + TimeSpan.FromMinutes(1)),
            Entry("notepad.exe", "", (int)PcRung.R4, PcBorrowProofs.Restore, "Win32", "Passed", hour),
            Entry("notepad.exe", "10.0.26100.1", (int)PcRung.R4, PcBorrowProofs.Foreground, "", "Passed", hour),
            Entry("notepad.exe", "10.0.26100.1", (int)PcRung.R4, PcBorrowProofs.Pointer, "Electron", "Passed", hour),
            Entry("notepad.exe", "10.0.26100.1", (int)PcRung.R4, PcBorrowProofs.Pointer, "1", "Passed", hour),
            new JsonObject { ["exe"] = "notepad.exe", ["method"] = "Invoke", ["state"] = "Passed" },
        };
        var capabilities = Fresh();

        capabilities.Import(PcCapabilityFile.Valid(items.ToJsonString(), Start));

        Assert.Equal("notepad.exe//2/WM_SETTEXT/Edit/Passed | notepad.exe/10.0.26100.1/1/Invoke/Button/Refused | notepad.exe/10.0.26100.1/4/activate_caption/Win32/Passed", Keys(capabilities));
    }

    [Fact]
    public void A_file_that_cannot_be_written_keeps_learning_in_memory()
    {
        using var folder = new PcTestFolder("pccaps");
        var blocker = Path.Combine(folder.Path, "a-file");
        File.WriteAllText(blocker, "x");
        var path = Path.Combine(blocker, PcCapabilityFile.FileName);
        var capabilities = Fresh();

        using (var file = PcCapabilityFile.Open(capabilities, path, () => Start))
        {
            capabilities.Refuse(SaveInvoke);
            file.Flush();
        }

        Assert.Equal(PcLearned.Refused, capabilities.State(SaveInvoke));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void The_assembly_loads_the_learned_file_and_saves_it_when_Tactus_ends()
    {
        using var folder = new PcTestFolder("pccaps");
        var path = Path.Combine(folder.Path, "pc-control", PcCapabilityFile.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, new JsonArray { Entry("notepad.exe", "10.0.26100.1", (int)PcRung.R4, PcBorrowProofs.Caption, "Win32", "Passed", Start - TimeSpan.FromDays(2)) }.ToJsonString());
        using var rig = new PcAssemblyRig();
        var backends = new PcBackends
        {
            Catalog = rig.Desktop,
            Security = rig.Desktop,
            Reader = rig.Desktop,
            Feed = rig.Desktop,
            Messages = rig.Desktop,
            Foreground = rig.Desktop,
            Own = rig.Desktop,
            Launcher = rig.Desktop,
            Clock = rig.Desktop.Clock,
            Input = rig.Input,
            Hold = rig.Hold,
            RealHost = new PcFakeRealInputHost(rig.Input, rig.Desktop.Clock),
            Now = () => Start,
            CapabilitiesFile = () => path,
        };

        var assembly = new PcAssembly(backends, () => rig.Settings, _ => rig.StopKey);
        Assert.Equal(PcLearned.Passed, assembly.Capabilities.State(PcBorrowProofs.Key(Notepad, PcBorrowProofs.Caption)));
        assembly.Capabilities.Refuse(SaveInvoke);
        assembly.Dispose();

        var saved = Fresh();
        saved.Import(File.ReadAllText(path));
        Assert.Equal(PcLearned.Refused, saved.State(SaveInvoke));
        Assert.Equal(PcLearned.Passed, saved.State(PcBorrowProofs.Key(Notepad, PcBorrowProofs.Caption)));
    }

    [Fact]
    public void Without_a_file_the_assembly_keeps_what_it_learns_in_memory_only()
    {
        Assert.Null(new PcBackends
        {
            Catalog = null!,
            Security = null!,
            Reader = null!,
            Feed = null!,
            Messages = null!,
            Foreground = null!,
            Own = null!,
            Launcher = null!,
            Clock = null!,
            Input = null!,
            Hold = null!,
            RealHost = null!,
        }.CapabilitiesFile());
    }
}

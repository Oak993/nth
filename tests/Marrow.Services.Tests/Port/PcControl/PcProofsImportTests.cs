using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-45; DESIGN.md 2.6 "Self-check", 2.7 steps 5 and 6). The NP-4 smoke
// file is only an optional import for development builds: a missing, broken or old file proves nothing (the per-exe answer
// stays "activation unproven" and learned ways start unknown); only fresh passes of the borrow's ways for a real exe with
// a version and a framework come in, never "*", never over what was learned here, and never for another version or
// framework.
[Trait("Decision", "ED-NEW-pcctl-45")]
public sealed class PcProofsImportTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly PcProofTarget Notepad = new("notepad.exe", "10.0.26100.1", PcFramework.Win32);

    private static PcCapabilities Fresh() => new(() => Start);

    private static JsonObject Proof(string exe, string version, string method, string control, DateTimeOffset at, string state = "Passed", int rung = (int)PcRung.R4) => new()
    {
        ["exe"] = exe,
        ["version"] = version,
        ["rung"] = rung,
        ["method"] = method,
        ["control"] = control,
        ["state"] = state,
        ["at"] = at.ToUnixTimeMilliseconds(),
    };

    private static JsonObject Recent(string method) => Proof(Notepad.Exe, Notepad.Version, method, "Win32", Start - TimeSpan.FromDays(1));

    private static PcCapabilities Imported(params JsonObject[] proofs)
    {
        var capabilities = Fresh();
        capabilities.Import(PcCapabilityFile.Proofs(new JsonArray(proofs).ToJsonString(), Start, capabilities));
        return capabilities;
    }

    [Fact]
    public void A_fresh_proof_of_the_same_target_is_imported_through_the_store()
    {
        var capabilities = Imported(Recent(PcBorrowProofs.Foreground), Recent(PcBorrowProofs.Restore));

        Assert.Equal(PcLearned.Passed, capabilities.State(PcBorrowProofs.Key(Notepad, PcBorrowProofs.Foreground)));
        Assert.Equal(PcLearned.Passed, capabilities.State(PcBorrowProofs.Key(Notepad, PcBorrowProofs.Restore)));
        Assert.Equal(PcActivation.Foreground, PcBorrowProofs.Activation(capabilities, Notepad, PcBorrowKind.Focus));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[{\"exe\":")]
    [InlineData("{\"proofs\":[]}")]
    public void A_missing_or_broken_proofs_file_proves_nothing(string? content)
    {
        using var folder = new PcTestFolder("pcproofs");
        var proofs = Path.Combine(folder.Path, "tactus-smoke", "np4-proofs.json");
        if (content is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(proofs)!);
            File.WriteAllText(proofs, content);
        }
        var capabilities = Fresh();

        using var file = PcCapabilityFile.Open(capabilities, Path.Combine(folder.Path, PcCapabilityFile.FileName), () => Start, proofs);

        Assert.Equal("[]", capabilities.Export());
        Assert.Equal(PcRealInputReason.ActivationUnproven, PcBorrowProofs.Unproven(capabilities, "notepad.exe", PcBorrowKind.Focus));
        Assert.Equal(PcLearned.Unknown, capabilities.State(PcBorrowProofs.Key(Notepad, PcBorrowProofs.Restore)));
    }

    [Fact]
    public void A_proof_older_than_30_days_is_not_imported()
    {
        var capabilities = Imported(
            Proof(Notepad.Exe, Notepad.Version, PcBorrowProofs.Caption, "Win32", Start - PcCapabilityFile.ProofsFor),
            Proof(Notepad.Exe, Notepad.Version, PcBorrowProofs.Restore, "Win32", Start - PcCapabilityFile.ProofsFor - TimeSpan.FromMinutes(1)));

        Assert.Equal(PcLearned.Passed, capabilities.State(PcBorrowProofs.Key(Notepad, PcBorrowProofs.Caption)));
        Assert.Equal(PcLearned.Unknown, capabilities.State(PcBorrowProofs.Key(Notepad, PcBorrowProofs.Restore)));
    }

    [Fact]
    public void A_proof_for_every_app_or_without_a_version_or_a_framework_is_not_imported()
    {
        var day = Start - TimeSpan.FromDays(1);

        var capabilities = Imported(
            Proof(PcBorrowProofs.AnyApp, Notepad.Version, PcBorrowProofs.Caption, "Win32", day),
            Proof(Notepad.Exe, "", PcBorrowProofs.Caption, "Win32", day),
            Proof(Notepad.Exe, Notepad.Version, PcBorrowProofs.Caption, "", day),
            Proof("", Notepad.Version, PcBorrowProofs.Caption, "Win32", day));

        Assert.Equal("[]", capabilities.Export());
    }

    [Theory]
    [InlineData("10.0.26100.2", PcFramework.Win32)]
    [InlineData("10.0.26100.1", PcFramework.Xaml)]
    public void A_proof_from_another_version_or_framework_does_not_prove_this_target(string version, PcFramework framework)
    {
        var capabilities = Imported(Recent(PcBorrowProofs.Foreground), Recent(PcBorrowProofs.Restore));
        var live = new PcProofTarget(Notepad.Exe, version, framework);

        Assert.Equal(PcLearned.Unknown, capabilities.State(PcBorrowProofs.Key(live, PcBorrowProofs.Foreground)));
        Assert.Equal(PcLearned.Unknown, capabilities.State(PcBorrowProofs.Key(live, PcBorrowProofs.Restore)));
        Assert.Equal(PcActivation.Caption, PcBorrowProofs.Activation(capabilities, live, PcBorrowKind.Focus));
    }

    [Fact]
    public void Only_passes_of_the_borrow_ways_are_imported()
    {
        var day = Start - TimeSpan.FromDays(1);

        var capabilities = Imported(
            Proof(Notepad.Exe, Notepad.Version, PcBorrowProofs.Caption, "Win32", day, state: "Refused"),
            Proof(Notepad.Exe, Notepad.Version, PcBorrowProofs.Caption, "Win32", day, state: "FocusMoving"),
            Proof(Notepad.Exe, Notepad.Version, "Invoke", "Button", day, rung: (int)PcRung.R1),
            Proof(Notepad.Exe, Notepad.Version, "Invoke", "Win32", day),
            Proof(Notepad.Exe, Notepad.Version, PcBorrowProofs.Caption, "Win32", day, rung: (int)PcRung.R3));

        Assert.Equal("[]", capabilities.Export());
    }

    [Fact]
    public void An_import_never_overrides_what_was_learned_here()
    {
        using var folder = new PcTestFolder("pcproofs");
        var path = Path.Combine(folder.Path, PcCapabilityFile.FileName);
        var learned = Fresh();
        using (PcCapabilityFile.Open(learned, path, () => Start))
            PcBorrowProofs.Learn(learned, Notepad, PcActivation.Caption, new PcBorrowOutcome(PcBorrowStatus.Done) { ForegroundRestored = false });
        var proofs = Path.Combine(folder.Path, "np4-proofs.json");
        File.WriteAllText(proofs, new JsonArray(Recent(PcBorrowProofs.Restore), Recent(PcBorrowProofs.Foreground)).ToJsonString());
        var capabilities = Fresh();

        using var file = PcCapabilityFile.Open(capabilities, path, () => Start, proofs);

        Assert.Equal(PcLearned.Refused, capabilities.State(PcBorrowProofs.Key(Notepad, PcBorrowProofs.Restore)));
        Assert.Equal(PcLearned.Passed, capabilities.State(PcBorrowProofs.Key(Notepad, PcBorrowProofs.Foreground)));
        Assert.Equal(PcRealInputReason.ActivationUnproven, PcBorrowProofs.Unproven(capabilities, Notepad, PcBorrowKind.Focus));
    }
}

using System.Text.Json.Nodes;
using Marrow.App.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 0 point 10, 7.4; ED-NEW-dextra-as10-14): the two background switches are both off until the owner
// taps them, and nothing is written anywhere until he does. "Keep working" is kept in the store and survives a restart; "Start with
// Windows" is the Run entry itself. A folder a duty watches is approved by his yes and kept apart from the duties file.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-14")]
public sealed class BackgroundSwitchesTests
{
    private const string Exe = @"C:\Users\me\AppData\Local\nth\current\nth.exe";

    private readonly FakeRunKey key = new();
    private JsonObject? stored;
    private int saves;
    private Exception? saveFails;

    private BackgroundSwitches Make() => new(() => stored?.DeepClone() as JsonObject, document =>
    {
        if (saveFails is { } failure) throw failure;
        stored = (JsonObject)document.DeepClone();
        saves++;
    }, new StartWithWindows(key), () => Exe);

    [Fact]
    public void Both_switches_are_off_and_nothing_is_written_until_the_owner_taps_one()
    {
        var switches = Make();
        Assert.False(switches.KeepWorkingWhenClosed);
        Assert.False(switches.StartWithWindowsOn);
        Assert.False(switches.FirstDutyOffered);
        Assert.Equal(0, saves);
        Assert.Empty(key.Values);
    }

    [Fact]
    public void Keep_working_is_kept_and_survives_a_restart_and_turning_it_off_is_kept_too()
    {
        var switches = Make();
        var changes = 0;
        switches.Changed += () => changes++;
        Assert.Null(switches.SetKeepWorking(true));
        Assert.True(switches.KeepWorkingWhenClosed);
        Assert.Equal(1, changes);
        Assert.Null(switches.SetKeepWorking(true)); // no change, no second save
        Assert.Equal(1, saves);
        Assert.True(Make().KeepWorkingWhenClosed);
        Assert.Null(switches.SetKeepWorking(false));
        Assert.False(Make().KeepWorkingWhenClosed);
    }

    [Fact]
    public void A_choice_that_cannot_be_saved_does_not_change_and_says_why()
    {
        var switches = Make();
        saveFails = new IOException("The disk is full.");
        var problem = switches.SetKeepWorking(true);
        Assert.Contains("could not save", problem, StringComparison.Ordinal);
        Assert.False(switches.KeepWorkingWhenClosed);
    }

    [Fact]
    public void Start_with_windows_is_the_run_entry_itself()
    {
        var switches = Make();
        Assert.Null(switches.SetStartWithWindows(true));
        Assert.True(switches.StartWithWindowsOn);
        Assert.Equal("\"" + Exe + "\" --hidden", Assert.Single(key.Values).Value);
        Assert.Null(switches.SetStartWithWindows(false));
        Assert.False(switches.StartWithWindowsOn);
        Assert.Empty(key.Values);
        key.Throw = new UnauthorizedAccessException("denied");
        Assert.NotNull(switches.SetStartWithWindows(true));
    }

    [Fact]
    public void The_card_is_marked_offered_once_and_stays_marked_after_a_restart()
    {
        var switches = Make();
        switches.MarkFirstDutyOffered();
        switches.MarkFirstDutyOffered();
        Assert.Equal(1, saves);
        Assert.True(Make().FirstDutyOffered);
    }

    [Fact]
    public void A_folder_approved_for_a_duty_covers_what_is_inside_it_and_nothing_beside_it_and_is_kept()
    {
        var switches = Make();
        Assert.Null(switches.ApproveFolder(@"C:\Store\Invoices\"));
        Assert.True(switches.IsFolderApproved(@"C:\Store\Invoices"));
        Assert.True(switches.IsFolderApproved(@"C:\store\invoices\2026"));
        Assert.False(switches.IsFolderApproved(@"C:\Store\Invoices-old"));
        Assert.False(switches.IsFolderApproved(@"C:\Store"));
        Assert.False(switches.IsFolderApproved(@"D:\Store\Invoices"));
        Assert.True(Make().IsFolderApproved(@"C:\Store\Invoices\x"));
        Assert.Null(switches.ApproveFolder(@"C:\STORE\invoices"));
        Assert.Equal(1, saves);
        Assert.NotNull(switches.ApproveFolder("relative"));
    }

    [Fact]
    public void A_stored_list_with_nonsense_in_it_keeps_only_full_paths_and_the_number_of_folders_is_capped()
    {
        stored = new JsonObject
        {
            ["approvedFolders"] = new JsonArray(@"C:\good", "relative", 7, null, @"D:\also good"),
            ["keepWorking"] = "yes",
        };
        var switches = Make();
        Assert.True(switches.IsFolderApproved(@"C:\good\x"));
        Assert.True(switches.IsFolderApproved(@"D:\also good"));
        Assert.False(switches.IsFolderApproved("relative"));
        Assert.False(switches.KeepWorkingWhenClosed); // a value of the wrong kind is off, never on
        var capped = Make();
        for (var i = 0; i < BackgroundSwitches.FolderLimit + 5; i++) capped.ApproveFolder($@"C:\f{i}");
        Assert.NotNull(capped.ApproveFolder(@"C:\one-more"));
    }

    [Fact]
    public void A_folder_that_cannot_be_saved_is_not_approved()
    {
        var switches = Make();
        saveFails = new IOException("The disk is full.");
        Assert.NotNull(switches.ApproveFolder(@"C:\Store"));
        Assert.False(switches.IsFolderApproved(@"C:\Store"));
    }

    [Fact]
    public void A_file_of_this_version_keeps_the_fields_this_build_does_not_know_when_a_switch_is_saved()
    {
        stored = new JsonObject { ["version"] = 1, ["keepWorking"] = false, ["tomorrow"] = new JsonObject { ["x"] = 7 } };
        Assert.Null(Make().SetKeepWorking(true));
        Assert.True(stored["keepWorking"]!.GetValue<bool>());
        Assert.Equal(7, stored["tomorrow"]!["x"]!.GetValue<int>());
        Assert.Equal(1, stored["version"]!.GetValue<int>());
    }

    [Fact]
    public void A_file_a_newer_nth_wrote_is_read_but_never_written_and_the_switch_stays_as_it_was()
    {
        stored = new JsonObject { ["version"] = 2, ["keepWorking"] = false, ["approvedFolders"] = new JsonArray(@"C:\good") };
        var before = stored.ToJsonString();
        var switches = Make();
        Assert.True(switches.IsFolderApproved(@"C:\good"));
        Assert.Contains("newer nth", switches.SetKeepWorking(true), StringComparison.Ordinal);
        Assert.False(switches.KeepWorkingWhenClosed);
        Assert.NotNull(switches.ApproveFolder(@"C:\other"));
        Assert.Equal(0, saves);
        Assert.Equal(before, stored.ToJsonString());
    }
}

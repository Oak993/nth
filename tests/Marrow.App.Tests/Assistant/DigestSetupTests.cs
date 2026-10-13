using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-11 (DESIGN.md rev 5, 5.2, 5.4; ED-NEW-dextra-as11-9): what the app gives the digest of itself. The owner's chats become habits (a chat hidden from the Assistant
// is not read; Dextra's own and a head's are not his), the projects carry when he last worked in each, the day finals come from the activity log's own files, and there are no
// expired sign-ins on a model with no accounts.
[Trait("Feature", "AS-11")]
[Trait("Decision", "ED-NEW-dextra-as11-9")]
public sealed class DigestSetupTests
{
    private static ChatThread Chat(Project project, string title, double daysAgo) =>
        AppModelSamples.Thread(project) with { Title = title, CreatedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo), UpdatedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo) };

    [Fact]
    public void The_owners_chats_become_habits_the_projects_carry_when_he_last_worked_and_a_hidden_chat_is_left_out()
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("App", harness.NewFolder("app"));
        var chats = new[]
        {
            Chat(project, "Re-price listings 1", 1), Chat(project, "Re-price listings 2", 8), Chat(project, "Re-price listings 3", 15),
            Chat(project, "Re-price listings 4", 22) with { HiddenFromAssistant = true },
            Chat(project, "Dextra's own", 2) with { AssistantID = Guid.NewGuid() },
        };
        harness.WriteLibrary([project], chats);
        var model = harness.MakeModel();
        harness.Bootstrap(model);
        using var rig = new DutyHostRig();
        var setup = model.DigestSetupOf(rig.Controller);

        var habit = Assert.Single(setup.Habits!());
        Assert.Equal(3, habit.Starts.Count);                                         // the hidden chat was not read
        Assert.Equal("App", habit.ProjectName);
        Assert.StartsWith("Re-price listings", habit.Label, StringComparison.Ordinal);

        var used = Assert.Single(setup.Projects!());
        Assert.Equal((project.Id, "App"), (used.Id, used.Name));
        Assert.InRange((DateTimeOffset.UtcNow - used.LastUsed).TotalDays, 0.9, 1.1); // his own chats only: the one Dextra owns is not his work
        Assert.Empty(setup.ExpiredLogins!());
    }

    [Fact]
    public void The_day_final_comes_from_the_activity_logs_day_file_and_is_none_without_a_directory()
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        using var rig = new DutyHostRig();
        Assert.Null(model.DigestSetupOf(rig.Controller).DayFinal);
        using var temp = new AssistantTempRoot();
        using (var log = AuditLog.Open(temp.Roots, temp.Keys, new Marrow.Testing.ManualTimeProvider(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero))))
            log.Append(new AuditEntry { Assistant = "dextra", Tool = "digest", ActionClass = "ReadLocal", Result = "ok" });
        var setup = model.DigestSetupOf(rig.Controller, temp.AuditDirectory);
        Assert.Equal(AuditVerifier.DayFinal(temp.AuditDirectory, new DateOnly(2026, 10, 12)), setup.DayFinal!(new DateOnly(2026, 10, 12)));
        Assert.Null(setup.DayFinal!(new DateOnly(2026, 10, 11)));
    }
}

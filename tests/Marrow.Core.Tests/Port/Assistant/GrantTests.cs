using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-1 (DESIGN.md rev 5, 12.2, 9.1; ED-NEW-dextracore-3): structured grants. Live by scope and revocation,
// covering by class, target and field, and a child grant never broader than its parent's.
public sealed class GrantTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    private static Grant Make(ActionClass actionClass, GrantTarget target, GrantScope scope, string assistant = "dextra") =>
        new(Guid.NewGuid(), assistant, actionClass, target, scope) { Created = Now };

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-3")]
    public void A_grant_is_live_by_its_scope_until_revoked()
    {
        var site = GrantTarget.Site("https://voice.example");
        var mission = Guid.NewGuid();
        var once = Make(ActionClass.Login, site, GrantScope.Once);
        Assert.True(once.IsLive(Now, null));
        Assert.False(once.UsedByEntry("audit-1").IsLive(Now, null));
        var forMission = Make(ActionClass.Login, site, GrantScope.ForMission(mission));
        Assert.True(forMission.IsLive(Now, mission));
        Assert.False(forMission.IsLive(Now, Guid.NewGuid()));
        Assert.False(forMission.IsLive(Now, null));
        var until = Make(ActionClass.Login, site, GrantScope.UntilDate(Now.AddHours(1)));
        Assert.True(until.IsLive(Now, null));
        Assert.False(until.IsLive(Now.AddHours(1), null));
        var standing = Make(ActionClass.Login, site, GrantScope.Standing);
        Assert.True(standing.IsLive(Now.AddYears(5), null));
        // Revoking is a state change, enforced from that moment on.
        var revoked = standing.Revoked(Now.AddMinutes(5));
        Assert.True(revoked.IsLive(Now, null));
        Assert.False(revoked.IsLive(Now.AddMinutes(5), null));
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-3")]
    public void A_grant_covers_its_class_target_and_fields_only()
    {
        var inbox = Make(ActionClass.ConnectorWrite, GrantTarget.Connector("gmail"), GrantScope.Standing) with
        {
            Fields = new ValueList<string>(["draft", "label"]),
            Excluded = new ValueList<string>(["send"]),
        };
        Assert.True(inbox.Covers(ActionClass.ConnectorWrite, GrantTarget.Connector("Gmail"), "draft"));
        Assert.False(inbox.Covers(ActionClass.ConnectorWrite, GrantTarget.Connector("gmail"), "send"));
        Assert.False(inbox.Covers(ActionClass.ConnectorWrite, GrantTarget.Connector("gmail"), "archive"));
        Assert.False(inbox.Covers(ActionClass.ConnectorWrite, GrantTarget.Connector("gmail"), null));
        Assert.False(inbox.Covers(ActionClass.ConnectorWrite, GrantTarget.Connector("slack"), "draft"));
        Assert.False(inbox.Covers(ActionClass.Login, GrantTarget.Connector("gmail"), "draft"));
        Assert.False(inbox.Covers(ActionClass.ConnectorWrite, null, "draft"));

        var site = Make(ActionClass.Login, GrantTarget.Site("https://voice.example"), GrantScope.Standing);
        Assert.True(site.Covers(ActionClass.Login, GrantTarget.Site("https://voice.example/sign-in?next=/"), null));
        Assert.False(site.Covers(ActionClass.Login, GrantTarget.Site("http://voice.example"), null));
        Assert.False(site.Covers(ActionClass.Login, GrantTarget.Site("https://voice.example.evil.test"), null));

        var folder = Make(ActionClass.WriteElsewhere, GrantTarget.Folder(@"C:\Data"), GrantScope.Standing);
        Assert.True(folder.Covers(ActionClass.WriteElsewhere, GrantTarget.Folder(@"C:\data\reports\a.csv"), "write"));
        Assert.False(folder.Covers(ActionClass.WriteElsewhere, GrantTarget.Folder(@"C:\Database\a.csv"), "write"));
        Assert.False(folder.Covers(ActionClass.WriteElsewhere, GrantTarget.Folder(@"C:\Data\..\Windows\a.dll"), "write"));

        var people = Make(ActionClass.ConnectorWrite, GrantTarget.Recipients("ana@example.com", "raj@example.com"), GrantScope.Standing);
        Assert.True(people.Covers(ActionClass.ConnectorWrite, GrantTarget.Recipients("raj@example.com"), null));
        Assert.False(people.Covers(ActionClass.ConnectorWrite, GrantTarget.Recipients("raj@example.com", "x@example.com"), null));
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-3")]
    public void Only_grant_possible_classes_take_a_grant()
    {
        foreach (var actionClass in ActionClasses.All)
        {
            var grant = Make(actionClass, GrantTarget.App("Excel"), GrantScope.Standing);
            Assert.Equal(ActionClasses.IsGrantable(actionClass), GrantRules.Invalid(grant) is null);
            Assert.Equal(ActionClasses.IsGrantable(actionClass), grant.IsLive(Now, null));
        }
        Assert.Equal(
            new[] { ActionClass.Login, ActionClass.WriteElsewhere, ActionClass.ConnectorWrite },
            ActionClasses.All.Where(ActionClasses.IsGrantable));
        Assert.NotNull(GrantRules.Invalid(Make(ActionClass.Login, new GrantTarget(GrantTargetKind.App, ValueList<string>.Empty), GrantScope.Standing)));
        Assert.NotNull(GrantRules.Invalid(Make(ActionClass.Login, GrantTarget.App("x"), new GrantScope(GrantScopeKind.Mission))));
        Assert.NotNull(GrantRules.Invalid(Make(ActionClass.Login, GrantTarget.App("x"), new GrantScope(GrantScopeKind.Until))));
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-3")]
    public void A_child_grant_is_never_broader_than_its_parent()
    {
        var mission = Guid.NewGuid();
        var parent = Make(ActionClass.ConnectorWrite, GrantTarget.Connector("gmail"), GrantScope.UntilDate(Now.AddDays(7))) with
        {
            Fields = new ValueList<string>(["draft", "label"]),
            Excluded = new ValueList<string>(["send"]),
        };
        var child = Make(ActionClass.ConnectorWrite, GrantTarget.Connector("gmail"), GrantScope.UntilDate(Now.AddDays(1)), "channel-a") with
        {
            Fields = new ValueList<string>(["draft"]),
            Excluded = new ValueList<string>(["send"]),
        };
        Assert.True(GrantRules.IsWithin(child, parent));
        // Broader in each way, one at a time.
        Assert.False(GrantRules.IsWithin(child with { Class = ActionClass.Login }, parent));
        Assert.False(GrantRules.IsWithin(child with { Target = GrantTarget.Connector("slack") }, parent));
        Assert.False(GrantRules.IsWithin(child with { Fields = new ValueList<string>(["draft", "archive"]) }, parent));
        Assert.False(GrantRules.IsWithin(child with { Fields = ValueList<string>.Empty }, parent));
        Assert.False(GrantRules.IsWithin(child with { Fields = new ValueList<string>(["send", "draft"]), Excluded = ValueList<string>.Empty }, parent));
        Assert.False(GrantRules.IsWithin(child with { Scope = GrantScope.UntilDate(Now.AddDays(8)) }, parent));
        Assert.False(GrantRules.IsWithin(child with { Scope = GrantScope.Standing }, parent));
        Assert.False(GrantRules.IsWithin(child with { Scope = GrantScope.ForMission(mission) }, parent));
        Assert.True(GrantRules.IsWithin(child with { Scope = GrantScope.Once }, parent));
        // Fields listed by the child that the parent excludes are not allowed back in by dropping the exclusion.
        var noFields = parent with { Fields = ValueList<string>.Empty };
        Assert.False(GrantRules.IsWithin(child with { Fields = ValueList<string>.Empty, Excluded = ValueList<string>.Empty }, noFields));
        Assert.True(GrantRules.IsWithin(child with { Fields = new ValueList<string>(["draft"]), Excluded = ValueList<string>.Empty }, noFields));
        // Scopes nest: a mission inside the same mission, a folder inside a folder.
        var missionParent = parent with { Scope = GrantScope.ForMission(mission), Fields = ValueList<string>.Empty, Excluded = ValueList<string>.Empty };
        Assert.True(GrantRules.IsWithin(child with { Scope = GrantScope.ForMission(mission), Fields = ValueList<string>.Empty, Excluded = ValueList<string>.Empty }, missionParent));
        Assert.False(GrantRules.IsWithin(child with { Scope = GrantScope.ForMission(Guid.NewGuid()), Fields = ValueList<string>.Empty, Excluded = ValueList<string>.Empty }, missionParent));
        var folderParent = Make(ActionClass.WriteElsewhere, GrantTarget.Folder(@"C:\Data"), GrantScope.Standing);
        Assert.True(GrantRules.IsWithin(Make(ActionClass.WriteElsewhere, GrantTarget.Folder(@"C:\Data\a"), GrantScope.Standing), folderParent));
        Assert.False(GrantRules.IsWithin(Make(ActionClass.WriteElsewhere, GrantTarget.Folder(@"C:\"), GrantScope.Standing), folderParent));
        // An irreversible class is within nothing, not even itself.
        var outward = Make(ActionClass.Outward, GrantTarget.App("x"), GrantScope.Standing);
        Assert.False(GrantRules.IsWithin(outward, outward));
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-3")]
    public void Path_rules_compare_full_paths_without_case_and_never_place_a_relative_path()
    {
        Assert.True(PathRules.IsInside(@"C:\A\b\c.txt", @"c:\a", strict: true));
        Assert.True(PathRules.IsInside(@"C:\A", @"C:\A\", strict: false));
        Assert.False(PathRules.IsInside(@"C:\A", @"C:\A", strict: true));
        Assert.False(PathRules.IsInside(@"C:\AB\c", @"C:\A", strict: false));
        Assert.False(PathRules.IsInside(@"C:\A\..\B", @"C:\A", strict: false));
        Assert.False(PathRules.IsInside(@"a\b", @"C:\A", strict: false));
        Assert.Null(PathRules.Normalize(@"C:relative"));
        Assert.Null(PathRules.Normalize(""));
        Assert.Equal(@"C:\A\b", PathRules.Normalize(@"C:\A\b\"));
        Assert.Equal(@"C:\", PathRules.Normalize(@"C:\"));
    }
}

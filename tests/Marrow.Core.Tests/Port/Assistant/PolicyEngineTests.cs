using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-1 (DESIGN.md rev 5, 12.1, 12.2, 12.4, 12.7; ED-NEW-dextracore-6): the policy engine's table, written out
// literally: every class against every owner choice, with and without a covering grant, clean and tainted. A = Allow,
// Q = Ask, D = Deny.
public sealed class PolicyEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly AssistantProfile Dextra = new("dextra", "Dextra");
    private const string Folder = @"C:\Data\reports";

    private static PolicyContext Context(AssistantProfile? profile = null, bool tainted = false, bool purchasesOn = false, Grant[]? grants = null) =>
        new(new ValueList<AssistantProfile>([profile ?? Dextra]), Now)
        {
            Grants = new ValueList<Grant>(grants ?? []),
            TaintContext = tainted ? TaintContext.Clean.Absorb(TaintedText.Read(ContentOrigin.Email, "mail from x", "body")) : TaintContext.Clean,
            PurchasesOn = purchasesOn,
        };

    private static Classification Leaf(ActionClass actionClass) =>
        new(actionClass, "test")
        {
            GrantTarget = GrantTarget.Folder(Folder + @"\q3.csv"),
            Field = "write",
            Money = actionClass == ActionClass.Money ? MoneyKind.Purchase : MoneyKind.None,
        };

    private static Grant StandingGrant(ActionClass actionClass, string assistant = "dextra") =>
        new(Guid.NewGuid(), assistant, actionClass, GrantTarget.Folder(Folder), GrantScope.Standing) { Created = Now };

    private static char Letter(Verdict verdict) => verdict switch { Verdict.Allow => 'A', Verdict.Ask => 'Q', _ => 'D' };

    // Each line: the class, then no grant clean, grant clean, no grant tainted, grant tainted.
    private const string DefaultTable = """
        Agents A A A A
        Work A A A A
        Run A A Q Q
        ReadWeb A A A A
        ReadLocal A A A A
        Login Q A Q Q
        WriteElsewhere Q A Q Q
        ConnectorWrite Q A Q Q
        Outward Q Q Q Q
        Commitment Q Q Q Q
        Destructive Q Q Q Q
        Install Q Q Q Q
        Money D D D D
        PersonalDataOut Q Q Q Q
        HardDeny D D D D
        Unknown Q Q Q Q
        """;

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-6")]
    public void Every_class_by_its_default_with_and_without_a_grant_clean_and_tainted()
    {
        var lines = DefaultTable.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(ActionClasses.All.Count, lines.Length);
        foreach (var line in lines)
        {
            var cells = line.Split(' ');
            var actionClass = Enum.Parse<ActionClass>(cells[0]);
            var leaf = Leaf(actionClass);
            var grant = StandingGrant(actionClass);
            var actual = string.Join(' ',
                Letter(PolicyEngine.Decide(leaf, Context()).Verdict),
                Letter(PolicyEngine.Decide(leaf, Context(grants: [grant])).Verdict),
                Letter(PolicyEngine.Decide(leaf, Context(tainted: true)).Verdict),
                Letter(PolicyEngine.Decide(leaf, Context(tainted: true, grants: [grant])).Verdict));
            Assert.True(string.Join(' ', cells.Skip(1)) == actual, $"{actionClass}: expected {string.Join(' ', cells.Skip(1))}, got {actual}");
        }
    }

    // Each line: the class, then the verdict under each owner choice (Alone, AsksGrantPossible, AsksEveryTime,
    // AlwaysConfirmed, Never), with a covering grant held, clean, purchases on.
    private const string ChoiceTable = """
        Agents A Q Q Q D
        Work A Q Q Q D
        Run A Q Q Q D
        ReadWeb A Q Q Q D
        ReadLocal A Q Q Q D
        Login A A Q Q D
        WriteElsewhere A A Q Q D
        ConnectorWrite A A Q Q D
        Outward Q Q Q Q D
        Commitment Q Q Q Q D
        Destructive Q Q Q Q D
        Install Q Q Q Q D
        Money Q Q Q Q D
        PersonalDataOut Q Q Q Q D
        HardDeny D D D D D
        Unknown Q Q Q Q D
        """;

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-6")]
    public void Every_class_by_every_owner_choice_and_a_looser_choice_never_applies()
    {
        var lines = ChoiceTable.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(ActionClasses.All.Count, lines.Length);
        var choices = Enum.GetValues<ClassDefault>();
        foreach (var line in lines)
        {
            var cells = line.Split(' ');
            var actionClass = Enum.Parse<ActionClass>(cells[0]);
            var leaf = Leaf(actionClass);
            var grant = StandingGrant(actionClass);
            var actual = string.Join(' ', choices.Select(choice =>
            {
                var profile = Dextra with { ClassRules = new ValueMap<ActionClass, ClassDefault>([new(actionClass, choice)]) };
                return Letter(PolicyEngine.Decide(leaf, Context(profile, purchasesOn: true, grants: [grant])).Verdict);
            }));
            Assert.True(string.Join(' ', cells.Skip(1)) == actual, $"{actionClass}: expected {string.Join(' ', cells.Skip(1))}, got {actual}");
        }
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-6")]
    public void An_irreversible_step_is_never_allowed_by_a_grant()
    {
        foreach (var actionClass in ActionClasses.All.Where(ActionClasses.IsIrreversible))
        {
            var grant = StandingGrant(actionClass);
            Assert.Equal("Irreversible steps are always confirmed; no grant can cover them.", GrantRules.Invalid(grant));
            Assert.False(grant.IsLive(Now, null));
            foreach (var scope in new[] { GrantScope.Once, GrantScope.Standing, GrantScope.UntilDate(Now.AddDays(1)) })
            {
                var decision = PolicyEngine.Decide(Leaf(actionClass), Context(purchasesOn: true, grants: [grant with { Scope = scope }]));
                Assert.NotEqual(Verdict.Allow, decision.Verdict);
                Assert.Empty(decision.GrantsUsed);
            }
        }
        // Unknown and hard deny take no grant either.
        Assert.NotNull(GrantRules.Invalid(StandingGrant(ActionClass.Unknown)));
        Assert.NotNull(GrantRules.Invalid(StandingGrant(ActionClass.HardDeny)));
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-6")]
    public void Ask_first_wins_over_a_grant_and_over_alone()
    {
        var grant = StandingGrant(ActionClass.Login);
        var login = Leaf(ActionClass.Login);
        Assert.Equal(Verdict.Allow, PolicyEngine.Decide(login, Context(grants: [grant])).Verdict);
        var byClass = Dextra with { AskFirst = new ValueList<AskFirstRule>([new AskFirstRule(Class: ActionClass.Login)]) };
        var decision = PolicyEngine.Decide(login, Context(byClass, grants: [grant]));
        Assert.Equal(Verdict.Ask, decision.Verdict);
        Assert.Equal(PolicyRule.AskFirst, decision.Rule);
        // By target alone, on a class that runs alone.
        var byTarget = Dextra with { AskFirst = new ValueList<AskFirstRule>([new AskFirstRule(Target: GrantTarget.Folder(Folder))]) };
        Assert.Equal(Verdict.Ask, PolicyEngine.Decide(Leaf(ActionClass.Work), Context(byTarget)).Verdict);
        // The owner's global rule, and a parent's rule binding its Astur.
        var global = Context(grants: [grant]) with { AskFirst = new ValueList<AskFirstRule>([new AskFirstRule(Field: "write")]) };
        Assert.Equal(Verdict.Ask, PolicyEngine.Decide(login, global).Verdict);
        var astur = new AssistantProfile("channel-a", "Channel A") { ParentId = "dextra" };
        var chain = new ValueList<AssistantProfile>([astur, byClass]);
        var asturContext = new PolicyContext(chain, Now) { Grants = new ValueList<Grant>([grant, StandingGrant(ActionClass.Login, "channel-a")]) };
        Assert.Equal(Verdict.Ask, PolicyEngine.Decide(login, asturContext).Verdict);
        // A rule that matches nothing changes nothing; an empty rule matches nothing.
        var other = Dextra with { AskFirst = new ValueList<AskFirstRule>([new AskFirstRule(Class: ActionClass.Outward), new AskFirstRule()]) };
        Assert.Equal(Verdict.Allow, PolicyEngine.Decide(login, Context(other, grants: [grant])).Verdict);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-3")]
    public void An_Astur_grant_is_used_only_within_a_live_grant_of_every_parent()
    {
        var astur = new AssistantProfile("channel-a", "Channel A") { ParentId = "dextra" };
        var chain = new ValueList<AssistantProfile>([astur, Dextra]);
        var asturGrant = StandingGrant(ActionClass.WriteElsewhere, "channel-a");
        var leaf = Leaf(ActionClass.WriteElsewhere);
        PolicyDecision Decide(params Grant[] grants) => PolicyEngine.Decide(leaf, new PolicyContext(chain, Now) { Grants = new ValueList<Grant>(grants) });

        // The parent lacks the grant: the Astur's own grant is not enough.
        Assert.Equal(Verdict.Ask, Decide(asturGrant).Verdict);
        // The parent holds a broader one: allowed, by the Astur's grant.
        var parentGrant = new Grant(Guid.NewGuid(), "dextra", ActionClass.WriteElsewhere, GrantTarget.Folder(@"C:\Data"), GrantScope.Standing);
        var allowed = Decide(asturGrant, parentGrant);
        Assert.Equal(Verdict.Allow, allowed.Verdict);
        Assert.Equal(new[] { asturGrant.Id }, allowed.GrantsUsed);
        // The parent's is narrower, revoked, or ends sooner: asks.
        var narrower = parentGrant with { Target = GrantTarget.Folder(@"C:\Data\reports\2025") };
        Assert.Equal(Verdict.Ask, Decide(asturGrant, narrower).Verdict);
        Assert.Equal(Verdict.Ask, Decide(asturGrant, parentGrant.Revoked(Now.AddMinutes(-1))).Verdict);
        Assert.Equal(Verdict.Ask, Decide(asturGrant, parentGrant with { Scope = GrantScope.UntilDate(Now.AddDays(1)) }).Verdict);
        // The parent's grant alone does not act for the Astur.
        Assert.Equal(Verdict.Ask, Decide(parentGrant).Verdict);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-3")]
    public void An_Astur_choice_looser_than_its_parent_never_applies()
    {
        var parent = Dextra with { ClassRules = new ValueMap<ActionClass, ClassDefault>([new(ActionClass.ReadWeb, ClassDefault.AsksEveryTime)]) };
        var astur = new AssistantProfile("research", "Research desk")
        {
            ParentId = "dextra",
            ClassRules = new ValueMap<ActionClass, ClassDefault>([new(ActionClass.ReadWeb, ClassDefault.Alone)]),
        };
        var chain = AssistantChain.Of("research", [parent, astur]);
        Assert.Equal(new[] { "research", "dextra" }, chain.Select(p => p.Id));
        Assert.Equal(ClassDefault.AsksEveryTime, AssistantChain.EffectiveRule(chain, ActionClass.ReadWeb));
        Assert.Equal(Verdict.Ask, PolicyEngine.Decide(Leaf(ActionClass.ReadWeb), new PolicyContext(chain, Now)).Verdict);
        Assert.NotNull(AssistantChain.LooserThanParent(astur, [parent]));
        // A stricter choice applies and is fine.
        var stricter = astur with { ClassRules = new ValueMap<ActionClass, ClassDefault>([new(ActionClass.Work, ClassDefault.AsksEveryTime)]) };
        Assert.Null(AssistantChain.LooserThanParent(stricter, [parent]));
        Assert.Equal(Verdict.Ask, PolicyEngine.Decide(Leaf(ActionClass.Work), new PolicyContext(AssistantChain.Of("research", [parent, stricter]), Now)).Verdict);
        // A loop in the parents is refused.
        var loopA = new AssistantProfile("a", "A") { ParentId = "b" };
        var loopB = new AssistantProfile("b", "B") { ParentId = "a" };
        Assert.Throws<InvalidOperationException>(() => AssistantChain.Of("a", [loopA, loopB]));
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-6")]
    public void A_duty_can_only_narrow_its_assistants_grants()
    {
        var grant = StandingGrant(ActionClass.ConnectorWrite);
        var leaf = Leaf(ActionClass.ConnectorWrite);
        var free = Context(grants: [grant]);
        Assert.Equal(Verdict.Allow, PolicyEngine.Decide(leaf, free).Verdict);
        // The duty lists other grants only: this one does not count during the duty.
        var narrowed = free with { Duty = new DutyScope { GrantIds = new ValueList<Guid>([Guid.NewGuid()]) } };
        Assert.Equal(Verdict.Ask, PolicyEngine.Decide(leaf, narrowed).Verdict);
        // A duty naming a grant the assistant does not hold adds nothing.
        var added = Context() with { Duty = new DutyScope { GrantIds = new ValueList<Guid>([grant.Id]) } };
        Assert.Equal(Verdict.Ask, PolicyEngine.Decide(leaf, added).Verdict);
        // A duty limited to reading: plain work now asks, a read still runs alone.
        var readOnly = free with { Duty = new DutyScope { AllowedClasses = new ValueList<ActionClass>([ActionClass.ReadLocal, ActionClass.ReadWeb]) } };
        Assert.Equal(Verdict.Ask, PolicyEngine.Decide(Leaf(ActionClass.Work), readOnly).Verdict);
        Assert.Equal(PolicyRule.OutsideDuty, PolicyEngine.Decide(Leaf(ActionClass.Work), readOnly).Rule);
        Assert.Equal(Verdict.Allow, PolicyEngine.Decide(Leaf(ActionClass.ReadWeb), readOnly).Verdict);
        // A duty never loosens: an always-confirmed class still asks, whatever it lists.
        var loose = free with { Duty = new DutyScope { AllowedClasses = new ValueList<ActionClass>([ActionClass.Outward]) } };
        Assert.Equal(Verdict.Ask, PolicyEngine.Decide(Leaf(ActionClass.Outward), loose).Verdict);
    }

    [Theory]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-4")]
    [InlineData("schedule_duty")]
    [InlineData("watch_folder")]
    [InlineData("watch_page")]
    [InlineData("create_assistant")]
    [InlineData("request_grant")]
    [InlineData("edit_grant")]
    [InlineData("set_reserve_rule")]
    [InlineData("propose_capability")]
    [InlineData("suggest")]
    public void A_tainted_context_is_refused_standing_configuration(string tool)
    {
        var classification = ActionClassifier.Classify(new ActionRequest.AssistantTool(tool), ActionClassifierTests.Context());
        var refused = PolicyEngine.Decide(classification, Context(tainted: true));
        Assert.Equal(Verdict.Deny, refused.Verdict);
        Assert.Equal(PolicyEngine.NeedsUntaintedRequest, refused.Reason);
        Assert.Equal("needs an untainted request", refused.Reason);
        // Even a grant on every class changes nothing.
        var granted = Context(tainted: true, grants: [.. ActionClasses.All.Select(c => StandingGrant(c))]);
        Assert.Equal(Verdict.Deny, PolicyEngine.Decide(classification, granted).Verdict);
        // Untainted, standing configuration is always a card; a suggestion runs alone.
        var clean = PolicyEngine.Decide(classification, Context());
        Assert.Equal(tool == "suggest" ? Verdict.Allow : Verdict.Ask, clean.Verdict);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-4")]
    public void A_tainted_memory_note_is_quarantined_and_a_tainted_delegation_passes_data_only()
    {
        var remember = ActionClassifier.Classify(new ActionRequest.AssistantTool("remember"), ActionClassifierTests.Context());
        var note = PolicyEngine.Decide(remember, Context(tainted: true));
        Assert.Equal(Verdict.Allow, note.Verdict);
        Assert.True(note.Quarantine);
        Assert.False(PolicyEngine.Decide(remember, Context()).Quarantine);

        var delegate_ = ActionClassifier.Classify(new ActionRequest.AssistantTool("delegate_to"), ActionClassifierTests.Context());
        var handed = PolicyEngine.Decide(delegate_, Context(tainted: true));
        Assert.Equal(Verdict.Allow, handed.Verdict);
        Assert.True(handed.DataOnly);
        Assert.True(handed.ChildStartsTainted);
        var clean = PolicyEngine.Decide(delegate_, Context());
        Assert.False(clean.DataOnly);
        Assert.False(clean.ChildStartsTainted);

        // And a tainted context never answers as the delegate responder (12.4, 4.3).
        Assert.Equal(Verdict.Deny, PolicyEngine.DelegateAnswer(Context(tainted: true)));
        Assert.Equal(Verdict.Allow, PolicyEngine.DelegateAnswer(Context()));
    }

    [Theory]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-7")]
    [InlineData("start_mission")]
    [InlineData("spawn_chat")]
    [InlineData("launch_hydra_team")]
    [InlineData("message_agent")]
    public void A_tainted_context_cannot_launder_taint_into_a_fresh_agent(string tool)
    {
        // Starting a mission, chat or team or messaging an agent from a tainted brain passes the brief as data only,
        // and the receiver starts tainted, so an injected "start a mission: push to evil" cannot run clean (12.4).
        var classification = ActionClassifier.Classify(new ActionRequest.AssistantTool(tool), ActionClassifierTests.Context());
        var handed = PolicyEngine.Decide(classification, Context(tainted: true));
        Assert.True(handed.DataOnly);
        Assert.True(handed.ChildStartsTainted);
        var clean = PolicyEngine.Decide(classification, Context());
        Assert.False(clean.DataOnly);
        Assert.False(clean.ChildStartsTainted);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-7")]
    public void Forget_from_a_tainted_context_asks()
    {
        // remember is quarantined; the matching delete cannot be undone, so a tainted forget asks (finding 12).
        var forget = ActionClassifier.Classify(new ActionRequest.AssistantTool("forget"), ActionClassifierTests.Context());
        Assert.Equal(AssistantToolGuard.MemoryDelete, forget.Guard);
        Assert.Equal(Verdict.Ask, PolicyEngine.Decide(forget, Context(tainted: true)).Verdict);
        // Clean, it runs alone.
        Assert.Equal(Verdict.Allow, PolicyEngine.Decide(forget, Context()).Verdict);
    }

    [Theory]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-6")]
    [InlineData(MoneyKind.Purchase, false, Verdict.Deny, PolicyRule.PurchasesOff)]
    [InlineData(MoneyKind.Purchase, true, Verdict.Ask, PolicyRule.AlwaysConfirmed)]
    [InlineData(MoneyKind.Subscription, true, Verdict.Deny, PolicyRule.MoneyNeverAllowed)]
    [InlineData(MoneyKind.Transfer, true, Verdict.Deny, PolicyRule.MoneyNeverAllowed)]
    [InlineData(MoneyKind.ToPerson, true, Verdict.Deny, PolicyRule.MoneyNeverAllowed)]
    [InlineData(MoneyKind.None, true, Verdict.Deny, PolicyRule.MoneyNeverAllowed)]
    public void Only_a_purchase_with_purchases_on_reaches_a_card(MoneyKind kind, bool purchasesOn, Verdict verdict, PolicyRule rule)
    {
        var leaf = new Classification(ActionClass.Money, "pays") { Money = kind };
        var decision = PolicyEngine.Decide(leaf, Context(purchasesOn: purchasesOn));
        Assert.Equal(verdict, decision.Verdict);
        Assert.Equal(rule, decision.Rule);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-6")]
    public void A_command_of_several_parts_gets_its_strictest_part()
    {
        var context = Context();
        var classifier = ActionClassifierTests.Context();
        PolicyDecision Decide(string command) => PolicyEngine.Decide(ActionClassifier.Classify(new ActionRequest.ShellCommand(command), classifier), context);
        Assert.Equal(Verdict.Allow, Decide("git status && git diff").Verdict);
        var ask = Decide("git status && git push origin main");
        Assert.Equal(Verdict.Ask, ask.Verdict);
        Assert.Equal(PolicyRule.AlwaysConfirmed, ask.Rule);
        Assert.NotEmpty(ask.Parts);
        Assert.Equal(Verdict.Deny, Decide(@"git push; reg add HKLM\Software\X /f").Verdict);
    }

    [Fact]
    [Trait("Feature", "AS-1")]
    [Trait("Decision", "ED-NEW-dextracore-3")]
    public void A_read_outside_the_access_folders_runs_alone_only_under_a_folder_grant()
    {
        var read = new Classification(ActionClass.ReadLocal, "reads outside")
        {
            GrantTarget = GrantTarget.Folder(Folder + @"\q3.csv"), OutsideAccess = true, Field = "read",
        };
        var noGrant = PolicyEngine.Decide(read, Context());
        Assert.Equal(Verdict.Ask, noGrant.Verdict);
        Assert.Equal(PolicyRule.NeedsGrant, noGrant.Rule);
        var grant = StandingGrant(ActionClass.ReadLocal);
        Assert.Null(GrantRules.Invalid(grant));
        Assert.Equal(Verdict.Allow, PolicyEngine.Decide(read, Context(grants: [grant])).Verdict);
        // A Read local grant on anything but a folder is not a grant.
        Assert.NotNull(GrantRules.Invalid(grant with { Target = GrantTarget.Site("https://x.example") }));
    }
}

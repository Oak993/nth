using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Models;

// Source-derived tests for DroppyCode/Core/Models/Hydra.swift (B3 52bc792), unit Core.Models.Hydra, parts 1-4
// of 4. Every expected value is transcribed from the cited Swift lines or the record itself, never from the C#
// under test (R-TEST-4). Records proved: part 1, PR-HYDRA-001 to PR-HYDRA-024; part 2, PR-HYDRA-025 to
// PR-HYDRA-052 (excluding 029, 030, 041, 042, not named by this part's records); part 4, PR-HYDRA-080, 082,
// 083, 084, 085, 088 to 093, 097, 098, 248 to 251, PR-HYDRAMERGE-083, UI-HYDRAMERGE-003.
//
// PR-HYDRA-084, PR-HYDRA-085 and PR-HYDRA-098 (HydraPrompts.Delegations and its private body parser) are
// proved through Delegations(string) below, which since the orchestrator's ruling on this unit's STOP also
// exercises HydraPrompts.Delegation(fromEntry:) (src/Marrow.Core/Models/HydraDelegationStream.cs, ported
// from DroppyCode/Core/Models/HydraDelegationStream.swift:110-118 per R-FILE-1).
//
// PR-HYDRA-016 (the fixed order of the in-turn budget checks and the out-of-turn backstop's allowance) describes
// runtime behaviour in AppModel+Hydra.swift / ThreadRuntime.swift (the turn loop and the backstop timer), not
// anything HydraBudget's six constants or Hydra.swift itself compute; this file, which is scoped to Hydra.swift,
// has no loop or timer to exercise it against. It is untestable at this unit and is left for the runtime unit
// that owns the turn loop.
//
// HydraPrompts.HowToWork and WorkplaceRules (PR-HYDRA-031, 039, 040) are Swift `private` members whose only
// caller (DelegatedHeadPrompt, QueuedHeadPrompt) is a later part's work, so they are exercised directly as
// `internal` members here (see Models/Hydra.cs's part-2 file header note), not through a public prompt builder.
public sealed class HydraTests
{
    // ---- PR-HYDRA-001: HydraHeadProfile persists exactly five fields and round-trips through the settings store ----

    [Trait("Req", "PR-HYDRA-001")] // DroppyCode/Core/Models/Hydra.swift:17-29
    [Fact]
    public void A_new_profile_has_a_fresh_id_the_given_name_and_nil_provider_model_and_effort()
    {
        var profile = new HydraHeadProfile("quick");

        Assert.NotEqual(Guid.Empty, profile.Id);
        Assert.Equal("quick", profile.Name);
        Assert.Null(profile.Provider);
        Assert.Null(profile.Model);
        Assert.Null(profile.Effort);
    }

    [Trait("Req", "PR-HYDRA-001")] // DroppyCode/Core/Models/Hydra.swift:17-29
    [Fact]
    public void Two_freshly_created_profiles_get_different_ids()
    {
        var a = new HydraHeadProfile("quick");
        var b = new HydraHeadProfile("quick");

        Assert.NotEqual(a.Id, b.Id);
    }

    [Trait("Req", "PR-HYDRA-001")] // DroppyCode/Core/Models/Hydra.swift:17-29
    [Fact]
    public void A_fully_populated_profile_round_trips_through_the_storage_encoder_with_exactly_five_keys()
    {
        var profile = new HydraHeadProfile("deep") with
        {
            Provider = ProviderKind.Claude,
            Model = "claude-opus-5",
            Effort = "high",
        };

        var bytes = Coding.Encode(profile);
        var document = JsonDocument.Parse(bytes);
        var keys = document.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(["effort", "id", "model", "name", "provider"], keys);

        var decoded = Coding.Decode<HydraHeadProfile>(bytes);
        Assert.Equal(profile, decoded);
    }

    [Trait("Req", "PR-HYDRA-001")] // DroppyCode/Core/Models/Hydra.swift:17-29
    [Fact]
    public void A_profile_with_nil_provider_model_and_effort_round_trips_without_those_keys()
    {
        var profile = new HydraHeadProfile("quick");

        var bytes = Coding.Encode(profile);
        var document = JsonDocument.Parse(bytes);
        var keys = document.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(["id", "name"], keys);

        var decoded = Coding.Decode<HydraHeadProfile>(bytes);
        Assert.Equal(profile, decoded);
    }

    // ---- PR-HYDRA-002: briefLabel joins model and provider display name with " on " ----

    [Trait("Req", "PR-HYDRA-002")] // DroppyCode/Core/Models/Hydra.swift:32-35
    [Fact]
    public void BriefLabel_with_model_and_provider_puts_the_model_before_the_provider_display_name()
    {
        var profile = new HydraHeadProfile("deep") with { Model = "claude-opus-5", Provider = ProviderKind.Claude };

        Assert.Equal("deep (claude-opus-5 on Claude)", profile.BriefLabel);
    }

    [Trait("Req", "PR-HYDRA-002")] // DroppyCode/Core/Models/Hydra.swift:32-35
    [Fact]
    public void BriefLabel_with_only_a_provider_wraps_just_the_display_name()
    {
        var profile = new HydraHeadProfile("quick") with { Provider = ProviderKind.Claude };

        Assert.Equal("quick (Claude)", profile.BriefLabel);
    }

    [Trait("Req", "PR-HYDRA-002")] // DroppyCode/Core/Models/Hydra.swift:32-35
    [Fact]
    public void BriefLabel_with_only_a_model_wraps_just_the_model()
    {
        var profile = new HydraHeadProfile("quick") with { Model = "claude-opus-5" };

        Assert.Equal("quick (claude-opus-5)", profile.BriefLabel);
    }

    [Trait("Req", "PR-HYDRA-002")] // DroppyCode/Core/Models/Hydra.swift:32-35
    [Fact]
    public void BriefLabel_with_neither_set_is_the_bare_name()
    {
        var profile = new HydraHeadProfile("quick");

        Assert.Equal("quick", profile.BriefLabel);
    }

    // ---- PR-HYDRA-003: agentSlug lowercases the name and replaces non-alphanumeric characters with hyphens ----

    [Trait("Req", "PR-HYDRA-003")] // DroppyCode/Core/Models/Hydra.swift:39-41
    [Fact]
    public void AgentSlug_lowercases_and_hyphenates_a_multi_word_name()
    {
        var profile = new HydraHeadProfile("Deep Work");

        Assert.Equal("deep-work", profile.AgentSlug);
    }

    [Trait("Req", "PR-HYDRA-003")] // DroppyCode/Core/Models/Hydra.swift:39-41
    [Fact]
    public void AgentSlug_replaces_every_non_letter_non_number_character_one_for_one_without_collapsing_runs()
    {
        var profile = new HydraHeadProfile("A!!B");

        Assert.Equal("a--b", profile.AgentSlug);
    }

    [Trait("Req", "PR-HYDRA-003")] // DroppyCode/Core/Models/Hydra.swift:39-41
    [Fact]
    public void AgentSlug_of_an_all_punctuation_name_is_only_hyphens()
    {
        var profile = new HydraHeadProfile("!!!");

        Assert.Equal("---", profile.AgentSlug);
    }

    // ---- PR-HYDRA-004: matches accepts the trimmed case-insensitive name or an exact agentSlug match ----

    [Trait("Req", "PR-HYDRA-004")] // DroppyCode/Core/Models/Hydra.swift:46-50
    [Fact]
    public void Matches_accepts_the_name_case_insensitively_after_trimming_both_sides()
    {
        var profile = new HydraHeadProfile("Deep Work");

        Assert.True(profile.Matches("  deep work  "));
    }

    [Trait("Req", "PR-HYDRA-004")] // DroppyCode/Core/Models/Hydra.swift:46-50
    [Fact]
    public void Matches_accepts_an_exact_case_sensitive_agent_slug()
    {
        var profile = new HydraHeadProfile("Deep Work");

        Assert.True(profile.Matches("deep-work"));
        Assert.False(profile.Matches("Deep-Work"));
    }

    [Trait("Req", "PR-HYDRA-004")] // DroppyCode/Core/Models/Hydra.swift:46-50
    [Fact]
    public void Matches_rejects_a_name_that_is_neither_the_trimmed_name_nor_the_slug()
    {
        var profile = new HydraHeadProfile("Deep Work");

        Assert.False(profile.Matches("shallow work"));
    }

    // ---- PR-HYDRA-005: HydraPair persists ten fields and clamps maxHeads into 1...8 on decode ----

    [Trait("Req", "PR-HYDRA-005")] // DroppyCode/Core/Models/Hydra.swift:58-94
    [Fact]
    public void A_pair_missing_provider_fails_to_decode()
    {
        var json = """{"id":"11111111-1111-1111-1111-111111111111"}"""u8.ToArray();

        Assert.Throws<DecodingError>(() => Coding.Decode<HydraPair>(json));
    }

    [Trait("Req", "PR-HYDRA-005")] // DroppyCode/Core/Models/Hydra.swift:82-94
    [Fact]
    public void A_pair_with_only_provider_decodes_every_other_field_to_its_default()
    {
        var json = """{"provider":"claude"}"""u8.ToArray();

        var pair = Coding.Decode<HydraPair>(json)!;

        Assert.NotEqual(Guid.Empty, pair.Id);
        Assert.Equal(ProviderKind.Claude, pair.Provider);
        Assert.Null(pair.OrchestratorModel);
        Assert.Null(pair.OrchestratorEffort);
        Assert.Null(pair.WorkerProvider);
        Assert.Null(pair.WorkerModel);
        Assert.Null(pair.WorkerEffort);
        Assert.Null(pair.MaxHeads);
        Assert.Null(pair.Name);
        Assert.Empty(pair.HeadProfiles);
    }

    [Trait("Req", "PR-HYDRA-005")] // DroppyCode/Core/Models/Hydra.swift:74, :92, :109-111
    [Fact]
    public void A_stored_maxHeads_of_zero_decodes_as_one()
    {
        var json = """{"provider":"claude","maxHeads":0}"""u8.ToArray();

        var pair = Coding.Decode<HydraPair>(json)!;

        Assert.Equal(1, pair.MaxHeads);
    }

    [Trait("Req", "PR-HYDRA-005")] // DroppyCode/Core/Models/Hydra.swift:74, :92, :109-111
    [Fact]
    public void A_stored_maxHeads_of_ninety_nine_decodes_as_eight()
    {
        var json = """{"provider":"claude","maxHeads":99}"""u8.ToArray();

        var pair = Coding.Decode<HydraPair>(json)!;

        Assert.Equal(8, pair.MaxHeads);
    }

    [Trait("Req", "PR-HYDRA-005")] // DroppyCode/Core/Models/Hydra.swift:59-72
    [Fact]
    public void A_fully_populated_pair_round_trips_all_ten_stored_fields()
    {
        var profile = new HydraHeadProfile("quick");
        var pair = new HydraPair(ProviderKind.Claude) with
        {
            OrchestratorModel = "claude-opus-5",
            OrchestratorEffort = "high",
            WorkerProvider = ProviderKind.Codex,
            WorkerModel = "gpt-5",
            WorkerEffort = "medium",
            MaxHeads = 4,
            Name = "My Pair",
            HeadProfiles = new ValueList<HydraHeadProfile>([profile]),
        };

        var decoded = Coding.Decode<HydraPair>(Coding.Encode(pair))!;

        Assert.Equal(pair, decoded);
    }

    // ---- PR-HYDRA-006: headsProvider and sendsHeadsElsewhere derive from workerProvider ----

    [Trait("Req", "PR-HYDRA-006")] // DroppyCode/Core/Models/Hydra.swift:97, :100
    [Fact]
    public void HeadsProvider_is_the_lead_provider_when_no_worker_provider_is_set()
    {
        var pair = new HydraPair(ProviderKind.Claude);

        Assert.Equal(ProviderKind.Claude, pair.HeadsProvider);
        Assert.False(pair.SendsHeadsElsewhere);
    }

    [Trait("Req", "PR-HYDRA-006")] // DroppyCode/Core/Models/Hydra.swift:97, :100
    [Fact]
    public void HeadsProvider_is_the_worker_provider_when_it_differs_and_sendsHeadsElsewhere_is_true()
    {
        var pair = new HydraPair(ProviderKind.Claude) with { WorkerProvider = ProviderKind.Codex };

        Assert.Equal(ProviderKind.Codex, pair.HeadsProvider);
        Assert.True(pair.SendsHeadsElsewhere);
    }

    [Trait("Req", "PR-HYDRA-006")] // DroppyCode/Core/Models/Hydra.swift:100
    [Fact]
    public void SendsHeadsElsewhere_is_false_when_the_worker_provider_equals_the_lead_provider()
    {
        var pair = new HydraPair(ProviderKind.Claude) with { WorkerProvider = ProviderKind.Claude };

        Assert.False(pair.SendsHeadsElsewhere);
    }

    // ---- PR-HYDRA-007: customName trims the stored name and treats blank as none ----

    [Trait("Req", "PR-HYDRA-007")] // DroppyCode/Core/Models/Hydra.swift:103-106
    [Fact]
    public void CustomName_is_nil_for_a_nil_name()
    {
        var pair = new HydraPair(ProviderKind.Claude);

        Assert.Null(pair.CustomName);
    }

    [Trait("Req", "PR-HYDRA-007")] // DroppyCode/Core/Models/Hydra.swift:103-106
    [Fact]
    public void CustomName_is_nil_for_a_name_of_only_whitespace_and_newlines()
    {
        var pair = new HydraPair(ProviderKind.Claude) with { Name = " \n\t " };

        Assert.Null(pair.CustomName);
    }

    [Trait("Req", "PR-HYDRA-007")] // DroppyCode/Core/Models/Hydra.swift:103-106
    [Fact]
    public void CustomName_trims_a_non_blank_name()
    {
        var pair = new HydraPair(ProviderKind.Claude) with { Name = "  My Pair  " };

        Assert.Equal("My Pair", pair.CustomName);
    }

    // ---- PR-HYDRA-008: projectPair applies a project's pair rule only under all three conditions ----

    [Trait("Req", "PR-HYDRA-008")] // DroppyCode/Core/Models/Hydra.swift:117-120
    [Fact]
    public void ProjectPair_resolves_when_hydra_is_on_the_project_names_a_pair_and_it_is_available()
    {
        var pair = new HydraPair(ProviderKind.Claude);
        var other = new HydraPair(ProviderKind.Codex);

        var resolved = HydraPair.ProjectPair(pair.Id, hydraOn: true, [other, pair]);

        Assert.Equal(pair, resolved);
    }

    [Trait("Req", "PR-HYDRA-008")] // DroppyCode/Core/Models/Hydra.swift:117-120
    [Fact]
    public void ProjectPair_is_nil_when_hydra_is_off()
    {
        var pair = new HydraPair(ProviderKind.Claude);

        Assert.Null(HydraPair.ProjectPair(pair.Id, hydraOn: false, [pair]));
    }

    [Trait("Req", "PR-HYDRA-008")] // DroppyCode/Core/Models/Hydra.swift:117-120
    [Fact]
    public void ProjectPair_is_nil_when_the_project_names_no_pair()
    {
        var pair = new HydraPair(ProviderKind.Claude);

        Assert.Null(HydraPair.ProjectPair(null, hydraOn: true, [pair]));
    }

    [Trait("Req", "PR-HYDRA-008")] // DroppyCode/Core/Models/Hydra.swift:117-120
    [Fact]
    public void ProjectPair_is_nil_when_the_named_pair_is_not_in_the_available_list()
    {
        var missingID = Guid.NewGuid();
        var pair = new HydraPair(ProviderKind.Claude);

        Assert.Null(HydraPair.ProjectPair(missingID, hydraOn: true, [pair]));
    }

    // ---- PR-HYDRA-009: uncappedConcurrency is 8 and only bounds Droppy-run scheduling ----

    [Trait("Req", "PR-HYDRA-009")] // DroppyCode/Core/Models/Hydra.swift:168
    [Fact]
    public void UncappedConcurrency_is_eight()
    {
        Assert.Equal(8, HydraLaunch.UncappedConcurrency);
    }

    [Trait("Req", "PR-HYDRA-009")] // DroppyCode/Core/Models/Hydra.swift:171-173
    [Fact]
    public void HasRoom_with_no_cap_allows_up_to_the_uncapped_concurrency()
    {
        var launch = new HydraLaunch { HeadsProvider = ProviderKind.Claude, RunsNatively = false };

        Assert.True(launch.HasRoom(7));
        Assert.False(launch.HasRoom(8));
    }

    [Trait("Req", "PR-HYDRA-009")] // DroppyCode/Core/Models/Hydra.swift:171-173
    [Fact]
    public void HasRoom_with_a_cap_bounds_at_the_cap_not_at_eight()
    {
        var launch = new HydraLaunch { HeadsProvider = ProviderKind.Claude, RunsNatively = false, MaxHeads = 2 };

        Assert.True(launch.HasRoom(1));
        Assert.False(launch.HasRoom(2));
    }

    // ---- PR-HYDRA-010: HydraLaunch's twelve fields keep these in-source defaults ----

    [Trait("Req", "PR-HYDRA-010")] // DroppyCode/Core/Models/Hydra.swift:125-180
    [Fact]
    public void HydraLaunch_keeps_its_in_source_defaults_when_only_the_two_required_fields_are_given()
    {
        var launch = new HydraLaunch { HeadsProvider = ProviderKind.Claude, RunsNatively = true };

        Assert.Equal(ProviderKind.Claude, launch.HeadsProvider);
        Assert.True(launch.RunsNatively);
        Assert.Null(launch.HeadsLabel);
        Assert.Null(launch.WorkerModel);
        Assert.Null(launch.WorkerEffort);
        Assert.Null(launch.MaxHeads);
        Assert.Empty(launch.HeadProfiles);
        Assert.True(launch.IsolatesHeads);
        Assert.False(launch.AutoMerges);
        Assert.False(launch.ReviewsHeads);
        Assert.Empty(launch.Projects);
        Assert.True(launch.AntiSlopEnabled);
    }

    // ---- PR-HYDRA-011: profile(named:) returns the first matching head profile ----

    [Trait("Req", "PR-HYDRA-011")] // DroppyCode/Core/Models/Hydra.swift:177-179
    [Fact]
    public void Profile_named_returns_the_first_matching_profile_in_order()
    {
        var first = new HydraHeadProfile("Quick");
        var second = new HydraHeadProfile("Deep Work");
        var launch = new HydraLaunch
        {
            HeadsProvider = ProviderKind.Claude,
            RunsNatively = false,
            HeadProfiles = new ValueList<HydraHeadProfile>([first, second]),
        };

        Assert.Equal(second, launch.Profile("deep-work"));
    }

    [Trait("Req", "PR-HYDRA-011")] // DroppyCode/Core/Models/Hydra.swift:177-179
    [Fact]
    public void Profile_named_returns_nil_for_an_unknown_name()
    {
        var launch = new HydraLaunch
        {
            HeadsProvider = ProviderKind.Claude,
            RunsNatively = false,
            HeadProfiles = new ValueList<HydraHeadProfile>([new HydraHeadProfile("Quick")]),
        };

        Assert.Null(launch.Profile("nonexistent"));
    }

    // ---- PR-HYDRA-012: HydraBudget's six numeric constants ----

    [Trait("Req", "PR-HYDRA-012")] // DroppyCode/Core/Models/Hydra.swift:186-201
    [Fact]
    public void HydraBudget_has_exactly_these_six_literal_values()
    {
        Assert.Equal(24, HydraBudget.PacingTools);
        Assert.Equal(120, HydraBudget.WrapUpTools);
        Assert.Equal(160, HydraBudget.MaxTools);
        Assert.Equal(1500, HydraBudget.WrapUpSeconds);
        Assert.Equal(2100, HydraBudget.MaxSeconds);
        Assert.Equal(180, HydraBudget.ReportSeconds);
    }

    // ---- PR-HYDRA-013 to 015: the three budget notes, byte for byte with the OD-0012 F1 rename ----

    [Trait("Req", "PR-HYDRA-013")] // DroppyCode/Core/Models/Hydra.swift:198
    [Fact]
    public void PacingNote_is_the_deviation_worded_note_with_twenty_four_interpolated()
    {
        Assert.Equal(
            "[Nth] You have run 24 tools without changing a file. If the task is research, reply with your findings now. Otherwise act on what you know: make the change, or reply with what blocks you.",
            HydraBudget.PacingNote);
    }

    [Trait("Req", "PR-HYDRA-014")] // DroppyCode/Core/Models/Hydra.swift:199
    [Fact]
    public void WrapUpNote_is_the_deviation_worded_note_verbatim()
    {
        Assert.Equal(
            "[Nth] Your budget is nearly spent. Finish now: complete the smallest correct version of the task, then reply with your report.",
            HydraBudget.WrapUpNote);
    }

    [Trait("Req", "PR-HYDRA-015")] // DroppyCode/Core/Models/Hydra.swift:200
    [Fact]
    public void FinalNote_is_the_deviation_worded_note_verbatim()
    {
        Assert.Equal(
            "[Nth] Your budget is spent and your tools are gone. Reply now with your report: what you changed, how far it got, and what is left.",
            HydraBudget.FinalNote);
    }

    // ---- PR-HYDRA-017: the roster holds exactly 25 personas in order ----

    [Trait("Req", "PR-HYDRA-017")] // DroppyCode/Core/Models/Hydra.swift:231-257
    [Fact]
    public void The_roster_holds_exactly_twenty_five_personas_in_this_order()
    {
        var expected = new (string Name, string Asset, uint Hex)[]
        {
            ("Hank", "hydra-head-01", 0xFF8A3Du), ("Walter", "hydra-head-02", 0x3D8BFFu), ("Ada", "hydra-head-03", 0x34C46Au),
            ("Otto", "hydra-head-04", 0xA35BE0u), ("Nova", "hydra-head-05", 0xF25C9Au), ("Remy", "hydra-head-06", 0x2BB5B0u),
            ("Iris", "hydra-head-07", 0xE8B430u), ("Milo", "hydra-head-08", 0xE84F4Fu), ("Juno", "hydra-head-09", 0x6A5CFFu),
            ("Ezra", "hydra-head-10", 0x4FD1A1u), ("Lena", "hydra-head-11", 0x2FB8E6u), ("Bo", "hydra-head-12", 0xB0784Au),
            ("Kai", "hydra-head-13", 0x8FD14Fu), ("Vera", "hydra-head-14", 0xD946EFu), ("Finn", "hydra-head-15", 0xF97316u),
            ("Mira", "hydra-head-16", 0xC084FCu), ("Odin", "hydra-head-17", 0xB45309u), ("Suki", "hydra-head-18", 0xF472B6u),
            ("Rex", "hydra-head-19", 0x2563EBu), ("Zola", "hydra-head-20", 0x65A30Du), ("Pip", "hydra-head-21", 0x94A3B8u),
            ("Ivo", "hydra-head-22", 0x0D9488u), ("Lux", "hydra-head-23", 0xFACC15u), ("Tova", "hydra-head-24", 0x059669u),
            ("Gus", "hydra-head-25", 0x7DD3FCu),
        };

        Assert.Equal(25, HydraRoster.Personas.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Name, HydraRoster.Personas[i].Name);
            Assert.Equal(expected[i].Asset, HydraRoster.Personas[i].Asset);
            Assert.Equal(expected[i].Hex, HydraRoster.Personas[i].Hex);
        }
    }

    // ---- PR-HYDRA-018: persona(at:) wraps with a double modulo and appends a round suffix past 25 ----

    [Trait("Req", "PR-HYDRA-018")] // DroppyCode/Core/Models/Hydra.swift:259-265
    [Fact]
    public void Persona_at_zero_through_twenty_four_gives_the_bare_roster_persona()
    {
        Assert.Equal("Hank", HydraRoster.Persona(0).Name);
        Assert.Equal("Gus", HydraRoster.Persona(24).Name);
    }

    [Trait("Req", "PR-HYDRA-018")] // DroppyCode/Core/Models/Hydra.swift:259-265
    [Fact]
    public void Persona_at_twenty_five_starts_the_second_round()
    {
        var persona = HydraRoster.Persona(25);

        Assert.Equal("Hank 2", persona.Name);
        Assert.Equal("hydra-head-01", persona.Asset);
        Assert.Equal(0xFF8A3Du, persona.Hex);
    }

    [Trait("Req", "PR-HYDRA-018")] // DroppyCode/Core/Models/Hydra.swift:259-265
    [Fact]
    public void Persona_at_forty_nine_and_fifty_cross_into_the_third_round()
    {
        Assert.Equal("Gus 2", HydraRoster.Persona(49).Name);
        Assert.Equal("Hank 3", HydraRoster.Persona(50).Name);
    }

    [Trait("Req", "PR-HYDRA-018")] // DroppyCode/Core/Models/Hydra.swift:259-265
    [Fact]
    public void Persona_at_a_negative_index_wraps_to_a_valid_slot_with_no_round_suffix()
    {
        var persona = HydraRoster.Persona(-1);

        Assert.Equal("Gus", persona.Name);
    }

    // ---- PR-HYDRA-019: index(named:) strips a trailing integer token and matches case-insensitively ----

    [Trait("Req", "PR-HYDRA-019")] // DroppyCode/Core/Models/Hydra.swift:273-280
    [Fact]
    public void Index_named_strips_the_trailing_round_number_and_matches_case_insensitively()
    {
        Assert.Equal(3, HydraRoster.Index("Otto 2"));
        Assert.Equal(3, HydraRoster.Index(" otto "));
    }

    [Trait("Req", "PR-HYDRA-019")] // DroppyCode/Core/Models/Hydra.swift:273-280
    [Fact]
    public void Index_named_returns_nil_for_a_name_off_the_roster()
    {
        Assert.Null(HydraRoster.Index("Ives"));
    }

    [Trait("Req", "PR-HYDRA-019")] // DroppyCode/Core/Models/Hydra.swift:273-280
    [Fact]
    public void Index_named_only_drops_the_last_token_when_it_parses_as_an_integer()
    {
        Assert.Null(HydraRoster.Index("Otto Work"));
    }

    // ---- PR-HYDRA-020: persona(named:) keeps a round number above 1 by offsetting the index ----

    [Trait("Req", "PR-HYDRA-020")] // DroppyCode/Core/Models/Hydra.swift:284-292
    [Fact]
    public void Persona_named_with_a_round_above_one_offsets_by_whole_roster_lengths()
    {
        var persona = HydraRoster.Persona("Nova 3");

        Assert.NotNull(persona);
        Assert.Equal("Nova 3", persona!.Name);
    }

    [Trait("Req", "PR-HYDRA-020")] // DroppyCode/Core/Models/Hydra.swift:284-292
    [Fact]
    public void Persona_named_lowercase_with_no_round_gives_the_bare_persona()
    {
        Assert.Equal("Otto", HydraRoster.Persona("otto")!.Name);
    }

    [Trait("Req", "PR-HYDRA-020")] // DroppyCode/Core/Models/Hydra.swift:284-292
    [Fact]
    public void Persona_named_with_round_exactly_one_takes_the_bare_name()
    {
        Assert.Equal("Otto", HydraRoster.Persona("Otto 1")!.Name);
    }

    [Trait("Req", "PR-HYDRA-020")] // DroppyCode/Core/Models/Hydra.swift:284-292
    [Fact]
    public void Persona_named_off_roster_is_nil()
    {
        Assert.Null(HydraRoster.Persona("Ives"));
    }

    // ---- PR-HYDRA-021: HydraPersona.color decomposes the hex into 0-255 channels divided by 255 ----

    [Trait("Req", "PR-HYDRA-021")] // DroppyCode/Core/Models/Hydra.swift:218-224
    [Fact]
    public void Color_decomposes_the_hex_into_three_channels_divided_by_255()
    {
        var persona = new HydraPersona("Hank", "hydra-head-01", 0xFF8A3D);

        var (red, green, blue, alpha) = persona.Color;

        Assert.Equal(0xFF / 255.0, red);
        Assert.Equal(0x8A / 255.0, green);
        Assert.Equal(0x3D / 255.0, blue);
        Assert.Equal(1.0, alpha);
    }

    // ---- PR-HYDRA-022: HydraHeadInfo persists 24 fields and decodes an unreadable status as stopped ----

    [Trait("Req", "PR-HYDRA-022")] // DroppyCode/Core/Models/Hydra.swift:323-433
    [Fact]
    public void A_head_info_record_with_no_stored_fields_decodes_to_every_in_source_default()
    {
        var info = Coding.Decode<HydraHeadInfo>("{}"u8.ToArray())!;

        Assert.Equal(0, info.Index);
        Assert.Equal("", info.Task);
        Assert.Equal(HydraHeadInfo.Kind.Droppy, info.HydraHeadInfoKind);
        Assert.Equal(HydraHeadInfo.Origin.Delegated, info.HydraHeadInfoOrigin);
        Assert.Equal(HydraHeadInfo.Status.Stopped, info.HydraHeadInfoStatus);
        Assert.Null(info.Summary);
        Assert.Null(info.Activity);
        Assert.Equal(0, info.ToolCalls);
        Assert.Equal(0, info.Tokens);
        Assert.Null(info.FinishedAt);
        Assert.Null(info.QueuedAt);
        Assert.Null(info.ExecutionStartedAt);
        Assert.Null(info.ExecutionFinishedAt);
        Assert.Null(info.LandingStartedAt);
        Assert.Null(info.LandingFinishedAt);
        Assert.Null(info.NativeID);
        Assert.Null(info.NativeTaskID);
        Assert.Null(info.ToolUseID);
        Assert.Null(info.BatchID);
        Assert.Null(info.Profile);
        // canStop defaults to kind == .droppy (:428), and kind defaulted to .droppy above.
        Assert.True(info.CanStop);
        Assert.True(info.IsBackground);
        Assert.Null(info.BaseTree);
        Assert.Null(info.Landing);
        Assert.Null(info.MergedAt);
    }

    [Trait("Req", "PR-HYDRA-022")] // DroppyCode/Core/Models/Hydra.swift:398-433
    [Fact]
    public void An_unreadable_status_string_decodes_as_stopped_never_completed()
    {
        var json = """{"index":1,"task":"x","kind":"droppy","origin":"delegated","status":"unknown-from-a-newer-build"}"""u8.ToArray();

        var info = Coding.Decode<HydraHeadInfo>(json)!;

        Assert.Equal(HydraHeadInfo.Status.Stopped, info.HydraHeadInfoStatus);
    }

    [Trait("Req", "PR-HYDRA-022")] // DroppyCode/Core/Models/Hydra.swift:368, :426-428
    [Fact]
    public void CanStop_defaults_to_false_for_a_native_head_with_no_stored_canStop()
    {
        var json = """{"kind":"native"}"""u8.ToArray();

        var info = Coding.Decode<HydraHeadInfo>(json)!;

        Assert.False(info.CanStop);
    }

    [Trait("Req", "PR-HYDRA-022")] // DroppyCode/Core/Models/Hydra.swift:297-389
    [Fact]
    public void A_fully_populated_head_info_round_trips_every_stored_field_with_a_persisted_canStop()
    {
        using var runtime = new TestRuntime();

        var info = new HydraHeadInfo(3, "task text", HydraHeadInfo.Kind.Native, HydraHeadInfo.Origin.Queued) with
        {
            HydraHeadInfoStatus = HydraHeadInfo.Status.Completed,
            Summary = "summary",
            Activity = "activity",
            ToolCalls = 5,
            Tokens = 100,
            FinishedAt = SwiftDate.Now,
            QueuedAt = SwiftDate.Now,
            ExecutionStartedAt = SwiftDate.Now,
            ExecutionFinishedAt = SwiftDate.Now,
            LandingStartedAt = SwiftDate.Now,
            LandingFinishedAt = SwiftDate.Now,
            NativeID = "native-id",
            NativeTaskID = "native-task-id",
            ToolUseID = "tool-use-id",
            BatchID = Guid.NewGuid(),
            Profile = "deep",
            CanStop = true,
            IsBackground = false,
            BaseTree = "abc123",
            Landing = new HydraLanding
            {
                Files = new ValueList<HydraLanding.File>([new HydraLanding.File { Path = "a.txt", Additions = 1, Deletions = 0 }]),
                Conflicts = new ValueList<string>(["b.txt"]),
                DroppedBuildOutputFiles = 2,
                PatchPath = "patch.diff",
                Error = "oops",
            },
            MergedAt = SwiftDate.Now,
        };

        var decoded = Coding.Decode<HydraHeadInfo>(Coding.Encode(info))!;

        Assert.Equal(info, decoded);
    }

    // ---- PR-HYDRA-023: Kind, Origin and Status have exactly these raw values, and isFinished is status != .running ----

    [Trait("Req", "PR-HYDRA-023")] // DroppyCode/Core/Models/Hydra.swift:300-320
    [Fact]
    public void Kind_origin_and_status_have_exactly_these_raw_values()
    {
        Assert.Equal("native", SwiftEnum<HydraHeadInfo.Kind>.RawValue(HydraHeadInfo.Kind.Native));
        Assert.Equal("droppy", SwiftEnum<HydraHeadInfo.Kind>.RawValue(HydraHeadInfo.Kind.Droppy));

        Assert.Equal("delegated", SwiftEnum<HydraHeadInfo.Origin>.RawValue(HydraHeadInfo.Origin.Delegated));
        Assert.Equal("queued", SwiftEnum<HydraHeadInfo.Origin>.RawValue(HydraHeadInfo.Origin.Queued));
        Assert.Equal("sent", SwiftEnum<HydraHeadInfo.Origin>.RawValue(HydraHeadInfo.Origin.Sent));

        Assert.Equal("running", SwiftEnum<HydraHeadInfo.Status>.RawValue(HydraHeadInfo.Status.Running));
        Assert.Equal("completed", SwiftEnum<HydraHeadInfo.Status>.RawValue(HydraHeadInfo.Status.Completed));
        Assert.Equal("failed", SwiftEnum<HydraHeadInfo.Status>.RawValue(HydraHeadInfo.Status.Failed));
        Assert.Equal("stopped", SwiftEnum<HydraHeadInfo.Status>.RawValue(HydraHeadInfo.Status.Stopped));
    }

    [Trait("Req", "PR-HYDRA-023")] // DroppyCode/Core/Models/Hydra.swift:319
    [Fact]
    public void IsFinished_is_true_for_every_status_but_running()
    {
        Assert.False(HydraHeadInfo.Status.Running.IsFinished);
        Assert.True(HydraHeadInfo.Status.Completed.IsFinished);
        Assert.True(HydraHeadInfo.Status.Failed.IsFinished);
        Assert.True(HydraHeadInfo.Status.Stopped.IsFinished);
    }

    // ---- PR-HYDRA-024: HydraHeadInfo derives persona, displayName, isFinished and hasOwnCopy ----

    [Trait("Req", "PR-HYDRA-024")] // DroppyCode/Core/Models/Hydra.swift:384-389
    [Fact]
    public void Persona_and_displayName_are_recomputed_from_the_stored_index()
    {
        var info = new HydraHeadInfo(25, "task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated);

        Assert.Equal("Hank 2", info.Persona.Name);
        Assert.Equal("Hank 2", info.DisplayName);
    }

    [Trait("Req", "PR-HYDRA-024")] // DroppyCode/Core/Models/Hydra.swift:387
    [Fact]
    public void IsFinished_reflects_the_stored_status()
    {
        var running = new HydraHeadInfo(0, "task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated);
        var stopped = running with { HydraHeadInfoStatus = HydraHeadInfo.Status.Stopped };

        Assert.False(running.IsFinished);
        Assert.True(stopped.IsFinished);
    }

    [Trait("Req", "PR-HYDRA-024")] // DroppyCode/Core/Models/Hydra.swift:389
    [Fact]
    public void HasOwnCopy_is_true_exactly_when_baseTree_is_set()
    {
        var info = new HydraHeadInfo(0, "task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated);

        Assert.False(info.HasOwnCopy);
        Assert.True((info with { BaseTree = "abc123" }).HasOwnCopy);
    }

    // ==== Part 2 of 4 (PR-HYDRA-025 to 052) ====

    // ---- PR-HYDRA-025: HydraMentionTarget's URL scheme is droppycode-head with the thread UUID as host ----

    [Trait("Req", "PR-HYDRA-025")] // DroppyCode/Core/Models/Hydra.swift:439-451
    [Fact]
    public void Url_is_the_scheme_and_the_uppercase_thread_UUID_as_host()
    {
        var id = Guid.Parse("0192a1b2-c3d4-4e5f-8a9b-0c1d2e3f4a5b");
        var target = new HydraMentionTarget { ThreadID = id, Task = "task", Status = HydraHeadInfo.Status.Running };

        Assert.Equal($"droppycode-head://{SwiftUUID.UuidString(id)}", target.Url.AbsoluteString);
        Assert.Equal("droppycode-head", target.Url.Scheme);
    }

    [Trait("Req", "PR-HYDRA-025")] // DroppyCode/Core/Models/Hydra.swift:444
    [Fact]
    public void Scheme_constant_is_droppycode_head()
    {
        Assert.Equal("droppycode-head", HydraMentionTarget.Scheme);
    }

    [Trait("Req", "PR-HYDRA-025")] // DroppyCode/Core/Models/Hydra.swift:447-450
    [Fact]
    public void ThreadIDIn_parses_the_host_of_a_droppycode_head_URL()
    {
        var id = Guid.Parse("0192a1b2-c3d4-4e5f-8a9b-0c1d2e3f4a5b");
        var url = SwiftURL.Create($"droppycode-head://{SwiftUUID.UuidString(id)}")!;

        Assert.Equal(id, HydraMentionTarget.ThreadIDIn(url));
    }

    [Trait("Req", "PR-HYDRA-025")] // DroppyCode/Core/Models/Hydra.swift:448 guard url.scheme == scheme
    [Fact]
    public void ThreadIDIn_is_nil_for_a_different_scheme()
    {
        var url = SwiftURL.Create("https://0192A1B2-C3D4-4E5F-8A9B-0C1D2E3F4A5B")!;

        Assert.Null(HydraMentionTarget.ThreadIDIn(url));
    }

    [Trait("Req", "PR-HYDRA-025")] // DroppyCode/Core/Models/Hydra.swift:449 UUID(uuidString: host)
    [Fact]
    public void ThreadIDIn_is_nil_when_the_host_does_not_parse_as_a_UUID()
    {
        var url = SwiftURL.Create("droppycode-head://not-a-uuid")!;

        Assert.Null(HydraMentionTarget.ThreadIDIn(url));
    }

    // ---- PR-HYDRA-026: HydraLanding.isEmpty and landed are distinct predicates over the same five fields ----

    [Trait("Req", "PR-HYDRA-026")] // DroppyCode/Core/Models/Hydra.swift:473
    [Fact]
    public void IsEmpty_is_true_only_with_no_files_no_patchPath_and_no_error()
    {
        Assert.True(new HydraLanding().IsEmpty);
        Assert.False((new HydraLanding() with { Files = new ValueList<HydraLanding.File>([new HydraLanding.File { Path = "a", Additions = 1, Deletions = 0 }]) }).IsEmpty);
        Assert.False((new HydraLanding() with { PatchPath = "p" }).IsEmpty);
        Assert.False((new HydraLanding() with { Error = "e" }).IsEmpty);
    }

    [Trait("Req", "PR-HYDRA-026")] // DroppyCode/Core/Models/Hydra.swift:475
    [Fact]
    public void Landed_requires_files_and_no_patchPath_and_no_error()
    {
        var withFiles = new HydraLanding() with { Files = new ValueList<HydraLanding.File>([new HydraLanding.File { Path = "a", Additions = 1, Deletions = 0 }]) };

        Assert.True(withFiles.Landed);
        Assert.False(new HydraLanding().Landed);
        Assert.False((withFiles with { PatchPath = "p" }).Landed);
        Assert.False((withFiles with { Error = "e" }).Landed);
    }

    [Trait("Req", "PR-HYDRA-026")] // DroppyCode/Core/Models/Hydra.swift:463-470 a landing with conflicts still lands
    [Fact]
    public void A_landing_with_conflicts_still_counts_as_landed()
    {
        var landing = new HydraLanding()
        {
            Files = new ValueList<HydraLanding.File>([new HydraLanding.File { Path = "a", Additions = 1, Deletions = 0 }]),
            Conflicts = new ValueList<string>(["a"]),
        };

        Assert.True(landing.Landed);
        Assert.False(landing.IsEmpty);
    }

    [Trait("Req", "PR-HYDRA-026")] // DroppyCode/Core/Models/Hydra.swift:469-470 both patchPath and error set
    [Fact]
    public void A_landing_with_both_a_patchPath_and_an_error_is_neither_empty_nor_landed()
    {
        var landing = new HydraLanding()
        {
            Files = new ValueList<HydraLanding.File>([new HydraLanding.File { Path = "a", Additions = 1, Deletions = 0 }]),
            PatchPath = "p",
            Error = "e",
        };

        Assert.False(landing.IsEmpty);
        Assert.False(landing.Landed);
    }

    // ---- PR-HYDRA-027: HydraDelegation carries exactly five fields ----

    [Trait("Req", "PR-HYDRA-027")] // DroppyCode/Core/Models/Hydra.swift:493-504
    [Fact]
    public void HydraDelegation_holds_task_prompt_and_three_optional_fields()
    {
        var delegation = new HydraDelegation { Task = "t", Prompt = "p" };

        Assert.Equal("t", delegation.Task);
        Assert.Equal("p", delegation.Prompt);
        Assert.Null(delegation.Name);
        Assert.Null(delegation.Project);
        Assert.Null(delegation.Profile);

        var named = delegation with { Name = "Otto", Project = "gaze-site", Profile = "deep" };
        Assert.Equal("Otto", named.Name);
        Assert.Equal("gaze-site", named.Project);
        Assert.Equal("deep", named.Profile);
    }

    // ---- PR-HYDRA-028: HydraMergeStage has exactly seven cases with these exact words in order ----

    [Trait("Req", "PR-HYDRA-028")] // DroppyCode/Core/Models/Hydra.swift:509-530
    [Trait("Req", "UI-HYDRAMERGE-003")] // DroppyCode/Core/Models/Hydra.swift:509-530 the merge pill's "words" text (its geometry/typography/colors/transitions/shimmer are TimelineRows.swift, a UI unit, untestable here)
    [Fact]
    public void Words_are_exactly_the_seven_stage_sentences_in_order()
    {
        Assert.Equal(
            [
                "Gathering the team's files", // SOURCE FACT: Hydra.swift:521 bytes 74 65 61 6d 27 73 - ASCII apostrophe U+0027 (od -c)
                "Writing the commit",
                "Writing the commit message",
                "Pushing the branch",
                "Opening the merge request",
                "Merging",
                "Bringing the checkout up to date",
            ],
            HydraMergeStageExtensions.AllCases.Select(stage => stage.Words).ToArray());
    }

    [Trait("Req", "PR-HYDRA-028")] // DroppyCode/Core/Models/Hydra.swift:511-517 declaration order
    [Fact]
    public void AllCases_is_the_seven_stages_in_declaration_order()
    {
        Assert.Equal(
            [
                HydraMergeStage.Gathering, HydraMergeStage.Committing, HydraMergeStage.Describing,
                HydraMergeStage.Pushing, HydraMergeStage.Opening, HydraMergeStage.Merging, HydraMergeStage.Syncing,
            ],
            HydraMergeStageExtensions.AllCases);
    }

    // ---- PR-HYDRA-031: HydraPrompts.HowToWork is five newline-separated bullets, no trailing newline ----

    [Trait("Req", "PR-HYDRA-031")] // DroppyCode/Core/Models/Hydra.swift:674-680
    [Fact]
    public void HowToWork_is_exactly_five_bullets_joined_by_single_newlines_with_no_trailing_newline()
    {
        var expected = string.Join("\n",
        [
            "- Start on the task right away: read what you need and no more, then make the change.",
            "- A brief that names the file, the line and the change needs no second reading: make that edit first, then the next. Think briefly between tool calls; if you find yourself weighing designs for minutes, take the simplest reading of the brief and do it, and mention the choice in your report.",
            "- Do exactly this task and only this. Do not widen it, do not touch files it does not name unless it cannot be done otherwise, and never revert, reformat or clean up work that is not yours.",
            "- Never run a build, a test, a lint, a type check or any other check of your work, and never wait on one: the lead runs every check itself, in its own checkout. Make the edits your brief names and report.",
            "- If something blocks you, stop and say so instead of guessing or working around it.",
        ]);

        Assert.Equal(HydraPrompts.HowToWork, expected);
        Assert.False(HydraPrompts.HowToWork.EndsWith('\n'));
    }

    // ---- PR-HYDRA-032: HydraPrompts.HowToReport is one exact paragraph used in three places ----

    [Trait("Req", "PR-HYDRA-032")] // DroppyCode/Core/Models/Hydra.swift:682
    [Fact]
    public void HowToReport_is_the_exact_paragraph()
    {
        Assert.Equal(
            "Report the result, the files you changed and material blockers once, in a few lines the lead can act on: what changed, where, and anything still open or uncertain. Name the caveats and the unfinished work plainly rather than leaving them out. No preamble, no restating the brief, no logs. The lead handles validation.",
            HydraPrompts.HowToReport);
    }

    [Trait("Req", "PR-HYDRA-032")] // DroppyCode/Core/Models/Hydra.swift:796 workerPrompt appends it after a blank line
    [Fact]
    public void WorkerPrompt_appends_HowToReport_after_a_blank_line()
    {
        Assert.EndsWith("\n\n" + HydraPrompts.HowToReport, HydraPrompts.WorkerPrompt, StringComparison.Ordinal);
    }

    // ---- PR-HYDRA-033: HydraPrompts.BriefRule is one exact sentence group ----

    [Trait("Req", "PR-HYDRA-033")] // DroppyCode/Core/Models/Hydra.swift:688
    [Fact]
    public void BriefRule_is_the_exact_text()
    {
        Assert.Equal(
            "Write each brief as numbered, executable steps naming the file, symbol or line anchor, and exact change. Resolve design choices first; ask no questions and offer no alternatives. Give enough context to begin editing within a few tool calls.",
            HydraPrompts.BriefRule);
    }

    // ---- PR-HYDRA-034: HydraPrompts.VerificationRule is one exact sentence group ----

    [Trait("Req", "PR-HYDRA-034")] // DroppyCode/Core/Models/Hydra.swift:692
    [Fact]
    public void VerificationRule_is_the_exact_text()
    {
        Assert.Equal(
            "Heads edit or research and report; they never build, test, lint or verify. Do not assign checks or request pass reports. Run required validation yourself in the checkout after the heads return.",
            HydraPrompts.VerificationRule);
    }

    // ---- PR-HYDRA-035: HydraPrompts.ProjectRule names this chat's project and lists the others ----

    [Trait("Req", "PR-HYDRA-035")] // DroppyCode/Core/Models/Hydra.swift:698-703
    [Fact]
    public void ProjectRule_with_no_projects_uses_the_bare_this_chats_project()
    {
        var text = HydraPrompts.ProjectRule([]);

        Assert.StartsWith("Heads may work in any project. A head works in this chat's project unless it is sent to another:", text, StringComparison.Ordinal);
        Assert.Contains("which Nth then adds to the sidebar), and a head spawned with your own agent tool is told the project's folder in its prompt. Whatever a head changes lands", text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-035")] // DroppyCode/Core/Models/Hydra.swift:699 the own clause with one project
    [Fact]
    public void ProjectRule_with_one_project_names_it_and_has_no_named_others_clause()
    {
        var text = HydraPrompts.ProjectRule([new HydraProjectRef { Name = "gaze-site", Path = "/repos/gaze-site" }]);

        Assert.StartsWith("Heads may work in any project. A head works in this chat's project, gaze-site (`/repos/gaze-site`) unless it is sent to another:", text, StringComparison.Ordinal);
        Assert.Contains("in its prompt. Whatever a head changes lands", text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-035")] // DroppyCode/Core/Models/Hydra.swift:700-701 named others clause
    [Fact]
    public void ProjectRule_with_several_projects_lists_the_others_after_the_first()
    {
        var text = HydraPrompts.ProjectRule([
            new HydraProjectRef { Name = "own", Path = "/r/own" },
            new HydraProjectRef { Name = "b", Path = "/r/b" },
            new HydraProjectRef { Name = "c", Path = "/r/c" },
        ]);

        Assert.Contains("in its prompt. The other projects in the sidebar: b (`/r/b`), c (`/r/c`). Whatever a head changes lands", text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-035")] // DroppyCode/Core/Models/Hydra.swift:702 full exact text with no other projects
    [Fact]
    public void ProjectRule_full_text_with_no_projects_is_exact()
    {
        Assert.Equal(
            "Heads may work in any project. A head works in this chat's project unless it is sent to another: an entry of the delegation block names it with \"project\" (a sidebar project's name or path, or the absolute path of any git repository on this Mac, which Nth then adds to the sidebar), and a head spawned with your own agent tool is told the project's folder in its prompt. Whatever a head changes lands in the checkout of the project it worked in, and the team's work is merged per project, one merge request each. Your own tools run in this chat's checkout, and you may read and edit files in any project all the same: what you change elsewhere is merged along with that project's work. Run your own checks in the checkout itself: never make a worktree or a copy of the project for them.",
            HydraPrompts.ProjectRule([]));
    }

    // ---- PR-HYDRA-036: HydraPrompts.AutoMergeRule is one exact paragraph ----

    [Trait("Req", "PR-HYDRA-036")] // DroppyCode/Core/Models/Hydra.swift:708 (B3 52bc792)
    [Fact]
    public void AutoMergeRule_is_the_exact_text_with_Marrow_in_place_of_Droppy_Code() // DEVIATION OD-0012 F1
    {
        // B3 adds "if it says Marrow retries by itself..." and "A merge never waits on another chat or session...".
        Assert.Equal(
            "Nth handles branches, commits and merge requests after all heads return and you finish. Never run or delegate those operations. For merge/status questions, use only the current [Hydra] note: if merged, give its link; if files remain, say they land when your answer ends; if it says Nth retries by itself, say the work lands by itself and ask nothing; if merging failed or files are outside every project, give the stated reason and required user action. A merge never waits on another chat or session: never say it does, and never ask the user to merge. If nothing is pending, promise no merge. Never claim a merge without the note confirming it.",
            HydraPrompts.AutoMergeRule);
    }

    // ---- PR-HYDRA-037: HydraPrompts.ReviewRule is one exact paragraph ----

    [Trait("Req", "PR-HYDRA-037")] // DroppyCode/Core/Models/Hydra.swift:713
    [Fact]
    public void ReviewRule_is_the_exact_text()
    {
        Assert.Equal(
            "Read every changed file named in the reports and its affected callers. Correct integration issues yourself, run the narrowest required build in the checkout, and disclose your corrections. Never delegate this review.",
            HydraPrompts.ReviewRule);
    }

    // ---- PR-HYDRA-038: HydraPrompts.ReportStyleRule is always present, unconditional ----

    [Trait("Req", "PR-HYDRA-038")] // DroppyCode/Core/Models/Hydra.swift:718
    [Fact]
    public void ReportStyleRule_is_the_exact_text()
    {
        Assert.Equal(
            "For each head, give one plain sentence starting with its name and describing the user-visible result; add the file details only when useful, and keep the whole account bounded to a line or two per head. Omit repeated summaries, audit jargon and decorative symbols. Say plainly what you still need from the user and what you settled or fixed yourself.",
            HydraPrompts.ReportStyleRule);
    }

    // ---- PR-HYDRA-039: workplaceRules(.ownCopy) is two exact lines separated by a single newline ----

    [Trait("Req", "PR-HYDRA-039")] // DroppyCode/Core/Models/Hydra.swift:659-665
    [Fact]
    public void WorkplaceRules_ownCopy_is_two_lines_with_the_path_interpolated_and_Marrow_in_place_of_Droppy_Code() // DEVIATION OD-0012 F1
    {
        var text = HydraPrompts.WorkplaceRules(new HydraPrompts.Workplace.OwnCopy(@"C:\worktrees\otto"));

        var expected = string.Join("\n",
        [
            @"You have your own copy of the project at C:\worktrees\otto: a git worktree Nth made for you from the project's checkout as it was when you were sent out, uncommitted work included. Work in it directly, on the files as they are; your tools already run there. When you report, Nth carries your changes into that checkout itself. So never commit, branch, stash, push, check out, reset, restore or clean anything, and never make or remove worktrees, whatever the project's own guidelines say about agents and worktrees: this copy already is yours. Do not use git to check your work either.",
            "Never build, test or verify anything in your copy: the lead runs every check itself, in its own checkout. Anything under the project folder that git does not ignore lands in the lead's checkout, so leave no build output there.",
        ]);

        Assert.Equal(expected, text);
    }

    // ---- PR-HYDRA-040: workplaceRules(.shared) is one exact line ----

    [Trait("Req", "PR-HYDRA-040")] // DroppyCode/Core/Models/Hydra.swift:666-669
    [Fact]
    public void WorkplaceRules_shared_is_one_line_with_the_path_interpolated()
    {
        var text = HydraPrompts.WorkplaceRules(new HydraPrompts.Workplace.Shared(@"C:\repos\gaze-site"));

        Assert.Equal(
            @"You work in the lead's checkout at C:\repos\gaze-site, alongside the lead and the other heads, who are changing other files at the same time. Changes you did not make are expected there: leave every one of them alone, never stash, check out, reset, restore or clean anything, and never move work into a branch or worktree, whatever the project's own guidelines say about agents and worktrees. Do not use git status or git diff to check your work: they show everyone's changes, and the lead does the verifying.",
            text);
    }

    // ---- PR-HYDRA-043: nativeRoutableProfiles keeps only profiles on the heads' own provider tuning something ----

    [Trait("Req", "PR-HYDRA-043")] // DroppyCode/Core/Models/Hydra.swift:642-647
    [Fact]
    public void NativeRoutableProfiles_keeps_only_profiles_matching_provider_and_tuning_something()
    {
        var launch = new HydraLaunch { HeadsProvider = ProviderKind.Claude, RunsNatively = true };
        var noProvider = new HydraHeadProfile("no-provider") with { Model = "opus" };            // kept: provider nil
        var sameProvider = new HydraHeadProfile("same") with { Provider = ProviderKind.Claude, Effort = "high" }; // kept
        var otherProvider = new HydraHeadProfile("other") with { Provider = ProviderKind.Codex, Model = "gpt" }; // dropped: elsewhere
        var untuned = new HydraHeadProfile("plain") with { Provider = ProviderKind.Claude };      // dropped: tunes nothing
        launch = launch with { HeadProfiles = new ValueList<HydraHeadProfile>([noProvider, sameProvider, otherProvider, untuned]) };

        var kept = HydraPrompts.NativeRoutableProfiles(launch);

        Assert.Equal([noProvider, sameProvider], kept);
    }

    [Trait("Req", "PR-HYDRA-043")] // DroppyCode/Core/Models/Hydra.swift:642-647 empty result
    [Fact]
    public void NativeRoutableProfiles_is_empty_when_nothing_qualifies()
    {
        var launch = new HydraLaunch { HeadsProvider = ProviderKind.Claude, RunsNatively = true };

        Assert.Empty(HydraPrompts.NativeRoutableProfiles(launch));
    }

    // ---- PR-HYDRA-044: the two base agent names and the per-profile variant name ----

    [Trait("Req", "PR-HYDRA-044")] // DroppyCode/Core/Models/Hydra.swift:621-622
    [Fact]
    public void WorkerAgentName_and_ScoutAgentName_are_the_two_exact_literals()
    {
        Assert.Equal("droppy-worker", HydraPrompts.WorkerAgentName);
        Assert.Equal("droppy-scout", HydraPrompts.ScoutAgentName);
    }

    [Trait("Req", "PR-HYDRA-044")] // DroppyCode/Core/Models/Hydra.swift:627-629
    [Fact]
    public void WorkerAgentNameFor_appends_the_profiles_agent_slug()
    {
        var deep = new HydraHeadProfile("deep");
        var deepWork = new HydraHeadProfile("Deep Work");

        Assert.Equal("droppy-worker-deep", HydraPrompts.WorkerAgentNameFor(deep));
        Assert.Equal("droppy-worker-deep-work", HydraPrompts.WorkerAgentNameFor(deepWork));
    }

    // ---- PR-HYDRA-045: profileName(forAgentName:) routes only droppy-worker- prefixed names ----

    [Trait("Req", "PR-HYDRA-045")] // DroppyCode/Core/Models/Hydra.swift:633-636
    [Fact]
    public void ProfileName_extracts_the_slug_after_the_worker_prefix()
    {
        Assert.Equal("deep", HydraPrompts.ProfileName("droppy-worker-deep"));
    }

    [Trait("Req", "PR-HYDRA-045")] // DroppyCode/Core/Models/Hydra.swift:634 the base names and nil route nowhere
    [Fact]
    public void ProfileName_is_nil_for_the_base_names_other_names_and_nil()
    {
        Assert.Null(HydraPrompts.ProfileName("droppy-worker"));
        Assert.Null(HydraPrompts.ProfileName("droppy-scout"));
        Assert.Null(HydraPrompts.ProfileName("something-else"));
        Assert.Null(HydraPrompts.ProfileName(null));
    }

    // ---- PR-HYDRA-046, 047, 048, 049, 050: the native lead policy body, per-provider spawn/wait text and profileRule ----

    private static HydraLaunch NativeLaunch(ProviderKind provider, long? maxHeads = null, bool autoMerges = false,
        bool reviewsHeads = false, IReadOnlyList<HydraProjectRef>? projects = null, IReadOnlyList<HydraHeadProfile>? headProfiles = null) =>
        new()
        {
            HeadsProvider = provider,
            RunsNatively = true,
            MaxHeads = maxHeads,
            AutoMerges = autoMerges,
            ReviewsHeads = reviewsHeads,
            Projects = new ValueList<HydraProjectRef>(projects ?? []),
            HeadProfiles = new ValueList<HydraHeadProfile>(headProfiles ?? []),
        };

    [Trait("Req", "PR-HYDRA-046")] // DroppyCode/Core/Models/Hydra.swift:722-790
    [Fact]
    public void Policy_starts_with_the_Hydra_heading_and_the_howToSpawn_and_Marrow_sentence()
    {
        var text = HydraPrompts.Policy(ProviderKind.Claude, NativeLaunch(ProviderKind.Claude));

        Assert.StartsWith("# Hydra\n\nThe user switched on Hydra for this chat:", text, StringComparison.Ordinal);
        Assert.Contains("unless asked. Two agent types are yours: `droppy-worker` (edits files, runs commands) and `droppy-scout` (read-only research). Spawn them with the Agent tool and prefer them over other agents while Hydra is on: they run on the model and effort the user chose for heads. The agents you spawn this way are the Hydra heads themselves: Nth shows each one in the team's panel", text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-046")] // DroppyCode/Core/Models/Hydra.swift:774-778 the fixed delegate bullets
    [Fact]
    public void Policy_delegate_bullets_include_briefRule_verificationRule_and_projectRule_verbatim()
    {
        var text = HydraPrompts.Policy(ProviderKind.Claude, NativeLaunch(ProviderKind.Claude));

        Assert.Contains($"- {HydraPrompts.BriefRule}\n", text, StringComparison.Ordinal);
        Assert.Contains($"- {HydraPrompts.VerificationRule}\n", text, StringComparison.Ordinal);
        Assert.Contains($"- {HydraPrompts.ProjectRule([])}\n", text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-046")] // DroppyCode/Core/Models/Hydra.swift:780 roster order sentence, Marrow named
    [Fact]
    public void Policy_names_Marrow_in_the_roster_order_sentence()
    {
        var text = HydraPrompts.Policy(ProviderKind.Claude, NativeLaunch(ProviderKind.Claude));

        Assert.Contains("Nth names the heads in roster order (Hank, Walter, Ada, Otto, Nova, Remy, Iris, Milo, Juno, Ezra, Lena, Bo, Kai, Vera, Finn, Mira, Odin, Suki, Rex, Zola, Pip, Ivo, Lux, Tova, Gus, then Hank 2 and so on)", text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-047")] // DroppyCode/Core/Models/Hydra.swift:782 the reproduced contradiction, upstream defect
    [Fact]
    public void Policy_tells_the_lead_heads_never_go_out_through_a_fenced_block_and_that_Marrow_sends_them_that_way_anyway()
    {
        var text = HydraPrompts.Policy(ProviderKind.Claude, NativeLaunch(ProviderKind.Claude));

        Assert.Contains(
            "- Heads go out through the tools above, never through a fenced hydra block: that is the delegation format for providers without agent tools of their own. If you end a reply with one anyway, Nth still sends those heads out as threads of their own, but your turn ends there and their reports come back as a later message.",
            text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-048")] // DroppyCode/Core/Models/Hydra.swift:754 claude howToSpawn
    [Fact]
    public void HowToSpawn_on_Claude_names_the_two_agents_and_the_Agent_tool()
    {
        var text = HydraPrompts.Policy(ProviderKind.Claude, NativeLaunch(ProviderKind.Claude));

        Assert.Contains("Two agent types are yours: `droppy-worker` (edits files, runs commands) and `droppy-scout` (read-only research). Spawn them with the Agent tool and prefer them over other agents while Hydra is on: they run on the model and effort the user chose for heads.", text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-048")] // DroppyCode/Core/Models/Hydra.swift:757 codex howToSpawn names worker/explorer not droppy-
    [Fact]
    public void HowToSpawn_on_Codex_names_worker_and_explorer_not_the_droppy_prefixed_agents()
    {
        var text = HydraPrompts.Policy(ProviderKind.Codex, NativeLaunch(ProviderKind.Codex));

        Assert.Contains("Spawn heads with `spawn_agent`: the `worker` agent for anything that edits files or runs commands, the `explorer` agent for read-only research.", text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-048")] // DroppyCode/Core/Models/Hydra.swift:760 copilot howToSpawn names the task tool
    [Fact]
    public void HowToSpawn_on_Copilot_names_the_task_tool()
    {
        var text = HydraPrompts.Policy(ProviderKind.Copilot, NativeLaunch(ProviderKind.Copilot));

        Assert.Contains("Two agents are yours: `droppy-worker` (edits files, runs commands) and `droppy-scout` (read-only research). Start them with the task tool and prefer them over other agents while Hydra is on: they run on the model and effort the user chose for heads.", text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-049")] // DroppyCode/Core/Models/Hydra.swift:755 claude howToWait with and without a cap
    [Fact]
    public void HowToWait_on_Claude_differs_with_and_without_a_cap()
    {
        var capped = HydraPrompts.Policy(ProviderKind.Claude, NativeLaunch(ProviderKind.Claude, maxHeads: 6));
        var uncapped = HydraPrompts.Policy(ProviderKind.Claude, NativeLaunch(ProviderKind.Claude));

        Assert.Contains("- Launch every head for a job in one message so they run in parallel, in the foreground, and run at most 6 at once.\n", capped, StringComparison.Ordinal);
        Assert.Contains("- Launch every head for a job in one message so they run in parallel, in the foreground.\n", uncapped, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-049")] // DroppyCode/Core/Models/Hydra.swift:758 codex howToWait with and without a cap
    [Fact]
    public void HowToWait_on_Codex_differs_with_and_without_a_cap()
    {
        var capped = HydraPrompts.Policy(ProviderKind.Codex, NativeLaunch(ProviderKind.Codex, maxHeads: 6));
        var uncapped = HydraPrompts.Policy(ProviderKind.Codex, NativeLaunch(ProviderKind.Codex));

        Assert.Contains("- Spawn every head for a job before waiting, so they run in parallel, and collect them with `wait_agent`; never leave a head running when you answer. Run at most 6 at once.\n", capped, StringComparison.Ordinal);
        Assert.Contains("- Spawn every head for a job before waiting, so they run in parallel, and collect them with `wait_agent`; never leave a head running when you answer.\n", uncapped, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-049")] // DroppyCode/Core/Models/Hydra.swift:761 copilot howToWait with and without a cap
    [Fact]
    public void HowToWait_on_Copilot_differs_with_and_without_a_cap()
    {
        var capped = HydraPrompts.Policy(ProviderKind.Copilot, NativeLaunch(ProviderKind.Copilot, maxHeads: 6));
        var uncapped = HydraPrompts.Policy(ProviderKind.Copilot, NativeLaunch(ProviderKind.Copilot));

        Assert.Contains("- Start every head for a job at once so they run in parallel, and run at most 6 at a time.\n", capped, StringComparison.Ordinal);
        Assert.Contains("- Start every head for a job at once so they run in parallel.\n", uncapped, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-050")] // DroppyCode/Core/Models/Hydra.swift:735-737 claude/copilot agent-variant profileRule
    [Fact]
    public void ProfileRule_on_Claude_and_Copilot_routes_by_agent_variant()
    {
        var deep = new HydraHeadProfile("deep") with { Model = "claude-opus-5" };
        var quick = new HydraHeadProfile("quick") with { Effort = "low" };
        var launch = NativeLaunch(ProviderKind.Claude, headProfiles: [deep, quick]);

        var text = HydraPrompts.Policy(ProviderKind.Claude, launch);

        Assert.Contains(
            " Some heads are tuned for a purpose: route the task to `droppy-worker-deep` for deep, `droppy-worker-quick` for quick instead of the plain `droppy-worker` when the work belongs to that purpose.",
            text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-050")] // DroppyCode/Core/Models/Hydra.swift:738-745 codex knob-passing profileRule
    [Fact]
    public void ProfileRule_on_Codex_routes_by_passing_the_profiles_own_model_and_effort_knobs()
    {
        var deep = new HydraHeadProfile("deep") with { Model = "claude-opus-5", Effort = "high" };
        var launch = NativeLaunch(ProviderKind.Codex, headProfiles: [deep]);

        var text = HydraPrompts.Policy(ProviderKind.Codex, launch);

        Assert.Contains(
            " Some heads are tuned for a purpose: route the task by passing the profile's own knobs in `spawn_agent` \u2014 deep (pass `model` = claude-opus-5 and `reasoning_effort` = high) \u2014 when the work belongs to that purpose.",
            text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-050")] // DroppyCode/Core/Models/Hydra.swift:731-732 empty result is the empty string
    [Fact]
    public void ProfileRule_is_empty_with_no_routable_profiles()
    {
        var text = HydraPrompts.Policy(ProviderKind.Claude, NativeLaunch(ProviderKind.Claude));

        Assert.Contains("they run on the model and effort the user chose for heads. The agents you spawn this way are", text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-046")] // DroppyCode/Core/Models/Hydra.swift:787 the reviewsHeads bullet, conditionally inserted
    [Fact]
    public void Policy_inserts_the_review_bullet_only_when_reviewsHeads_is_true()
    {
        var without = HydraPrompts.Policy(ProviderKind.Claude, NativeLaunch(ProviderKind.Claude));
        var with = HydraPrompts.Policy(ProviderKind.Claude, NativeLaunch(ProviderKind.Claude, reviewsHeads: true));

        Assert.DoesNotContain(HydraPrompts.ReviewRule, without, StringComparison.Ordinal);
        Assert.Contains($"- {HydraPrompts.ReviewRule}\n- Then finish the job:", with, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-046")] // DroppyCode/Core/Models/Hydra.swift:788 the autoMerges block, conditionally appended
    [Fact]
    public void Policy_appends_the_autoMergeRule_block_only_when_autoMerges_is_true_and_still_ends_with_a_newline_otherwise()
    {
        var without = HydraPrompts.Policy(ProviderKind.Claude, NativeLaunch(ProviderKind.Claude));
        var with = HydraPrompts.Policy(ProviderKind.Claude, NativeLaunch(ProviderKind.Claude, autoMerges: true));

        Assert.EndsWith("\n", without, StringComparison.Ordinal);
        Assert.DoesNotContain(HydraPrompts.AutoMergeRule, without, StringComparison.Ordinal);
        Assert.EndsWith(HydraPrompts.AutoMergeRule + "\n", with, StringComparison.Ordinal);
    }

    // ---- PR-HYDRA-051: claudeAgents emits droppy-worker, droppy-scout and one variant per routable profile ----

    [Trait("Req", "PR-HYDRA-051")] // DroppyCode/Core/Models/Hydra.swift:807-839
    [Fact]
    public void ClaudeAgents_has_the_worker_and_scout_with_their_exact_descriptions_and_no_model_or_effort_by_default()
    {
        var launch = NativeLaunch(ProviderKind.Claude);

        var agents = HydraPrompts.ClaudeAgents(launch);

        var worker = agents["droppy-worker"]!;
        Assert.Equal(
            "Hydra head that implements one delegated task: edits files, runs commands. Use proactively when a request splits into independent pieces or touches several parts of the codebase.",
            worker["description"]!.JSONValueString);
        Assert.Equal(HydraPrompts.WorkerPrompt + "\n\n" + AntiSlopPolicy.Instructions(true), worker["prompt"]!.JSONValueString);
        Assert.Equal(false, worker["background"]!.JSONValueBool);
        Assert.Null(worker["model"]);
        Assert.Null(worker["effort"]);

        var scout = agents["droppy-scout"]!;
        Assert.Equal(
            "Hydra head for read-only research: finds files, reads code, gathers facts and reports back. Use proactively when a job needs many files or sources read.",
            scout["description"]!.JSONValueString);
        Assert.Equal(HydraPrompts.ScoutPrompt + "\n\n" + AntiSlopPolicy.Instructions(true), scout["prompt"]!.JSONValueString);
        Assert.Equal(["Read", "Grep", "Glob", "WebFetch", "WebSearch"], scout["tools"]!.JSONValueArray!.Select(v => v.JSONValueString));
        Assert.Equal(false, scout["background"]!.JSONValueBool);
    }

    [Trait("Req", "PR-HYDRA-051")] // DroppyCode/Core/Models/Hydra.swift:820-827 model/effort on both base agents, "default" sentinel
    [Fact]
    public void ClaudeAgents_adds_model_and_effort_to_both_base_agents_unless_the_model_is_the_default_sentinel()
    {
        var withModel = HydraPrompts.ClaudeAgents(NativeLaunch(ProviderKind.Claude) with { WorkerModel = "claude-opus-5", WorkerEffort = "high" });
        Assert.Equal("claude-opus-5", withModel["droppy-worker"]!["model"]!.JSONValueString);
        Assert.Equal("high", withModel["droppy-worker"]!["effort"]!.JSONValueString);
        Assert.Equal("claude-opus-5", withModel["droppy-scout"]!["model"]!.JSONValueString);

        var defaultModel = HydraPrompts.ClaudeAgents(NativeLaunch(ProviderKind.Claude) with { WorkerModel = "default" });
        Assert.Null(defaultModel["droppy-worker"]!["model"]);
    }

    [Trait("Req", "PR-HYDRA-051")] // DroppyCode/Core/Models/Hydra.swift:831-837 one variant per routable profile
    [Fact]
    public void ClaudeAgents_adds_one_worker_variant_per_routable_profile_with_the_tuned_description()
    {
        var deep = new HydraHeadProfile("deep") with { Model = "claude-opus-5" };
        var launch = NativeLaunch(ProviderKind.Claude, headProfiles: [deep]);

        var agents = HydraPrompts.ClaudeAgents(launch);

        var variant = agents["droppy-worker-deep"]!;
        Assert.Equal(
            "Hydra head that implements one delegated task, tuned as the 'deep' profile. Prefer it for work that belongs to 'deep' over the plain droppy-worker.",
            variant["description"]!.JSONValueString);
        Assert.Equal("claude-opus-5", variant["model"]!.JSONValueString);
        Assert.Null(variant["tools"]); // cloned from the worker, which has no tools key
    }

    // ---- PR-HYDRA-052: copilotAgents emits an array with name/displayName/description/prompt/infer, never tools ----

    [Trait("Req", "PR-HYDRA-052")] // DroppyCode/Core/Models/Hydra.swift:842-888
    [Fact]
    public void CopilotAgents_is_an_array_of_worker_then_scout_with_exact_descriptions_and_no_tools_key()
    {
        var launch = NativeLaunch(ProviderKind.Copilot);

        var agents = HydraPrompts.CopilotAgents(launch);

        Assert.Equal(2, agents.Count);
        var worker = agents[0];
        Assert.Equal("droppy-worker", worker["name"]!.JSONValueString);
        Assert.Equal("Hydra worker", worker["displayName"]!.JSONValueString);
        Assert.Equal(
            "Hydra head that implements one delegated task: edits files, runs commands. Use when a request splits into independent pieces.",
            worker["description"]!.JSONValueString);
        Assert.Equal(HydraPrompts.WorkerPrompt + "\n\n" + AntiSlopPolicy.Instructions(true), worker["prompt"]!.JSONValueString);
        Assert.Equal(true, worker["infer"]!.JSONValueBool);
        Assert.Null(worker["tools"]);

        var scout = agents[1];
        Assert.Equal("droppy-scout", scout["name"]!.JSONValueString);
        Assert.Equal("Hydra scout", scout["displayName"]!.JSONValueString);
        Assert.Equal(
            "Hydra head for read-only research: finds files, reads code and reports back. Use when a job needs many files or sources read.",
            scout["description"]!.JSONValueString);
        Assert.Null(scout["tools"]);
    }

    [Trait("Req", "PR-HYDRA-052")] // DroppyCode/Core/Models/Hydra.swift:853-854 model/reasoningEffort, "auto" sentinel
    [Fact]
    public void CopilotAgents_adds_model_and_reasoningEffort_unless_the_model_is_the_auto_sentinel()
    {
        var withModel = HydraPrompts.CopilotAgents(NativeLaunch(ProviderKind.Copilot) with { WorkerModel = "gpt-5", WorkerEffort = "high" });
        Assert.Equal("gpt-5", withModel[0]["model"]!.JSONValueString);
        Assert.Equal("high", withModel[0]["reasoningEffort"]!.JSONValueString);

        var autoModel = HydraPrompts.CopilotAgents(NativeLaunch(ProviderKind.Copilot) with { WorkerModel = "auto" });
        Assert.Null(autoModel[0]["model"]);
    }

    [Trait("Req", "PR-HYDRA-052")] // DroppyCode/Core/Models/Hydra.swift:878-886 one variant per profile, middle dot display name
    [Fact]
    public void CopilotAgents_appends_one_worker_variant_per_routable_profile_with_a_middle_dot_display_name()
    {
        var deep = new HydraHeadProfile("deep") with { Model = "gpt-5" };
        var launch = NativeLaunch(ProviderKind.Copilot, headProfiles: [deep]);

        var agents = HydraPrompts.CopilotAgents(launch);

        Assert.Equal(3, agents.Count);
        var variant = agents[2];
        Assert.Equal("droppy-worker-deep", variant["name"]!.JSONValueString);
        Assert.Equal("Hydra worker \u00B7 deep", variant["displayName"]!.JSONValueString); // U+00B7 MIDDLE DOT
        Assert.Equal(
            "Hydra head that implements one delegated task, tuned as the 'deep' profile. Prefer it for work that belongs to 'deep' over the plain droppy-worker.",
            variant["description"]!.JSONValueString);
        Assert.Equal("gpt-5", variant["model"]!.JSONValueString);
        Assert.Null(variant["tools"]);
    }

    // ==== Core.Models.Hydra part 3 of 4 (PR-HYDRA-053 to 079, excluding 054's own duplicate coverage) ====
    // Every expected value is transcribed from the cited Swift lines or the record itself (R-TEST-4).

    private static HydraLaunch FallbackLaunch(string? headsLabel = null, long? maxHeads = null, bool isolatesHeads = true,
        bool autoMerges = false, bool reviewsHeads = false, IReadOnlyList<HydraProjectRef>? projects = null,
        IReadOnlyList<HydraHeadProfile>? headProfiles = null) =>
        new()
        {
            HeadsProvider = ProviderKind.Claude,
            RunsNatively = false,
            HeadsLabel = headsLabel,
            MaxHeads = maxHeads,
            IsolatesHeads = isolatesHeads,
            AutoMerges = autoMerges,
            ReviewsHeads = reviewsHeads,
            Projects = new ValueList<HydraProjectRef>(projects ?? []),
            HeadProfiles = new ValueList<HydraHeadProfile>(headProfiles ?? []),
        };

    // ---- PR-HYDRA-053: codexConfig always emits agents and features, no model sentinel at all ----

    [Trait("Req", "PR-HYDRA-053")] // DroppyCode/Core/Models/Hydra.swift:894-904
    [Fact]
    public void CodexConfig_emits_an_empty_agents_object_and_a_features_object_with_no_cap_model_or_effort()
    {
        var config = HydraPrompts.CodexConfig(NativeLaunch(ProviderKind.Codex));

        var agents = Assert.IsType<JSONValue.Object>(config["agents"]);
        Assert.Empty(agents.Value);
        Assert.Equal(true, config["features"]!["multi_agent"]!.JSONValueBool);
        Assert.Null(config["features"]!["multi_agent_v2"]);
    }

    [Trait("Req", "PR-HYDRA-053")] // DroppyCode/Core/Models/Hydra.swift:896-898
    [Fact]
    public void CodexConfig_sets_cap_model_and_effort_keys_only_when_present_and_model_has_no_sentinel_check()
    {
        var launch = NativeLaunch(ProviderKind.Codex, maxHeads: 4) with { WorkerModel = "default", WorkerEffort = "high" };

        var config = HydraPrompts.CodexConfig(launch);

        var agents = Assert.IsType<JSONValue.Object>(config["agents"]);
        Assert.Equal(4, agents.Value["max_concurrent_threads_per_session"]!.JSONValueInt);
        // Unlike Claude/Copilot, Codex's model test is only !model.isEmpty: "default" is NOT a sentinel here.
        Assert.Equal("default", agents.Value["default_subagent_model"]!.JSONValueString);
        Assert.Equal("high", agents.Value["default_subagent_reasoning_effort"]!.JSONValueString);
    }

    [Trait("Req", "PR-HYDRA-053")] // DroppyCode/Core/Models/Hydra.swift:900-902
    [Fact]
    public void CodexConfig_adds_multi_agent_v2_only_when_a_native_routable_profile_exists()
    {
        var deep = new HydraHeadProfile("deep") with { Model = "o1" };
        var launch = NativeLaunch(ProviderKind.Codex, headProfiles: [deep]);

        var config = HydraPrompts.CodexConfig(launch);

        Assert.Equal(true, config["features"]!["multi_agent_v2"]!["expose_spawn_agent_model_overrides"]!.JSONValueBool);
    }

    // ---- PR-HYDRA-054: workerPrompt and scoutPrompt are two exact system prompts (implemented in part 2; proved here) ----

    [Trait("Req", "PR-HYDRA-054")] // DroppyCode/Core/Models/Hydra.swift:793-797
    [Fact]
    public void WorkerPrompt_is_the_exact_two_paragraph_literal_with_Marrow_and_howToReport()
    {
        Assert.Equal(
            "You are a Hydra head in Nth: one of several helpers working in parallel for a lead agent, in the lead's own checkout, where the lead and the other heads are changing other files at the same time. Do exactly the task you were given, and only that: do not widen it, do not touch files it does not name unless the task cannot be done otherwise, and never revert, reformat or clean up work that is not yours. Changes you did not make are expected in the checkout: leave them alone, never stash, check out, reset, restore or clean anything, and never move work into a branch or worktree, whatever the project's own guidelines say about agents and worktrees. Do not use git status or git diff to check your work: they show everyone's changes. Never run a build, a test, a lint or any other check of your work: the lead runs every check itself. Make the edits your brief names and report. If something blocks you, say so instead of guessing.\n\n" +
            HydraPrompts.HowToReport,
            HydraPrompts.WorkerPrompt);
    }

    [Trait("Req", "PR-HYDRA-054")] // DroppyCode/Core/Models/Hydra.swift:800-804
    [Fact]
    public void ScoutPrompt_second_paragraph_is_its_own_text_not_howToReport()
    {
        Assert.Equal(
            "You are a Hydra head in Nth: a read-only researcher working in parallel for a lead agent, in the lead's own checkout, where the lead and other heads are changing files at the same time; uncommitted changes there are theirs and expected. Answer exactly the question you were given, from the code and sources you can read. Change nothing.\n\n" +
            "Reply with a short report the lead can act on: the findings, with file paths and line references, and anything that contradicts what the lead assumed. No preamble.",
            HydraPrompts.ScoutPrompt);
        Assert.DoesNotContain(HydraPrompts.HowToReport, HydraPrompts.ScoutPrompt, StringComparison.Ordinal);
    }

    // ---- PR-HYDRA-056, 057, 058: fallbackPolicy body, its computed pieces and the naming paragraph ----

    [Trait("Req", "PR-HYDRA-056")] // DroppyCode/Core/Models/Hydra.swift:961 nine spaces before "Finish your reply"
    [Fact]
    public void FallbackPolicy_first_paragraph_ends_with_exactly_nine_spaces_before_Finish_your_reply()
    {
        var text = HydraPrompts.FallbackPolicy(null, isolated: true);

        Assert.Contains("not a team of heads.         Finish your reply with one fenced block", text, StringComparison.Ordinal);
        Assert.DoesNotContain("not a team of heads.          Finish", text, StringComparison.Ordinal); // ten spaces
        Assert.DoesNotContain("not a team of heads.        Finish", text, StringComparison.Ordinal); // eight spaces
    }

    [Trait("Req", "PR-HYDRA-056")] // DroppyCode/Core/Models/Hydra.swift:963-965 the embedded fenced hydra example
    [Fact]
    public void FallbackPolicy_embeds_the_fenced_hydra_example_between_blank_lines()
    {
        var text = HydraPrompts.FallbackPolicy(null, isolated: true);

        Assert.Contains(
            "block\n\n```hydra\n[{\"task\": \"short title\", \"prompt\": \"complete, self-contained instructions with the exact files and acceptance criteria\"}]\n```\n\nand stop there",
            text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-056")] // DroppyCode/Core/Models/Hydra.swift:967 the closing paragraph, DEVIATION OD-0012 F1
    [Fact]
    public void FallbackPolicy_closing_paragraph_says_Marrow_not_Droppy_Code_and_appends_conditional_tails_and_reportStyleRule()
    {
        var plain = HydraPrompts.FallbackPolicy(null, isolated: true);
        Assert.Contains("A message that opens with [Hydra] is from Nth, not the user. " + HydraPrompts.ReportStyleRule, plain, StringComparison.Ordinal);

        var both = HydraPrompts.FallbackPolicy(null, isolated: true, autoMerges: true, reviewsHeads: true);
        Assert.Contains(
            "A message that opens with [Hydra] is from Nth, not the user. " + HydraPrompts.ReviewRule + " " + HydraPrompts.AutoMergeRule + " " + HydraPrompts.ReportStyleRule,
            both, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-057")] // DroppyCode/Core/Models/Hydra.swift:942-944 whereHeadsWork
    [Fact]
    public void FallbackPolicy_whereHeadsWork_says_Marrow_lands_its_changes_when_isolated_else_your_checkout()
    {
        var isolated = HydraPrompts.FallbackPolicy(null, isolated: true);
        Assert.Contains("Each head works in a copy of its project of its own and Nth lands its changes in that project's checkout when it reports; heads never see your context", isolated, StringComparison.Ordinal);

        var shared = HydraPrompts.FallbackPolicy(null, isolated: false);
        Assert.Contains("The heads work in your checkout; heads never see your context", shared, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-057")] // DroppyCode/Core/Models/Hydra.swift:945 team, :950-952 whoTheHeadsAre, :955 whatHeadsCanDo, :959 profileRule
    [Fact]
    public void FallbackPolicy_team_whoTheHeadsAre_whatHeadsCanDo_and_profileRule_have_their_exact_forms()
    {
        var capped = HydraPrompts.FallbackPolicy(6, isolated: true);
        Assert.Contains("You lead a team of up to 6 helper agents (\"heads\")", capped, StringComparison.Ordinal);

        var uncapped = HydraPrompts.FallbackPolicy(null, isolated: true);
        Assert.Contains("You lead a team of helper agents (\"heads\")", uncapped, StringComparison.Ordinal);

        var noHeadsLabel = HydraPrompts.FallbackPolicy(null, isolated: true, heads: null);
        Assert.DoesNotContain("Your heads run on", noHeadsLabel, StringComparison.Ordinal);
        // whatHeadsCanDo is unconditional.
        Assert.Contains(" Every head, whatever its provider, can read files, edit files and run commands in its copy, with no permission prompts to wait on; brief it to do the work, not just to describe it.", noHeadsLabel, StringComparison.Ordinal);

        var withLabel = HydraPrompts.FallbackPolicy(null, isolated: true, heads: "GPT-5");
        Assert.Contains(
            " Your heads run on GPT-5, a different model from yours: it is quick, so give each one a well-bounded task with everything it needs written down, and keep the design, the judgement calls and the integration for yourself. The block is the only way to send heads out: never use an agent, task or sub-agent tool of your own, which would run heads on your own model instead of the one the user chose.",
            withLabel, StringComparison.Ordinal);

        var noProfiles = HydraPrompts.FallbackPolicy(null, isolated: true, profiles: []);
        Assert.DoesNotContain("Some heads are tuned for a purpose", noProfiles, StringComparison.Ordinal);

        var deep = new HydraHeadProfile("deep") with { Model = "o1" };
        var withProfiles = HydraPrompts.FallbackPolicy(null, isolated: true, profiles: [deep]);
        Assert.Contains(
            " Some heads are tuned for a purpose: deep (o1). Route a task to one with \"profile\", as in `{\"task\": \"...\", \"prompt\": \"...\", \"profile\": \"deep\"}`; a task that fits none of them goes without a profile and runs on the heads' shared model, as does one whose profile names none of these.",
            withProfiles, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-058")] // DroppyCode/Core/Models/Hydra.swift:967 the naming paragraph, both statements verbatim
    [Fact]
    public void FallbackPolicy_naming_paragraph_states_both_authoritative_names_and_off_roster_fallback_verbatim()
    {
        var text = HydraPrompts.FallbackPolicy(null, isolated: true);

        Assert.Contains(
            "the announced name is authoritative and the spawned head carries exactly it, so repeating the same block spawns the same names.",
            text, StringComparison.Ordinal);
        Assert.Contains(
            "Never invent names outside the roster: there is no head called Ives (the roster has Ivo), and an unknown name falls back to the next head in order rather than renaming anyone.",
            text, StringComparison.Ordinal);
        Assert.Contains("(Hank, Walter, Ada, Otto, Nova, Remy, Iris, Milo, Juno, Ezra, Lena, Bo, Kai, Vera, Finn, Mira, Odin, Suki, Rex, Zola, Pip, Ivo, Lux, Tova, Gus, then Hank 2 and so on)", text, StringComparison.Ordinal);
    }

    // ---- PR-HYDRA-059: fallbackPolicy(_ launch:) forwards headProfiles unfiltered, unlike the native Policy path ----

    [Trait("Req", "PR-HYDRA-059")] // DroppyCode/Core/Models/Hydra.swift:974-976
    [Fact]
    public void FallbackPolicy_from_launch_lists_every_head_profile_even_one_that_tunes_nothing_or_runs_elsewhere()
    {
        var untuned = new HydraHeadProfile("blank"); // no model, no effort: nativeRoutableProfiles would drop this
        var elsewhere = new HydraHeadProfile("far") with { Provider = ProviderKind.Codex, Model = "o1" }; // another provider
        var launch = FallbackLaunch(headProfiles: [untuned, elsewhere]);

        var text = HydraPrompts.FallbackPolicy(launch);

        Assert.Contains("Some heads are tuned for a purpose: blank, far (o1 on Codex).", text, StringComparison.Ordinal);
    }

    // ---- PR-HYDRA-061: delegatedHeadPrompt is the five-section brief, task on the heading line, filtered prompt ----

    [Trait("Req", "PR-HYDRA-061")] // DroppyCode/Core/Models/Hydra.swift:1245-1261
    [Fact]
    public void DelegatedHeadPrompt_is_the_exact_five_section_brief_with_the_task_on_the_heading_line()
    {
        var persona = HydraRoster.Persona(3);
        var delegation = new HydraDelegation { Task = "Fix the widget", Prompt = "Edit Widget.swift.\nrun the tests" };
        var workplace = new HydraPrompts.Workplace.Shared("C:\\repo");

        var text = HydraPrompts.DelegatedHeadPrompt(persona, delegation, workplace);

        Assert.Equal(
            $"You are {persona.Name}, a Hydra head in Nth: one of several helpers working in parallel for a lead agent. The lead delegated this task to you.\n\n" +
            "## Your task: Fix the widget\n" +
            HydraPrompts.WithoutVerificationSteps(delegation.Prompt) + "\n\n" +
            "## Where you work\n" +
            HydraPrompts.WorkplaceRules(workplace) + "\n\n" +
            "## How to work\n" +
            HydraPrompts.HowToWork + "\n\n" +
            "## Your report\n" +
            HydraPrompts.HowToReport,
            text);
    }

    // ---- PR-HYDRA-062: queuedHeadPrompt builds a context block and passes the task through raw ----

    [Trait("Req", "PR-HYDRA-062")] // DroppyCode/Core/Models/Hydra.swift:1275-1309
    [Fact]
    public void QueuedHeadPrompt_with_no_context_says_the_lead_has_not_said_anything_yet_and_keeps_the_task_raw()
    {
        var persona = HydraRoster.Persona(0);
        var context = new HydraPrompts.ChatContext();
        var workplace = new HydraPrompts.Workplace.Shared("C:\\repo");

        var text = HydraPrompts.QueuedHeadPrompt(persona, "run the tests everywhere", context, workplace);

        Assert.Contains("## What is going on in the main chat\nThe lead has not said anything yet.\n\n", text, StringComparison.Ordinal);
        // The task is NOT filtered through withoutVerificationSteps: "run the tests" stays verbatim.
        Assert.Contains("## Your task\nrun the tests everywhere\n\n", text, StringComparison.Ordinal);
        Assert.Contains($"You are {persona.Name}, a Hydra head in Nth: a helper running in parallel with the lead agent. The user queued this task for you while the lead works on something else.\n\n", text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-062")] // DroppyCode/Core/Models/Hydra.swift:1277-1286 the three context lines, in order
    [Fact]
    public void QueuedHeadPrompt_context_block_has_up_to_three_lines_in_order_and_the_leadIsWorking_wording()
    {
        var persona = HydraRoster.Persona(0);
        var context = new HydraPrompts.ChatContext
        {
            LastUserPrompt = "fix the bug",
            LastReply = "working on it",
            TouchedPaths = new ValueList<string>(["a.cs", "b.cs"]),
        };
        var workplace = new HydraPrompts.Workplace.Shared("C:\\repo");

        var working = HydraPrompts.QueuedHeadPrompt(persona, "task", context, workplace, leadIsWorking: true);
        Assert.Contains(
            "- The user last asked the lead: \u201Cfix the bug\u201D\n" + // U+201C U+201D
            "- The lead's latest reply so far: \u201Cworking on it\u201D\n" +
            "- Files the lead is changing in its current turn: a.cs, b.cs. Leave these alone; if your task cannot be done without touching one, keep the change minimal and say so in your report.",
            working, StringComparison.Ordinal);
        Assert.Contains("The user queued this task for you while the lead works on something else.", working, StringComparison.Ordinal);

        var idle = HydraPrompts.QueuedHeadPrompt(persona, "task", context, workplace, leadIsWorking: false);
        Assert.Contains("- The lead's latest reply: \u201Cworking on it\u201D\n", idle, StringComparison.Ordinal); // no " so far"
        Assert.DoesNotContain("Files the lead is changing", idle, StringComparison.Ordinal); // only shown while leadIsWorking
        Assert.Contains("The user sent this task straight to you; the lead is idle and will hear your report.", idle, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-062")] // DroppyCode/Core/Models/Hydra.swift:1284 prefix(20) and ", " join
    [Fact]
    public void QueuedHeadPrompt_caps_touched_paths_at_twenty_joined_by_comma_space()
    {
        var paths = Enumerable.Range(0, 25).Select(i => $"file{i}.cs").ToArray();
        var context = new HydraPrompts.ChatContext { TouchedPaths = new ValueList<string>(paths) };
        var workplace = new HydraPrompts.Workplace.Shared("C:\\repo");

        var text = HydraPrompts.QueuedHeadPrompt(HydraRoster.Persona(0), "task", context, workplace);

        Assert.Contains("Files the lead is changing in its current turn: " + string.Join(", ", paths.Take(20)) + ".", text, StringComparison.Ordinal);
        Assert.DoesNotContain("file20.cs", text, StringComparison.Ordinal);
    }

    // ---- PR-HYDRA-063: quoted() trims, truncates with U+2026 and wraps in curly quotes U+201C/U+201D ----

    [Trait("Req", "PR-HYDRA-063")] // DroppyCode/Core/Models/Hydra.swift:1433-1437
    [Fact]
    public void QueuedHeadPrompt_quotes_the_last_user_prompt_trimmed_and_untruncated_when_under_the_limit()
    {
        var context = new HydraPrompts.ChatContext { LastUserPrompt = "  hello world  \n" };
        var text = HydraPrompts.QueuedHeadPrompt(HydraRoster.Persona(0), "task", context, new HydraPrompts.Workplace.Shared("C:\\repo"));

        Assert.Contains("- The user last asked the lead: \u201Chello world\u201D\n", text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-063")] // DroppyCode/Core/Models/Hydra.swift:1435 truncation appends U+2026 beyond the limit
    [Fact]
    public void QueuedHeadPrompt_truncates_a_long_user_prompt_at_1200_characters_and_appends_the_ellipsis()
    {
        var longPrompt = new string('x', 1201);
        var context = new HydraPrompts.ChatContext { LastUserPrompt = longPrompt };
        var text = HydraPrompts.QueuedHeadPrompt(HydraRoster.Persona(0), "task", context, new HydraPrompts.Workplace.Shared("C:\\repo"));

        var expected = "\u201C" + new string('x', 1200) + "\u2026\u201D"; // U+201C, 1200 x's, U+2026, U+201D
        Assert.Contains("- The user last asked the lead: " + expected + "\n", text, StringComparison.Ordinal);
    }

    // ---- PR-HYDRA-064: withoutVerificationSteps drops lines matching any of 34 markers, unanchored substring ----

    [Trait("Req", "PR-HYDRA-064")] // DroppyCode/Core/Models/Hydra.swift:1234-1242
    [Fact]
    public void WithoutVerificationSteps_returns_the_prompt_unchanged_when_no_line_matches_a_marker()
    {
        const string prompt = "Edit Foo.cs\nAdd a null check";
        Assert.Same(prompt, HydraPrompts.WithoutVerificationSteps(prompt));
    }

    [Trait("Req", "PR-HYDRA-064")] // DroppyCode/Core/Models/Hydra.swift:1235-1241 drop, then append the closing sentence
    [Fact]
    public void WithoutVerificationSteps_drops_a_matching_line_and_appends_the_closing_sentence()
    {
        var prompt = "Edit Foo.cs\nrun the tests\nAdd a null check";

        var result = HydraPrompts.WithoutVerificationSteps(prompt);

        Assert.Equal("Edit Foo.cs\nAdd a null check\nMake the assigned edits and report; the lead handles all validation.", result);
    }

    [Trait("Req", "PR-HYDRA-064")] // DroppyCode/Core/Models/Hydra.swift:1229 "pytest" matches unanchored, even inside a path
    [Fact]
    public void WithoutVerificationSteps_matches_unanchored_so_a_marker_inside_a_file_path_drops_the_whole_line()
    {
        var prompt = "Edit tools/pytest_helpers.py\nAdd a null check";

        var result = HydraPrompts.WithoutVerificationSteps(prompt);

        Assert.Equal("Add a null check\nMake the assigned edits and report; the lead handles all validation.", result);
    }

    [Trait("Req", "PR-HYDRA-064")] // DroppyCode/Core/Models/Hydra.swift:1223 "tsc " keeps its trailing space
    [Fact]
    public void WithoutVerificationSteps_tsc_marker_keeps_its_trailing_space_so_a_bare_tsc_word_is_not_dropped()
    {
        var prompt = "Edit tsconfig.json\nAdd a null check";

        var result = HydraPrompts.WithoutVerificationSteps(prompt);

        Assert.Same(prompt, result); // "tsconfig" does not contain "tsc " (with the space)
    }

    [Trait("Req", "PR-HYDRA-064")] // DroppyCode/Core/Models/Hydra.swift:1235 omittingEmptySubsequences: false preserves blank lines
    [Fact]
    public void WithoutVerificationSteps_preserves_blank_lines_when_nothing_is_dropped()
    {
        const string prompt = "Edit Foo.cs\n\nAdd a null check";
        Assert.Same(prompt, HydraPrompts.WithoutVerificationSteps(prompt));
    }

    // ---- PR-HYDRA-065: teamStatus renders nine shapes and joins with ", " after the fixed opener ----

    [Trait("Req", "PR-HYDRA-065")] // DroppyCode/Core/Models/Hydra.swift:917
    [Fact]
    public void TeamStatus_is_nil_for_an_empty_team()
    {
        Assert.Null(HydraPrompts.TeamStatus([]));
    }

    [Trait("Req", "PR-HYDRA-065")] // DroppyCode/Core/Models/Hydra.swift:920-923 running: 0 minutes vs N minutes
    [Fact]
    public void TeamStatus_running_head_shows_working_with_no_minutes_at_zero_and_working_for_Nm_after()
    {
        var now = SwiftDate.Now;
        var justStarted = new HydraHeadInfo(0, "t", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) with { StartedAt = now };
        var fiveMinutesIn = new HydraHeadInfo(1, "t", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) with { StartedAt = SwiftDate.AddingTimeInterval(now, -5 * 60) };

        Assert.Equal("Your heads so far: Hank (working).", HydraPrompts.TeamStatus([justStarted], now));
        Assert.Equal("Your heads so far: Walter (working for 5m).", HydraPrompts.TeamStatus([fiveMinutesIn], now));
    }

    [Trait("Req", "PR-HYDRA-065")] // DroppyCode/Core/Models/Hydra.swift:925-930 completed: empty landing, landed N files, patch kept, no landing
    [Fact]
    public void TeamStatus_completed_head_shows_changed_nothing_landed_count_patch_kept_or_plain_done()
    {
        var now = SwiftDate.Now;
        HydraHeadInfo Head(long index, HydraLanding? landing) =>
            new HydraHeadInfo(index, "t", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) with
            {
                HydraHeadInfoStatus = HydraHeadInfo.Status.Completed,
                Landing = landing,
            };

        Assert.Equal("Your heads so far: Hank (done, changed nothing).", HydraPrompts.TeamStatus([Head(0, new HydraLanding())], now));
        Assert.Equal("Your heads so far: Walter (done, landed 1 file).", HydraPrompts.TeamStatus([Head(1, new HydraLanding { Files = new ValueList<HydraLanding.File>([new() { Path = "a", Additions = 1, Deletions = 0 }]) })], now));
        Assert.Equal("Your heads so far: Ada (done, landed 2 files).", HydraPrompts.TeamStatus([Head(2, new HydraLanding { Files = new ValueList<HydraLanding.File>([new() { Path = "a", Additions = 1, Deletions = 0 }, new() { Path = "b", Additions = 1, Deletions = 0 }]) })], now));
        Assert.Equal("Your heads so far: Otto (done, patch kept).", HydraPrompts.TeamStatus([Head(3, new HydraLanding { PatchPath = "p.patch" })], now));
        Assert.Equal("Your heads so far: Nova (done).", HydraPrompts.TeamStatus([Head(4, null)], now));
    }

    [Trait("Req", "PR-HYDRA-065")] // DroppyCode/Core/Models/Hydra.swift:931-932 failed and stopped
    [Fact]
    public void TeamStatus_failed_and_stopped_heads_and_the_join_with_a_fixed_opener()
    {
        var now = SwiftDate.Now;
        var failed = new HydraHeadInfo(0, "t", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) with { HydraHeadInfoStatus = HydraHeadInfo.Status.Failed };
        var stopped = new HydraHeadInfo(1, "t", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) with { HydraHeadInfoStatus = HydraHeadInfo.Status.Stopped };

        Assert.Equal("Your heads so far: Hank (failed), Walter (stopped).", HydraPrompts.TeamStatus([failed, stopped], now));
    }

    // ---- PR-HYDRA-066: mergeStatus renders eight shapes; the failed branch omits the pending sentence ----

    private static HydraMergeRecord Merge(HydraMergeRecord.Outcome outcome, DateTimeOffset at, long files = 0,
        string? label = null, SwiftURL? url = null, string? project = null, string? detail = null, bool? retryable = null) => new()
    {
        At = at,
        HydraMergeRecordOutcome = outcome,
        Files = files,
        Label = label,
        Url = url,
        Project = project,
        Detail = detail,
        Retryable = retryable,
    };

    [Trait("Req", "PR-HYDRA-066")] // DroppyCode/Core/Models/Hydra.swift:994-997 no records
    [Fact]
    public void MergeStatus_with_no_records_is_nil_when_nothing_unmerged_else_the_pending_line()
    {
        var now = SwiftDate.Now;
        Assert.Null(HydraPrompts.MergeStatus([], 0, now));
        Assert.Equal(
            "Merge state: 2 files changed in this chat are not merged yet; they go out by themselves once you finish this answer with every head back.",
            HydraPrompts.MergeStatus([], 2, now));
    }

    [Trait("Req", "PR-HYDRA-066")] // DroppyCode/Core/Models/Hydra.swift:999-1000 merged
    [Fact]
    public void MergeStatus_merged_outcome_gives_the_merged_sentence_then_the_pending_sentence()
    {
        var now = SwiftDate.Now;
        var url = SwiftURL.Create("https://example.com/mr/1")!;
        var record = Merge(HydraMergeRecord.Outcome.Merged, now, files: 3, label: "!1", url: url, project: "gaze-site");

        var text = HydraPrompts.MergeStatus([record], 0, now);

        Assert.StartsWith(
            $"Merge state: the team's work already merged as !1 {url.AbsoluteString} in gaze-site at ",
            text, StringComparison.Ordinal);
        Assert.EndsWith(" (3 files). Nothing has changed since, so no merge is pending and none is coming.", text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-066")] // DroppyCode/Core/Models/Hydra.swift:1001-1005 (B3) failed, non-retryable, omits the pending sentence
    [Trait("Req", "PR-HYDRAMERGE-083")] // DroppyCode/Core/Models/Hydra.swift:1001-1005 (B3) the .failed branch, non-retryable half
    [Fact]
    public void MergeStatus_failed_outcome_never_appends_the_pending_sentence()
    {
        var now = SwiftDate.Now;
        var record = Merge(HydraMergeRecord.Outcome.Failed, now, detail: "sign-in required");

        var text = HydraPrompts.MergeStatus([record], 5, now);

        Assert.Contains(
            "sign-in required. The work is still in the checkout and goes out by itself once that is settled; if the reason needs the user (a sign-in, conflict markers, a real conflict with the remote), say exactly that.",
            text, StringComparison.Ordinal);
        Assert.DoesNotContain("not merged yet", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Nothing has changed since", text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-066")] // DroppyCode/Core/Models/Hydra.swift:1001-1005 (B3) no reason recorded fallback
    [Fact]
    public void MergeStatus_failed_outcome_with_no_detail_says_no_reason_recorded()
    {
        var now = SwiftDate.Now;
        var record = Merge(HydraMergeRecord.Outcome.Failed, now);

        var text = HydraPrompts.MergeStatus([record], 0, now);

        Assert.Contains(": no reason recorded. The work is still", text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-253")] // DroppyCode/Core/Models/Hydra.swift:1001-1004 NEW at B3: retryable true
    [Trait("Req", "PR-HYDRAMERGE-132")] // DroppyCode/Core/Models/Timeline.swift:281-283
    [Trait("Req", "PR-HYDRAMERGE-083")] // DroppyCode/Core/Models/Hydra.swift:1001-1004 (B3) the .failed branch, retryable half
    [Fact]
    public void MergeStatus_failed_outcome_with_retryable_true_says_it_retries_by_itself_and_appends_no_pending_sentence() // DEVIATION OD-0012 F1
    {
        var now = SwiftDate.Now;
        var record = Merge(HydraMergeRecord.Outcome.Failed, now, detail: "still checking the forge", retryable: true);

        var text = HydraPrompts.MergeStatus([record], 5, now);

        Assert.Equal(
            "Merge state: the last try at " + FormattedShortTime(now) + " did not land yet (still checking the forge). Nth retries it by itself within a minute; nothing is needed from the user, so do not ask for anything or describe it as a problem. Do not claim a successful merge.",
            text);
        Assert.DoesNotContain("not merged yet", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Nothing has changed since", text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-253")] // DroppyCode/Core/Models/Hydra.swift:1001-1004 no reason recorded fallback
    [Fact]
    public void MergeStatus_failed_outcome_with_retryable_true_and_no_detail_says_no_reason_recorded()
    {
        var now = SwiftDate.Now;
        var record = Merge(HydraMergeRecord.Outcome.Failed, now, retryable: true);

        var text = HydraPrompts.MergeStatus([record], 0, now);

        Assert.Contains("did not land yet (no reason recorded). Nth retries it by itself within a minute", text, StringComparison.Ordinal);
    }

    // PR-HYDRAMERGE-132's failure_cases: "A record carrying false, which this build never writes, is treated
    // like nil by both readers." Retryable is compared with `== true` (:1002), so false and nil both fall through
    // to the non-retryable text; false is never distinguished from nil here, only from true. The record model's
    // own decode/encode fidelity for an explicit false (it is not squashed to null) is covered separately by
    // TimelineTests.cs's An_explicit_false_retryable_key_decodes_as_false_and_round_trips_as_false_not_null. The
    // other reader the record names, AppModel+HydraMerge.swift:233's fail() "saysIt" check (`$0.retryable ==
    // true`, the same comparison), is App-layer runtime behaviour outside Core.Models.Hydra and is left for the
    // unit that owns AppModel+HydraMerge.swift.
    [Trait("Req", "PR-HYDRA-253")] // DroppyCode/Core/Models/Hydra.swift:1002 `if last.retryable == true`
    [Trait("Req", "PR-HYDRAMERGE-132")] // DroppyCode/Core/Models/Timeline.swift:281-283: false treated like nil
    [Fact]
    public void MergeStatus_failed_outcome_with_retryable_false_matches_retryable_null_not_retryable_true()
    {
        var now = SwiftDate.Now;
        var falseRecord = Merge(HydraMergeRecord.Outcome.Failed, now, detail: "sign-in required", retryable: false);
        var nilRecord = Merge(HydraMergeRecord.Outcome.Failed, now, detail: "sign-in required", retryable: null);

        var falseText = HydraPrompts.MergeStatus([falseRecord], 5, now);
        var nilText = HydraPrompts.MergeStatus([nilRecord], 5, now);

        Assert.Equal(nilText, falseText); // false reads exactly like nil: the non-retryable sentence, not the retryable one
        Assert.DoesNotContain("retries it by itself", falseText, StringComparison.Ordinal);
        Assert.Contains("The work is still in the checkout and goes out by itself once that is settled", falseText, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-066")] // DroppyCode/Core/Models/Hydra.swift:1006-1011 (B3) held, legacy-only: no files left vs N files, using pendingLine
    [Trait("Req", "PR-HYDRAMERGE-083")] // DroppyCode/Core/Models/Hydra.swift:1006-1013 (B3)
    [Fact]
    public void MergeStatus_held_outcome_is_legacy_only_with_two_shapes_keyed_on_unmerged_files()
    {
        var now = SwiftDate.Now;
        var record = Merge(HydraMergeRecord.Outcome.Held, now);

        Assert.Equal(
            "Merge state: nothing from this job is left to merge. Do not claim a new merge or promise another one.",
            HydraPrompts.MergeStatus([record], 0, now));

        Assert.Equal(
            "Merge state: 4 files from this job are not merged yet; they go out by themselves once you finish this answer with every head back.",
            HydraPrompts.MergeStatus([record], 4, now));

        Assert.Equal(
            "Merge state: 1 file from this job is not merged yet; it goes out by itself once you finish this answer with every head back.",
            HydraPrompts.MergeStatus([record], 1, now));
    }

    [Trait("Req", "PR-HYDRA-066")] // DroppyCode/Core/Models/Hydra.swift:1012-1013 (B3) settled, exact assembly
    [Trait("Req", "PR-HYDRAMERGE-083")] // DroppyCode/Core/Models/Hydra.swift:1012-1013 (B3) the .settled branch
    [Fact]
    public void MergeStatus_settled_outcome_exact_text_with_pending_and_do_not_describe_sentence()
    {
        var now = SwiftDate.Now;
        var record = Merge(HydraMergeRecord.Outcome.Settled, now);

        var text = HydraPrompts.MergeStatus([record], 0, now);

        Assert.Equal(
            "Merge state: at " + FormattedShortTime(now) + ", Hydra found no recorded files left to merge. Nothing has changed since, so no merge is pending and none is coming. Do not describe this as a new merge.",
            text);
    }

    [Trait("Req", "PR-HYDRA-066")] // DroppyCode/Core/Models/Hydra.swift:1014-1020 stray: footnote to the last merged record, then the stray detail, then pending
    [Fact]
    public void MergeStatus_stray_outcome_footnotes_the_last_merged_record_then_its_own_detail_then_pending()
    {
        var now = SwiftDate.Now;
        var merged = Merge(HydraMergeRecord.Outcome.Merged, now, files: 1, label: "!2");
        var stray = Merge(HydraMergeRecord.Outcome.Stray, now, detail: "a rogue edit");

        var text = HydraPrompts.MergeStatus([merged, stray], 0, now);

        Assert.StartsWith("Merge state: the team's work already merged as !2", text, StringComparison.Ordinal);
        Assert.Contains(" At " + FormattedShortTime(now) + " a rogue edit ", text, StringComparison.Ordinal);
        Assert.EndsWith("Nothing has changed since, so no merge is pending and none is coming.", text, StringComparison.Ordinal);
    }

    private static string FormattedShortTime(DateTimeOffset date) =>
        SwiftFormatters.Formatted(date, DateFormatStyle.Of(DateFormatStyle.DateStyle.Omitted, DateFormatStyle.TimeStyle.Shortened));

    // ---- PR-HYDRA-067: delegationRequest is prepended only in front of an ordinary user message ----

    [Trait("Req", "PR-HYDRA-067")] // DroppyCode/Core/Models/Hydra.swift:1027-1039
    [Fact]
    public void DelegationRequest_native_route_says_use_the_configured_agent_tools()
    {
        var launch = NativeLaunch(ProviderKind.Claude);

        var text = HydraPrompts.DelegationRequest(launch);

        Assert.Equal(
            "[Hydra setting: enabled]\n" +
            "Use Hydra heads for this request when it involves multiple files, an audit, broad research, or independent tasks. Keep a single obvious change in one file with the lead. Use the configured Hydra agent tools and head profiles. Follow any explicit request below to work alone or limit delegation.\n" +
            "\n---\n\n",
            text);
    }

    [Trait("Req", "PR-HYDRA-067")] // DroppyCode/Core/Models/Hydra.swift:1030 fallback route, DEVIATION OD-0012 F1
    [Fact]
    public void DelegationRequest_fallback_route_says_Marrow_runs_the_selected_heads()
    {
        var launch = FallbackLaunch();

        var text = HydraPrompts.DelegationRequest(launch);

        Assert.Contains("Delegate through the final hydra block so Nth runs the selected heads; do not use native agent tools. Follow any explicit request below", text, StringComparison.Ordinal);
        Assert.EndsWith("\n\n---\n\n", text, StringComparison.Ordinal);
    }

    // ---- PR-HYDRA-068: fallbackTurnNote is empty with nothing to say, else a [Hydra] line ----

    [Trait("Req", "PR-HYDRA-068")] // DroppyCode/Core/Models/Hydra.swift:1042-1046
    [Fact]
    public void FallbackTurnNote_is_empty_with_neither_team_nor_merge_else_joins_them_with_the_trailer()
    {
        Assert.Equal("", HydraPrompts.FallbackTurnNote(null));
        Assert.Equal("[Hydra] team-line\n\n---\n\n", HydraPrompts.FallbackTurnNote("team-line"));
        Assert.Equal("[Hydra] team-line merge-line\n\n---\n\n", HydraPrompts.FallbackTurnNote("team-line", "merge-line"));
        Assert.Equal("[Hydra] merge-line\n\n---\n\n", HydraPrompts.FallbackTurnNote(null, "merge-line"));
    }

    // ---- PR-HYDRA-069: fallbackReportNote always opens the same way and carries a literal ```hydra when it can delegate ----

    [Trait("Req", "PR-HYDRA-069")] // DroppyCode/Core/Models/Hydra.swift:1051-1056
    [Fact]
    public void FallbackReportNote_opens_the_same_way_and_the_more_clause_has_an_unconditional_leading_space()
    {
        var noneText = HydraPrompts.FallbackReportNote(null, canDelegate: false);
        Assert.Equal("[Hydra] Your heads reported back below. Send out no more heads for this request; whatever is left, do yourself.\n\n---\n\n", noneText);

        var withBoth = HydraPrompts.FallbackReportNote("team-line", canDelegate: true, merge: "merge-line");
        Assert.StartsWith("[Hydra] Your heads reported back below. team-line merge-line ", withBoth, StringComparison.Ordinal);
        Assert.Contains("with the same ```hydra block at the end of your reply; never for verifying, redoing or finishing what a head already did.", withBoth, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-069")] // DroppyCode/Core/Models/Hydra.swift:1055 team.map/merge.map contribute a literal space even for an empty (non-nil) string
    [Fact]
    public void FallbackReportNote_never_collapses_the_space_a_present_but_empty_team_or_merge_string_contributes()
    {
        // team is present (not null) but empty: team.map{" "+$0} still fires, contributing a bare space, so the
        // unconditional " " before "more" then yields two adjacent spaces. The concatenation is never collapsed.
        var text = HydraPrompts.FallbackReportNote("", canDelegate: false);

        Assert.Equal("[Hydra] Your heads reported back below.  Send out no more heads for this request; whatever is left, do yourself.\n\n---\n\n", text);
    }

    // ---- PR-HYDRA-070: fallbackPreamble substitutes a bare separator when the turn note is empty ----

    [Trait("Req", "PR-HYDRA-070")] // DroppyCode/Core/Models/Hydra.swift:1060-1066
    [Fact]
    public void FallbackPreamble_substitutes_a_bare_separator_when_the_turn_note_is_empty()
    {
        var launch = FallbackLaunch();

        var text = HydraPrompts.FallbackPreamble(launch, null);

        Assert.Equal(HydraPrompts.FallbackPolicy(launch) + "\n\n---\n\n", text);
    }

    [Trait("Req", "PR-HYDRA-070")] // DroppyCode/Core/Models/Hydra.swift:1062
    [Fact]
    public void FallbackPreamble_uses_the_turn_note_when_it_is_not_empty()
    {
        var launch = FallbackLaunch();

        var text = HydraPrompts.FallbackPreamble(launch, "team-line");

        Assert.Equal(HydraPrompts.FallbackPolicy(launch) + "\n\n" + HydraPrompts.FallbackTurnNote("team-line"), text);
    }

    [Trait("Req", "PR-HYDRA-070")] // DroppyCode/Core/Models/Hydra.swift:1064-1066 fallbackReportPreamble is never empty
    [Fact]
    public void FallbackReportPreamble_is_the_policy_then_the_report_note()
    {
        var launch = FallbackLaunch();

        var text = HydraPrompts.FallbackReportPreamble(launch, "team-line", canDelegate: true);

        Assert.Equal(HydraPrompts.FallbackPolicy(launch) + "\n\n" + HydraPrompts.FallbackReportNote("team-line", true), text);
    }

    // ---- PR-HYDRA-072: maxDelegationRounds is 3 and heldBackMessage is emitted verbatim ----

    [Trait("Req", "PR-HYDRA-072")] // DroppyCode/Core/Models/Hydra.swift:912
    [Fact]
    public void MaxDelegationRounds_is_3()
    {
        Assert.Equal(3, HydraPrompts.MaxDelegationRounds);
    }

    [Trait("Req", "PR-HYDRA-072")] // DroppyCode/Core/Models/Hydra.swift:1070-1075
    [Fact]
    public void HeldBackMessage_singular_and_plural_head_wording()
    {
        Assert.Equal(
            "Hydra held back a head.\n" +
            "None of the heads you asked for went out: this request has had all 3 of its rounds of heads already. Do the rest yourself now, without git status or git diff on the heads' work, and answer the user.",
            HydraPrompts.HeldBackMessage(1));
        Assert.StartsWith("Hydra held back 2 heads.\n", HydraPrompts.HeldBackMessage(2), StringComparison.Ordinal);
    }

    // ---- PR-HYDRA-073: unreadableBlockMessage with its reason branch structurally present but unreachable ----

    [Trait("Req", "PR-HYDRA-073")] // DroppyCode/Core/Models/Hydra.swift:1211-1217, the single call site passes nil
    [Fact]
    public void UnreadableBlockMessage_with_no_reason_is_the_exact_base_plus_tail()
    {
        Assert.Equal(
            "[Hydra] Your delegation block could not be read, so no heads went out. Write the block again at the end of your reply: one fenced block whose info string is hydra, holding a JSON array of objects with a task string and a prompt string. Inside a prompt never open a fenced code block of your own (no three backticks): describe code in words, quote identifiers with single backticks, or indent snippets. This does not count as a round of heads.",
            HydraPrompts.UnreadableBlockMessage(null));
    }

    [Trait("Req", "PR-HYDRA-073")] // DroppyCode/Core/Models/Hydra.swift:1214 the reason clause and its terminal-punctuation set
    [Fact]
    public void UnreadableBlockMessage_reason_branch_adds_a_period_unless_the_reason_already_ends_in_terminal_punctuation()
    {
        Assert.StartsWith("[Hydra] Your delegation block could not be read, so no heads went out. bad json. Write the block again", HydraPrompts.UnreadableBlockMessage("bad json"), StringComparison.Ordinal);
        Assert.StartsWith("[Hydra] Your delegation block could not be read, so no heads went out. bad json! Write the block again", HydraPrompts.UnreadableBlockMessage("bad json!"), StringComparison.Ordinal);
        Assert.StartsWith("[Hydra] Your delegation block could not be read, so no heads went out. Write the block again", HydraPrompts.UnreadableBlockMessage("   "), StringComparison.Ordinal); // blank reason after trim is dropped entirely
    }

    // ---- PR-HYDRA-074, 075, 076, 077, 078, 079: reportMessage's three blocks ----

    private static HydraReport Report(long headIndex, HydraHeadInfo.Origin origin, HydraHeadInfo.Status status, string text,
        HydraLanding? landing = null, string? copyPath = null, double? elapsed = null, long toolCalls = 0) => new()
    {
        HeadIndex = headIndex,
        Task = "the task",
        Origin = origin,
        Status = status,
        Text = text,
        Landing = landing,
        CopyPath = copyPath,
        Elapsed = elapsed,
        ToolCalls = toolCalls,
    };

    [Trait("Req", "PR-HYDRA-074")] // DroppyCode/Core/Models/Hydra.swift:1390-1396
    [Fact]
    public void ReportMessage_has_three_blocks_separated_by_blank_lines()
    {
        var report = Report(0, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Completed, "done");

        var text = HydraPrompts.ReportMessage([report]);
        var blocks = text.Split("\n\n");

        Assert.Equal(3, blocks.Length);
        Assert.StartsWith("Hydra reports:", blocks[0], StringComparison.Ordinal);
        Assert.StartsWith("## Hank:", blocks[1], StringComparison.Ordinal);
        Assert.Contains("Build on the reports", blocks[2], StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-075")] // DroppyCode/Core/Models/Hydra.swift:1315-1319, :1425-1431 list() with no Oxford comma
    [Fact]
    public void ReportMessage_opening_lists_finished_names_with_no_oxford_comma_and_names_the_still_working_heads()
    {
        var reports = new[]
        {
            Report(0, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Completed, "a"),
            Report(1, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Completed, "b"),
            Report(2, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Completed, "c"),
        };

        var noneWorking = HydraPrompts.ReportMessage(reports);
        Assert.StartsWith("Hydra reports: Hank, Walter and Ada finished.\n\n", noneWorking, StringComparison.Ordinal);

        var oneStillWorking = HydraPrompts.ReportMessage(reports[..1], stillWorking: ["Otto"]);
        Assert.StartsWith("Hydra reports: Hank finished. Otto is still at work; its report comes as a later message.\n\n", oneStillWorking, StringComparison.Ordinal);

        var twoStillWorking = HydraPrompts.ReportMessage(reports[..1], stillWorking: ["Otto", "Nova"]);
        Assert.StartsWith("Hydra reports: Hank finished. Otto and Nova are still at work; their reports come as a later message.\n\n", twoStillWorking, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-076")] // DroppyCode/Core/Models/Hydra.swift:1326-1343, :1420-1423 (B3) duration()
    [Fact]
    public void ReportMessage_per_head_section_header_has_outcome_and_effort_and_falls_back_to_No_report()
    {
        var completed = Report(0, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Completed, "", elapsed: 125, toolCalls: 3);
        var failed = Report(1, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Failed, "found the bug", elapsed: 0.4);
        var stopped = Report(2, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Stopped, "partial", toolCalls: 1);

        var text = HydraPrompts.ReportMessage([completed, failed, stopped]);

        Assert.Contains("## Hank: the task (2m 5s, 3 tools)\nNo report.", text, StringComparison.Ordinal);
        Assert.Contains("## Walter: the task (failed)\nfound the bug", text, StringComparison.Ordinal); // elapsed 0.4s < 1 contributes nothing
        Assert.Contains("## Ada: the task (stopped by the user before finishing) (1 tool)\npartial", text, StringComparison.Ordinal);
    }

    // ---- PR-HYDRA-254: duration() is SwiftFormatters.DurationUnits({seconds, minutes}, narrow, max 2 units) ----

    [Theory]
    [Trait("Req", "PR-HYDRA-254")] // DroppyCode/Core/Models/Hydra.swift:1420-1423 (B3): Duration.UnitsFormatStyle, not the hand-built "Ns"/"Mm Ss"
    [InlineData(1.0, "1s")]
    [InlineData(45.0, "45s")]
    [InlineData(60.0, "1m")]   // B2 hand-built gave "1m 0s"; B3's ICU-backed formatter drops the zero-valued unit
    [InlineData(61.0, "1m 1s")]
    [InlineData(127.0, "2m 7s")]
    [InlineData(3599.0, "59m 59s")]
    [InlineData(3600.0, "60m")] // B2 hand-built gave "60m 0s"; minutes is not an allowed unit here, so never "1h"
    [InlineData(3725.0, "62m 5s")]
    public void Duration_matches_SwiftFormatters_DurationUnits_for_seconds_and_minutes(double elapsed, string expected)
    {
        var report = Report(0, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Completed, "", elapsed: elapsed);
        var text = HydraPrompts.ReportMessage([report]);
        Assert.Contains($"## Hank: the task ({expected})\nNo report.", text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-077")] // DroppyCode/Core/Models/Hydra.swift:1401-1418 landingLine's seven shapes
    [Fact]
    public void ReportMessage_landing_line_shapes_for_no_landing_empty_landed_conflicts_patch_and_error()
    {
        var noLandingNoCopy = Report(0, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Completed, "t");
        Assert.Contains("## Hank: the task\nt", HydraPrompts.ReportMessage([noLandingNoCopy]), StringComparison.Ordinal); // no landing line at all

        var completedWithCopy = Report(0, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Completed, "t", copyPath: "C:\\copy");
        Assert.Contains("## Hank: the task\nChanged nothing.\nt", HydraPrompts.ReportMessage([completedWithCopy]), StringComparison.Ordinal);

        var stoppedWithCopy = Report(0, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Stopped, "t", copyPath: "C:\\copy");
        Assert.Contains("Nothing landed; its unfinished work is in its copy at C:\\copy.", HydraPrompts.ReportMessage([stoppedWithCopy]), StringComparison.Ordinal);

        var emptyLanding = Report(0, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Completed, "t", landing: new HydraLanding());
        Assert.Contains("## Hank: the task\nChanged nothing.\nt", HydraPrompts.ReportMessage([emptyLanding]), StringComparison.Ordinal);

        var landed = Report(0, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Completed, "t",
            landing: new HydraLanding { Files = new ValueList<HydraLanding.File>([new() { Path = "a.cs", Additions = 3, Deletions = 1 }]) });
        Assert.Contains("Landed in your checkout: a.cs (+3 \u22121).", HydraPrompts.ReportMessage([landed]), StringComparison.Ordinal); // U+2212 MINUS SIGN

        var withConflict = Report(0, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Completed, "t",
            landing: new HydraLanding
            {
                Files = new ValueList<HydraLanding.File>([new() { Path = "a.cs", Additions = 1, Deletions = 0 }]),
                Conflicts = new ValueList<string>(["a.cs"]),
            });
        Assert.Contains("Landed in your checkout: a.cs (+1 \u22120) with conflicts.", HydraPrompts.ReportMessage([withConflict]), StringComparison.Ordinal);

        var patched = Report(0, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Completed, "t",
            landing: new HydraLanding
            {
                Files = new ValueList<HydraLanding.File>([new() { Path = "a.cs", Additions = 1, Deletions = 0 }]),
                PatchPath = "p.patch",
            });
        Assert.Contains("Did not land: a.cs (+1 \u22120).", HydraPrompts.ReportMessage([patched]), StringComparison.Ordinal);

        var errored = Report(0, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Completed, "t",
            landing: new HydraLanding
            {
                Files = new ValueList<HydraLanding.File>([new() { Path = "a.cs", Additions = 1, Deletions = 0 }]),
                Error = "disk full",
            });
        Assert.Contains("Did not land (disk full): a.cs (+1 \u22120).", HydraPrompts.ReportMessage([errored]), StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-077")] // DroppyCode/Core/Models/Hydra.swift:1407-1414 the 25-file cap and dropped build outputs
    [Fact]
    public void ReportMessage_landing_line_caps_at_25_files_and_reports_dropped_build_output_files()
    {
        var files = Enumerable.Range(0, 30).Select(i => new HydraLanding.File { Path = $"f{i}.cs", Additions = 1, Deletions = 0 }).ToArray();
        var landing = new HydraLanding { Files = new ValueList<HydraLanding.File>(files), DroppedBuildOutputFiles = 2 };
        var report = Report(0, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Completed, "t", landing: landing);

        var text = HydraPrompts.ReportMessage([report]);

        Assert.Contains(", and 5 more; 2 build-output files were left out.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("f25.cs", text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-078")] // DroppyCode/Core/Models/Hydra.swift:1341-1388 the closing sentences, fixed order and conditions
    [Fact]
    public void ReportMessage_closing_emits_landed_and_worked_in_checkout_sentences_when_both_apply()
    {
        var landedReport = Report(0, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Completed, "t",
            landing: new HydraLanding { Files = new ValueList<HydraLanding.File>([new() { Path = "a.cs", Additions = 1, Deletions = 0 }]) });
        var checkoutReport = Report(1, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Completed, "t");

        var closing = HydraPrompts.ReportMessage([landedReport, checkoutReport]).Split("\n\n")[^1];

        Assert.StartsWith(
            "The changes listed above are in your checkout already; nothing needs applying. The heads worked in your checkout, so their changes are there already.",
            closing, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-078")] // DroppyCode/Core/Models/Hydra.swift:1353-1361 shared-file sentence uses "both" and is sorted by path
    [Fact]
    public void ReportMessage_closing_names_two_heads_that_changed_the_same_file_sorted_by_path()
    {
        var a = Report(0, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Completed, "t",
            landing: new HydraLanding { Files = new ValueList<HydraLanding.File>([new() { Path = "b.cs", Additions = 1, Deletions = 0 }, new() { Path = "a.cs", Additions = 1, Deletions = 0 }]) });
        var b = Report(1, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Completed, "t",
            landing: new HydraLanding { Files = new ValueList<HydraLanding.File>([new() { Path = "a.cs", Additions = 1, Deletions = 0 }]) });

        var closing = HydraPrompts.ReportMessage([a, b]).Split("\n\n")[^1];

        Assert.Contains("Hank and Walter both changed a.cs: read it as it is now before you build on it.", closing, StringComparison.Ordinal);
        Assert.DoesNotContain("changed b.cs", closing, StringComparison.Ordinal); // b.cs was touched by only one head
    }

    [Trait("Req", "PR-HYDRA-078")] // DroppyCode/Core/Models/Hydra.swift:1362-1365 the kept-patch sentence, in report order
    [Fact]
    public void ReportMessage_closing_names_a_kept_patch_with_the_git_apply_command()
    {
        var report = Report(0, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Completed, "t",
            landing: new HydraLanding
            {
                Files = new ValueList<HydraLanding.File>([new() { Path = "a.cs", Additions = 1, Deletions = 0 }]),
                PatchPath = "C:\\p.patch",
            });

        var closing = HydraPrompts.ReportMessage([report]).Split("\n\n")[^1];

        Assert.Contains("Hank's changes would not apply on their own; the patch is at C:\\p.patch. Apply it with `git apply --3way C:\\p.patch` and settle what conflicts.", closing, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-078")] // DroppyCode/Core/Models/Hydra.swift:1366-1377 failed, stopped, queued, sent sentences
    [Fact]
    public void ReportMessage_closing_names_failed_stopped_queued_and_sent_conditions()
    {
        var failed = Report(0, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Failed, "t");
        var stopped = Report(1, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Stopped, "t");
        var queued = Report(2, HydraHeadInfo.Origin.Queued, HydraHeadInfo.Status.Completed, "t");
        var sent = Report(3, HydraHeadInfo.Origin.Sent, HydraHeadInfo.Status.Completed, "t");

        var closing = HydraPrompts.ReportMessage([failed, stopped, queued, sent]).Split("\n\n")[^1];

        Assert.Contains("A head that failed leaves its part to you: do it yourself or send it out again.", closing, StringComparison.Ordinal);
        Assert.Contains("A head the user stopped leaves its part alone unless the user asks for it.", closing, StringComparison.Ordinal);
        Assert.Contains("The user queued that work for the heads while you were busy; take it into account and carry on with the main job.", closing, StringComparison.Ordinal);
        Assert.Contains("The user sent that work straight to the heads instead of to you; take it into account, and reply to the user on it as if they had asked you.", closing, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-078")] // DroppyCode/Core/Models/Hydra.swift:1386-1388 the still-working closing sentence, singular and plural
    [Fact]
    public void ReportMessage_closing_names_still_working_heads_it_or_them_singular_and_plural()
    {
        var report = Report(0, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Completed, "t");

        var oneStillWorking = HydraPrompts.ReportMessage([report], stillWorking: ["Otto"]).Split("\n\n")[^1];
        Assert.Contains("Do not wait for Otto and do not take over its tasks; tell the user it is still at work and that you will hear from it.", oneStillWorking, StringComparison.Ordinal);

        var twoStillWorking = HydraPrompts.ReportMessage([report], stillWorking: ["Otto", "Nova"]).Split("\n\n")[^1];
        Assert.Contains("Do not wait for Otto and Nova and do not take over their tasks; tell the user they are still at work and that you will hear from them.", twoStillWorking, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-078")] // DroppyCode/Core/Models/Hydra.swift:1348-1350 the conflicts sentence
    [Fact]
    public void ReportMessage_closing_adds_the_conflicts_sentence_when_any_landing_has_a_conflict()
    {
        var report = Report(0, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Completed, "t",
            landing: new HydraLanding
            {
                Files = new ValueList<HydraLanding.File>([new() { Path = "a.cs", Additions = 1, Deletions = 0 }]),
                Conflicts = new ValueList<string>(["a.cs"]),
            });

        var closing = HydraPrompts.ReportMessage([report]).Split("\n\n")[^1];

        Assert.Contains("A file listed with conflicts was merged three-way and keeps conflict markers: resolve those first, and tell the user in one line that you settled them, so the landing card above does not read as broken.", closing, StringComparison.Ordinal);
    }

    // ---- PR-HYDRA-079: the closing carries exactly one of two review/no-review sentences ----

    [Trait("Req", "PR-HYDRA-079")] // DroppyCode/Core/Models/Hydra.swift:1380-1384
    [Fact]
    public void ReportMessage_closing_picks_the_review_variant_by_reviewsHeads()
    {
        var report = Report(0, HydraHeadInfo.Origin.Delegated, HydraHeadInfo.Status.Completed, "t");

        var reviewing = HydraPrompts.ReportMessage([report], reviewsHeads: true).Split("\n\n")[^1];
        Assert.Contains(
            "Do not check any of this with git status or git diff: the checkout changes under you while heads work, and the user may be editing too. Do not reconcile or revert anything. " + HydraPrompts.ReviewRule,
            reviewing, StringComparison.Ordinal);
        Assert.DoesNotContain("Do not reconcile, revert or redo anything.", reviewing, StringComparison.Ordinal);

        var notReviewing = HydraPrompts.ReportMessage([report], reviewsHeads: false).Split("\n\n")[^1];
        Assert.Contains(
            "Do not check any of this with git status or git diff: the checkout changes under you while heads work, and the user may be editing too. Do not reconcile, revert or redo anything. Build on the reports, read the files they name if something matters, run one verification if it matters, and finish the job.",
            notReviewing, StringComparison.Ordinal);
    }

    // ---- PR-HYDRA-080: delegationOpener accepts hydra, hydra-sent, hydra-leaving, case-insensitively ----
    // Exercised through the public surface (DelegationBlockRange/HasDelegationBlock/HasDelegationOpener),
    // since delegationOpener itself is private (Hydra.swift:1096-1098).

    [Trait("Req", "PR-HYDRA-080")] // DroppyCode/Core/Models/Hydra.swift:1096-1098
    [Fact]
    public void DelegationBlockRange_opener_accepts_the_three_dialects_case_insensitively()
    {
        Assert.NotNull(HydraPrompts.DelegationBlockRange("```hydra\n[]\n```"));
        Assert.NotNull(HydraPrompts.DelegationBlockRange("```HYDRA\n[]\n```"));
        Assert.NotNull(HydraPrompts.DelegationBlockRange("```Hydra-Sent\n[]\n```"));
        Assert.NotNull(HydraPrompts.DelegationBlockRange("```hydra-leaving\n[]\n```"));
        Assert.NotNull(HydraPrompts.DelegationBlockRange("```HYDRA-LEAVING\n[]\n```"));
    }

    [Trait("Req", "PR-HYDRA-080")] // DroppyCode/Core/Models/Hydra.swift:1096-1098
    [Fact]
    public void DelegationBlockRange_opener_allows_spaces_or_tabs_and_an_optional_CR_before_the_LF()
    {
        Assert.NotNull(HydraPrompts.DelegationBlockRange("```  hydra \t\n[]\n```"));
        Assert.NotNull(HydraPrompts.DelegationBlockRange("```hydra\r\n[]\n```"));
    }

    [Trait("Req", "PR-HYDRA-080")] // DroppyCode/Core/Models/Hydra.swift:1096-1098
    [Fact]
    public void DelegationBlockRange_opener_rejects_other_info_strings_and_other_whitespace_classes()
    {
        Assert.Null(HydraPrompts.DelegationBlockRange("```json\n[]\n```"));
        Assert.Null(HydraPrompts.DelegationBlockRange("```hydraX\n[]\n```"));
        // A vertical tab is not in the pattern's [ \t] whitespace class.
        Assert.Null(HydraPrompts.DelegationBlockRange("```hydra\u000B\n[]\n```"));
    }

    [Trait("Req", "PR-HYDRA-080")] // DroppyCode/Core/Models/Hydra.swift:1096-1098
    [Fact]
    public void DelegationBlockRange_opener_uses_the_first_match_in_the_text()
    {
        var range = HydraPrompts.DelegationBlockRange("noise\n```hydra\nA\n```\nmore\n```hydra-sent\nB\n```");
        Assert.NotNull(range);
        Assert.Equal("noise\n".Length, range!.Value.LowerBound);
    }

    // ---- PR-HYDRA-082/083: lineStartFences and delegationBlockRange (private helpers, exercised through
    // DelegationBlockRange, which is delegationOpener.lowerBound .. the LAST line-start fence's upperBound,
    // or the end of the text) ----

    [Trait("Req", "PR-HYDRA-082")] // DroppyCode/Core/Models/Hydra.swift:1103-1113
    [Fact]
    public void DelegationBlockRange_keeps_a_fence_only_when_it_opens_a_line()
    {
        // "``` ```" mid-line: not a line-start fence, so no closer is found and the block runs to the end.
        var text = "```hydra\n[] not a fence: ``` mid-line\n```";
        var range = HydraPrompts.DelegationBlockRange(text);
        Assert.NotNull(range);
        Assert.Equal(text.Length, range!.Value.UpperBound);
    }

    [Trait("Req", "PR-HYDRA-082")] // DroppyCode/Core/Models/Hydra.swift:1103-1113
    [Fact]
    public void DelegationBlockRange_counts_a_line_start_fence_that_carries_an_info_string()
    {
        var text = "```hydra\n[]\n```swift\nsome code\n```";
        var range = HydraPrompts.DelegationBlockRange(text);
        Assert.NotNull(range);
        // The LAST line-start fence is ```swift's closer at the very end, not the ```swift opener itself.
        Assert.Equal(text.Length, range!.Value.UpperBound);
    }

    [Trait("Req", "PR-HYDRA-082")] // DroppyCode/Core/Models/Hydra.swift:1103-1113
    [Fact]
    public void DelegationBlockRange_six_consecutive_backticks_yield_only_one_fence_after_the_opener()
    {
        // "``````" at line start: the first "```" is a line-start fence; scanning resumes right after it,
        // so the second "```" starts mid-token, not at a line start, and is not itself a fence.
        var text = "```hydra\n``````\ntail";
        var range = HydraPrompts.DelegationBlockRange(text);
        Assert.NotNull(range);
        var openerLen = "```hydra\n".Length;
        Assert.Equal(openerLen + 3, range!.Value.UpperBound);
    }

    [Trait("Req", "PR-HYDRA-082")] // DroppyCode/Core/Models/Hydra.swift:1103-1113
    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    [InlineData("\u0085")] // NEL
    [InlineData("\u2028")] // LINE SEPARATOR
    [InlineData("\u2029")] // PARAGRAPH SEPARATOR
    public void DelegationBlockRange_fence_detection_recognizes_every_Foundation_newline_class(string newline)
    {
        var text = "```hydra\n[]" + newline + "```";
        var range = HydraPrompts.DelegationBlockRange(text);
        Assert.NotNull(range);
        Assert.Equal(text.Length, range!.Value.UpperBound);
    }

    [Trait("Req", "PR-HYDRA-083")] // DroppyCode/Core/Models/Hydra.swift:1121-1125
    [Fact]
    public void DelegationBlockRange_ends_at_the_LAST_line_start_fence_not_the_first()
    {
        var text = "```hydra\n[]\n```\nafter, quoting a prompt's own fence\n```\ntail\n```";
        var range = HydraPrompts.DelegationBlockRange(text);
        Assert.NotNull(range);
        Assert.Equal(text.Length, range!.Value.UpperBound);
    }

    [Trait("Req", "PR-HYDRA-083")] // DroppyCode/Core/Models/Hydra.swift:1121-1125
    [Fact]
    public void DelegationBlockRange_runs_to_the_end_of_the_text_with_no_closing_fence_at_all()
    {
        var text = "```hydra\n[{\"task\": \"t\", \"prompt\": \"p\"}]";
        var range = HydraPrompts.DelegationBlockRange(text);
        Assert.NotNull(range);
        Assert.Equal(0, range!.Value.LowerBound);
        Assert.Equal(text.Length, range!.Value.UpperBound);
    }

    [Trait("Req", "PR-HYDRA-083")] // DroppyCode/Core/Models/Hydra.swift:1121-1125
    [Fact]
    public void DelegationBlockRange_is_null_with_no_opener_at_all()
    {
        Assert.Null(HydraPrompts.DelegationBlockRange("no block here at all"));
    }

    // ---- PR-HYDRA-084/085/098: Delegations walks candidate ends backwards; the body parser is tri-state ----

    [Trait("Req", "PR-HYDRA-084")] // DroppyCode/Core/Models/Hydra.swift:1144-1153
    [Trait("Req", "PR-HYDRA-085")] // DroppyCode/Core/Models/Hydra.swift:1160-1166
    [Fact]
    public void Delegations_parses_a_single_bare_object_body_as_one_entry()
    {
        var text = "```hydra\n{\"task\": \"t\", \"prompt\": \"p\"}\n```";
        var result = HydraPrompts.Delegations(text);
        Assert.NotNull(result);
        var one = Assert.Single(result!);
        Assert.Equal("t", one.Task);
        Assert.Equal("p", one.Prompt);
    }

    [Trait("Req", "PR-HYDRA-084")] // DroppyCode/Core/Models/Hydra.swift:1144-1153
    [Fact]
    public void Delegations_parses_an_array_of_entries_in_order()
    {
        var text = "```hydra\n[{\"task\": \"a\", \"prompt\": \"pa\"}, {\"task\": \"b\", \"prompt\": \"pb\"}]\n```";
        var result = HydraPrompts.Delegations(text);
        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Equal("a", result[0].Task);
        Assert.Equal("b", result[1].Task);
    }

    [Trait("Req", "PR-HYDRA-098")] // DroppyCode/Core/Models/Hydra.swift:1162, 1137-1143, 967
    [Fact]
    public void Delegations_returns_an_empty_list_not_null_for_an_explicitly_empty_array()
    {
        var text = "```hydra\n[]\n```";
        var result = HydraPrompts.Delegations(text);
        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Trait("Req", "PR-HYDRA-084")] // DroppyCode/Core/Models/Hydra.swift:1144-1153
    [Fact]
    public void Delegations_walks_line_start_fences_backwards_skipping_candidates_that_fail_to_parse()
    {
        // Two more line-start fences follow the block's real closer (a trailing markdown example the
        // lead added after its answer). Every candidate that includes that trailing text has non-JSON
        // content after the array and fails to parse; delegations(in:) must walk backwards past both
        // of them before it reaches the real closer, whose body is exactly the valid array.
        var text = "```hydra\n[{\"task\": \"a\", \"prompt\": \"pa\"}]\n```\n"
            + "Some trailing text with an extra example fence:\n```\nnotjson\n```";
        var result = HydraPrompts.Delegations(text);
        Assert.NotNull(result);
        var one = Assert.Single(result!);
        Assert.Equal("a", one.Task);
        Assert.Equal("pa", one.Prompt);
    }

    [Trait("Req", "PR-HYDRA-085")] // DroppyCode/Core/Models/Hydra.swift:1160-1166
    [Fact]
    public void Delegations_with_an_entry_missing_a_prompt_falls_back_to_the_whole_tail_and_still_fails()
    {
        // Every candidate body (the whole array, since there is only one fence, the opener) parses as
        // JSON but every entry lacks "prompt", so delegation(fromEntry:) rejects all of them and
        // delegations(fromBody:) returns nil (not []), so delegations(in:) exhausts every candidate and
        // returns null.
        var text = "```hydra\n[{\"task\": \"t\"}]\n```";
        Assert.Null(HydraPrompts.Delegations(text));
    }

    [Trait("Req", "PR-HYDRA-084")] // DroppyCode/Core/Models/Hydra.swift:1144-1153
    [Fact]
    public void Delegations_is_null_when_no_candidate_body_parses()
    {
        Assert.Null(HydraPrompts.Delegations("```hydra\nnot json at all\n```"));
    }

    [Trait("Req", "PR-HYDRA-084")] // DroppyCode/Core/Models/Hydra.swift:1144-1153
    [Fact]
    public void Delegations_is_null_with_no_opener_at_all()
    {
        Assert.Null(HydraPrompts.Delegations("no block here at all"));
    }

    [Trait("Req", "PR-HYDRA-085")] // DroppyCode/Core/Models/HydraDelegationStream.swift:110-118
    [Fact]
    public void Delegations_titles_an_entry_without_a_task_from_the_prompt()
    {
        var text = "```hydra\n{\"prompt\": \"fix the login bug\"}\n```";
        var result = HydraPrompts.Delegations(text);
        Assert.NotNull(result);
        var one = Assert.Single(result!);
        Assert.Equal("fix the login bug", one.Task);
        Assert.Equal("fix the login bug", one.Prompt);
        Assert.Null(one.Name);
        Assert.Null(one.Project);
        Assert.Null(one.Profile);
    }

    [Trait("Req", "PR-HYDRA-085")] // DroppyCode/Core/Models/HydraDelegationStream.swift:110-118
    [Fact]
    public void Delegations_reads_head_and_repo_as_fallbacks_for_name_and_project()
    {
        var text = "```hydra\n{\"prompt\": \"p\", \"head\": \"scout\", \"repo\": \"marrow\", \"profile\": \"fast\"}\n```";
        var result = HydraPrompts.Delegations(text);
        Assert.NotNull(result);
        var one = Assert.Single(result!);
        Assert.Equal("scout", one.Name);
        Assert.Equal("marrow", one.Project);
        Assert.Equal("fast", one.Profile);
    }

    [Trait("Req", "PR-HYDRA-085")] // DroppyCode/Core/Models/HydraDelegationStream.swift:110-118
    [Fact]
    public void Delegations_name_and_project_take_precedence_over_head_and_repo_when_both_are_present()
    {
        var text = "```hydra\n{\"prompt\": \"p\", \"name\": \"lead\", \"head\": \"scout\", "
            + "\"project\": \"app\", \"repo\": \"marrow\"}\n```";
        var result = HydraPrompts.Delegations(text);
        Assert.NotNull(result);
        var one = Assert.Single(result!);
        Assert.Equal("lead", one.Name);
        Assert.Equal("app", one.Project);
    }

    [Trait("Req", "PR-HYDRA-098")] // DroppyCode/Core/Models/Hydra.swift:1162
    [Fact]
    public void Delegations_with_a_blank_prompt_rejects_the_entry_and_the_others_all_blank_yields_null()
    {
        var text = "```hydra\n[{\"prompt\": \"   \"}]\n```";
        Assert.Null(HydraPrompts.Delegations(text));
    }

    // ---- PR-HYDRA-088: withoutDelegationBlock removes the block's range and trims both ends ----

    [Trait("Req", "PR-HYDRA-088")] // DroppyCode/Core/Models/Hydra.swift:1169-1173
    [Fact]
    public void WithoutDelegationBlock_removes_the_whole_range_and_trims_leading_and_trailing_whitespace()
    {
        var text = "  \n  Here is my answer.\n\n```hydra\n[{\"task\": \"t\", \"prompt\": \"p\"}]\n```\n  \n";
        var kept = HydraPrompts.WithoutDelegationBlock(text);
        Assert.Equal("Here is my answer.", kept);
    }

    [Trait("Req", "PR-HYDRA-088")] // DroppyCode/Core/Models/Hydra.swift:1169-1173
    [Fact]
    public void WithoutDelegationBlock_with_no_block_present_returns_the_text_trimmed()
    {
        Assert.Equal("just an answer", HydraPrompts.WithoutDelegationBlock("  just an answer  \n"));
    }

    // ---- PR-HYDRA-089: hasDelegationBlock and hasDelegationOpener are the two block-presence tests ----

    [Trait("Req", "PR-HYDRA-089")] // DroppyCode/Core/Models/Hydra.swift:1128
    [Fact]
    public void HasDelegationBlock_is_true_whenever_a_block_range_exists_readable_or_not()
    {
        Assert.True(HydraPrompts.HasDelegationBlock("```hydra\nnot valid json at all\n```"));
        Assert.True(HydraPrompts.HasDelegationBlock("```hydra\n[]\n```"));
        Assert.False(HydraPrompts.HasDelegationBlock("no block here"));
    }

    [Trait("Req", "PR-HYDRA-089")] // DroppyCode/Core/Models/Hydra.swift:1133-1135
    [Fact]
    public void HasDelegationOpener_applies_the_same_three_dialect_regex_over_a_streaming_tail()
    {
        Assert.True(HydraPrompts.HasDelegationOpener("```hydra\n"));
        Assert.True(HydraPrompts.HasDelegationOpener("some prior text\n```HYDRA-SENT\n"));
        Assert.False(HydraPrompts.HasDelegationOpener("```hydra")); // no terminating LF yet: still streaming
    }

    // ---- PR-HYDRA-090: markingDelegationBlockSent rewrites the opener to ```hydra-sent, once ----

    [Trait("Req", "PR-HYDRA-090")] // DroppyCode/Core/Models/Hydra.swift:1204-1207
    [Fact]
    public void MarkingDelegationBlockSent_rewrites_the_opener_to_exactly_hydra_sent_with_a_single_LF()
    {
        var result = HydraPrompts.MarkingDelegationBlockSent("before\n```hydra\n[]\n```\nafter");
        Assert.Equal("before\n```hydra-sent\n[]\n```\nafter", result);
    }

    [Trait("Req", "PR-HYDRA-090")] // DroppyCode/Core/Models/Hydra.swift:1204-1207
    [Fact]
    public void MarkingDelegationBlockSent_replaces_the_whole_matched_range_CRLF_included_with_plain_LF()
    {
        var result = HydraPrompts.MarkingDelegationBlockSent("```  hydra \t\r\n[]\n```");
        Assert.Equal("```hydra-sent\n[]\n```", result);
    }

    [Trait("Req", "PR-HYDRA-090")] // DroppyCode/Core/Models/Hydra.swift:1204-1207
    [Fact]
    public void MarkingDelegationBlockSent_is_idempotent_and_does_nothing_with_no_opener()
    {
        var already = "```hydra-sent\n[]\n```";
        Assert.Equal(already, HydraPrompts.MarkingDelegationBlockSent(already));
        var noOpener = "no block here";
        Assert.Equal(noOpener, HydraPrompts.MarkingDelegationBlockSent(noOpener));
    }

    // ---- PR-HYDRA-091: markingDelegationBlockLeaving rewrites a sent opener to ```hydra-leaving, once ----

    [Trait("Req", "PR-HYDRA-091")] // DroppyCode/Core/Models/Hydra.swift:1197-1200
    [Fact]
    public void MarkingDelegationBlockLeaving_rewrites_a_sent_opener_to_exactly_hydra_leaving()
    {
        var result = HydraPrompts.MarkingDelegationBlockLeaving("```hydra-sent\n[]\n```");
        Assert.Equal("```hydra-leaving\n[]\n```", result);
    }

    [Trait("Req", "PR-HYDRA-091")] // DroppyCode/Core/Models/Hydra.swift:1197-1200
    [Fact]
    public void MarkingDelegationBlockLeaving_does_nothing_without_a_sent_block_or_when_already_leaving()
    {
        var notSent = "```hydra\n[]\n```";
        Assert.Equal(notSent, HydraPrompts.MarkingDelegationBlockLeaving(notSent));
        var alreadyLeaving = "```hydra-leaving\n[]\n```";
        Assert.Equal(alreadyLeaving, HydraPrompts.MarkingDelegationBlockLeaving(alreadyLeaving));
    }

    // ---- PR-HYDRA-092: hasSentDelegationBlock and hasLeavingDelegationBlock use narrower regexes ----

    [Trait("Req", "PR-HYDRA-092")] // DroppyCode/Core/Models/Hydra.swift:1185-1192
    [Fact]
    public void HasSentDelegationBlock_matches_sent_or_leaving_but_not_the_plain_hydra_opener()
    {
        Assert.True(HydraPrompts.HasSentDelegationBlock("```hydra-sent\n"));
        Assert.True(HydraPrompts.HasSentDelegationBlock("```HYDRA-LEAVING\n"));
        Assert.False(HydraPrompts.HasSentDelegationBlock("```hydra\n"));
    }

    [Trait("Req", "PR-HYDRA-092")] // DroppyCode/Core/Models/Hydra.swift:1185-1192
    [Fact]
    public void HasLeavingDelegationBlock_matches_only_leaving_and_not_sent()
    {
        Assert.True(HydraPrompts.HasLeavingDelegationBlock("```hydra-leaving\n"));
        Assert.False(HydraPrompts.HasLeavingDelegationBlock("```hydra-sent\n"));
    }

    [Trait("Req", "PR-HYDRA-092")] // DroppyCode/Core/Models/Hydra.swift:1185-1192
    [Fact]
    public void HasSentDelegationBlock_uses_contains_so_the_match_may_sit_anywhere_in_the_text()
    {
        Assert.True(HydraPrompts.HasSentDelegationBlock("some preceding reply text\n```hydra-sent\nrest"));
    }

    // ---- PR-HYDRA-093: the delegation card's hold and fade durations ----

    [Trait("Req", "PR-HYDRA-093")] // DroppyCode/Core/Models/Hydra.swift:1177
    [Fact]
    public void DelegationSentHold_is_exactly_3_point_2_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(3.2), HydraPrompts.DelegationSentHold);
    }

    [Trait("Req", "PR-HYDRA-093")] // DroppyCode/Core/Models/Hydra.swift:1181
    [Fact]
    public void DelegationLeaveFade_is_exactly_450_milliseconds()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(450), HydraPrompts.DelegationLeaveFade);
    }
    // The re-check-before-each-step sequencing (send -> hold -> mark leaving -> fade -> strip, each step
    // re-checking a sent block is still present) is driven by ThreadRuntime.swift's turn loop, not by
    // anything in Hydra.swift; it is untestable at this unit and left for the runtime unit that owns it.

    // ---- PR-HYDRA-097: the fallback policy's fenced-code-block prohibition, reproduced verbatim ----

    [Trait("Req", "PR-HYDRA-097")] // DroppyCode/Core/Models/Hydra.swift:967
    [Fact]
    public void FallbackPolicy_forbids_a_fenced_code_block_of_the_leads_own_inside_a_prompt()
    {
        var text = HydraPrompts.FallbackPolicy(null, isolated: true);

        Assert.Contains(
            "Inside a prompt never open a fenced code block of your own (three backticks would end the hydra block early and no head would go out): describe code in words, quote identifiers with single backticks, or indent a snippet by four spaces.",
            text, StringComparison.Ordinal);
    }

    // ---- PR-HYDRA-248: HydraDelegationBudget counts rounds, allows at most 3, resets only on a user turn ----

    [Trait("Req", "PR-HYDRA-248")] // DroppyCode/Core/Models/Hydra.swift:571
    [Fact]
    public void CanDelegate_is_true_below_MaxDelegationRounds_and_false_at_or_above_it()
    {
        var budget = new HydraDelegationBudget();
        Assert.True(budget.CanDelegate);
        budget = budget.ChargeRound().ChargeRound().ChargeRound();
        Assert.Equal(3, budget.Rounds);
        Assert.False(budget.CanDelegate);
    }

    [Trait("Req", "PR-HYDRA-248")] // DroppyCode/Core/Models/Hydra.swift:577-581
    [Fact]
    public void BeginTurn_resets_rounds_and_refusals_only_when_userAuthored()
    {
        var budget = new HydraDelegationBudget().ChargeRound().ChargeRound();
        var (afterRefuse, _) = budget.Refuse("m");
        budget = afterRefuse;
        Assert.Equal(2, budget.Rounds);
        Assert.Equal(1, budget.Refusals);

        var notReset = budget.BeginTurn(userAuthored: false);
        Assert.Equal(2, notReset.Rounds);
        Assert.Equal(1, notReset.Refusals);

        var reset = budget.BeginTurn(userAuthored: true);
        Assert.Equal(0, reset.Rounds);
        Assert.Equal(0, reset.Refusals);
    }

    [Trait("Req", "PR-HYDRA-248")] // DroppyCode/Core/Models/Hydra.swift:577-581
    [Fact]
    public void BeginTurn_never_clears_a_waiting_drop_notice_even_on_a_user_authored_turn()
    {
        var budget = new HydraDelegationBudget();
        for (var i = 0; i < 3; i++) (budget, _) = budget.Refuse("m" + i);
        (budget, _) = budget.Refuse("dropped"); // past maxRefusals: sets the drop notice
        Assert.NotNull(budget.DropNotice);

        var afterBeginTurn = budget.BeginTurn(userAuthored: true);
        Assert.NotNull(afterBeginTurn.DropNotice);
    }

    [Trait("Req", "PR-HYDRA-248")] // DroppyCode/Core/Models/Hydra.swift:584-586
    [Fact]
    public void ChargeRound_increments_rounds_by_one()
    {
        var budget = new HydraDelegationBudget();
        Assert.Equal(1, budget.ChargeRound().Rounds);
        Assert.Equal(2, budget.ChargeRound().ChargeRound().Rounds);
    }

    // ---- PR-HYDRA-249: past 3 refusals a block gets no turn of its own; its message becomes a one-shot drop notice ----

    [Trait("Req", "PR-HYDRA-249")] // DroppyCode/Core/Models/Hydra.swift:591-598
    [Fact]
    public void Refuse_the_first_three_times_returns_true_and_counts_a_refusal()
    {
        var budget = new HydraDelegationBudget();
        for (var i = 0; i < 3; i++)
        {
            var (next, result) = budget.Refuse("refused " + i);
            Assert.True(result);
            budget = next;
        }
        Assert.Equal(3, budget.Refusals);
        Assert.Null(budget.DropNotice);
    }

    [Trait("Req", "PR-HYDRA-249")] // DroppyCode/Core/Models/Hydra.swift:591-598
    [Fact]
    public void Refuse_past_the_cap_returns_false_counts_nothing_and_stores_the_drop_notice()
    {
        var budget = new HydraDelegationBudget();
        for (var i = 0; i < 3; i++) (budget, _) = budget.Refuse("m" + i);

        var (afterFourth, result) = budget.Refuse("the fourth message");
        Assert.False(result);
        Assert.Equal(3, afterFourth.Refusals);
        Assert.Equal(HydraPrompts.DroppedBlockNotice("the fourth message"), afterFourth.DropNotice);
    }

    [Trait("Req", "PR-HYDRA-249")] // DroppyCode/Core/Models/Hydra.swift:602-604
    [Fact]
    public void NoteDropped_stores_the_message_wrapped_as_a_drop_notice_and_a_later_drop_replaces_an_earlier_one()
    {
        var budget = new HydraDelegationBudget().NoteDropped("first");
        Assert.Equal(HydraPrompts.DroppedBlockNotice("first"), budget.DropNotice);

        var replaced = budget.NoteDropped("second");
        Assert.Equal(HydraPrompts.DroppedBlockNotice("second"), replaced.DropNotice);
    }

    [Trait("Req", "PR-HYDRA-249")] // DroppyCode/Core/Models/Hydra.swift:607-610
    [Fact]
    public void TakeDropNotice_returns_the_notice_once_and_clears_it()
    {
        var budget = new HydraDelegationBudget().NoteDropped("waiting");
        var (cleared, taken) = budget.TakeDropNotice();
        Assert.Equal(HydraPrompts.DroppedBlockNotice("waiting"), taken);
        Assert.Null(cleared.DropNotice);

        var (stillCleared, takenAgain) = cleared.TakeDropNotice();
        Assert.Null(takenAgain);
        Assert.Null(stillCleared.DropNotice);
    }

    [Trait("Req", "PR-HYDRA-249")] // DroppyCode/Core/Models/Hydra.swift:616
    [Fact]
    public void MaxRefusals_is_exactly_3()
    {
        Assert.Equal(3, HydraDelegationBudget.MaxRefusals);
    }

    // ---- PR-HYDRA-250: a dropped block's notice is '[Hydra] ' + message + blank line, '---', blank line ----

    [Trait("Req", "PR-HYDRA-250")] // DroppyCode/Core/Models/Hydra.swift:1090-1092
    [Fact]
    public void DroppedBlockNotice_is_exactly_Hydra_prefix_message_then_a_separator()
    {
        Assert.Equal("[Hydra] a message\n\n---\n\n", HydraPrompts.DroppedBlockNotice("a message"));
    }

    [Trait("Req", "PR-HYDRA-250")] // DroppyCode/Core/Models/Hydra.swift:1090-1092
    [Fact]
    public void DroppedBlockNotice_of_an_already_prefixed_unreadable_message_doubles_the_Hydra_prefix()
    {
        var message = "[Hydra] Your delegation block could not be read, so no heads went out.";
        Assert.Equal("[Hydra] [Hydra] Your delegation block could not be read, so no heads went out.\n\n---\n\n",
            HydraPrompts.DroppedBlockNotice(message));
    }

    // ---- PR-HYDRA-251: interruptedBlockMessage tells the lead its block's heads never went out ----

    [Trait("Req", "PR-HYDRA-251")] // DroppyCode/Core/Models/Hydra.swift:1077-1084
    [Fact]
    public void InterruptedBlockMessage_stopped_says_was_stopped()
    {
        Assert.Equal(
            "No heads went out.\nYour reply's delegation block was dropped: the turn was stopped before the heads could go out, and none of that work was delegated. Do it yourself, or send the block again.",
            HydraPrompts.InterruptedBlockMessage(stopped: true));
    }

    [Trait("Req", "PR-HYDRA-251")] // DroppyCode/Core/Models/Hydra.swift:1077-1084
    [Fact]
    public void InterruptedBlockMessage_not_stopped_says_failed()
    {
        Assert.Equal(
            "No heads went out.\nYour reply's delegation block was dropped: the turn failed before the heads could go out, and none of that work was delegated. Do it yourself, or send the block again.",
            HydraPrompts.InterruptedBlockMessage(stopped: false));
    }
}

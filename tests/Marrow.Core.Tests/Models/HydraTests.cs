using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Models;

// Source-derived tests for DroppyCode/Core/Models/Hydra.swift (B2 1fbf81e), unit Core.Models.Hydra, parts 1-2
// of 4. Every expected value is transcribed from the cited Swift lines or the record itself, never from the C#
// under test (R-TEST-4). Records proved: part 1, PR-HYDRA-001 to PR-HYDRA-024; part 2, PR-HYDRA-025 to
// PR-HYDRA-052 (excluding 029, 030, 041, 042, not named by this part's records).
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
            "[Marrow] You have run 24 tools without changing a file. If the task is research, reply with your findings now. Otherwise act on what you know: make the change, or reply with what blocks you.",
            HydraBudget.PacingNote);
    }

    [Trait("Req", "PR-HYDRA-014")] // DroppyCode/Core/Models/Hydra.swift:199
    [Fact]
    public void WrapUpNote_is_the_deviation_worded_note_verbatim()
    {
        Assert.Equal(
            "[Marrow] Your budget is nearly spent. Finish now: complete the smallest correct version of the task, then reply with your report.",
            HydraBudget.WrapUpNote);
    }

    [Trait("Req", "PR-HYDRA-015")] // DroppyCode/Core/Models/Hydra.swift:200
    [Fact]
    public void FinalNote_is_the_deviation_worded_note_verbatim()
    {
        Assert.Equal(
            "[Marrow] Your budget is spent and your tools are gone. Reply now with your report: what you changed, how far it got, and what is left.",
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
    [Fact]
    public void Words_are_exactly_the_seven_stage_sentences_in_order()
    {
        Assert.Equal(
            [
                "Gathering the team\u2019s files", // U+2019 RIGHT SINGLE QUOTATION MARK
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
        Assert.Contains("which Marrow then adds to the sidebar), and a head spawned with your own agent tool is told the project's folder in its prompt. Whatever a head changes lands", text, StringComparison.Ordinal);
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
            "Heads may work in any project. A head works in this chat's project unless it is sent to another: an entry of the delegation block names it with \"project\" (a sidebar project's name or path, or the absolute path of any git repository on this Mac, which Marrow then adds to the sidebar), and a head spawned with your own agent tool is told the project's folder in its prompt. Whatever a head changes lands in the checkout of the project it worked in, and the team's work is merged per project, one merge request each. Your own tools run in this chat's checkout, and you may read and edit files in any project all the same: what you change elsewhere is merged along with that project's work. Run your own checks in the checkout itself: never make a worktree or a copy of the project for them.",
            HydraPrompts.ProjectRule([]));
    }

    // ---- PR-HYDRA-036: HydraPrompts.AutoMergeRule is one exact paragraph ----

    [Trait("Req", "PR-HYDRA-036")] // DroppyCode/Core/Models/Hydra.swift:708
    [Fact]
    public void AutoMergeRule_is_the_exact_text_with_Marrow_in_place_of_Droppy_Code() // DEVIATION OD-0012 F1
    {
        Assert.Equal(
            "Marrow handles branches, commits and merge requests after all heads return and you finish. Never run or delegate those operations. For merge/status questions, use only the current [Hydra] note: if merged, give its link; if files remain, say they land when your answer ends; if merging failed or files are outside every project, give the stated reason and required user action. If nothing is pending, promise no merge. Never claim a merge without the note confirming it.",
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
            @"You have your own copy of the project at C:\worktrees\otto: a git worktree Marrow made for you from the project's checkout as it was when you were sent out, uncommitted work included. Work in it directly, on the files as they are; your tools already run there. When you report, Marrow carries your changes into that checkout itself. So never commit, branch, stash, push, check out, reset, restore or clean anything, and never make or remove worktrees, whatever the project's own guidelines say about agents and worktrees: this copy already is yours. Do not use git to check your work either.",
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
        Assert.Contains("unless asked. Two agent types are yours: `droppy-worker` (edits files, runs commands) and `droppy-scout` (read-only research). Spawn them with the Agent tool and prefer them over other agents while Hydra is on: they run on the model and effort the user chose for heads. The agents you spawn this way are the Hydra heads themselves: Marrow shows each one in the team's panel", text, StringComparison.Ordinal);
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

        Assert.Contains("Marrow names the heads in roster order (Hank, Walter, Ada, Otto, Nova, Remy, Iris, Milo, Juno, Ezra, Lena, Bo, Kai, Vera, Finn, Mira, Odin, Suki, Rex, Zola, Pip, Ivo, Lux, Tova, Gus, then Hank 2 and so on)", text, StringComparison.Ordinal);
    }

    [Trait("Req", "PR-HYDRA-047")] // DroppyCode/Core/Models/Hydra.swift:782 the reproduced contradiction, upstream defect
    [Fact]
    public void Policy_tells_the_lead_heads_never_go_out_through_a_fenced_block_and_that_Marrow_sends_them_that_way_anyway()
    {
        var text = HydraPrompts.Policy(ProviderKind.Claude, NativeLaunch(ProviderKind.Claude));

        Assert.Contains(
            "- Heads go out through the tools above, never through a fenced hydra block: that is the delegation format for providers without agent tools of their own. If you end a reply with one anyway, Marrow still sends those heads out as threads of their own, but your turn ends there and their reports come back as a later message.",
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
}

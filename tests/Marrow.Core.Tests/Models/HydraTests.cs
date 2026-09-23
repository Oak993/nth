using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Models;

// Source-derived tests for DroppyCode/Core/Models/Hydra.swift (B2 1fbf81e), unit Core.Models.Hydra, part 1 of 4.
// Every expected value is transcribed from the cited Swift lines or the record itself, never from the C# under
// test (R-TEST-4). Records proved here: PR-HYDRA-001 to PR-HYDRA-024.
//
// PR-HYDRA-016 (the fixed order of the in-turn budget checks and the out-of-turn backstop's allowance) describes
// runtime behaviour in AppModel+Hydra.swift / ThreadRuntime.swift (the turn loop and the backstop timer), not
// anything HydraBudget's six constants or Hydra.swift itself compute; this file, which is scoped to Hydra.swift,
// has no loop or timer to exercise it against. It is untestable at this unit and is left for the runtime unit
// that owns the turn loop.
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
}

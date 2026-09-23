using Marrow.Core.Models;
using Marrow.Core.Port;

// Source: DroppyCode/Services/Providers/ProviderRegistry.swift (B2 1fbf81e), :1-879. Skeleton: ProviderStatus's
// stored properties (:10-15), the Auth cases (:4-8), the observed properties (:33-67, :281, :284) and the seeds
// that are plain data (:134-142, :146, :153) are complete; the @ObservationIgnored and private members (:38-56,
// :61-63, :68-125, :157-160, :230-258, :381-425, :442-449, :521-601, :644-647, :788-878) and every member with
// behaviour are left to the implementing unit.

namespace Marrow.Services.Providers;

// :3-27. A Swift struct -> immutable record (R-VAL-1).
public sealed record ProviderStatus
{
    // :4-8 (R-ENUM-2). SignedIn's unlabeled String? is named after the binding summary gives it (`let account`).
    public abstract record Auth
    {
        private Auth() { }
        public sealed record Unknown : Auth;                      // :5
        public sealed record SignedIn(string? Account) : Auth;    // :6
        public sealed record SignedOut : Auth;                    // :7
    }

    public SwiftURL? Executable { get; init; }                         // :10
    public string? Version { get; init; }                              // :11
    public Auth ProviderStatusAuth { get; init; } = new Auth.Unknown(); // :12 Swift: auth (R-NAME-3)
    public bool IsChecking { get; init; }                              // :13
    public bool ApiKeyConfigured { get; init; }                        // :15

    // :17
    public bool IsInstalled =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:17");

    // :19-26
    public string Summary =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:19");
}

// :30-879. @MainActor @Observable -> MainActorModel (R-ISO-1, R-OBS-6).
public sealed class ProviderRegistry : MainActorModel
{
    private ValueMap<ProviderKind, ProviderStatus> statuses = ValueMap<ProviderKind, ProviderStatus>.Empty;
    private ValueMap<ProviderKind, ValueList<ModelOption>> catalogs = ValueMap<ProviderKind, ValueList<ModelOption>>.Empty;
    private ValueMap<string, ValueList<SlashCommand>> commands = new([], SwiftStringComparer.Instance);
    private ValueMap<string, string> commandErrors = new([], SwiftStringComparer.Instance);
    private ValueMap<string, ValueList<string>> recentCommands = new([], SwiftStringComparer.Instance);
    private ValueSet<ProviderKind> loadingCatalogs = ValueSet<ProviderKind>.Empty;
    private ValueMap<ProviderKind, PlanLimits> planLimits = ValueMap<ProviderKind, PlanLimits>.Empty;
    private ValueSet<ProviderKind> loadingLimits = ValueSet<ProviderKind>.Empty;
    private string? redeemingReset;
    private ValueMap<ProviderKind, ProviderCredits> credits = ValueMap<ProviderKind, ProviderCredits>.Empty;
    private ValueSet<ProviderKind> loadingCredits = ValueSet<ProviderKind>.Empty;
    private bool isRefreshing;
    private ProviderKind? signingIn;

    public ValueMap<ProviderKind, ProviderStatus> Statuses { get => Read(statuses); private set => Set(ref statuses, value); } // :33
    public ValueMap<ProviderKind, ValueList<ModelOption>> Catalogs { get => Read(catalogs); private set => Set(ref catalogs, value); } // :34
    public ValueMap<string, ValueList<SlashCommand>> Commands { get => Read(commands); private set => Set(ref commands, value); } // :35
    public ValueMap<string, string> CommandErrors { get => Read(commandErrors); private set => Set(ref commandErrors, value); } // :36
    public ValueMap<string, ValueList<string>> RecentCommands { get => Read(recentCommands); private set => Set(ref recentCommands, value); } // :37
    public ValueSet<ProviderKind> LoadingCatalogs { get => Read(loadingCatalogs); private set => Set(ref loadingCatalogs, value); } // :45
    public ValueMap<ProviderKind, PlanLimits> PlanLimits { get => Read(planLimits); private set => Set(ref planLimits, value); } // :57
    public ValueSet<ProviderKind> LoadingLimits { get => Read(loadingLimits); private set => Set(ref loadingLimits, value); } // :58
    public string? RedeemingReset { get => Read(redeemingReset); private set => Set(ref redeemingReset, value); } // :60
    public ValueMap<ProviderKind, ProviderCredits> Credits { get => Read(credits); private set => Set(ref credits, value); } // :66
    public ValueSet<ProviderKind> LoadingCredits { get => Read(loadingCredits); private set => Set(ref loadingCredits, value); } // :67
    public bool IsRefreshing { get => Read(isRefreshing); private set => Set(ref isRefreshing, value); } // :281
    public ProviderKind? SigningIn { get => Read(signingIn); private set => Set(ref signingIn, value); } // :284

    // :127. Built with DeepSeekAPI.option(for:).
    public static ValueList<ModelOption> DeepseekSeed =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:127");

    // :128-129. Built with MetaAPI.option(for:).
    public static ValueList<ModelOption> MetaSeed =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:128");

    // :130. Built with ZaiAPI.option(for:).
    public static ValueList<ModelOption> ZaiSeed =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:130");

    // :134-142
    public static readonly ValueList<ModelOption> AntigravitySeed = new(new[]
    {
        new ModelOption("gemini-3.8-flash", "Gemini 3.8 Flash", efforts: new(new[] { "low", "medium", "high" }), defaultEffort: "high", isDefault: true), // :135
        new ModelOption("gemini-3.7-flash", "Gemini 3.7 Flash", efforts: new(new[] { "low", "medium", "high" }), defaultEffort: "high"), // :136
        new ModelOption("gemini-3.6-flash", "Gemini 3.6 Flash", efforts: new(new[] { "low", "medium", "high" }), defaultEffort: "high"), // :137
        new ModelOption("gemini-3.1-pro", "Gemini 3.1 Pro", efforts: new(new[] { "low", "high" }), defaultEffort: "high"), // :138
        new ModelOption("claude-sonnet-4-6", "Claude Sonnet 4.6 (Thinking)", efforts: ValueList<string>.Empty), // :139
        new ModelOption("claude-opus-4-6-thinking", "Claude Opus 4.6 (Thinking)", efforts: ValueList<string>.Empty), // :140
        new ModelOption("gpt-oss-120b", "GPT-OSS 120B", efforts: new(new[] { "medium" }), defaultEffort: "medium"), // :141
    });

    // :146
    public static readonly ModelOption CopilotAuto =
        new("auto", "Auto", detail: "Copilot picks the model for each request", isDefault: true);

    // :150. CommandCodeAPI.seed.
    public static ValueList<ModelOption> CommandcodeSeed =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:150");

    // :153
    public static readonly ValueList<ModelOption> PiSeed = new(new[]
    {
        new ModelOption("default", "Default", detail: "The model pi itself is set to",
            efforts: new(new[] { "off", "minimal", "low", "medium", "high", "xhigh", "max" }), defaultEffort: "medium", isDefault: true),
    });

    // :162-196
    public ProviderRegistry(ProviderSettings settings) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:162");

    // :198-200
    public ValueList<ProviderKind> AvailableProviders =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:198");

    // :206-208
    public bool CountsAsInstalled(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:206");

    // :210-217
    public ProviderStatus Status(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:210");

    // :219-226. Swift: executable(for:).
    public SwiftURL? Executable(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:219");

    // :260-279. Swift: environment(for:).
    public ValueMap<string, string> Environment(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:260");

    // :289-310
    public Task<string?> SignIn(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:289");

    // :315-333
    public Task RefreshEverything() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:315");

    // :340-356
    public Task RefreshAll() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:340");

    // :358-378
    public Task Refresh(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:358");

    // :429-431. Swift: models(for:).
    public ValueList<ModelOption> Models(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:429");

    // :433-440. Swift: model(_:for:).
    public ModelOption? Model(string? id, ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:433");

    // :451-457. Swift: defaultModel(for:).
    public ModelOption? DefaultModel(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:451");

    // :459-517
    public Task LoadCatalog(ProviderKind provider, bool force = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:459");

    // :606-641
    public void RefreshPlanLimits(ProviderKind provider, bool force = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:606");

    // :650-676
    public Task<PlanLimits.ResetOutcome> ConsumeResetCredit(ProviderKind provider, string? creditID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:650");

    // :681-700
    public void RefreshCredits(ProviderKind provider, bool force = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:681");

    // :704-707
    public void InvalidateCredits(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:704");

    // :712-715
    public void InvalidatePlanLimits(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:712");

    // :717-734. Swift: updateCatalog(_:for:).
    public void UpdateCatalog(ValueList<ModelOption> list, ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:717");

    // :736-738
    public static string CommandKey(ProviderKind provider, string directory) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:736");

    // :740-751. Swift: updateCommands(_:for:directory:).
    public void UpdateCommands(ValueList<SlashCommand> list, ProviderKind provider, string directory) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:740");

    // :753-759. Swift: recordCommand(_:for:).
    public void RecordCommand(string name, ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:753");

    // :762-784
    public Task LoadCommands(ProviderKind provider, string directory, bool force = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderRegistry.swift:762");
}

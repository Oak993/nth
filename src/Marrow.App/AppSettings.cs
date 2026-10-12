using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.Settings;
using Marrow.Core.Port.Voice;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.UI.Port;
using Marrow.UI.Theme;

// Source: DroppyCode/App/AppSettings.swift (B3 52bc792), :1-951. Skeleton (c100 group 1): the four enums' cases
// (:3-93), the ModelPin and ModelPreference shapes (:96-107), the defaults keys (:117-180), the constants (:115,
// :182, :299) and every stored property's shape are complete; every didSet, every computed property, the init
// (:707-790), the Keychain fill (:402-498) and every method are left to the implementing unit. Forms: CGSize is
// Avalonia.Size, CGPoint is Avalonia.Point, CGFloat is double (R-VIEW-11, ED-095); `defaults` is
// UserDefaults.Standard (CaptureRun.defaults is NOT-PORTED, ED-001; B2 named the same NOT-PORTED indirection
// WebsiteCaptures.defaults); a Swift Int is long (R-NUM-4).
// The `private var keyFallbacks` tuple (:424-431) and the private helpers store/load (:864-871) are left to the
// implementing unit.
//
// B3 (impact R3-48 APIKeychain #1, PR-PROVAPI-192, PR-SETTINGS-088/089/272): the four key observers' didSet bodies
// and the four per-provider readKeys calls now go through one shared storeAPIKeyChange(:483-487)/storedKey(:443-
// 445) pair over the new Services.Providers.APIKeychain, which takes a ProviderKind instead of an account string;
// the deleted DeepSeekKeychain/MetaKeychain/ZaiKeychain/CommandCodeKeychain wrapper types are gone from this file's
// callers. Citation only: the trim, fallback-write, legacy-removal and refusal behaviour is unchanged.

namespace Marrow.App;

// :3-19 (R-ENUM-1)
public enum TextGenerationChoice
{
    [RawValue("automatic")] Automatic,
    [RawValue("claude")] Claude,
    [RawValue("codex")] Codex,
    [RawValue("off")] Off,
}

// :9-18. Members of an enum -> static extension class (R-NAME-4).
public static class TextGenerationChoiceExtensions
{
    extension(TextGenerationChoice choice)
    {
        // :9
        public string Id => SwiftEnum<TextGenerationChoice>.RawValue(choice);

        // :11-18. PR-SETTINGS-106.
        public string Title => choice switch
        {
            TextGenerationChoice.Automatic => "Automatic", // :13
            TextGenerationChoice.Claude => "Claude",       // :14
            TextGenerationChoice.Codex => "Codex",         // :15
            TextGenerationChoice.Off => "Off",             // :16
            _ => throw new ArgumentOutOfRangeException(nameof(choice), choice, null),
        };
    }
}

// :22-43 (R-ENUM-1)
public enum ThreadFinishAction
{
    [RawValue("settle")] Settle,   // :24
    [RawValue("archive")] Archive, // :26
}

public static class ThreadFinishActionExtensions
{
    extension(ThreadFinishAction action)
    {
        // :28
        public string Id => SwiftEnum<ThreadFinishAction>.RawValue(action);

        // :30-35. PR-SETTINGS-253, PR-SETTINGS-254.
        public string Title => action switch
        {
            ThreadFinishAction.Settle => "Settle",   // :32
            ThreadFinishAction.Archive => "Archive", // :33
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };

        // :37-42. PR-SETTINGS-253, PR-SETTINGS-254.
        public string Detail => action switch
        {
            ThreadFinishAction.Settle =>
                "A settled thread drops to the bottom of the sidebar, small and grey, until you reopen it", // :39
            ThreadFinishAction.Archive =>
                "An archived thread leaves the sidebar for the Archive page", // :40
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };
    }
}

// :46-68 (R-ENUM-1)
public enum SidebarMode
{
    [RawValue("column")] Column,
    [RawValue("floating")] Floating,
    [RawValue("panelOnly")] PanelOnly,
}

public static class SidebarModeExtensions
{
    extension(SidebarMode mode)
    {
        // :51
        public string Id => SwiftEnum<SidebarMode>.RawValue(mode);

        // :53-59. PR-SETTINGS-255, PR-SETTINGS-256, PR-SETTINGS-257.
        public string Title => mode switch
        {
            SidebarMode.Column => "Column",       // :55
            SidebarMode.Floating => "Floating",   // :56
            SidebarMode.PanelOnly => "Panel only", // :57
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };

        // :61-67. PR-SETTINGS-255, PR-SETTINGS-256, PR-SETTINGS-257.
        public string Detail => mode switch
        {
            SidebarMode.Column =>
                "A column beside the chat; the list button in the toolbar shows and hides it", // :63
            SidebarMode.Floating =>
                "With the column collapsed, the thread list floats as a panel over the chat and stays where you leave it", // :64
            SidebarMode.PanelOnly =>
                "No column and no toolbar button; the floating panel is the sidebar", // :65
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }
}

// :72-93 (R-ENUM-1)
public enum ActivityThreadStyle
{
    [RawValue("icon")] Icon,               // :74
    [RawValue("projectName")] ProjectName, // :76
}

public static class ActivityThreadStyleExtensions
{
    extension(ActivityThreadStyle style)
    {
        // :78
        public string Id => SwiftEnum<ActivityThreadStyle>.RawValue(style);

        // :80-85. PR-SETTINGS-258, PR-SETTINGS-259.
        public string Title => style switch
        {
            ActivityThreadStyle.Icon => "Icon",               // :82
            ActivityThreadStyle.ProjectName => "Project name", // :83
            _ => throw new ArgumentOutOfRangeException(nameof(style), style, null),
        };

        // :87-92. PR-SETTINGS-258, PR-SETTINGS-259.
        public string Detail => style switch
        {
            ActivityThreadStyle.Icon =>
                "Each project's emoji or symbol before the thread's name, one line per thread", // :89
            ActivityThreadStyle.ProjectName =>
                "The project's name, with a folder mark, on a line under the thread's title", // :90
            _ => throw new ArgumentOutOfRangeException(nameof(style), style, null),
        };
    }
}

// :96-101. ED-149: a readonly record struct, the one named exception to R-VAL-1. Its JSON converter writes and
// reads only "provider" and "modelID", as the synthesized Codable does (ED-149 (2)); Id is never encoded.
[JsonConverter(typeof(ModelPinConverter))]
public readonly record struct ModelPin(ProviderKind Provider, string ModelID) // :97-98
{
    // :100
    public string Id => SwiftEnum<ProviderKind>.RawValue(Provider) + "/" + ModelID;
}

// PORT-ONLY: ModelPin's synthesized Codable (ED-149 (2)): reads and writes exactly "provider" and "modelID".
internal sealed class ModelPinConverter : JsonConverter<ModelPin>
{
    public override ModelPin Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static ModelPin Decode(JsonElement element) => new(
        SwiftDecoding.Decode(element, "provider", SwiftDecoding.ReadRawValue<ProviderKind>),
        SwiftDecoding.Decode(element, "modelID", SwiftDecoding.ReadString));

    public override void Write(Utf8JsonWriter writer, ModelPin value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        WriteMembers(writer, value);
        writer.WriteEndObject();
    }

    internal static void WriteMembers(Utf8JsonWriter writer, ModelPin value)
    {
        writer.WriteString("provider", SwiftEnum<ProviderKind>.RawValue(value.Provider));
        writer.WriteString("modelID", value.ModelID);
    }
}

// :104-107 (R-VAL-1)
[JsonConverter(typeof(ModelPreferenceConverter))]
public sealed record ModelPreference
{
    public string? Effort { get; init; }        // :105
    public bool FastMode { get; init; } = false; // :106
}

// PORT-ONLY: ModelPreference's synthesized Codable: "effort" (optional, omitted when nil) and "fastMode".
internal sealed class ModelPreferenceConverter : JsonConverter<ModelPreference>
{
    public override ModelPreference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static ModelPreference Decode(JsonElement element) => new()
    {
        Effort = Coding.Value(element, "effort", SwiftDecoding.ReadString, () => (string?)null),
        FastMode = Coding.Value(element, "fastMode", SwiftDecoding.ReadBool, () => false),
    };

    public override void Write(Utf8JsonWriter writer, ModelPreference value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.Effort is { } effort) writer.WriteString("effort", effort);
        writer.WriteBoolean("fastMode", value.FastMode);
        writer.WriteEndObject();
    }
}

// :110-944. @MainActor @Observable -> MainActorModel (R-OBS-6); :947 extension AppSettings: ProviderSettings.
public sealed partial class AppSettings : MainActorModel, ProviderSettings, AccountProviderSettings
{
    // :115
    public const string HydraDefaultEnabledKey = ChatThread.HydraDefaultKey;

    // :117-180
    private static class Key
    {
        public const string DefaultProvider = "defaultProvider";
        public const string RuntimeMode = "defaultRuntimeMode";
        public const string WorkspaceMode = "defaultWorkspaceMode";
        public const string Notify = "notifyWhenFinished";
        public const string Chime = "chimeWhenFinished";
        public const string ConfirmDelete = "confirmBeforeDeleting";
        public const string AutoContinueAfterLimit = "autoContinueAfterLimit";
        public const string ThreadFinishAction = "threadFinishAction";
        public const string SettleSound = "settleSound";
        public const string ShowReasoning = "showReasoning";
        public const string ChronologicalTimeline = "chronologicalTimeline";
        public const string AntiSlopEnabled = "antiSlopEnabled";
        public const string ShowsWorkingCard = "showsWorkingCard";
        public const string AppZoom = "appZoom";
        public const string SidebarActivityView = "sidebarActivityView";
        public const string ActivityThreadStyle = "activityThreadStyle";
        public const string SettledCollapsed = "settledSectionCollapsed";
        public const string AppTheme = "appTheme";
        public const string Accent = "accent"; // PORT-ONLY (OD-0088, ED-250): the accent picked in Settings > Appearance
        public const string BackdropOpacity = "backdropOpacity";
        public const string Appearance = "appearance";
        public const string BinaryPaths = "providerBinaryPaths";
        public const string DisabledProviders = "disabledProviders";
        public const string Models = "lastModels";
        public const string Efforts = "lastEfforts";
        public const string LastHydraPair = "lastHydraPairID";
        public const string TextGeneration = "textGeneration";
        public const string CommitInstructions = "commitInstructions";
        public const string TerminalHeight = "terminalHeight";
        public const string ModelList = "modelList";
        public const string ModelPreferences = "modelPreferences";
        public const string RecentDownloadsPicker = "recentDownloadsPicker";
        public const string LastProjectID = "lastProjectID";
        public const string ProjectsEnabled = "projectsEnabled";
        public const string ProjectActivationOverrides = "projectActivationOverrides";
        public const string DeepseekAPIKey = "deepseekAPIKey";
        public const string MetaAPIKey = "metaAPIKey";
        public const string ZaiAPIKey = "zaiAPIKey";
        public const string CommandcodeAPIKey = "commandcodeAPIKey";
        public const string OpenRouterAPIKey = "openRouterAPIKey"; // PORT-ONLY (ED-234): only ever cleared, as the other four are
        public const string HydraEnabled = "hydraEnabled";
        public const string PanelSize = "floatingPanelSize";
        public const string HydraDefaultEnabled = AppSettings.HydraDefaultEnabledKey;
        public const string HydraQueueHeads = "hydraQueueHeads";
        public const string HydraAlwaysHeads = "hydraAlwaysHeads";
        public const string HydraIsolateHeads = "hydraIsolateHeads";
        public const string HydraAutoMerge = "hydraAutoMerge";
        public const string HydraReviewHeads = "hydraReviewHeads";
        public const string HydraAutoClearFinished = "hydraAutoClearFinished";
        public const string HydraAutoHidesIdleHeads = "hydraAutoHidesIdleHeads";
        public const string HydraShowsHeadDetails = "hydraShowsHeadDetails";
        public const string HydraAutoPopsHeads = "hydraAutoPopsHeads";
        public const string HydraTempersHeadEffort = "hydraTempersHeadEffort";
        public const string HydraMaxHeads = "hydraMaxHeads";
        public const string ShowsUsagePanel = "showsUsagePanel";
        public const string SidebarFloats = "sidebarFloats";
        public const string SidebarOnlyFloats = "sidebarOnlyFloats";
        public const string SidebarPanelOrigin = "sidebarPanelOrigin";
        public const string SidebarPanelSize = "sidebarPanelSize";
        public const string UsagePanelDock = "usagePanelDock";
        public const string UsagePanelHeight = "usagePanelHeight";
        public const string HydraPairs = "hydraPairs";
        public const string HasSeenTour = "hasSeenTour";
        public const string HasSeenHydraIntro = "hasSeenHydraIntro";
        public const string ComposerLight = ComposerLightSettings.StoreKey; // PORT-ONLY, owner-ruled (OD-0089, ED-251)
        // PORT-ONLY, first-release SA-1 (voice-SPEC V-16, customization-SPEC CZ-13.3, CZ-13.4): the key names the records own.
        public const string VoiceOrb = VoiceOrbSettings.StoreKey;
        public const string VoiceTyping = VoiceTypingSettings.StoreKey;
        public const string VoiceHardware = VoiceHardwareRecord.StoreKey;
        public const string VoiceNotes = VoiceNotesSettings.StoreKey;
        public const string WorkingDots = WorkingDotsSettings.StoreKey;
    }

    // :182
    public const long ModelListLimit = 15;

    // :184. CaptureRun is NOT-PORTED (ED-001): always UserDefaults.Standard. B2 named this same indirection
    // WebsiteCaptures.defaults (:184, :708, :869 at B3); the rename carries no behaviour change.
    private readonly ISettingsStore defaults = UserDefaults.Standard.Current;
    // PORT-ONLY: OD-0114 / ED-404. Read settings only; never inspect or move CLI credentials during migration.
    private AccountStore? accounts;
    public AccountStore Accounts
    {
        get
        {
            if (accounts is not null) return accounts;
            AccountDefaults Defaults(ProviderKind provider) => new()
            {
                Model = LastModel(provider),
                Effort = LastEffort(provider) ?? Preference(provider, LastModel(provider)).Effort,
                FastMode = Preference(provider, LastModel(provider)).FastMode,
            };
            try { accounts = new AccountStore(defaults, Defaults); }
            catch (InvalidDataException) { accounts = AccountStore.LegacyReadOnly(defaults, Defaults); }
            return accounts;
        }
    }

    // :299
    public const double DefaultBackdropOpacity = 0.5;

    // :407-415
    private sealed record StoredKey
    {
        public required string Value { get; init; }    // :408
        public required bool InKeychain { get; init; } // :409
    }

    // :417-422
    private sealed record StoredKeys
    {
        public required StoredKey Deepseek { get; init; }    // :418
        public required StoredKey Meta { get; init; }        // :419
        public required StoredKey Zai { get; init; }         // :420
        public required StoredKey Commandcode { get; init; } // :421
        public required StoredKey OpenRouter { get; init; }  // PORT-ONLY (ED-234)
    }

    // OD-0012 E5: the one line shown under a key field whose key the protected store refused.
    internal const string CredentialRefusalLine = "Credential Manager did not accept this key, so it was not saved.";

    // :402-403
    private bool didReadKeychain;
    private bool isReadingKeychain;

    // PORT-ONLY: per-provider refusal state (OD-0008 A6, OD-0012 E5). Not a Swift stored property: upstream has
    // no refusal text at all (:371, :485-494 write silently). Session-only, never persisted.
    private readonly Dictionary<ProviderKind, string> apiKeyRefusals = [];

    // OD-0012 E5. Null when the provider's key was accepted, cleared, or never edited.
    public string? APIKeyRefusal(ProviderKind provider) => apiKeyRefusals.GetValueOrDefault(provider);

    private void SetAPIKeyRefusal(ProviderKind provider, bool refused)
    {
        if (refused) apiKeyRefusals[provider] = CredentialRefusalLine;
        else apiKeyRefusals.Remove(provider);
    }

    private ProviderKind defaultProvider;
    private RuntimeMode defaultRuntimeMode;
    private WorkspaceMode defaultWorkspaceMode;
    private bool notifyWhenFinished;
    private bool chimeWhenFinished;
    private bool confirmBeforeDeleting;
    private bool autoContinueAfterLimit;
    private ThreadFinishAction threadFinishAction;
    private bool settleSound;
    private bool showReasoning;
    private bool chronologicalTimeline;
    private bool antiSlopEnabled;
    private bool showsWorkingCard;
    private long appZoom;
    private Size? panelSize;
    private bool sidebarActivityView;
    private ActivityThreadStyle activityThreadStyle;
    private bool settledCollapsed;
    private AppTheme theme;
    private string accent = AccentPalette.WindowsId;
    private double backdropOpacity;
    private ComposerLightSettings composerLight;
    private VoiceOrbSettings voiceOrb = VoiceOrbSettings.Default;
    private VoiceTypingSettings voiceTyping = VoiceTypingSettings.Default;
    private VoiceHardwareRecord? voiceHardware;
    private VoiceNotesSettings voiceNotes = VoiceNotesSettings.Default;
    private WorkingDotsSettings workingDots = WorkingDotsSettings.Default;
    private TextGenerationChoice textGeneration;
    private string commitInstructions = "";
    private double terminalHeight;
    private bool recentDownloadsPicker;
    private Guid? lastProjectID;
    private bool projectsEnabled;
    private ValueMap<string, bool> projectActivationOverrides = ValueMap<string, bool>.Empty;
    private string deepseekAPIKeyInput = "";
    private string metaAPIKeyInput = "";
    private string zaiAPIKeyInput = "";
    private string commandcodeAPIKeyInput = "";
    private string openRouterAPIKeyInput = "";
    private ValueMap<string, string> binaryPaths = ValueMap<string, string>.Empty;
    private ValueList<string> disabledProviders = ValueList<string>.Empty;
    private ValueMap<string, string> lastModels = ValueMap<string, string>.Empty;
    private ValueMap<string, string> lastEfforts = ValueMap<string, string>.Empty;
    private Guid? lastHydraPairID;
    private ValueList<ModelPin> modelList = ValueList<ModelPin>.Empty;
    private ValueMap<string, ModelPreference> modelPreferences = ValueMap<string, ModelPreference>.Empty;
    private bool hydraEnabled;
    private bool hydraDefaultEnabled;
    private bool hasSeenTour;
    private bool hasSeenHydraIntro;
    private bool hydraQueueHeads;
    private bool hydraAlwaysHeads;
    private bool hydraIsolateHeads;
    private long? hydraMaxHeads;
    private bool hydraAutoMerge;
    private bool hydraReviewHeads;
    private bool hydraAutoClearFinished;
    private bool hydraAutoHidesIdleHeads;
    private bool hydraShowsHeadDetails;
    private bool hydraAutoPopsHeads;
    private bool hydraTempersHeadEffort;
    private bool showsUsagePanel;
    private bool sidebarFloats;
    private bool sidebarOnlyFloats;
    private Point? sidebarPanelOrigin;
    private Size? sidebarPanelSize;
    private PanelDockCorner usagePanelDock;
    private double? usagePanelHeight;
    private ValueList<HydraPair> hydraPairs = ValueList<HydraPair>.Empty;

    // :189-191
    public ProviderKind DefaultProvider
    {
        get => Read(defaultProvider);
        set => Set(ref defaultProvider, value, _ => defaults.Set(Key.DefaultProvider, SwiftEnum<ProviderKind>.RawValue(value)));
    }

    // :193-195
    public RuntimeMode DefaultRuntimeMode
    {
        get => Read(defaultRuntimeMode);
        set => Set(ref defaultRuntimeMode, value, _ => defaults.Set(Key.RuntimeMode, SwiftEnum<RuntimeMode>.RawValue(value)));
    }

    // :197-199
    public WorkspaceMode DefaultWorkspaceMode
    {
        get => Read(defaultWorkspaceMode);
        set => Set(ref defaultWorkspaceMode, value, _ => defaults.Set(Key.WorkspaceMode, SwiftEnum<WorkspaceMode>.RawValue(value)));
    }

    // :201-203
    public bool NotifyWhenFinished
    {
        get => Read(notifyWhenFinished);
        set => Set(ref notifyWhenFinished, value, _ => defaults.Set(Key.Notify, value));
    }

    // :206-208
    public bool ChimeWhenFinished
    {
        get => Read(chimeWhenFinished);
        set => Set(ref chimeWhenFinished, value, _ => defaults.Set(Key.Chime, value));
    }

    // :210-212
    public bool ConfirmBeforeDeleting
    {
        get => Read(confirmBeforeDeleting);
        set => Set(ref confirmBeforeDeleting, value, _ => defaults.Set(Key.ConfirmDelete, value));
    }

    // :216-218
    public bool AutoContinueAfterLimit
    {
        get => Read(autoContinueAfterLimit);
        set => Set(ref autoContinueAfterLimit, value, _ => defaults.Set(Key.AutoContinueAfterLimit, value));
    }

    // :221-223
    public ThreadFinishAction ThreadFinishAction
    {
        get => Read(threadFinishAction);
        set => Set(ref threadFinishAction, value, _ => defaults.Set(Key.ThreadFinishAction, SwiftEnum<ThreadFinishAction>.RawValue(value)));
    }

    // :226-228
    public bool SettleSound
    {
        get => Read(settleSound);
        set => Set(ref settleSound, value, _ => defaults.Set(Key.SettleSound, value));
    }

    // :230-232
    public bool ShowReasoning
    {
        get => Read(showReasoning);
        set => Set(ref showReasoning, value, _ => defaults.Set(Key.ShowReasoning, value));
    }

    // :237-239
    public bool ChronologicalTimeline
    {
        get => Read(chronologicalTimeline);
        set => Set(ref chronologicalTimeline, value, _ => defaults.Set(Key.ChronologicalTimeline, value));
    }

    // :241-243
    public bool AntiSlopEnabled
    {
        get => Read(antiSlopEnabled);
        set => Set(ref antiSlopEnabled, value, _ => defaults.Set(Key.AntiSlopEnabled, value));
    }

    // :247-249
    public bool ShowsWorkingCard
    {
        get => Read(showsWorkingCard);
        set => Set(ref showsWorkingCard, value, _ => defaults.Set(Key.ShowsWorkingCard, value));
    }

    // :253-255. The property shadows the static class Marrow.UI.Common.AppZoom inside this class: the body
    // reaches that class fully qualified.
    public long AppZoom
    {
        get => Read(appZoom);
        set => Set(ref appZoom, value, _ => defaults.Set(Key.AppZoom, value));
    }

    // :260-268
    public Size? PanelSize
    {
        get => Read(panelSize);
        set => Set(ref panelSize, value, _ =>
        {
            if (value is { } size) defaults.Set(Key.PanelSize, new object[] { size.Width, size.Height });
            else defaults.RemoveObject(Key.PanelSize);
        });
    }

    // :271-273
    public bool SidebarActivityView
    {
        get => Read(sidebarActivityView);
        set => Set(ref sidebarActivityView, value, _ => defaults.Set(Key.SidebarActivityView, value));
    }

    // :277-279
    public ActivityThreadStyle ActivityThreadStyle
    {
        get => Read(activityThreadStyle);
        set => Set(ref activityThreadStyle, value,
            _ => defaults.Set(Key.ActivityThreadStyle, SwiftEnum<ActivityThreadStyle>.RawValue(value)));
    }

    // :282-284
    public bool SettledCollapsed
    {
        get => Read(settledCollapsed);
        set => Set(ref settledCollapsed, value, _ => defaults.Set(Key.SettledCollapsed, value));
    }

    // :286-291
    public AppTheme Theme
    {
        get => Read(theme);
        set => Set(ref theme, value, _ =>
        {
            defaults.Set(Key.AppTheme, SwiftEnum<AppTheme>.RawValue(value));
            ThemeManager.Current = value;
        });
    }

    // PORT-ONLY (OD-0088, OD-0075; ED-250): the accent the user picked (Port/AccentPalette.cs ids), mirrored into
    // AccentChoice as the theme is into ThemeManager (:286-291). "windows" is the default accent: the Windows accent
    // under System, Light and Dark, the theme's own under a named theme. Independent of the theme: a theme change keeps it.
    public string Accent
    {
        get => Read(accent);
        set => Set(ref accent, AccentPalette.Normalize(value), _ =>
        {
            defaults.Set(Key.Accent, accent);
            AccentChoice.Current = accent;
        });
    }

    // :296-298
    public double BackdropOpacity
    {
        get => Read(backdropOpacity);
        set => Set(ref backdropOpacity, value, _ => defaults.Set(Key.BackdropOpacity, value));
    }

    // PORT-ONLY, owner-ruled addition (OD-0083, OD-0085, OD-0089; ED-251): the light around every composer, Border beam by
    // default, chosen and tuned in Settings > Customization. Stored as one JSON object (the playground's keys); a change
    // applies to every composer at once (each ComposerLightSlot reads this property).
    public ComposerLightSettings ComposerLight
    {
        get => Read(composerLight);
        set
        {
            var clamped = value.Clamped();
            Set(ref composerLight, clamped, _ => Store(Key.ComposerLight, clamped.ToJson()));
        }
    }

    // PORT-ONLY, owner-ruled additions of the first release (SA-1; voice-SPEC V-16.1, V-16.2, V-16.4, V-16.5; customization-SPEC
    // CZ-13.3, CZ-13.4). Each is one JSON object under its own key, written by the record that owns the format (VT-1 for the voice
    // keys) and read leniently: unreadable data is the default. A change is current in memory at once and reaches the store through
    // the coalescer (CZ-13.5).

    /// <summary><c>voiceOrb</c> (V-16.1): the orb, its attachment, what happens after voice input, the pill words, the light and the language default.</summary>
    public VoiceOrbSettings VoiceOrb
    {
        get => Read(voiceOrb);
        set => Set(ref voiceOrb, value, _ => Store(Key.VoiceOrb, value.ToJson()));
    }

    /// <summary><c>voiceTyping</c> (V-16.2): the engine, language, model, microphone and the slow-PC answer.</summary>
    public VoiceTypingSettings VoiceTyping
    {
        get => Read(voiceTyping);
        set => Set(ref voiceTyping, value, _ => { Marrow.App.Port.Voice.VoiceTypingMirror.Publish(defaults, value); Store(Key.VoiceTyping, value.ToJson()); });
    }

    /// <summary><c>voiceHardware</c> (V-16.4): the stored result of the hardware check; null until one has run (setting null removes the key).</summary>
    public VoiceHardwareRecord? VoiceHardware
    {
        get => Read(voiceHardware);
        set => Set(ref voiceHardware, value, _ => Store(Key.VoiceHardware, value?.ToJson()));
    }

    /// <summary><c>voiceNotes</c> (V-16.5): whether each engine one-time privacy note has been shown.</summary>
    public VoiceNotesSettings VoiceNotes
    {
        get => Read(voiceNotes);
        set => Set(ref voiceNotes, value, _ => Store(Key.VoiceNotes, value.ToJson()));
    }

    /// <summary><c>workingDots</c> (CZ-13.3): where the composer working dots take their colour from.</summary>
    public WorkingDotsSettings WorkingDots
    {
        get => Read(workingDots);
        set => Set(ref workingDots, value, _ => Store(Key.WorkingDots, value.ToJson()));
    }

    // PORT-ONLY (CZ-13.5): the one place the keys above and composerLight reach the store: the in-memory value is already current,
    // and the store is written at most every 250 ms per key while values keep changing.
    private readonly SettingsWriteCoalescer writes;

    private void Store(string key, object? value) => writes.Write(key, value);

    /// <summary>
    /// PORT-ONLY (CZ-13.5): writes every value still waiting for its interval, now. A page calls it when a drag ends, and the app on exit.
    /// </summary>
    public void FlushPendingWrites() => writes.Flush();

    // :301-303
    public TextGenerationChoice TextGeneration
    {
        get => Read(textGeneration);
        set => Set(ref textGeneration, value,
            _ => defaults.Set(Key.TextGeneration, SwiftEnum<TextGenerationChoice>.RawValue(value)));
    }

    // :305-307
    public string CommitInstructions
    {
        get => Read(commitInstructions);
        set => Set(ref commitInstructions, value, _ => defaults.Set(Key.CommitInstructions, value));
    }

    // :309-311
    public double TerminalHeight
    {
        get => Read(terminalHeight);
        set => Set(ref terminalHeight, value, _ => defaults.Set(Key.TerminalHeight, value));
    }

    // :314-316
    public bool RecentDownloadsPicker
    {
        get => Read(recentDownloadsPicker);
        set => Set(ref recentDownloadsPicker, value, _ => defaults.Set(Key.RecentDownloadsPicker, value));
    }

    // :320-328
    public Guid? LastProjectID
    {
        get => Read(lastProjectID);
        set => Set(ref lastProjectID, value, _ =>
        {
            if (value is { } id) defaults.Set(Key.LastProjectID, SwiftUUID.UuidString(id));
            else defaults.RemoveObject(Key.LastProjectID);
        });
    }

    // :333-335
    public bool ProjectsEnabled
    {
        get => Read(projectsEnabled);
        set => Set(ref projectsEnabled, value, _ => defaults.Set(Key.ProjectsEnabled, value));
    }

    // :339-341. Keys are project UUID strings.
    public ValueMap<string, bool> ProjectActivationOverrides
    {
        get => Read(projectActivationOverrides);
        private set => Set(ref projectActivationOverrides, value,
            _ => StoreObject(value, Key.ProjectActivationOverrides, (writer, active) => writer.WriteBooleanValue(active)));
    }

    // :344-346
    public bool IsProjectActive(Guid id) =>
        projectActivationOverrides.TryGetValue(SwiftUUID.UuidString(id), out var active) ? active : ProjectsEnabled;

    // :349-351. Swift: setProjectActive(_:for:).
    public void SetProjectActive(bool active, Guid id) =>
        ProjectActivationOverrides = projectActivationOverrides.Mutating(map => map[SwiftUUID.UuidString(id)] = active);

    // :354-356. Swift: clearProjectActivationOverride(for:).
    public void ClearProjectActivationOverride(Guid id) =>
        ProjectActivationOverrides = projectActivationOverrides.Mutating(map => map.Remove(SwiftUUID.UuidString(id)));

    // :359-361. Swift: hasProjectActivationOverride(for:).
    public bool HasProjectActivationOverride(Guid id) =>
        projectActivationOverrides.ContainsKey(SwiftUUID.UuidString(id));

    // :367-371. Named deviation OD-0008 A6, B3; OD-0012 E5 (PR-PROVAPI-008, PR-PROVREG-239): the protected store
    // is written; a key it refuses is reported (APIKeyRefusal) and never kept in plain text anywhere. At B3 the
    // didSet body is one call into the shared StoreAPIKeyChange (:483-487), replacing the deleted DeepSeekKeychain
    // wrapper (PR-PROVAPI-192).
    public string DeepseekAPIKeyInput
    {
        get => Read(deepseekAPIKeyInput);
        set => Set(ref deepseekAPIKeyInput, value, oldValue =>
            StoreAPIKeyChange(value, oldValue, ProviderKind.Deepseek, Key.DeepseekAPIKey));
    }

    // :376-380. PR-PROVAPI-009, PR-PROVREG-239. B3: the deleted MetaKeychain wrapper is replaced by the shared
    // StoreAPIKeyChange.
    public string MetaAPIKeyInput
    {
        get => Read(metaAPIKeyInput);
        set => Set(ref metaAPIKeyInput, value, oldValue =>
            StoreAPIKeyChange(value, oldValue, ProviderKind.Meta, Key.MetaAPIKey));
    }

    // :387-391. PR-PROVAPI-010, PR-PROVREG-239. B3: the deleted ZaiKeychain wrapper is replaced by the shared
    // StoreAPIKeyChange.
    public string ZaiAPIKeyInput
    {
        get => Read(zaiAPIKeyInput);
        set => Set(ref zaiAPIKeyInput, value, oldValue =>
            StoreAPIKeyChange(value, oldValue, ProviderKind.Zai, Key.ZaiAPIKey));
    }

    // :396-400. PR-PROVREG-239. B3: the deleted CommandCodeKeychain wrapper is replaced by the shared
    // StoreAPIKeyChange.
    public string CommandcodeAPIKeyInput
    {
        get => Read(commandcodeAPIKeyInput);
        set => Set(ref commandcodeAPIKeyInput, value, oldValue =>
            StoreAPIKeyChange(value, oldValue, ProviderKind.Commandcode, Key.CommandcodeAPIKey));
    }

    // PORT-ONLY (ED-234): the OpenRouter key, kept in the protected store like the other four (the account is in APIKeychain).
    public string OpenRouterAPIKeyInput
    {
        get => Read(openRouterAPIKeyInput);
        set => Set(ref openRouterAPIKeyInput, value, oldValue =>
            StoreAPIKeyChange(value, oldValue, ProviderKind.OpenRouter, Key.OpenRouterAPIKey));
    }

    /// <summary>
    /// One provider key changed: hand it to the protected store and keep a plaintext copy in the defaults only for
    /// a Mac (upstream) whose Keychain refused it; the port keeps only the clearing half (OD-0008 A6).
    /// </summary>
    // :483-487 storeAPIKeyChange(_:from:provider:fallbackKey:), the one shared body every key observer now calls
    // through, replacing the four separate WriteAPIKeyEdit call sites (each closing over its own deleted *Keychain.
    // setAPIKey) B2 had. guard !isReadingKeychain, value != oldValue else { return } (:484); APIKeychain.setKey
    // (:485) now takes the ProviderKind directly (PR-PROVAPI-192) instead of a per-wrapper account string.
    private void StoreAPIKeyChange(string value, string oldValue, ProviderKind provider, string fallbackKey)
    {
        if (isReadingKeychain || SwiftString.Equal(value, oldValue)) return;
        var kept = APIKeychain.SetKey(value, provider);
        SetAPIKeyRefusal(provider, !kept);
        StoreAPIKeyFallback(kept ? "" : value, fallbackKey);
    }

    // :489-498. Only the clearing half is kept (PR-PROVAPI-011, OD-0008 A6): a non-empty value is never written
    // to the settings store; the caller already reported the refusal.
    private void StoreAPIKeyFallback(string value, string key)
    {
        var trimmed = FoundationText.Trim(value, includeNewlines: true);
        if (trimmed.Length == 0) defaults.RemoveObject(key);
    }

    // :433-441 readKeys(fallbacks:). One Keychain round trip per key, off the caller's thread. B3: each call goes
    // through StoredKeyFor(:443-445), replacing the four deleted per-wrapper ApiKey(fallback:) calls B2 made here.
    private static StoredKeys ReadKeys((string Deepseek, string Meta, string Zai, string Commandcode, string OpenRouter) fallbacks) => new()
    {
        Deepseek = StoredKeyFor(ProviderKind.Deepseek, fallbacks.Deepseek),
        Meta = StoredKeyFor(ProviderKind.Meta, fallbacks.Meta),
        Zai = StoredKeyFor(ProviderKind.Zai, fallbacks.Zai),
        Commandcode = StoredKeyFor(ProviderKind.Commandcode, fallbacks.Commandcode),
        OpenRouter = StoredKeyFor(ProviderKind.OpenRouter, fallbacks.OpenRouter),
    };

    // :443-445 storedKey(for:fallback:): APIKeychain.key(for: provider, fallback: "") (named StoredKeyFor rather
    // than Swift's storedKey to avoid colliding with the nested StoredKey record above; C# has no separate
    // type/member namespace the way Swift's type and function do).
    // DEVIATION OD-0008 A6: upstream falls back to the plaintext defaults copy (StoredKey.init(keychain:fallback:))
    // when the Keychain is empty; the port never reads that copy, so an account the store does not hold leaves
    // its field empty. `fallback` is kept in the signature only to mirror the Swift shape; it is never read.
    private static StoredKey StoredKeyFor(ProviderKind provider, string fallback)
    {
        _ = fallback;
        var keychain = APIKeychain.Key(provider, "");
        return new StoredKey { Value = keychain, InKeychain = keychain.Length != 0 };
    }

    // :424-431
    private (string Deepseek, string Meta, string Zai, string Commandcode, string OpenRouter) KeyFallbacks => (
        defaults.String(Key.DeepseekAPIKey) ?? "",
        defaults.String(Key.MetaAPIKey) ?? "",
        defaults.String(Key.ZaiAPIKey) ?? "",
        defaults.String(Key.CommandcodeAPIKey) ?? "",
        defaults.String(Key.OpenRouterAPIKey) ?? "");

    // :450-453
    private void ReadKeychainIfNeeded()
    {
        if (didReadKeychain) return;
        FillKeys(ReadKeys(KeyFallbacks));
    }

    // :457-462
    public async Task LoadKeychainInBackground()
    {
        if (didReadKeychain) return;
        var fallbacks = KeyFallbacks;
        var keys = await Task.Run(() => ReadKeys(fallbacks));
        FillKeys(keys);
    }

    // :464-479
    private void FillKeys(StoredKeys keys)
    {
        if (didReadKeychain) return;
        didReadKeychain = true;
        isReadingKeychain = true;
        DeepseekAPIKeyInput = keys.Deepseek.Value;
        MetaAPIKeyInput = keys.Meta.Value;
        ZaiAPIKeyInput = keys.Zai.Value;
        CommandcodeAPIKeyInput = keys.Commandcode.Value;
        OpenRouterAPIKeyInput = keys.OpenRouter.Value;
        isReadingKeychain = false;
        if (keys.Deepseek.InKeychain) defaults.RemoveObject(Key.DeepseekAPIKey);
        if (keys.Meta.InKeychain) defaults.RemoveObject(Key.MetaAPIKey);
        if (keys.Zai.InKeychain) defaults.RemoveObject(Key.ZaiAPIKey);
        if (keys.Commandcode.InKeychain) defaults.RemoveObject(Key.CommandcodeAPIKey);
        if (keys.OpenRouter.InKeychain) defaults.RemoveObject(Key.OpenRouterAPIKey);
    }

    // :500-502. Keys are ProviderKind raw values.
    public ValueMap<string, string> BinaryPaths
    {
        get => Read(binaryPaths);
        private set => Set(ref binaryPaths, value, _ => defaults.Set(Key.BinaryPaths, ToObjectMap(value)));
    }

    // :504-506
    public ValueList<string> DisabledProviders
    {
        get => Read(disabledProviders);
        private set => Set(ref disabledProviders, value, _ => defaults.Set(Key.DisabledProviders, value.ToList<object>()));
    }

    // :508-510
    public ValueMap<string, string> LastModels
    {
        get => Read(lastModels);
        private set => Set(ref lastModels, value, _ => defaults.Set(Key.Models, ToObjectMap(value)));
    }

    // :512-514
    public ValueMap<string, string> LastEfforts
    {
        get => Read(lastEfforts);
        private set => Set(ref lastEfforts, value, _ => defaults.Set(Key.Efforts, ToObjectMap(value)));
    }

    private static Dictionary<string, object> ToObjectMap(ValueMap<string, string> map) =>
        map.ToDictionary(pair => pair.Key, object (pair) => pair.Value);

    // :519-521
    public Guid? LastHydraPairID
    {
        get => Read(lastHydraPairID);
        set => Set(ref lastHydraPairID, value, _ => defaults.Set(Key.LastHydraPair, value is { } id ? SwiftUUID.UuidString(id) : null));
    }

    // :523-525
    public ValueList<ModelPin> ModelList
    {
        get => Read(modelList);
        private set => Set(ref modelList, value, _ => StoreArray(value, Key.ModelList, ModelPinConverter.WriteMembers));
    }

    // :527-529. Keys are ModelPin ids.
    public ValueMap<string, ModelPreference> ModelPreferences
    {
        get => Read(modelPreferences);
        private set => Set(ref modelPreferences, value, _ => StoreObject(value, Key.ModelPreferences,
            (writer, preference) => new ModelPreferenceConverter().Write(writer, preference, DefaultJsonOptions)));
    }

    private static readonly JsonSerializerOptions DefaultJsonOptions = new();

    // :864-866. `try? JSONEncoder().encode(value)` -> compact bytes, unsorted keys (SwiftJsonStyle.PlainEncoder).
    private void StoreArray<T>(IReadOnlyList<T> items, string key, Action<Utf8JsonWriter, T> writeMembers)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var item in items)
            {
                writer.WriteStartObject();
                writeMembers(writer, item);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        using var document = JsonDocument.Parse(stream.ToArray(), SwiftJson.DocumentOptions);
        defaults.Set(key, SwiftJson.Write(document.RootElement, SwiftJsonStyle.PlainEncoder));
    }

    private void StoreObject<T>(ValueMap<string, T> map, string key, Action<Utf8JsonWriter, T> writeValue)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (memberKey, value) in map)
            {
                writer.WritePropertyName(memberKey);
                writeValue(writer, value);
            }
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray(), SwiftJson.DocumentOptions);
        defaults.Set(key, SwiftJson.Write(document.RootElement, SwiftJsonStyle.PlainEncoder));
    }

    // :868-871. `try? JSONDecoder().decode(...)`: any failure (including no stored data) is nil.
    private static ValueList<T>? LoadArray<T>(ISettingsStore store, string key, Func<JsonElement, T> decode)
    {
        var data = store.Data(key);
        if (data is null) return null;
        try
        {
            using var document = JsonDocument.Parse(data, SwiftJson.DocumentOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            return new ValueList<T>(document.RootElement.EnumerateArray().Select(decode).ToArray());
        }
        catch (JsonException) { return null; }
    }

    private static ValueMap<string, T>? LoadObject<T>(ISettingsStore store, string key, Func<JsonElement, T> decode)
    {
        var data = store.Data(key);
        if (data is null) return null;
        try
        {
            using var document = JsonDocument.Parse(data, SwiftJson.DocumentOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            return new ValueMap<string, T>(document.RootElement.EnumerateObject()
                .Select(member => new KeyValuePair<string, T>(member.Name, decode(member.Value))));
        }
        catch (JsonException) { return null; }
    }

    // :789. `Self.load([Lenient<HydraPair>].self, forKey:)?.compactMap(\.value) ?? []`: a pair that fails to
    // decode is dropped, the surviving ones keep their order (PR-HYDRA-029).
    private static ValueList<HydraPair> LoadHydraPairsLenient(ISettingsStore store, string key)
    {
        var data = store.Data(key);
        if (data is null) return ValueList<HydraPair>.Empty;
        try
        {
            using var document = JsonDocument.Parse(data, SwiftJson.DocumentOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return ValueList<HydraPair>.Empty;
            var pairs = new List<HydraPair>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                try { pairs.Add(element.Deserialize<HydraPair>()!); }
                catch (JsonException) { /* Lenient<HydraPair>().value is nil: dropped */ }
            }
            return new ValueList<HydraPair>(pairs);
        }
        catch (JsonException) { return ValueList<HydraPair>.Empty; }
    }

    // :533-535
    public bool HydraEnabled
    {
        get => Read(hydraEnabled);
        set => Set(ref hydraEnabled, value, _ => defaults.Set(Key.HydraEnabled, value));
    }

    // :540-542
    public bool HydraDefaultEnabled
    {
        get => Read(hydraDefaultEnabled);
        set => Set(ref hydraDefaultEnabled, value, _ => defaults.Set(Key.HydraDefaultEnabled, value));
    }

    // :544-546
    public bool HasSeenTour
    {
        get => Read(hasSeenTour);
        set => Set(ref hasSeenTour, value, _ => defaults.Set(Key.HasSeenTour, value));
    }

    // :549-551
    public bool HasSeenHydraIntro
    {
        get => Read(hasSeenHydraIntro);
        set => Set(ref hasSeenHydraIntro, value, _ => defaults.Set(Key.HasSeenHydraIntro, value));
    }

    // :555-557
    public bool HydraQueueHeads
    {
        get => Read(hydraQueueHeads);
        set => Set(ref hydraQueueHeads, value, _ => defaults.Set(Key.HydraQueueHeads, value));
    }

    // :562-564
    public bool HydraAlwaysHeads
    {
        get => Read(hydraAlwaysHeads);
        set => Set(ref hydraAlwaysHeads, value, _ => defaults.Set(Key.HydraAlwaysHeads, value));
    }

    // :569-571
    public bool HydraIsolateHeads
    {
        get => Read(hydraIsolateHeads);
        set => Set(ref hydraIsolateHeads, value, _ => defaults.Set(Key.HydraIsolateHeads, value));
    }

    // :575-583
    public long? HydraMaxHeads
    {
        get => Read(hydraMaxHeads);
        set => Set(ref hydraMaxHeads, value, _ =>
        {
            if (value is { } cap) defaults.Set(Key.HydraMaxHeads, cap);
            else defaults.RemoveObject(Key.HydraMaxHeads);
        });
    }

    // :587-589
    public bool HydraAutoMerge
    {
        get => Read(hydraAutoMerge);
        set => Set(ref hydraAutoMerge, value, _ => defaults.Set(Key.HydraAutoMerge, value));
    }

    // :593-595
    public bool HydraReviewHeads
    {
        get => Read(hydraReviewHeads);
        set => Set(ref hydraReviewHeads, value, _ => defaults.Set(Key.HydraReviewHeads, value));
    }

    // :599-601
    public bool HydraAutoClearFinished
    {
        get => Read(hydraAutoClearFinished);
        set => Set(ref hydraAutoClearFinished, value, _ => defaults.Set(Key.HydraAutoClearFinished, value));
    }

    // :606-608
    public bool HydraAutoHidesIdleHeads
    {
        get => Read(hydraAutoHidesIdleHeads);
        set => Set(ref hydraAutoHidesIdleHeads, value, _ => defaults.Set(Key.HydraAutoHidesIdleHeads, value));
    }

    // :613-615
    public bool HydraShowsHeadDetails
    {
        get => Read(hydraShowsHeadDetails);
        set => Set(ref hydraShowsHeadDetails, value, _ => defaults.Set(Key.HydraShowsHeadDetails, value));
    }

    // :619-621
    public bool HydraAutoPopsHeads
    {
        get => Read(hydraAutoPopsHeads);
        set => Set(ref hydraAutoPopsHeads, value, _ => defaults.Set(Key.HydraAutoPopsHeads, value));
    }

    // :627-629
    public bool HydraTempersHeadEffort
    {
        get => Read(hydraTempersHeadEffort);
        set => Set(ref hydraTempersHeadEffort, value, _ => defaults.Set(Key.HydraTempersHeadEffort, value));
    }

    // :635-637
    public bool ShowsUsagePanel
    {
        get => Read(showsUsagePanel);
        set => Set(ref showsUsagePanel, value, _ => defaults.Set(Key.ShowsUsagePanel, value));
    }

    // :641-643
    public bool SidebarFloats
    {
        get => Read(sidebarFloats);
        set => Set(ref sidebarFloats, value, _ => defaults.Set(Key.SidebarFloats, value));
    }

    // :647-649
    public bool SidebarOnlyFloats
    {
        get => Read(sidebarOnlyFloats);
        set => Set(ref sidebarOnlyFloats, value, _ => defaults.Set(Key.SidebarOnlyFloats, value));
    }

    // :652-661. A computed get/set over the two switches above.
    public SidebarMode SidebarMode
    {
        get
        {
            if (!SidebarFloats) return Marrow.App.SidebarMode.Column;
            return SidebarOnlyFloats ? Marrow.App.SidebarMode.PanelOnly : Marrow.App.SidebarMode.Floating;
        }
        set
        {
            SidebarFloats = value != Marrow.App.SidebarMode.Column;
            SidebarOnlyFloats = value == Marrow.App.SidebarMode.PanelOnly;
        }
    }

    // :664-672
    public Point? SidebarPanelOrigin
    {
        get => Read(sidebarPanelOrigin);
        set => Set(ref sidebarPanelOrigin, value, _ =>
        {
            if (value is { } origin) defaults.Set(Key.SidebarPanelOrigin, new object[] { origin.X, origin.Y });
            else defaults.RemoveObject(Key.SidebarPanelOrigin);
        });
    }

    // :675-683
    public Size? SidebarPanelSize
    {
        get => Read(sidebarPanelSize);
        set => Set(ref sidebarPanelSize, value, _ =>
        {
            if (value is { } size) defaults.Set(Key.SidebarPanelSize, new object[] { size.Width, size.Height });
            else defaults.RemoveObject(Key.SidebarPanelSize);
        });
    }

    // :687-689
    public PanelDockCorner UsagePanelDock
    {
        get => Read(usagePanelDock);
        set => Set(ref usagePanelDock, value, _ => defaults.Set(Key.UsagePanelDock, SwiftEnum<PanelDockCorner>.RawValue(value)));
    }

    // :692-700
    public double? UsagePanelHeight
    {
        get => Read(usagePanelHeight);
        set => Set(ref usagePanelHeight, value, _ =>
        {
            if (value is { } height) defaults.Set(Key.UsagePanelHeight, height);
            else defaults.RemoveObject(Key.UsagePanelHeight);
        });
    }

    // :703-705
    public ValueList<HydraPair> HydraPairs
    {
        get => Read(hydraPairs);
        private set => Set(ref hydraPairs, value, _ =>
            defaults.Set(Key.HydraPairs, SwiftJson.Write(JsonSerializer.SerializeToElement(value, DefaultJsonOptions), SwiftJsonStyle.PlainEncoder)));
    }

    // :707-790. Constructors assign backing fields directly: no didSet runs, nothing is written back (R-OBS-1).
    public AppSettings()
    {
        writes = new SettingsWriteCoalescer(defaults); // PORT-ONLY (CZ-13.5)
        defaultProvider = SwiftEnum<ProviderKind>.FromRawValue(defaults.String(Key.DefaultProvider) ?? "") ?? ProviderKind.Codex;
        defaultRuntimeMode = SwiftEnum<RuntimeMode>.FromRawValue(defaults.String(Key.RuntimeMode) ?? "") ?? RuntimeMode.FullAccess;
        defaultWorkspaceMode = SwiftEnum<WorkspaceMode>.FromRawValue(defaults.String(Key.WorkspaceMode) ?? "") ?? WorkspaceMode.Local;
        notifyWhenFinished = Plist.AsBool(defaults.Object(Key.Notify)) ?? true;
        chimeWhenFinished = Plist.AsBool(defaults.Object(Key.Chime)) ?? true;
        confirmBeforeDeleting = Plist.AsBool(defaults.Object(Key.ConfirmDelete)) ?? true;
        autoContinueAfterLimit = Plist.AsBool(defaults.Object(Key.AutoContinueAfterLimit)) ?? false;
        threadFinishAction = SwiftEnum<ThreadFinishAction>.FromRawValue(defaults.String(Key.ThreadFinishAction) ?? "") ?? ThreadFinishAction.Settle;
        settleSound = Plist.AsBool(defaults.Object(Key.SettleSound)) ?? true;
        showReasoning = Plist.AsBool(defaults.Object(Key.ShowReasoning)) ?? false;
        chronologicalTimeline = Plist.AsBool(defaults.Object(Key.ChronologicalTimeline)) ?? false;
        antiSlopEnabled = Plist.AsBool(defaults.Object(Key.AntiSlopEnabled)) ?? true;
        showsWorkingCard = Plist.AsBool(defaults.Object(Key.ShowsWorkingCard)) ?? true;
        appZoom = Marrow.UI.Common.AppZoom.Normalized((int)(Plist.AsInt(defaults.Object(Key.AppZoom)) ?? 100));
        sidebarActivityView = defaults.Bool(Key.SidebarActivityView);
        activityThreadStyle = SwiftEnum<ActivityThreadStyle>.FromRawValue(defaults.String(Key.ActivityThreadStyle) ?? "") ?? ActivityThreadStyle.Icon;
        settledCollapsed = Plist.AsBool(defaults.Object(Key.SettledCollapsed)) ?? true;
        backdropOpacity = Plist.AsDouble(defaults.Object(Key.BackdropOpacity)) ?? DefaultBackdropOpacity;
        // :729-733. The old System/Light/Dark choice maps straight onto the same themes.
        var initialTheme = SwiftEnum<AppTheme>.FromRawValue(defaults.String(Key.AppTheme) ?? "")
            ?? SwiftEnum<AppTheme>.FromRawValue(defaults.String(Key.Appearance) ?? "")
            ?? Marrow.UI.Theme.AppTheme.System;
        theme = initialTheme;
        // PORT-ONLY (OD-0088): a missing or unknown accent reads as the default, and nothing is written until a pick. It is
        // mirrored before the theme, so the one ThemeManager change of :732 builds the spec with it and posts once.
        accent = AccentPalette.Normalize(defaults.String(Key.Accent));
        AccentChoice.Restore(accent);
        ThemeManager.Current = initialTheme;
        textGeneration = SwiftEnum<TextGenerationChoice>.FromRawValue(defaults.String(Key.TextGeneration) ?? "") ?? TextGenerationChoice.Automatic;
        commitInstructions = defaults.String(Key.CommitInstructions) ?? "";
        terminalHeight = Plist.AsDouble(defaults.Object(Key.TerminalHeight)) ?? 260;
        recentDownloadsPicker = Plist.AsBool(defaults.Object(Key.RecentDownloadsPicker)) ?? true;
        lastProjectID = defaults.String(Key.LastProjectID) is { } storedProjectID ? SwiftUUID.Parse(storedProjectID) : null;
        projectsEnabled = Plist.AsBool(defaults.Object(Key.ProjectsEnabled)) ?? true;
        projectActivationOverrides = LoadObject(defaults, Key.ProjectActivationOverrides, SwiftDecoding.ReadBool) ?? ValueMap<string, bool>.Empty;
        // :744-748. Read from the Keychain later (LoadKeychainInBackground), not before the first frame.
        deepseekAPIKeyInput = "";
        metaAPIKeyInput = "";
        zaiAPIKeyInput = "";
        commandcodeAPIKeyInput = "";
        openRouterAPIKeyInput = "";
        binaryPaths = defaults.Dictionary(Key.BinaryPaths) is { } binaryPathsStored && binaryPathsStored.Values.All(v => v is string)
            ? new ValueMap<string, string>(binaryPathsStored.Select(p => new KeyValuePair<string, string>(p.Key, (string)p.Value)))
            : ValueMap<string, string>.Empty;
        disabledProviders = defaults.StringArray(Key.DisabledProviders) is { } disabledStored
            ? new ValueList<string>(disabledStored) : ValueList<string>.Empty;
        lastModels = defaults.Dictionary(Key.Models) is { } modelsStored && modelsStored.Values.All(v => v is string)
            ? new ValueMap<string, string>(modelsStored.Select(p => new KeyValuePair<string, string>(p.Key, (string)p.Value)))
            : ValueMap<string, string>.Empty;
        lastEfforts = defaults.Dictionary(Key.Efforts) is { } effortsStored && effortsStored.Values.All(v => v is string)
            ? new ValueMap<string, string>(effortsStored.Select(p => new KeyValuePair<string, string>(p.Key, (string)p.Value)))
            : ValueMap<string, string>.Empty;
        lastHydraPairID = defaults.String(Key.LastHydraPair) is { } storedHydraPairID ? SwiftUUID.Parse(storedHydraPairID) : null;
        modelList = LoadArray(defaults, Key.ModelList, ModelPinConverter.Decode) ?? ValueList<ModelPin>.Empty;
        modelPreferences = LoadObject(defaults, Key.ModelPreferences, ModelPreferenceConverter.Decode) ?? ValueMap<string, ModelPreference>.Empty;
        hydraEnabled = Plist.AsBool(defaults.Object(Key.HydraEnabled)) ?? true;
        hydraDefaultEnabled = Plist.AsBool(defaults.Object(Key.HydraDefaultEnabled)) ?? true;
        hasSeenTour = Plist.AsBool(defaults.Object(Key.HasSeenTour)) ?? false;
        hasSeenHydraIntro = Plist.AsBool(defaults.Object(Key.HasSeenHydraIntro)) ?? false;
        if (defaults.Array(Key.PanelSize) is { } panelSizeStored && panelSizeStored.Count == 2)
            panelSize = new Size((double)Plist.AsDouble(panelSizeStored[0])!, (double)Plist.AsDouble(panelSizeStored[1])!);
        hydraQueueHeads = Plist.AsBool(defaults.Object(Key.HydraQueueHeads)) ?? true;
        hydraAlwaysHeads = Plist.AsBool(defaults.Object(Key.HydraAlwaysHeads)) ?? false;
        hydraIsolateHeads = Plist.AsBool(defaults.Object(Key.HydraIsolateHeads)) ?? true;
        hydraMaxHeads = Marrow.Core.Models.HydraPair.ClampedCap(Plist.AsInt(defaults.Object(Key.HydraMaxHeads)));
        hydraAutoMerge = Plist.AsBool(defaults.Object(Key.HydraAutoMerge)) ?? false;
        hydraReviewHeads = Plist.AsBool(defaults.Object(Key.HydraReviewHeads)) ?? false;
        hydraAutoClearFinished = Plist.AsBool(defaults.Object(Key.HydraAutoClearFinished)) ?? false;
        hydraAutoHidesIdleHeads = Plist.AsBool(defaults.Object(Key.HydraAutoHidesIdleHeads)) ?? false;
        hydraShowsHeadDetails = Plist.AsBool(defaults.Object(Key.HydraShowsHeadDetails)) ?? false;
        hydraAutoPopsHeads = Plist.AsBool(defaults.Object(Key.HydraAutoPopsHeads)) ?? false;
        hydraTempersHeadEffort = Plist.AsBool(defaults.Object(Key.HydraTempersHeadEffort)) ?? true;
        showsUsagePanel = Plist.AsBool(defaults.Object(Key.ShowsUsagePanel)) ?? false;
        sidebarFloats = Plist.AsBool(defaults.Object(Key.SidebarFloats)) ?? false;
        sidebarOnlyFloats = Plist.AsBool(defaults.Object(Key.SidebarOnlyFloats)) ?? false;
        if (defaults.Array(Key.SidebarPanelOrigin) is { } originStored && originStored.Count == 2)
            sidebarPanelOrigin = new Point((double)Plist.AsDouble(originStored[0])!, (double)Plist.AsDouble(originStored[1])!);
        else sidebarPanelOrigin = null;
        if (defaults.Array(Key.SidebarPanelSize) is { } sizeStored && sizeStored.Count == 2)
            sidebarPanelSize = new Size((double)Plist.AsDouble(sizeStored[0])!, (double)Plist.AsDouble(sizeStored[1])!);
        else sidebarPanelSize = null;
        usagePanelDock = defaults.String(Key.UsagePanelDock) is { } dockStored
            ? SwiftEnum<PanelDockCorner>.FromRawValue(dockStored) ?? PanelDockCorner.BottomLeading
            : PanelDockCorner.BottomLeading;
        usagePanelHeight = Plist.AsDouble(defaults.Object(Key.UsagePanelHeight));
        hydraPairs = LoadHydraPairsLenient(defaults, Key.HydraPairs);
        // PORT-ONLY (OD-0089, ED-251): unreadable or missing, the default (Border beam)
        composerLight = defaults.Data(Key.ComposerLight) is { } lightStored ? ComposerLightSettings.FromJson(lightStored) : ComposerLightSettings.Default;
        // PORT-ONLY (SA-1): unreadable or missing, the record default (the hardware record: none)
        voiceOrb = defaults.Data(Key.VoiceOrb) is { } orbStored ? VoiceOrbSettings.FromJson(orbStored) : VoiceOrbSettings.Default;
        voiceTyping = defaults.Data(Key.VoiceTyping) is { } typingStored ? VoiceTypingSettings.FromJson(typingStored) : VoiceTypingSettings.Default;
        Marrow.App.Port.Voice.VoiceTypingMirror.Publish(defaults, voiceTyping); // the readers without a settings object (VT-14 review)
        voiceHardware = defaults.Data(Key.VoiceHardware) is { } hardwareStored ? VoiceHardwareRecord.FromJson(hardwareStored) : null;
        voiceNotes = defaults.Data(Key.VoiceNotes) is { } notesStored ? VoiceNotesSettings.FromJson(notesStored) : VoiceNotesSettings.Default;
        workingDots = defaults.Data(Key.WorkingDots) is { } dotsStored ? WorkingDotsSettings.FromJson(dotsStored) : WorkingDotsSettings.Default;
    }

    // MARK: - Hydra pairs

    // :794-796
    public void AddHydraPair(HydraPair pair) => HydraPairs = hydraPairs.Appending(pair);

    // :798-805. An (inout HydraPair) -> Void closure is a Func returning the changed copy (R-VAL-1).
    public void UpdateHydraPair(Guid id, Func<HydraPair, HydraPair> change)
    {
        var index = hydraPairs.ToList().FindIndex(p => p.Id == id);
        if (index < 0) return;
        var pair = change(hydraPairs[index]);
        pair = pair with { MaxHeads = Marrow.Core.Models.HydraPair.ClampedCap(pair.MaxHeads) };
        if (pair == hydraPairs[index]) return;
        HydraPairs = hydraPairs.SetItem(index, pair);
    }

    // :807-809
    public void RemoveHydraPair(Guid id) => HydraPairs = new ValueList<HydraPair>(hydraPairs.Where(p => p.Id != id));

    // :811-814. Swift: hydraPair(_:).
    public HydraPair? HydraPair(Guid? id)
    {
        if (id is not { } wanted) return null;
        foreach (var pair in hydraPairs) if (pair.Id == wanted) return pair;
        return null;
    }

    // :818-823. Swift: hydraPair(for:model:).
    public HydraPair? HydraPair(ProviderKind provider, string? model)
    {
        var candidates = hydraPairs.Where(p => p.Provider == provider).ToList();
        if (model is { } wanted)
        {
            foreach (var candidate in candidates) if (candidate.OrchestratorModel == wanted) return candidate;
        }
        foreach (var candidate in candidates) if (candidate.OrchestratorModel is null) return candidate;
        return candidates.Count > 0 ? candidates[0] : null;
    }

    // MARK: - Model picker

    // :827-829
    public bool IsInModelList(ModelPin pin) => modelList.Contains(pin);

    // :831-834
    public void AddToModelList(ModelPin pin)
    {
        if (modelList.Contains(pin) || modelList.Count >= ModelListLimit) return;
        ModelList = modelList.Appending(pin);
    }

    // :836-838
    public void RemoveFromModelList(ModelPin pin) => ModelList = new ValueList<ModelPin>(modelList.Where(p => p != pin));

    // :841-852. Swift: moveModel(_:to:placeAfter:), @discardableResult.
    public bool MoveModel(ModelPin pin, ModelPin to, bool placeAfter)
    {
        if (pin == to) return false;
        var from = modelList.ToList().IndexOf(pin);
        var toIndex = modelList.ToList().IndexOf(to);
        if (from < 0 || toIndex < 0) return false;
        var dest = toIndex + (placeAfter ? 1 : 0);
        if (from < dest) dest -= 1;
        if (from == dest) return false;
        var list = modelList.ToList();
        var moved = list[from];
        list.RemoveAt(from);
        list.Insert(dest, moved);
        ModelList = new ValueList<ModelPin>(list);
        return true;
    }

    // :854-857. Swift: preference(for:model:).
    public ModelPreference Preference(ProviderKind provider, string? model)
    {
        if (model is not { } wanted) return new ModelPreference();
        var key = new ModelPin(provider, wanted).Id;
        return ModelPreferences.TryGetValue(key, out var preference) ? preference : new ModelPreference();
    }

    // :859-862. Swift: setPreference(_:for:model:).
    public void SetPreference(ModelPreference preference, ProviderKind provider, string model)
    {
        var key = new ModelPin(provider, model).Id;
        ModelPreferences = preference == new ModelPreference()
            ? modelPreferences.Mutating(map => map.Remove(key))
            : modelPreferences.Mutating(map => map[key] = preference);
    }

    // :873-875. Swift: binaryPath(for:).
    public string BinaryPath(ProviderKind provider) =>
        binaryPaths.TryGetValue(SwiftEnum<ProviderKind>.RawValue(provider), out var path) ? path : "";

    // :877-880. Swift: setBinaryPath(_:for:).
    public void SetBinaryPath(string path, ProviderKind provider)
    {
        var trimmed = FoundationText.Trim(path, includeNewlines: true);
        var key = SwiftEnum<ProviderKind>.RawValue(provider);
        BinaryPaths = trimmed.Length == 0
            ? binaryPaths.Mutating(map => map.Remove(key))
            : binaryPaths.Mutating(map => map[key] = trimmed);
    }

    // :882-884. Swift: isEnabled(_:). It reads the observed property, as Swift's @Observable read of disabledProviders does: the Providers page's switch
    // tracks this read, and reading the backing field left the switch unobserved, so a click changed the setting but never moved the knob (ED-NEW-providers-switch-1).
    public bool IsEnabled(ProviderKind provider) => !DisabledProviders.Contains(SwiftEnum<ProviderKind>.RawValue(provider));

    // :886-889. Swift: setEnabled(_:for:).
    public void SetEnabled(bool enabled, ProviderKind provider)
    {
        var raw = SwiftEnum<ProviderKind>.RawValue(provider);
        var list = disabledProviders.Where(p => p != raw).ToList();
        if (!enabled) list.Add(raw);
        DisabledProviders = new ValueList<string>(list);
    }

    // :891-893. Swift: lastModel(for:).
    public string? LastModel(ProviderKind provider) => lastModels.GetValueOrDefault(SwiftEnum<ProviderKind>.RawValue(provider));

    // :895-897. Swift: lastEffort(for:).
    public string? LastEffort(ProviderKind provider) => lastEfforts.GetValueOrDefault(SwiftEnum<ProviderKind>.RawValue(provider));

    // :899-902. Swift: remember(model:effort:for:).
    public void Remember(string? model, string? effort, ProviderKind provider)
    {
        var raw = SwiftEnum<ProviderKind>.RawValue(provider);
        LastModels = model is { } m ? lastModels.Mutating(map => map[raw] = m) : lastModels.Mutating(map => map.Remove(raw));
        LastEfforts = effort is { } e ? lastEfforts.Mutating(map => map[raw] = e) : lastEfforts.Mutating(map => map.Remove(raw));
    }

    // :908-920. Swift: apiKey(for:); the ProviderSettings requirement.
    public string ApiKey(ProviderKind provider)
    {
        ReadKeychainIfNeeded();
        var stored = FoundationText.Trim(provider switch
        {
            ProviderKind.Deepseek => deepseekAPIKeyInput,
            ProviderKind.Meta => metaAPIKeyInput,
            ProviderKind.Zai => zaiAPIKeyInput,
            ProviderKind.Commandcode => commandcodeAPIKeyInput,
            ProviderKind.OpenRouter => openRouterAPIKeyInput, // PORT-ONLY (ED-234)
            _ => "",
        }, includeNewlines: true);
        if (stored.Length != 0) return stored;
        if (!provider.AcceptsAPIKey || provider.ApiKeyEnvVar is not { } envVar) return "";
        return FoundationText.Trim(LoginEnvironment.Current.GetValueOrDefault(envVar) ?? "", includeNewlines: true);
    }

    // :922-924. Swift: hasAPIKey(for:).
    public bool HasAPIKey(ProviderKind provider) => ApiKey(provider).Length != 0;

    // :927-936. Swift: apiKeyInput(for:).
    public string ApiKeyInput(ProviderKind provider)
    {
        ReadKeychainIfNeeded();
        return provider switch
        {
            ProviderKind.Deepseek => deepseekAPIKeyInput,
            ProviderKind.Meta => metaAPIKeyInput,
            ProviderKind.Zai => zaiAPIKeyInput,
            ProviderKind.Commandcode => commandcodeAPIKeyInput,
            ProviderKind.OpenRouter => openRouterAPIKeyInput, // PORT-ONLY (ED-234)
            _ => "",
        };
    }

    // :938-947. Swift: setAPIKeyInput(_:for:).
    public void SetAPIKeyInput(string value, ProviderKind provider)
    {
        ReadKeychainIfNeeded();
        switch (provider)
        {
            case ProviderKind.Commandcode: CommandcodeAPIKeyInput = value; break;
            case ProviderKind.Deepseek: DeepseekAPIKeyInput = value; break;
            case ProviderKind.Meta: MetaAPIKeyInput = value; break;
            case ProviderKind.Zai: ZaiAPIKeyInput = value; break;
            case ProviderKind.OpenRouter: OpenRouterAPIKeyInput = value; break; // PORT-ONLY (ED-234)
        }
    }
}

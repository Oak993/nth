using Avalonia;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Theme;

// Source: DroppyCode/App/AppSettings.swift (B2 1fbf81e), :1-947. Skeleton (c100 group 1): the four enums' cases
// (:3-93), the ModelPin and ModelPreference shapes (:96-107), the defaults keys (:117-180), the constants (:115,
// :182, :299) and every stored property's shape are complete; every didSet, every computed property, the init
// (:703-786), the Keychain fill (:410-494) and every method are left to the implementing unit. Forms: CGSize is
// Avalonia.Size, CGPoint is Avalonia.Point, CGFloat is double (R-VIEW-11, ED-095); `defaults` is
// UserDefaults.Standard (WebsiteCaptures.defaults is NOT-PORTED, ED-001); a Swift Int is long (R-NUM-4).
// The `private var keyFallbacks` tuple (:432-439) and the private helpers store/load (:860-867) are left to the
// implementing unit.

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
        public string Id => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:9");

        // :11-18
        public string Title => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:11");
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
        public string Id => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:28");

        // :30-35
        public string Title => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:30");

        // :37-42
        public string Detail => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:37");
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
        public string Id => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:51");

        // :53-59
        public string Title => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:53");

        // :61-67
        public string Detail => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:61");
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
        public string Id => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:78");

        // :80-85
        public string Title => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:80");

        // :87-92
        public string Detail => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:87");
    }
}

// :96-101. ED-149: a readonly record struct, the one named exception to R-VAL-1.
public readonly record struct ModelPin(ProviderKind Provider, string ModelID) // :97-98
{
    // :100
    public string Id => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:100");
}

// :104-107 (R-VAL-1)
public sealed record ModelPreference
{
    public string? Effort { get; init; }        // :105
    public bool FastMode { get; init; } = false; // :106
}

// :110-944. @MainActor @Observable -> MainActorModel (R-OBS-6); :947 extension AppSettings: ProviderSettings.
public sealed class AppSettings : MainActorModel, ProviderSettings
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
    }

    // :182
    public const long ModelListLimit = 15;

    // :299
    public const double DefaultBackdropOpacity = 0.5;

    // :415-423
    private sealed record StoredKey
    {
        public required string Value { get; init; }    // :416
        public required bool InKeychain { get; init; } // :417
    }

    // :425-430
    private sealed record StoredKeys
    {
        public required StoredKey Deepseek { get; init; }    // :426
        public required StoredKey Meta { get; init; }        // :427
        public required StoredKey Zai { get; init; }         // :428
        public required StoredKey Commandcode { get; init; } // :429
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
    private double backdropOpacity;
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
        set => Set(ref defaultProvider, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:190"));
    }

    // :193-195
    public RuntimeMode DefaultRuntimeMode
    {
        get => Read(defaultRuntimeMode);
        set => Set(ref defaultRuntimeMode, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:194"));
    }

    // :197-199
    public WorkspaceMode DefaultWorkspaceMode
    {
        get => Read(defaultWorkspaceMode);
        set => Set(ref defaultWorkspaceMode, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:198"));
    }

    // :201-203
    public bool NotifyWhenFinished
    {
        get => Read(notifyWhenFinished);
        set => Set(ref notifyWhenFinished, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:202"));
    }

    // :206-208
    public bool ChimeWhenFinished
    {
        get => Read(chimeWhenFinished);
        set => Set(ref chimeWhenFinished, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:207"));
    }

    // :210-212
    public bool ConfirmBeforeDeleting
    {
        get => Read(confirmBeforeDeleting);
        set => Set(ref confirmBeforeDeleting, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:211"));
    }

    // :216-218
    public bool AutoContinueAfterLimit
    {
        get => Read(autoContinueAfterLimit);
        set => Set(ref autoContinueAfterLimit, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:217"));
    }

    // :221-223
    public ThreadFinishAction ThreadFinishAction
    {
        get => Read(threadFinishAction);
        set => Set(ref threadFinishAction, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:222"));
    }

    // :226-228
    public bool SettleSound
    {
        get => Read(settleSound);
        set => Set(ref settleSound, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:227"));
    }

    // :230-232
    public bool ShowReasoning
    {
        get => Read(showReasoning);
        set => Set(ref showReasoning, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:231"));
    }

    // :237-239
    public bool ChronologicalTimeline
    {
        get => Read(chronologicalTimeline);
        set => Set(ref chronologicalTimeline, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:238"));
    }

    // :241-243
    public bool AntiSlopEnabled
    {
        get => Read(antiSlopEnabled);
        set => Set(ref antiSlopEnabled, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:242"));
    }

    // :247-249
    public bool ShowsWorkingCard
    {
        get => Read(showsWorkingCard);
        set => Set(ref showsWorkingCard, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:248"));
    }

    // :253-255. The property shadows the static class Marrow.UI.Common.AppZoom inside this class: the body
    // reaches that class fully qualified.
    public long AppZoom
    {
        get => Read(appZoom);
        set => Set(ref appZoom, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:254"));
    }

    // :260-268
    public Size? PanelSize
    {
        get => Read(panelSize);
        set => Set(ref panelSize, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:261"));
    }

    // :271-273
    public bool SidebarActivityView
    {
        get => Read(sidebarActivityView);
        set => Set(ref sidebarActivityView, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:272"));
    }

    // :277-279
    public ActivityThreadStyle ActivityThreadStyle
    {
        get => Read(activityThreadStyle);
        set => Set(ref activityThreadStyle, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:278"));
    }

    // :282-284
    public bool SettledCollapsed
    {
        get => Read(settledCollapsed);
        set => Set(ref settledCollapsed, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:283"));
    }

    // :286-291
    public AppTheme Theme
    {
        get => Read(theme);
        set => Set(ref theme, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:287"));
    }

    // :296-298
    public double BackdropOpacity
    {
        get => Read(backdropOpacity);
        set => Set(ref backdropOpacity, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:297"));
    }

    // :301-303
    public TextGenerationChoice TextGeneration
    {
        get => Read(textGeneration);
        set => Set(ref textGeneration, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:302"));
    }

    // :305-307
    public string CommitInstructions
    {
        get => Read(commitInstructions);
        set => Set(ref commitInstructions, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:306"));
    }

    // :309-311
    public double TerminalHeight
    {
        get => Read(terminalHeight);
        set => Set(ref terminalHeight, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:310"));
    }

    // :314-316
    public bool RecentDownloadsPicker
    {
        get => Read(recentDownloadsPicker);
        set => Set(ref recentDownloadsPicker, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:315"));
    }

    // :320-328
    public Guid? LastProjectID
    {
        get => Read(lastProjectID);
        set => Set(ref lastProjectID, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:321"));
    }

    // :333-335
    public bool ProjectsEnabled
    {
        get => Read(projectsEnabled);
        set => Set(ref projectsEnabled, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:334"));
    }

    // :339-341. Keys are project UUID strings.
    public ValueMap<string, bool> ProjectActivationOverrides
    {
        get => Read(projectActivationOverrides);
        private set => Set(ref projectActivationOverrides, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:340"));
    }

    // :344-346
    public bool IsProjectActive(Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:344");

    // :349-351. Swift: setProjectActive(_:for:).
    public void SetProjectActive(bool active, Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:349");

    // :354-356. Swift: clearProjectActivationOverride(for:).
    public void ClearProjectActivationOverride(Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:354");

    // :359-361. Swift: hasProjectActivationOverride(for:).
    public bool HasProjectActivationOverride(Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:359");

    // :367-373
    public string DeepseekAPIKeyInput
    {
        get => Read(deepseekAPIKeyInput);
        set => Set(ref deepseekAPIKeyInput, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:368"));
    }

    // :378-384
    public string MetaAPIKeyInput
    {
        get => Read(metaAPIKeyInput);
        set => Set(ref metaAPIKeyInput, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:379"));
    }

    // :391-397
    public string ZaiAPIKeyInput
    {
        get => Read(zaiAPIKeyInput);
        set => Set(ref zaiAPIKeyInput, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:392"));
    }

    // :402-408
    public string CommandcodeAPIKeyInput
    {
        get => Read(commandcodeAPIKeyInput);
        set => Set(ref commandcodeAPIKeyInput, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:403"));
    }

    // :461-466
    public Task LoadKeychainInBackground() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:461");

    // :496-498. Keys are ProviderKind raw values.
    public ValueMap<string, string> BinaryPaths
    {
        get => Read(binaryPaths);
        private set => Set(ref binaryPaths, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:497"));
    }

    // :500-502
    public ValueList<string> DisabledProviders
    {
        get => Read(disabledProviders);
        private set => Set(ref disabledProviders, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:501"));
    }

    // :504-506
    public ValueMap<string, string> LastModels
    {
        get => Read(lastModels);
        private set => Set(ref lastModels, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:505"));
    }

    // :508-510
    public ValueMap<string, string> LastEfforts
    {
        get => Read(lastEfforts);
        private set => Set(ref lastEfforts, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:509"));
    }

    // :515-517
    public Guid? LastHydraPairID
    {
        get => Read(lastHydraPairID);
        set => Set(ref lastHydraPairID, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:516"));
    }

    // :519-521
    public ValueList<ModelPin> ModelList
    {
        get => Read(modelList);
        private set => Set(ref modelList, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:520"));
    }

    // :523-525. Keys are ModelPin ids.
    public ValueMap<string, ModelPreference> ModelPreferences
    {
        get => Read(modelPreferences);
        private set => Set(ref modelPreferences, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:524"));
    }

    // :529-531
    public bool HydraEnabled
    {
        get => Read(hydraEnabled);
        set => Set(ref hydraEnabled, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:530"));
    }

    // :536-538
    public bool HydraDefaultEnabled
    {
        get => Read(hydraDefaultEnabled);
        set => Set(ref hydraDefaultEnabled, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:537"));
    }

    // :540-542
    public bool HasSeenTour
    {
        get => Read(hasSeenTour);
        set => Set(ref hasSeenTour, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:541"));
    }

    // :545-547
    public bool HasSeenHydraIntro
    {
        get => Read(hasSeenHydraIntro);
        set => Set(ref hasSeenHydraIntro, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:546"));
    }

    // :551-553
    public bool HydraQueueHeads
    {
        get => Read(hydraQueueHeads);
        set => Set(ref hydraQueueHeads, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:552"));
    }

    // :558-560
    public bool HydraAlwaysHeads
    {
        get => Read(hydraAlwaysHeads);
        set => Set(ref hydraAlwaysHeads, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:559"));
    }

    // :565-567
    public bool HydraIsolateHeads
    {
        get => Read(hydraIsolateHeads);
        set => Set(ref hydraIsolateHeads, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:566"));
    }

    // :571-579
    public long? HydraMaxHeads
    {
        get => Read(hydraMaxHeads);
        set => Set(ref hydraMaxHeads, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:572"));
    }

    // :583-585
    public bool HydraAutoMerge
    {
        get => Read(hydraAutoMerge);
        set => Set(ref hydraAutoMerge, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:584"));
    }

    // :589-591
    public bool HydraReviewHeads
    {
        get => Read(hydraReviewHeads);
        set => Set(ref hydraReviewHeads, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:590"));
    }

    // :595-597
    public bool HydraAutoClearFinished
    {
        get => Read(hydraAutoClearFinished);
        set => Set(ref hydraAutoClearFinished, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:596"));
    }

    // :602-604
    public bool HydraAutoHidesIdleHeads
    {
        get => Read(hydraAutoHidesIdleHeads);
        set => Set(ref hydraAutoHidesIdleHeads, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:603"));
    }

    // :609-611
    public bool HydraShowsHeadDetails
    {
        get => Read(hydraShowsHeadDetails);
        set => Set(ref hydraShowsHeadDetails, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:610"));
    }

    // :615-617
    public bool HydraAutoPopsHeads
    {
        get => Read(hydraAutoPopsHeads);
        set => Set(ref hydraAutoPopsHeads, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:616"));
    }

    // :623-625
    public bool HydraTempersHeadEffort
    {
        get => Read(hydraTempersHeadEffort);
        set => Set(ref hydraTempersHeadEffort, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:624"));
    }

    // :631-633
    public bool ShowsUsagePanel
    {
        get => Read(showsUsagePanel);
        set => Set(ref showsUsagePanel, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:632"));
    }

    // :637-639
    public bool SidebarFloats
    {
        get => Read(sidebarFloats);
        set => Set(ref sidebarFloats, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:638"));
    }

    // :643-645
    public bool SidebarOnlyFloats
    {
        get => Read(sidebarOnlyFloats);
        set => Set(ref sidebarOnlyFloats, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:644"));
    }

    // :648-657. A computed get/set over the two switches above.
    public SidebarMode SidebarMode
    {
        get => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:649");
        set => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:653");
    }

    // :660-668
    public Point? SidebarPanelOrigin
    {
        get => Read(sidebarPanelOrigin);
        set => Set(ref sidebarPanelOrigin, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:661"));
    }

    // :671-679
    public Size? SidebarPanelSize
    {
        get => Read(sidebarPanelSize);
        set => Set(ref sidebarPanelSize, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:672"));
    }

    // :683-685
    public PanelDockCorner UsagePanelDock
    {
        get => Read(usagePanelDock);
        set => Set(ref usagePanelDock, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:684"));
    }

    // :688-696
    public double? UsagePanelHeight
    {
        get => Read(usagePanelHeight);
        set => Set(ref usagePanelHeight, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:689"));
    }

    // :699-701
    public ValueList<HydraPair> HydraPairs
    {
        get => Read(hydraPairs);
        private set => Set(ref hydraPairs, value, _ => throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:700"));
    }

    // :703-786
    public AppSettings() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:703");

    // MARK: - Hydra pairs

    // :790-792
    public void AddHydraPair(HydraPair pair) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:790");

    // :794-801. An (inout HydraPair) -> Void closure is a Func returning the changed copy (R-VAL-1).
    public void UpdateHydraPair(Guid id, Func<HydraPair, HydraPair> change) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:794");

    // :803-805
    public void RemoveHydraPair(Guid id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:803");

    // :807-810. Swift: hydraPair(_:).
    public HydraPair? HydraPair(Guid? id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:807");

    // :814-819. Swift: hydraPair(for:model:).
    public HydraPair? HydraPair(ProviderKind provider, string? model) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:814");

    // MARK: - Model picker

    // :823-825
    public bool IsInModelList(ModelPin pin) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:823");

    // :827-830
    public void AddToModelList(ModelPin pin) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:827");

    // :832-834
    public void RemoveFromModelList(ModelPin pin) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:832");

    // :837-848. Swift: moveModel(_:to:placeAfter:), @discardableResult.
    public bool MoveModel(ModelPin pin, ModelPin to, bool placeAfter) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:838");

    // :850-853. Swift: preference(for:model:).
    public ModelPreference Preference(ProviderKind provider, string? model) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:850");

    // :855-858. Swift: setPreference(_:for:model:).
    public void SetPreference(ModelPreference preference, ProviderKind provider, string model) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:855");

    // :869-871. Swift: binaryPath(for:).
    public string BinaryPath(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:869");

    // :873-876. Swift: setBinaryPath(_:for:).
    public void SetBinaryPath(string path, ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:873");

    // :878-880. Swift: isEnabled(_:).
    public bool IsEnabled(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:878");

    // :882-885. Swift: setEnabled(_:for:).
    public void SetEnabled(bool enabled, ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:882");

    // :887-889. Swift: lastModel(for:).
    public string? LastModel(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:887");

    // :891-893. Swift: lastEffort(for:).
    public string? LastEffort(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:891");

    // :895-898. Swift: remember(model:effort:for:).
    public void Remember(string? model, string? effort, ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:895");

    // :904-916. Swift: apiKey(for:); the ProviderSettings requirement.
    public string ApiKey(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:904");

    // :918-920. Swift: hasAPIKey(for:).
    public bool HasAPIKey(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:918");

    // :923-932. Swift: apiKeyInput(for:).
    public string ApiKeyInput(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:923");

    // :934-943. Swift: setAPIKeyInput(_:for:).
    public void SetAPIKeyInput(string value, ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/AppSettings.swift:934");
}

// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 2.2 "Home view" in the main area, 2.6 first run, 16.2 the home as a full view while
// the pane workspace is absent; the AS-21 and AS-13 seams: the header menu opens the activity log and the reserves; a card's
// Lineage opens the log on its mission; ED-NEW-dextra-as8-8). No Swift twin.
// The home as the app shows it: the host's sources bound into an AssistantHomeView, the home thread's chat hosted the way the
// detail pane hosts any chat, the first-run card until it is done, and a sheet over the home for the activity log and the
// reserves. When the controller starts or stops, the home is built again over the new sources. Dextra off shows one line and
// Turn on.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

/// <summary>The views the home's menu and a card's Lineage open over the home; production reads the store and the audit log.</summary>
public sealed record HomeSheets
{
    /// <summary>The activity log, on one mission when it is given.</summary>
    public Func<DextraHost, string?, Control?>? ActivityLog { get; init; }

    public Func<DextraHost, Control?>? Reserves { get; init; }

    /// <summary>The app's: the AS-21 log over the audit folder (its key behind Credential Manager) and the AS-13 reserves.</summary>
    public static HomeSheets Production { get; } = new()
    {
        ActivityLog = (host, mission) =>
        {
            var labels = new ActivityLabels(
                Assistant: id => host.NameOf(id),
                Mission: id => Guid.TryParse(id, out var m) ? host.Controller?.Runner.Find(m)?.Mission.Title : null);
            var model = new ActivityLogModel(ActivityLogSource.For(AssistantStorage.Roots.Current, AuditKeyStore.Production()), labels: labels);
            _ = mission is null ? model.OpenAsync() : model.ShowMissionAsync(mission);
            return new ActivityLogView(model);
        },
        Reserves = host => host.Controller?.Reserve is { } guard ? new ReservesView(ReservesModel.For(guard), guard) : null,
    };
}

public sealed class AssistantHomeHost : Decorator
{
    /// <summary>How the home opens Settings (the app's window manager; a test replaces it).</summary>
    internal static Action<SettingsPage> OpenSettings { get; set; } = page => WindowManager.Shared.ShowSettings(page);

    private readonly AppModel model;
    private readonly WindowLiveResize? liveResize;
    private readonly DextraHost host;
    private readonly HomeSheets sheets;
    private readonly Panel layers = new();
    private readonly Decorator sheetHost = new() { IsVisible = false };
    private readonly Decorator page = new();
    private FirstRunCard? firstRun;
    private DextraLive? built;
    private bool? builtOn;
    private Guid? builtThread;
    private Control? sheetReturn;

    public AssistantHomeHost(AppModel model, WindowLiveResize? liveResize = null, HomeSheets? sheets = null)
    {
        this.model = model ?? throw new ArgumentNullException(nameof(model));
        this.liveResize = liveResize;
        host = model.AssistantHost;
        this.sheets = sheets ?? HomeSheets.Production;
        layers.Children.Add(page);
        layers.Children.Add(sheetHost);
        Child = layers;
        sheetHost.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || !sheetHost.IsVisible) return;
            e.Handled = true;
            CloseSheet();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        host.EnsureStarted();
        Build();
    }

    /// <summary>The home now, or null while Dextra is off.</summary>
    public AssistantHomeView? Home { get; private set; }

    /// <summary>The first-run card while first run is not done.</summary>
    public FirstRunCard? FirstRun => host.Settings.Value.FirstRunDone ? null : firstRun;

    /// <summary>The Turn on button while Dextra is off.</summary>
    public Button? TurnOnButton { get; private set; }

    /// <summary>The view over the home (the activity log, the reserves), or null.</summary>
    public Control? Sheet => sheetHost.IsVisible ? sheetHost.Child : null;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        host.Changed -= OnHost;
        host.Changed += OnHost;
        Build();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        host.Changed -= OnHost;
        if (asking is not null) asking.CardRequested -= OnCardRequested;
        asking = null;
        builtOn = null; // built again on the next attach
    }

    private void OnHost() => Dispatcher.UIThread.Post(Build);

    private void Build()
    {
        var on = host.Settings.Value.On;
        var live = host.Live;
        var thread = live.Controller is null ? null : host.HomeThread();
        if (builtOn == on && ReferenceEquals(built, live) && builtThread == thread?.Id)
        {
            Home?.RefreshConversation();
            return;
        }
        builtOn = on;
        built = live;
        builtThread = thread?.Id;
        CloseSheet();
        if (!on)
        {
            Home = null;
            page.Child = OffView();
            return;
        }
        TurnOnButton = null;
        Home = new AssistantHomeView(Parts(live, thread));
        page.Child = Home;
        ShowDecisionsWhenAsked();
        if (thread is not null)
        {
            var runtime = model.Runtime(thread.Id);
            var home = Home;
            home.Track(() => runtime.Entries.Count == 0, _ => home.RefreshConversation());
        }
    }

    // A card voice may not answer is opened on the screen (2.4): the Board shows the Decisions tab beside the sheet.
    private DextraVoice? asking;

    private void ShowDecisionsWhenAsked()
    {
        if (asking is not null) asking.CardRequested -= OnCardRequested;
        asking = host.Voice;
        if (asking is not null) asking.CardRequested += OnCardRequested;
    }

    private void OnCardRequested(Guid card) => Dispatcher.UIThread.Post(() => Home?.Board.Pick(BoardTab.Decisions));

    private AssistantHomeParts Parts(DextraLive live, Marrow.Core.Models.ChatThread? thread)
    {
        var controller = live.Controller;
        Func<Guid, string?> missionTitle = id => controller?.Runner.Find(id)?.Mission.Title;
        return new AssistantHomeParts(live.Jobs, live.Decisions, live.Controls)
        {
            Name = DextraHost.Name,
            DecisionContext = new DecisionContext(live.Decisions.Answer, host.NameOf, missionTitle),
            Feed = handler => model.AssistantEvents.Subscribe(handler),
            FeedSource = line => line.ThreadId is { } t && model.Thread(t) is { } chat ? chat.Title : DextraHost.Name,
            OpenFeed = line => { if (line.ThreadId is { } t && model.Thread(t) is not null) model.SelectedThreadID = t; },
            Profiles = host.Profiles,
            Conversation = thread is null ? null : () => new FrameHostingView(
                liveResize is null ? new ChatView(model.Runtime(thread.Id)) : new HostedEnvironment(model, liveResize, Zooms: false).Body(new ChatView(model.Runtime(thread.Id)))),
            ConversationEmpty = thread is null ? () => true : () => model.Runtime(thread.Id).Entries.Count == 0,
            Say = thread is null ? null : text => model.Runtime(thread.Id).SendMissionMessage(text),
            // AS-18: the header's Talk toggles the conversation, whose sheet opens over this column; the chord and the composer's talk button start it too
            Talk = host.Voice is { } voice ? voice.Conversation.Toggle : null,
            Voice = host.Voice?.Conversation,
            TalkChord = () => host.Settings.TalkChord,
            TaskActions = new TaskActions(
                Open: job => { if (job.LeadThread is { } lead && model.Thread(lead) is not null) model.SelectedThreadID = lead; },
                Redirect: controller is null ? null : (job, text) => controller.Runner.Redirect(job.Id, text),
                Review: job => Home?.Board.Pick(BoardTab.Decisions),
                Lineage: sheets.ActivityLog is null ? null : job => ShowSheet("Activity log", sheets.ActivityLog(host, job.Id.ToString("D")))),
            Menu = new HomeMenu(
                Reserves: sheets.Reserves is null || controller?.Reserve is null ? null : () => ShowSheet("Reserves", sheets.Reserves(host)),
                ActivityLog: sheets.ActivityLog is null ? null : () => ShowSheet("Activity log", sheets.ActivityLog(host, null)),
                Settings: () => OpenSettings(SettingsPage.Assistant)),
            FirstRun = () => host.Settings.Value.FirstRunDone ? null : firstRun ??= new FirstRunCard(host.Settings, BrainChoices.Of(model), done: null,
                openSettings: () => OpenSettings(SettingsPage.Assistant)),
        };
    }

    private Control OffView()
    {
        var line = new TextBlock { Text = AssistantSettingsWords.Off, HorizontalAlignment = HorizontalAlignment.Center };
        SwiftFont.System(15, SwiftFont.Weight.Medium).Apply(line);
        line.Foreground = Chrome.SecondaryText.Brush(line);
        var (turnOn, shown) = TaskLook.Small(AssistantSettingsWords.TurnOn, () => host.Settings.Update(p => p with { On = true }), primary: true, name: "Turn on Dextra");
        TurnOnButton = turnOn;
        shown.HorizontalAlignment = HorizontalAlignment.Center;
        return new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { line, shown } };
    }

    /// <summary>Shows a view over the home with its title and Close; Escape closes it and focus goes back where it was.</summary>
    internal void ShowSheet(string title, Control? content)
    {
        if (content is null) return;
        sheetReturn = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        var heading = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(15, SwiftFont.Weight.Semibold).Apply(heading);
        heading.Foreground = Chrome.PrimaryText.Brush(heading);
        AutomationProperties.SetHeadingLevel(heading, 2);
        var (close, closeShown) = TaskLook.Small("Close", CloseSheet, name: "Close " + title);
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { heading, closeShown } };
        Grid.SetColumn(closeShown, 1);
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 12, Children = { top, content } };
        Grid.SetRow(content, 1);
        var face = new Border { Padding = new Thickness(18, 14), Margin = new Thickness(24), Child = body };
        SwiftGlass.SetEffect(face, GlassEffect.Regular(GlassShape.RoundedRectangle(18, RoundedCornerStyle.Continuous)));
        AutomationProperties.SetName(face, title);
        sheetHost.Child = face;
        sheetHost.IsVisible = true;
        page.IsHitTestVisible = false;
        close.Focus(NavigationMethod.Tab);
    }

    internal void CloseSheet()
    {
        if (!sheetHost.IsVisible) return;
        if (sheetHost.Child is Border { Child: Grid { Children: [_, var content] } } && content is IDisposable owned) owned.Dispose();
        sheetHost.Child = null;
        sheetHost.IsVisible = false;
        page.IsHitTestVisible = true;
        if (sheetReturn is { } back && back.IsAttachedToVisualTree()) back.Focus(NavigationMethod.Tab);
        sheetReturn = null;
    }
}

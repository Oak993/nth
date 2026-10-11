// PORT-ONLY: RR-18, OD-0099 Custom C/Browse. Locked sd-page.js:238-243,280-303,440-460; sd-page.css:414-454.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System.Text.RegularExpressions;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using Marrow.Services.Port.Skills.Host;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Skills.Pages;

internal enum CustomAddKind { Plugins, Skills, Tools }
internal enum CustomAddSource { Folder, Zip, GitHub }
internal sealed record CustomAddRequest(CustomAddKind Kind, CustomAddSource Source, string Location, string Name, string Command);
internal sealed record CustomAddCandidate(string Key, string Name, string Description, string? Problem);

/// <summary>The UI's small seam for custom-source validation/installation. No fallback implementation pretends to install.</summary>
internal interface ICustomAddBackend
{
    Task<IReadOnlyList<CustomAddCandidate>> Check(CustomAddRequest request, CancellationToken cancel);
    Task Add(CustomAddCandidate candidate, IProgress<double> progress, CancellationToken cancel);

    /// <summary>Adds the checked candidate in place of the own item <paramref name="replacingId"/>: the new copy goes in first and the old one goes only after that worked, so a failed add never loses the old one (ED-NEW-mcpimport-6).</summary>
    Task Replace(string replacingId, CustomAddCandidate candidate, IProgress<double> progress, CancellationToken cancel);
}

internal sealed record CustomAddServices(Func<CustomAddKind, ICustomAddBackend?> For, CustomOwnModel? Own = null);

internal static class CustomAddSeams
{
    internal static CustomAddServices For(Marrow.Services.Port.Skills.Custom.CustomSources sources)
    {
        var own = new CustomOwnModel(sources);
        var adapters = Enum.GetValues<CustomAddKind>().ToDictionary(k => k, _ => (ICustomAddBackend)new CustomAddUiBackend(sources, own));
        return new(kind => adapters[kind], own);
    }
    internal static CustomAddServices Default()
    {
        try { return For(SkillsSeams.Host.Current.CustomSources); }
        catch (SwiftTrapException) { return new(_ => null); }
    }
    public static Ambient<CustomAddServices> Backends { get; } = new(Default);
}

internal sealed class CustomAddSheet : Border
{
    private readonly CustomAddKind kind;
    private readonly ICustomAddBackend? backend;
    private readonly Func<CustomAddSource, Task<string?>> pick;
    private readonly Action close;
    private readonly CancellationTokenSource cancellation = new();
    private readonly CzCrossfadeText title = new("") { FontSize = 15, FontWeight = FontWeight.SemiBold };
    private readonly CzCrossfadeText label = new("") { FontSize = 11, Secondary = true };
    private readonly CzCrossfadeText foot = new("") { FontSize = 11.5, Secondary = true, Wrap = true };
    private CzCrossfadeText found = new("") { FontSize = 12.5, FontWeight = FontWeight.SemiBold, Wrap = true };
    private readonly CrossfadeSlot results = new();
    private readonly CrossfadeSlot buttons = new();
    private readonly TextBox name = new() { PlaceholderText = "Name" };
    private readonly TextBox command = new() { PlaceholderText = "Command it provides" };
    private CustomAddSource source;
    private string? location;
    private string phase = "open";
    private int generation;
    private bool dismissed;
    private IReadOnlyList<CustomAddCandidate> candidates = [];
    private readonly Dictionary<string, CustomAddRow> rows = [];
    private readonly Dictionary<CustomAddSource, Button> sourceButtons = [];
    private readonly Dictionary<CustomAddSource, Border> sourceFaces = [];
    private IReadOnlyList<CustomAddCandidate> attempted = [];
    private static readonly Regex GitHubLink = new(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+(/\S+)?$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public CustomAddSheet(CustomAddKind kind, ICustomAddBackend? backend, Func<CustomAddSource, Task<string?>> pick, Action close)
    {
        this.kind = kind;
        this.backend = backend;
        this.pick = pick;
        this.close = close;
        Width = 500;
        Padding = new Thickness(18, 16, 18, 14);
        this.RoundedRectangle(18, RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(this, GlassEffect.Regular(GlassShape.RoundedRectangle(18, RoundedCornerStyle.Continuous)));
        this.Named("Add", "custom-add-sheet");
        var subtitle = LibraryUi.Text("Choose a folder or a .zip with Browse\u2026, or give a GitHub link.", 12, secondary: true, wrap: true);
        var headerWords = new StackPanel { Spacing = 2, Children = { title, subtitle } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*"), ColumnSpacing = 12, Children = { LibraryUi.Tile("plus", 36, 10), headerWords } };
        Grid.SetColumn(headerWords, 1);
        var sources = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 6 };
        foreach (var value in new[] { CustomAddSource.Folder, CustomAddSource.Zip, CustomAddSource.GitHub })
        {
            var caption = value switch { CustomAddSource.Folder => "Folder", CustomAddSource.Zip => ".zip", _ => "GitHub" };
            var symbol = value switch { CustomAddSource.Folder => "folder", CustomAddSource.Zip => "doc.zipper", _ => "link" };
            var face = new Border { Height = 32, Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Children = { LibraryUi.Glyph(symbol, 14, ChromeTokens.SecondaryText), LibraryUi.Text(caption, 12.5) } } }.RoundedRectangle(10, RoundedCornerStyle.Continuous);
            var button = PlainControls.Button(face, () => _ = SelectSource(value));
            button.Named(caption, "custom-source-" + value);
            button.Focusable = true;
            sourceFaces[value] = face;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(button, sources.Children.Count);
            sources.Children.Add(button);
            sourceButtons[value] = button;
        }
        Location = Field(new TextBox { IsReadOnly = true });
        Field(name); Field(command);
        Browse = LibraryButtons.Make("Browse\u2026", () => _ = BrowseAgain(), ButtonLook.Plain, "custom-browse");
        Location.TextChanged += (_, _) => { if (source == CustomAddSource.GitHub) { location = Location.Text; ApplyPrimary(); } };
        name.TextChanged += (_, _) => ApplyPrimary();
        command.TextChanged += (_, _) => ApplyPrimary();
        var field = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10, Children = { FieldSurface(Location), Browse } };
        Grid.SetColumn(Browse, 1);
        var fields = new StackPanel { Spacing = 3, Children = { label, field } };
        var nameField = new StackPanel { Spacing = 3, Children = { LibraryUi.Text("Name", 11, secondary: true), FieldSurface(name) } };
        var commandField = new StackPanel { Spacing = 3, Children = { LibraryUi.Text("Command it provides", 11, secondary: true), FieldSurface(command) } };
        var tools = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10, Children = { nameField, commandField }, IsVisible = kind == CustomAddKind.Tools };
        Grid.SetColumn(commandField, 1);
        var need = kind switch
        {
            CustomAddKind.Skills => "Each skill needs a SKILL.md with a name and a description.",
            CustomAddKind.Plugins => "Each plugin needs its manifest: .claude-plugin\\plugin.json or .codex-plugin\\plugin.json.",
            _ => "A tool needs an .exe, a name and the command it provides.",
        };
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12, Children = { foot, buttons } };
        Grid.SetColumn(buttons, 1);
        Child = new StackPanel { Spacing = 10, Children = { header, sources, fields, tools, LibraryUi.Text(need, 11.5, secondary: true, wrap: true), results, footer } };
        if (backend is null) ((StackPanel)Child).Children.Insert(1, LibraryUi.Text(Marrow.Core.Port.Skills.Pages.LibraryWords.Unavailable, 12, secondary: true, wrap: true));
        ApplySource();
        ApplyButtons();
        DetachedFromVisualTree += (_, _) => Cancel();
    }

    internal TextBox Location { get; }
    internal Button Browse { get; }
    internal Button Primary { get; private set; } = null!;
    internal string PrimaryText => LibraryButtons.Words(Primary);
    internal string TitleText => title.Text;
    internal string FoundText => found.Text;
    internal string FootText => foot.Text;
    internal string Phase => phase;
    internal IReadOnlyDictionary<string, CustomAddRow> Candidates => rows;
    private string Noun => kind switch { CustomAddKind.Plugins => "plugin", CustomAddKind.Skills => "skill", _ => "tool" };
    private string Plural => Noun + "s";
    private Animation? Fade => !FrameClock.IsBound ? null : LibraryUi.Still(this) ? Animation.EaseOut(.01) : CzMotion.Ease(.26);

    private static TextBox Field(TextBox box)
    {
        PlainControls.PlainTextField(box);
        box.Height = 30; box.Padding = new Thickness(10, 0);
        box.FontSize = 12; box.FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace");
        box.Foreground = ChromeTokens.PrimaryText.Brush(box);
        box.Background = ChromeTokens.Overlay(.06).Brush(box);
        return box;
    }

    private static Border FieldSurface(TextBox field)
    {
        var surface = new Border { Height = 30, Child = field, BorderThickness = new Thickness(1) }.RoundedRectangle(9, RoundedCornerStyle.Continuous);
        surface.Background = ChromeTokens.Overlay(.06).Brush(surface);
        surface.BorderBrush = ChromeTokens.Overlay(.12).Brush(surface);
        return surface;
    }

    internal async Task SelectSource(CustomAddSource next)
    {
        if (phase is "checking" or "adding" or "done") return;
        generation++;
        await ReleaseCandidates();
        source = next;
        location = null;
        phase = "open";
        candidates = [];
        rows.Clear();
        results.Show(new Border { Height = 0 }, Fade);
        ApplySource();
        ApplyButtons();
        if (next != CustomAddSource.GitHub) await BrowseAgain();
    }

    private void ApplySource()
    {
        title.SetText("Add " + Plural + " from " + (source switch { CustomAddSource.Folder => "a folder", CustomAddSource.Zip => "a .zip", _ => "GitHub" }));
        label.SetText(source switch { CustomAddSource.Folder => "Folder on this PC", CustomAddSource.Zip => ".zip file on this PC", _ => "GitHub link (owner/repo, optional path)" });
        Location.IsReadOnly = source != CustomAddSource.GitHub;
        Location.PlaceholderText = source == CustomAddSource.GitHub ? "owner/repo" : null;
        Location.Text = location ?? (source switch { CustomAddSource.Folder => "No folder chosen", CustomAddSource.Zip => "No .zip chosen", _ => "" });
        Browse.IsVisible = source != CustomAddSource.GitHub;
        foreach (var (value, face) in sourceFaces)
        {
            var on = value == source;
            face.Background = (on ? ChromeTokens.Accent.Opacity(.16) : ChromeTokens.Overlay(.03)).Brush(face);
            face.BorderBrush = (on ? ChromeTokens.Accent : ChromeTokens.Overlay(.12)).Brush(face);
            face.BorderThickness = new Thickness(on ? 1.5 : 1);
        }
        foot.SetText(location is null && source != CustomAddSource.GitHub ? "Choose it with Browse\u2026, then press Check." : "Nothing is added until you check it and press Add.");
    }

    private async Task BrowseAgain()
    {
        if (source == CustomAddSource.GitHub || phase is "checking" or "adding" or "done") return;
        var expected = ++generation;
        var chosen = await pick(source);
        if (expected != generation || cancellation.IsCancellationRequested) return;
        if (chosen is null) { ApplyPrimary(); return; }
        await ReleaseCandidates();
        location = chosen;
        phase = "open";
        rows.Clear(); candidates = [];
        results.Show(new Border { Height = 0 }, Fade);
        ApplySource();
        ApplyButtons();
    }

    private string? ChosenLocation => source == CustomAddSource.GitHub ? Location.Text : location;

    private bool CanCheck => backend is not null && !string.IsNullOrWhiteSpace(ChosenLocation) &&
        (source != CustomAddSource.GitHub || GitHubLink.IsMatch(ChosenLocation)) &&
        (kind != CustomAddKind.Tools || !string.IsNullOrWhiteSpace(name.Text) && !string.IsNullOrWhiteSpace(command.Text));

    internal async Task Check()
    {
        if (phase != "open" || !CanCheck) return;
        var expected = ++generation;
        phase = "checking";
        ApplyButtons();
        var searching = source switch { CustomAddSource.Folder => "Looking in the chosen folder for ", CustomAddSource.Zip => "Opening the chosen .zip and looking for ", _ => "Fetching the repository and looking for " };
        searching += kind switch { CustomAddKind.Skills => "SKILL.md files", CustomAddKind.Plugins => "plugin manifests", _ => "an .exe" };
        results.Show(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, Children = { new Marrow.UI.Port.Nth.NthSpinner { Width = 14, Height = 14 }, LibraryUi.Text(searching + "\u2026", 12.5, secondary: true, wrap: true) } }, Fade);
        foot.SetText("Checking what is there.");
        try
        {
            var foundItems = await backend!.Check(new CustomAddRequest(kind, source, ChosenLocation!, name.Text ?? "", command.Text ?? ""), cancellation.Token);
            if (expected != generation || cancellation.IsCancellationRequested)
            {
                if (backend is ICustomAddLifetime lifetime) await lifetime.Discard(foundItems);
                return;
            }
            candidates = foundItems;
            if (candidates.All(c => c.Problem is not null)) { Fail(NoItems()); return; }
            phase = "list";
            var valid = candidates.Count(c => c.Problem is null);
            var invalid = candidates.Count - valid;
            found = new CzCrossfadeText("") { FontSize = 12.5, FontWeight = FontWeight.SemiBold, Wrap = true };
            found.SetText("Found " + valid + " " + (valid == 1 ? Noun : Plural) + (invalid > 0 ? " and " + invalid + " that can\u2019t be added" : "") + ".");
            var list = new StackPanel { Spacing = 0 };
            var all = LibraryButtons.Make("Select all", () => { var tick = rows.Values.Any(r => r.Tick.IsEnabled && r.Tick.IsChecked != true); foreach (var r in rows.Values.Where(r => r.Tick.IsEnabled)) r.Tick.IsChecked = tick; ApplyPrimary(); }, ButtonLook.Plain, "custom-select-all", 12, 28);
            list.Children.Add(all);
            rows.Clear();
            foreach (var candidate in candidates)
            {
                var row = new CustomAddRow(candidate, ApplyPrimary, () => _ = AddOne(candidate));
                rows[candidate.Key] = row;
                list.Children.Add(row);
            }
            results.Show(new StackPanel { Spacing = 6, Children = { found, LibraryUi.Panel(LibraryUi.Scroller(list, 236), 11, .03, new Thickness(6, 3)) } }, Fade);
            foot.SetText("Ticked ones are added side by side.");
            ApplyButtons();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { if (expected == generation) Fail(error.Message); }
    }

    private string NoItems()
    {
        var where = source == CustomAddSource.Zip ? ".zip" : "folder";
        return kind switch
        {
            CustomAddKind.Skills => "No SKILL.md found in this " + where + ". Pick a " + (where == ".zip" ? ".zip with a " : "") + "skill\u2019s own folder, or a folder of skill folders.",
            CustomAddKind.Plugins => "No plugin manifest found in this " + where + " (.claude-plugin\\plugin.json or .codex-plugin\\plugin.json).",
            _ => "No .exe found in this " + where + ".",
        };
    }

    private void Fail(string reason)
    {
        phase = "fail";
        results.Show(LibraryUi.Text(reason, 12, wrap: true), Fade);
        foot.SetText("Nothing was added.");
        ApplyButtons();
    }

    internal async Task AddSelected()
    {
        if (phase != "list" || backend is null) return;
        var selected = candidates.Where(c => c.Problem is null && rows[c.Key].Tick.IsChecked == true).ToList();
        if (selected.Count == 0) return;
        attempted = selected;
        phase = "adding";
        foreach (var row in rows.Values) row.Tick.IsEnabled = false;
        ApplyButtons();
        await Task.WhenAll(selected.Select(AddOne));
        if (cancellation.IsCancellationRequested) return;
        phase = "done";
        Summary();
        ApplyButtons();
    }

    private void Summary()
    {
        var done = attempted.Count(c => rows[c.Key].State == "in");
        var failed = attempted.Count(c => rows[c.Key].State == "fail");
        var summary = done + " of " + attempted.Count + " added" + (failed > 0 ? " \u00b7 " + failed + " didn\u2019t" : "");
        if (phase == "done") summary += ". " + (done > 0 ? "The added ones are under Your own." : "Retry tries the failed ones again.");
        foot.SetText(summary);
    }

    private async Task AddOne(CustomAddCandidate candidate)
    {
        if (backend is null || cancellation.IsCancellationRequested) return;
        var row = rows[candidate.Key];
        row.Apply("dl");
        try
        {
            await backend.Add(candidate, new CustomAddProgress(value =>
            {
                void Apply() { if (!cancellation.IsCancellationRequested) row.Progress = value; }
                if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
            }), cancellation.Token);
            if (!cancellation.IsCancellationRequested) row.Apply("in");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { if (!cancellation.IsCancellationRequested) row.Apply("fail", error.Message); }
        if (!cancellation.IsCancellationRequested) Summary();
    }

    private void ApplyButtons()
    {
        foreach (var button in sourceButtons.Values) button.IsEnabled = phase is "open" or "list" or "fail";
        Browse.IsEnabled = phase is "open" or "list" or "fail";
        Location.IsEnabled = phase is "open" or "fail";
        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (phase is not ("adding" or "done")) group.Children.Add(LibraryButtons.Make("Cancel", Dismiss, ButtonLook.Plain, "custom-cancel"));
        Primary = LibraryButtons.Make("Check", () => _ = Check(), ButtonLook.Primary, "custom-primary");
        if (phase == "fail") Primary = LibraryButtons.Make("Choose another", () => _ = SelectSource(source), ButtonLook.Primary, "custom-primary");
        if (phase == "list") Primary = LibraryButtons.Make("", () => _ = AddSelected(), ButtonLook.Primary, "custom-primary");
        if (phase == "done") Primary = LibraryButtons.Make("Done", Dismiss, ButtonLook.Primary, "custom-primary");
        if (phase == "adding") group.Children.Add(LibraryUi.Text("Adding\u2026", 12, secondary: true));
        else if (phase != "checking") group.Children.Add(Primary);
        ApplyPrimary();
        buttons.Show(group, Fade);
    }

    private void ApplyPrimary()
    {
        if (Primary is null) return;
        if (phase == "open") Primary.IsEnabled = CanCheck;
        if (phase != "list") return;
        var count = rows.Values.Count(r => r.Tick.IsChecked == true && r.Candidate.Problem is null);
        LibraryButtons.SetWords(Primary, "Add " + count + " " + (count == 1 ? Noun : Plural));
        Primary.IsEnabled = count > 0 && backend is not null;
    }

    internal void Dismiss()
    {
        if (dismissed) return;
        dismissed = true;
        Cancel();
        close();
    }
    private void Cancel()
    {
        generation++;
        cancellation.Cancel();
        _ = ReleaseCandidates();
    }
    private async Task ReleaseCandidates()
    {
        var old = candidates;
        candidates = [];
        if (backend is ICustomAddLifetime lifetime) await lifetime.Discard(old);
    }
}

internal sealed class CustomAddProgress(Action<double> report) : IProgress<double>
{
    public void Report(double value) => report(value);
}

internal sealed class CustomAddRow : Decorator
{
    private readonly CrossfadeSlot status = new();
    private readonly ProgressRing ring = new() { Width = 18, Height = 18 };
    private readonly Action retry;
    public CustomAddRow(CustomAddCandidate candidate, Action tick, Action retry)
    {
        Candidate = candidate;
        this.retry = retry;
        Tick = new CheckBox { IsChecked = candidate.Problem is null, IsEnabled = candidate.Problem is null, Width = 16, Height = 16 };
        Tick.Template = new FuncControlTemplate<CheckBox>((box, _) =>
        {
            var glyph = LibraryUi.Glyph("checkmark", 11, SwiftColor.White, SwiftFont.Weight.Bold);
            var square = new Border { Width = 16, Height = 16, Child = glyph }.RoundedRectangle(5, RoundedCornerStyle.Continuous);
            var shown = new AnimatedScalar(square, box.IsChecked == true ? 1 : 0, value => { glyph.Opacity = value; glyph.RenderTransform = new ScaleTransform(.5 + .5 * value, .5 + .5 * value); });
            void Apply()
            {
                var on = box.IsChecked == true;
                square.Background = on ? ChromeTokens.Accent.Brush(square) : null;
                square.BorderBrush = on ? null : ChromeTokens.SecondaryText.Brush(square);
                square.BorderThickness = on ? default : new Thickness(1.3);
                shown.Set(on ? 1 : 0, LibraryUi.Ease(square, CzMotion.Ez(.28)));
            }
            box.IsCheckedChanged += (_, _) => Apply();
            Apply();
            return square;
        });
        Tick.IsCheckedChanged += (_, _) => tick();
        var title = LibraryUi.Mono(candidate.Name, 12);
        var detail = LibraryUi.Text(candidate.Problem ?? candidate.Description, 11, secondary: true, trim: true);
        var text = new StackPanel { Spacing = 1, Children = { title, detail } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("16,*,Auto"), ColumnSpacing = 10, MinHeight = 32, Margin = new Thickness(4, 2), Children = { Tick, text, status } };
        Grid.SetColumn(text, 1); Grid.SetColumn(status, 2);
        foreach (var part in grid.Children) part.VerticalAlignment = VerticalAlignment.Center;
        Child = grid;
        Opacity = candidate.Problem is null ? 1 : .5;
        Apply(candidate.Problem is null ? "av" : "bad");
    }
    internal CustomAddCandidate Candidate { get; }
    internal CheckBox Tick { get; }
    internal string State { get; private set; } = "av";
    internal double Progress { set => ring.Value = Math.Clamp(value, 0, 1); }
    internal void Apply(string next, string? error = null)
    {
        State = next;
        Control face = next switch
        {
            "dl" => ring,
            "in" => LibraryUi.Glyph("checkmark.circle.fill", 18, ChromeTokens.Success),
            "bad" => LibraryUi.Text("Can\u2019t be added", 11, secondary: true),
            "fail" => LibraryButtons.Make("Retry", retry, ButtonLook.Accent, "custom-retry-" + Candidate.Key, 11, 22),
            _ => new Border { Width = 24 },
        };
        if (error is not null) ToolTip.SetTip(face, error);
        status.Show(face, !FrameClock.IsBound ? null : LibraryUi.Still(this) ? Animation.EaseOut(.01) : CzMotion.Ease(.22));
    }
}

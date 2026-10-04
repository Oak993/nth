using Avalonia.Controls;
using Marrow.App.Window;
using Marrow.UI.Port;

namespace Marrow.App.Port.FirstRun;

// GR-2 skeleton (red): the API the acceptance tests are written against.

public static class GettingReadyLook
{
    public const double SheetWidth = 0, PaddingTop = 0, PaddingSide = 0, PaddingBottom = 0, CornerRadius = 0;
    public const double TitleSize = 0, TitleLine = 0, SubSize = 0, SubLine = 0, SubTop = 0, SubBottom = 0;
    public const int Columns = 0;
    public const double ColumnGap = 0, RowHeight = 0, GlyphGap = 0, GlyphSize = 0, MarkSize = 0, LabelSize = 0, ListBottom = 0, BarHeight = 0;
    public const double Hold = 0, HandOverSeconds = 0, LiftExtra = 0, HiSpan = 0, BlurSigma = 0, PaneOpacityRest = 0;
    public const string Title = "", Line = "", Done = "";
    public static double Ease(double x) => throw new NotImplementedException();
    public static double Hi(double hand) => throw new NotImplementedException();
    public static double LiftOffset(double hand, double sheetHeight) => throw new NotImplementedException();
    public static double SheetOpacity(double hand) => throw new NotImplementedException();
    public static double PaneBlurSigma(double hand) => throw new NotImplementedException();
    public static double PaneOpacity(double hand) => throw new NotImplementedException();
}

public enum GettingReadyPhase { Working, Ready, Lifting, Gone }

public sealed class GettingReadyRowView
{
    public string Id => throw new NotImplementedException();
    public string Label => throw new NotImplementedException();
    public FirstRunStepState State => throw new NotImplementedException();
    public double SpinnerOpacity => throw new NotImplementedException();
    public double SpinnerScale => throw new NotImplementedException();
    public double CheckOpacity => throw new NotImplementedException();
    public double CheckScale => throw new NotImplementedException();
    public double DotOpacity => throw new NotImplementedException();
    public double LabelOpacity => throw new NotImplementedException();
    public Control Box => throw new NotImplementedException();
    public Control Glyph => throw new NotImplementedException();
    public Control Text => throw new NotImplementedException();
    public Control Mark => throw new NotImplementedException();
}

public sealed class GettingReadySheet : Decorator, IFirstRunScreen
{
    public GettingReadySheet(IReadOnlyList<FirstRunStepInfo> plan) => throw new NotImplementedException();
    public GettingReadyPhase Phase => throw new NotImplementedException();
    public double Hand => throw new NotImplementedException();
    public double BarFraction => throw new NotImplementedException();
    public string TitleWord => throw new NotImplementedException();
    public double ReadyOpacity => throw new NotImplementedException();
    public double ReadyRise => throw new NotImplementedException();
    public IReadOnlyList<GettingReadyRowView> Rows => throw new NotImplementedException();
    public Control Card => throw new NotImplementedException();
    public Control TitleBox => throw new NotImplementedException();
    public Control LineText => throw new NotImplementedException();
    public Control List => throw new NotImplementedException();
    public Control BarTrack => throw new NotImplementedException();
    public Control BarFill => throw new NotImplementedException();
    public event Action<double>? HandChanged;
    public event Action? Lifted;
    public void Begin(IReadOnlyList<FirstRunStepInfo> steps) => throw new NotImplementedException();
    public void Report(FirstRunProgress progress) => throw new NotImplementedException();
    public Task EndAsync(FirstRunOutcome outcome) => throw new NotImplementedException();
    internal void Unused() { HandChanged?.Invoke(0); Lifted?.Invoke(); }
}

internal sealed class GettingReadyLaunch
{
    internal GettingReadyLaunch(FirstRunPreparation preparation) => throw new NotImplementedException();
    internal static GettingReadyLaunch Arm(WindowManager windows, FirstRunPreparation? preparation = null) => throw new NotImplementedException();
    internal Func<Control> Wrap(Func<Control> makeMain) => throw new NotImplementedException();
    internal GettingReadySheet? Sheet => throw new NotImplementedException();
    internal Task Done => throw new NotImplementedException();
    internal Task<FirstRunOutcome> RunAsync(TopLevel? window) => throw new NotImplementedException();
}

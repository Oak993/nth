using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Views.Sidebar.SidebarViewTests;

namespace Marrow.App.Tests.Views.Sidebar;

// The rig of SidebarFaceInPlaceTests: one live thread row in a window on a manual frame clock, with the reads of the four
// properties the face animates and the writes of the three inputs that reach it.
public enum FaceProperty { Padding, Weight, Opacity, Glyph }

public enum FaceInput { Thread, Selection, MergeStage }

internal sealed class ThreadFaceRig : IDisposable
{
    private readonly ManualFrameSource manual = new();
    private readonly IDisposable frames;
    private readonly IDisposable shared;
    public readonly AppModelHarness Harness = new();
    public readonly ChatThread Thread;
    public readonly AppModel Model;
    public Control Row = null!;
    public Avalonia.Controls.Window Window = null!;

    public ThreadFaceRig(string? projectName = "Alpha")
    {
        frames = FrameClock.Source.Override(manual);
        var project = NewProject(Harness, "Alpha");
        Thread = NewThread(project, "A long title that wants all the room it can get", 0);
        Model = SidebarViewTests.Model(Harness, [project], [Thread]);
        shared = RowGlideAnimator.Shared.Override(new RowGlideAnimator(new FakeGeniePlatform()));
        Harness.Main.OnMain(() =>
        {
            Model.Runtime(Thread.Id); // the runtime exists before the row reads it, so a merge stage written later is observed
            Row = LiveThreadRow(Thread, projectName);
            Window = WindowOf(Hosted(Model, Row));
            Window.UpdateLayout();
        });
    }

    public void Tick(int ms)
    {
        Harness.Main.AdvanceMilliseconds(ms);
        Harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(ms)); Dispatcher.UIThread.RunJobs(); });
    }

    public ThreadRowFace Face() => LogicalQuery.SelfAndDescendants<ThreadRowFace>(Row).Single();

    // the copies of the title's text: one at rest, a Regular under a Medium one while the weight changes
    public IReadOnlyList<TextBlock> Titles() => LogicalQuery.SelfAndDescendants<TextBlock>(Face()).Where(t => !"projectName".Equals(t.Tag)).ToList();

    public TextBlock Title() => Titles()[0];

    public TextBlock Label() => LogicalQuery.SelfAndDescendants<TextBlock>(Face()).Single(t => "projectName".Equals(t.Tag));

    public double Padding() => ((Border)Face().Child!).Padding.Right;

    // the weight the title is drawn at, as 400 + 100 x the Medium copy's opacity (a lone Regular copy is 400, a lone Medium one 500)
    public double Weight() => Titles().FirstOrDefault(t => t.FontWeight == FontWeight.Medium) is { } medium ? 400 + 100 * medium.Opacity : 400;

    public double OpacityByte() => ((ISolidColorBrush)Title().Foreground!).Color.A;

    // the incoming mark of the project line's crossfade (the branch), 1 once the swap is over
    public double Glyph() => LogicalQuery.SelfAndDescendants<SwiftImage>(Face()).Single(i => i.SymbolName == "arrow.triangle.branch").Opacity;

    public double Read(FaceProperty property) => property switch
    {
        FaceProperty.Padding => Padding(),
        FaceProperty.Weight => Weight(),
        FaceProperty.Opacity => OpacityByte(),
        _ => Glyph(),
    };

    public double FullOpacityByte() => Chrome.PrimaryText.Opacity(1).Resolve(Title().ActualThemeVariant).A;

    // the value the property comes to once its animation is over, for the driver `Start` used
    public double End(FaceProperty property) => property switch
    {
        FaceProperty.Padding => 8d + 52,
        FaceProperty.Weight => (int)FontWeight.Medium,
        FaceProperty.Opacity => FullOpacityByte(),
        _ => 1d,
    };

    // starts the property on its way in the transaction SwiftUI would write its input in
    public void Start(FaceProperty property)
    {
        switch (property)
        {
            case FaceProperty.Padding: // :1816-1818 the hover, withAnimation(Chrome.hover)
                Harness.Main.OnMain(() => Window.MouseMove(Row.TranslatePoint(new Point(10, 10), Window)!.Value));
                break;
            case FaceProperty.Weight: // :2041 an unread thread's title turns medium
                Harness.Main.OnMain(() => Transaction.WithAnimation(Chrome.Hover, () => Model.UpdateThread(Thread.Id, t => t with { HasUnread = true })));
                break;
            case FaceProperty.Opacity: // :2042 the selected title comes up to full
                Harness.Main.OnMain(() => Transaction.WithAnimation(Chrome.Hover, () => Model.SelectedThreadID = Thread.Id));
                break;
            default: // :2055 the project line's mark follows the worktree
                Harness.Main.OnMain(() => Transaction.WithAnimation(Chrome.Hover, () => Model.UpdateThread(Thread.Id, t => t with { WorktreePath = "C:\\wt" })));
                break;
        }
    }

    // changes one input of the face, in no transaction, and returns what proves the face was told
    public void Change(FaceInput input)
    {
        switch (input)
        {
            case FaceInput.Thread:
                Harness.Main.OnMain(() => Model.UpdateThread(Thread.Id, t => t with { Title = "Another title that is just as long as the first one was" }));
                break;
            case FaceInput.Selection:
                Harness.Main.OnMain(() => Model.SelectedThreadID = Thread.Id);
                break;
            default:
                Harness.Main.OnMain(() =>
                {
                    var runtime = Model.Runtime(Thread.Id);
                    runtime.HydraMergeStage = "Reviewing";
                    runtime.IsHydraMerging = true;
                });
                break;
        }
    }

    public void AssertTold(FaceInput input)
    {
        switch (input)
        {
            case FaceInput.Thread: Assert.StartsWith("Another title", Title().Text, StringComparison.Ordinal); break;
            case FaceInput.Selection: Assert.True(Model.IsSelected(Thread.Id)); break;
            default: Assert.Equal("Reviewing\U00002026", Label().Text); break; // :2057 the merge stage takes the project name's place
        }
    }

    public void Dispose()
    {
        Harness.Main.OnMain(() => Window.Close());
        shared.Dispose();
        frames.Dispose();
        Harness.Dispose();
    }
}

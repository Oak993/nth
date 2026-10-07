using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.App.Port.ModelsPalette;
using Marrow.App.Tests.Fixtures;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Marrow.UI.Port;
namespace Marrow.App.Tests.Port.ModelsPalette;
// PORT-ONLY: OD-0127, locked palette-refresh option A over the real fake-only engine; ED-618.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-modelspalette-7")]
public sealed class PaletteColoursSettingsTests(HeadlessSession headless)
{
    [Fact]
    public void Refresh_draws_the_real_result_and_keeps_the_summary_until_the_page_is_left()=>headless.OnUiThread(()=>
    {
        using var runtime=new TestRuntime();using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,runtime.Time,fake.Pack);
        using var controller=new PaletteColoursController(engine,()=>5,runtime.Time);
        var control=new PaletteColoursSection(controller);var window=new Avalonia.Controls.Window{Width=620,Content=control};window.Show();
        try
        {
            Assert.Equal("Colours",control.SectionTitle);Assert.Equal("Model colours",control.RowTitle);
            var run=controller.Refresh();Assert.True(controller.View.Running);Assert.Equal("Refreshing…",control.RefreshButton.Label);Assert.False(control.RefreshButton.IsEnabled);
            Assert.Equal("Checking OpenRouter and 5 providers… 0 of 1 models",control.DetailText.Text);Assert.True(control.ProgressBar.Active);
            runtime.Loop.RunUntil(()=>run.IsCompleted);run.GetAwaiter().GetResult();
            Assert.Equal("Checked just now · 2 models from 2 makers",control.DetailText.Text);
            Assert.Equal(new[]{"1 new models coloured","New maker: Acme"},controller.View.Summary);
            Assert.Equal("Refresh",control.RefreshButton.Label);Assert.True(control.RefreshButton.IsEnabled);Assert.False(control.ProgressBar.Active);
            runtime.Advance(TimeSpan.FromMinutes(1));controller.Tick();Assert.Equal(new[]{"1 new models coloured","New maker: Acme"},controller.View.Summary);
            window.Close();Assert.Empty(controller.View.Summary);
        }
        finally{window.Close();}
    });
    [Fact]
    public void A_no_change_refresh_uses_the_exact_words_and_offline_keeps_the_colours()=>headless.OnUiThread(()=>
    {
        using var runtime=new TestRuntime();using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,runtime.Time,fake.Pack);
        var warm=engine.CheckNow();runtime.Loop.RunUntil(()=>warm.IsCompleted);warm.GetAwaiter().GetResult();
        using var controller=new PaletteColoursController(engine,()=>0,runtime.Time);var control=new PaletteColoursSection(controller);
        var window=new Avalonia.Controls.Window{Width=620,Content=control};window.Show();
        try
        {
            var run=controller.Refresh();runtime.Loop.RunUntil(()=>run.IsCompleted);run.GetAwaiter().GetResult();
            Assert.Equal("Checked just now · no new models",control.DetailText.Text);Assert.Empty(controller.View.Summary);
            var digest=engine.Current.Digest;var dots=controller.View.Dots.ToArray();fake.FailModels=true;fake.FailHf=true;runtime.Advance(TimeSpan.FromSeconds(121));
            run=controller.Refresh();runtime.Loop.RunUntil(()=>run.IsCompleted);run.GetAwaiter().GetResult();
            Assert.Equal("Couldn't reach the model lists. Check your connection and try again.",control.DetailText.Text);
            Assert.Equal(digest,engine.Current.Digest);Assert.Equal(dots,controller.View.Dots);
            Assert.True(control.RefreshButton.IsEnabled);
        }
        finally{window.Close();}
    });
    [Fact]
    public void Maker_dots_glide_from_the_displayed_colour_and_reduce_motion_settles_them()=>headless.OnUiThread(()=>
    {
        using var runtime=new TestRuntime();var frames=new ManualFrameSource();using var source=FrameClock.Source.Override(frames);var dot=new PaletteMakerDot("#10AA60",false);
        var window=new Avalonia.Controls.Window{Content=dot};window.Show();
        try
        {
            var initial=dot.Colour;dot.Set("#6070D0");Assert.Equal(initial,dot.Colour);
            runtime.Advance(TimeSpan.FromMilliseconds(550));frames.Advance(TimeSpan.FromMilliseconds(550));Assert.NotEqual(initial,dot.Colour);Assert.NotEqual(dot.TargetColour,dot.Colour);
            var displayed=dot.Colour;dot.Set("#D04020");Assert.Equal(displayed,dot.Colour);
            runtime.Advance(TimeSpan.FromMilliseconds(1100));frames.Advance(TimeSpan.FromMilliseconds(1100));Assert.Equal(dot.TargetColour,dot.Colour);
            EnvironmentValues.SetAccessibilityReduceMotion(window,true);dot.Set("#20C080");Assert.Equal(dot.TargetColour,dot.Colour);
        }
        finally{window.Close();}
    });

    [Fact]
    public void Detail_uses_the_locked_opacity_curve_and_reduce_motion_lands_at_once()=>headless.OnUiThread(()=>
    {
        using var runtime=new TestRuntime();var frames=new ManualFrameSource();using var source=FrameClock.Source.Override(frames);
        var detail=new PaletteDetailText();detail.SetText("Checked just now · no new models");
        var window=new Avalonia.Controls.Window{Content=detail};window.Show();
        try
        {
            detail.SetText("Checking OpenRouter and 5 providers… 0 of 1 models");Assert.Equal(0,detail.IncomingOpacity);
            runtime.Advance(TimeSpan.FromMilliseconds(125));frames.Advance(TimeSpan.FromMilliseconds(125));
            Assert.Equal(Animation.TimingCurve(.25,.1,.25,1,.25).Progress(.125),detail.IncomingOpacity,10);
            EnvironmentValues.SetAccessibilityReduceMotion(window,true);detail.SetText("Checked just now · no new models");Assert.Equal(1,detail.IncomingOpacity);
        }
        finally{window.Close();}
    });

    [Fact]
    public void A_re_spaced_maker_summary_draws_its_real_colour_dot()=>headless.OnUiThread(()=>
    {
        using var runtime=new TestRuntime();using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,runtime.Time,fake.Pack);
        var warm=engine.CheckNow();runtime.Loop.RunUntil(()=>warm.IsCompleted);warm.GetAwaiter().GetResult();
        using var controller=new PaletteColoursController(engine,()=>0,runtime.Time);var control=new PaletteColoursSection(controller);
        var window=new Avalonia.Controls.Window{Content=control};window.Show();
        try
        {
            fake.ImageVersion=1;fake.Date="Thu, 08 Oct 2026 06:20:00 GMT";runtime.Advance(TimeSpan.FromDays(1));
            var run=controller.Refresh();runtime.Loop.RunUntil(()=>run.IsCompleted);run.GetAwaiter().GetResult();
            fake.Date="Fri, 09 Oct 2026 06:20:00 GMT";runtime.Advance(TimeSpan.FromDays(1));
            run=controller.Refresh();runtime.Loop.RunUntil(()=>run.IsCompleted);run.GetAwaiter().GetResult();
            Assert.Equal(new[]{"Acme's shades re-spaced"},controller.View.Summary);
            var dot=Assert.Single(control.GetVisualDescendants().OfType<PaletteMakerDot>(),d=>d.Width==9);
            Assert.Equal(Color.Parse(engine.Current.Makers["acme"].Heart),dot.TargetColour);
        }
        finally{window.Close();}
    });

}

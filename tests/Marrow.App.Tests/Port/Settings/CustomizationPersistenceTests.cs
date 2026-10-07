using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Marrow.App.Port;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models.ComposerLights;
using Marrow.UI.Port.ComposerLights;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-13.1 (the page edits AppSettings.ComposerLight and every change reaches every composer at once), CZ-13.5 (a drag does not write the store every frame; the end of a drag writes it).
[Collection("Headless")]
public sealed class CustomizationPersistenceTests(HeadlessSession headless)
{
    private static ThreadRuntimePart12Harness Harness()
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        var h = new ThreadRuntimePart12Harness();
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        return h;
    }

    private static void Settle(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 5; pass++)
        {
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }

    private static ComposerLightView LightOf(ComposerArea composer) => composer.GetLogicalDescendants().OfType<ComposerLightView>().Single();

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T26, CZ-13.1: a change on the page is the app's setting and reaches another composer's ComposerLightView.Settings, for each kind of control
    [Trait("Spec", "CZ-T26")]
    public void Every_page_change_reaches_a_second_composer_and_the_store() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var runtime = h.Main.OnMain(() => new Marrow.App.Runtime.ThreadRuntime(Guid.NewGuid(), h.App));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        var (composer, page, window) = h.Main.OnMain(() =>
        {
            var composer = new ComposerArea(runtime, workingDirectory: null);
            var page = new CustomizationSettingsPage();
            var column = new StackPanel { Width = 520, Children = { composer, page } };
            AppEnvironment.SetAppModel(column, h.App);
            var window = new AvaloniaWindow { Width = 700, Height = 900, Content = column };
            window.Show();
            Settle(window);
            return (composer, page, window);
        });
        try
        {
            void Pump() => h.Main.OnMain(() => Settle(window));
            Pump();
            h.Main.OnMain(() => Assert.Equal(ComposerLightSettings.Default, LightOf(composer).Settings));

            // a card
            h.Main.OnMain(() => page.StyleCards!.Select("aura"));
            Pump();
            h.Main.OnMain(() => Assert.Equal(ComposerLightStyle.Aura, LightOf(composer).Settings.Style));

            // a segmented control, a swatch, a switch, a slider, a colour (Uiverse: Custom, a switch, a slider)
            h.Main.OnMain(() => page.Aura!.Colours.Control.Select("custom"));
            h.Main.OnMain(() => page.Aura!.RimLight.Switch.Toggle());
            h.Main.OnMain(() => page.Aura!.Speed.Slider.SetValue(20.5));
            h.Main.OnMain(() => page.Aura!.FirstWell.SetColor("#123456CC"));
            Pump();
            h.Main.OnMain(() =>
            {
                var shown = LightOf(composer).Settings.Aura;
                Assert.Equal(AuraScheme.Custom, shown.Scheme);
                Assert.False(shown.Rim);
                Assert.Equal(20.5, shown.Speed);
                Assert.Equal("#123456CC", shown.Color1);
            });
            h.Main.OnMain(() => page.StyleCards!.Select("beam"));
            h.Main.OnMain(() => page.Beam!.Swatches.Select("ocean"));
            h.Main.OnMain(() => page.Beam!.Motion.Control.Select("pulse"));
            h.Main.OnMain(() => page.Beam!.Intensity.Slider.SetValue(0.5));
            Pump();
            h.Main.OnMain(() =>
            {
                var shown = LightOf(composer).Settings;
                Assert.Equal(ComposerLightStyle.Beam, shown.Style);
                Assert.Equal(BeamPalette.Ocean, shown.Beam.Palette);
                Assert.Equal(BeamFamily.Pulse, shown.Beam.Family);
                Assert.Equal(0.5, shown.Beam.Strength);
                Assert.Equal(h.App.Settings.ComposerLight, shown);
                // the last change is stored: the coalescer writes a lone change at once and holds the rest; the end of a drag writes the held one
                h.App.Settings.FlushPendingWrites();
                var stored = Assert.IsType<byte[]>(h.Store.Defaults.Object("composerLight"));
                Assert.Equal(shown, ComposerLightSettings.FromJson(stored));
            });
        }
        finally { h.Main.OnMain(window.Close); }
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T27 (page side), CZ-13.5: a drag moves the in-memory setting every step but writes the store at most every 250 ms; the end of the drag writes the last value
    [Trait("Spec", "CZ-T27")]
    public void A_drag_does_not_write_the_store_every_step_and_its_end_writes_the_last_value() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var speed = rig.Page.Beam!.Speed.Slider;
        var writes = () => rig.Defaults.Writes.Count(key => key == "composerLight");
        var baseline = writes();
        // a 2 second drag at 60 Hz
        for (var frame = 1; frame <= 120; frame++)
        {
            var value = 1 + frame * 0.1;
            rig.Do(() => speed.SetValue(value, dragging: true));
            Assert.Equal(Math.Round(value, 2), rig.Light.Beam.Duration); // the in-memory value is current every step
            rig.Main.AdvanceMilliseconds(1000.0 / 60);
        }
        var duringDrag = writes() - baseline;
        Assert.InRange(duringDrag, 1, 9);
        var last = rig.Light.Beam.Duration;
        // the drag ends (a key lifted on the slider is the same commit): the held write goes out now
        rig.Do(() => speed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Right, Source = speed }));
        var stored = ComposerLightSettings.FromJson(Assert.IsType<byte[]>(rig.Defaults.Object("composerLight")));
        Assert.Equal(last, stored.Beam.Duration);
    });
}

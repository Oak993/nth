using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.App.Views.Sidebar;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Window;

// ED-NEW-textsize-1 (owner, 2026-10-11: "Look at how small the text is"): the zoom in Settings › Appearance scales every surface
// of the app. The window roots carried it (WindowManager.HostedEnvironment), but a popover, a tooltip or a menu is a Popup in a
// top-level of its own (a PopupRoot, or the overlay layer's host above the window's content), outside the root's ZoomLayout,
// so its words stayed at 100 % beside a sidebar and composer drawn at 125 %. Each check measures a known text's drawn size:
// its font size times the scale it is drawn at in its window, at 100 % and at 125 %.
[Collection("Headless")]
public sealed class AppZoomEverywhereTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    // The size a text is drawn at in its window: its font size times the scale of everything between it and the window.
    private static double DrawnSize(TextBlock text, Visual window) => text.FontSize * Assert.IsType<Matrix>(text.TransformToVisual(window)).M11;

    private static double DrawnHeight(TextBlock text, Visual window) => text.Bounds.Height * Assert.IsType<Matrix>(text.TransformToVisual(window)).M22;

    private static Control? Host(Popup popup) => typeof(Popup).GetProperty("Host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(popup) as Control;

    [Theory]
    [Trait("Decision", "ED-NEW-textsize-1")]
    [InlineData(100)]
    [InlineData(125)]
    public void The_main_window_the_floating_sidebar_and_a_popover_draw_their_text_at_the_app_zoom(int percent)
    {
        WithManager((_, manager) =>
        {
            var scale = percent / 100.0 * AppZoom.BaseScale; // the level over the base scale (ED-NEW-textsize-2)
            TextBlock body = null!;
            Button anchor = null!;
            var words = new TextBlock { Text = "Opus 5.5", FontSize = 13 };
            var popup = new Popup { Placement = PlacementMode.Bottom, ShouldUseOverlayLayer = true, Child = words };
            FloatingSidebarPanel panel = null!;
            manager.Model.Settings.AppZoom = percent;
            manager.MainContent = () =>
            {
                body = new TextBlock { Text = "Ask anything", FontSize = 13, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
                anchor = new Button { Content = "Model", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel = new FloatingSidebarPanel(new Size(900, 700), true, () => { });
                popup.PlacementTarget = anchor;
                return new Grid { Children = { panel, body, anchor, popup } }; // the popup in the window's logical tree, as a site's is
            };
            var main = manager.PrepareMain();
            SettingsProviderPopupLayers.Install(main);
            manager.ShowMain();
            Drain(main);

            // The main window's own text and the floating sidebar's rows ("Add project", SidebarView.swift:165): under the root's zoom.
            Assert.Equal(13 * scale, DrawnSize(body, main), 6);
            var addProject = Nodes<TextBlock>(panel).First(text => text.Text == "Add project");
            Assert.Equal(addProject.FontSize * scale, DrawnSize(addProject, main), 6);
            Assert.Equal(13, addProject.FontSize); // the source's .body; the zoom scales the drawing, not the type size
            Assert.Equal(addProject.Bounds.Height * scale, DrawnHeight(addProject, main), 6);

            // A popover hung from a control in the main window: its own host, drawn at the same zoom.
            try
            {
                popup.IsOpen = true;
                var host = Assert.IsType<OverlayPopupHost>(Host(popup));
                host.Template = new FuncControlTemplate<OverlayPopupHost>((owner, scope) => new ContentPresenter { Name = "PART_ContentPresenter", [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty] }.RegisterInNameScope(scope));
                host.ApplyTemplate();
                host.InvalidateMeasure();
                Drain(main);
                Assert.True(words.IsAttachedToVisualTree());
                Assert.Same(popup, words.Parent); // the site's child keeps its logical parent: styles and popup.Child are unchanged
                Assert.Same(words, Assert.IsType<PopupZoom>(host.Content).Child);
                Assert.Equal(13 * scale, DrawnSize(words, main), 6);
                Assert.Equal(words.Bounds.Height * scale, DrawnHeight(words, main), 6);
                // The host is sized to the zoomed content, so nothing of it is clipped.
                Assert.True(host.Bounds.Width >= words.DesiredSize.Width * scale - 0.01 && host.Bounds.Height >= words.DesiredSize.Height * scale - 0.01,
                    $"host {host.Bounds.Size} is smaller than the drawn words {words.DesiredSize * scale}");
            }
            finally
            {
                popup.IsOpen = false;
                Drain(main);
            }
            // Closed, the child is let go of, so the popup opens again in a new host without a second visual parent.
            Assert.Null(words.GetVisualParent());
            popup.IsOpen = true;
            Assert.Same(popup, words.Parent);
            popup.IsOpen = false;
        }, realSettings: false);
    }

    [Theory]
    [Trait("Decision", "ED-NEW-textsize-1")]
    [InlineData(100)]
    [InlineData(125)]
    public void The_Settings_window_draws_its_text_at_the_app_zoom(int percent)
    {
        WithManager((_, manager) =>
        {
            manager.Model.Settings.AppZoom = percent;
            manager.ShowSettings(SettingsPage.Appearance);
            var window = Settings(manager);
            Drain(window);
            var title = Nodes<TextBlock>(View(manager)).First(text => text.Text == "Open the sidebar at launch");
            Assert.Equal(Marrow.UI.Chrome.Chrome.SettingsRowTitleSize, title.FontSize);
            Assert.Equal(Marrow.UI.Chrome.Chrome.SettingsRowTitleSize * percent / 100.0 * AppZoom.BaseScale, DrawnSize(title, window), 6);
            // ED-NEW-textsize-2: at 100 % the Settings type is drawn at the Claude desktop app's sizes (96 DPI, claude-settings-windowed.png):
            // navigation items 18 px on a 43 px pitch, row titles 18 px, descriptions 17 px, section headers 20 px.
            var level = percent / 100.0;
            var nav = Nodes<TextBlock>(View(manager)).First(text => text.Text == "Hydra");
            Assert.Equal(18.2 * level, DrawnSize(nav, window), 6);
            var navRow = nav.GetVisualAncestors().OfType<Border>().First(border => border.Height == Marrow.UI.Chrome.Chrome.SettingsNavRowHeight);
            Assert.Equal(42.9 * level, navRow.Bounds.Height * Assert.IsType<Matrix>(navRow.TransformToVisual(window)).M22, 6);
            Assert.Equal(18.2 * level, DrawnSize(title, window), 6);
            var detail = Nodes<TextBlock>(View(manager)).First(text => text.Text == "Collapsed otherwise; the list button in the toolbar shows and hides it any time");
            Assert.Equal(16.9 * level, DrawnSize(detail, window), 6);
            var header = Nodes<Marrow.UI.Chrome.ChromeSection>(View(manager)).SelectMany(section => Nodes<TextBlock>(section).Take(1)).First();
            Assert.Equal(19.5 * level, DrawnSize(header, window), 6);
            Assert.Equal(FontWeight.SemiBold, header.FontWeight);
        });
    }

    [Theory]
    [Trait("Decision", "ED-NEW-textsize-1")]
    [InlineData(100)]
    [InlineData(125)]
    public void A_window_of_its_own_takes_the_zoom_of_the_window_it_serves(int percent)
    {
        WithManager((_, manager) =>
        {
            manager.Model.Settings.AppZoom = percent;
            manager.ShowMain();
            var main = MainOf(manager);
            Drain(main);
            Assert.Equal(percent / 100.0 * AppZoom.BaseScale, AppZoom.ScaleOfWindow(main), 9);
            var message = new TextBlock { Text = "Send this conversation?", FontSize = 13 };
            var shown = AppZoom.Zoomed(message, AppZoom.ScaleOfWindow(main));
            var dialog = new Avalonia.Controls.Window { Content = shown, SizeToContent = SizeToContent.WidthAndHeight };
            dialog.Show();
            try
            {
                Drain(dialog);
                Assert.Equal(13 * percent / 100.0 * AppZoom.BaseScale, DrawnSize(message, dialog), 6);
                Assert.Same(message, AppZoom.Unzoomed(dialog.Content));
                Assert.True(dialog.ClientSize.Width >= message.DesiredSize.Width * percent / 100.0 * AppZoom.BaseScale - 0.01);
            }
            finally { dialog.Close(); }
        }, realSettings: false);
    }
}

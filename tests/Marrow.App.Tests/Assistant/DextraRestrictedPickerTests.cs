using Marrow.App.Assistant;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Testing;
namespace Marrow.App.Tests.Assistant;
[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-open-7")]
public sealed class DextraRestrictedPickerTests(HeadlessSession headless)
{
    [Fact]
    public void Keyboard_selection_and_a_deferred_long_RTL_menu_rebuild_keep_an_unavailable_provider_disabled()
    {
        headless.OnUiThread(() =>
        {
            using var still = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
            var settings = new DextraSettings(new InMemorySettingsStore());
            settings.Update(p => p with { Delegate = DelegateScope.Everywhere });
            var rows = new AssistantRows(settings, new BrainChoices(() => [ProviderKind.Claude, ProviderKind.Codex], _ => [], _ => []));
            rows.SetModelScope(DelegateModelScope.Everywhere);
            var window = new Avalonia.Controls.Window { Width = 700, Height = 240, Content = rows.WithRow };
            try
            {
                window.Show(); window.UpdateLayout(); rows.WithMenu.OpenMenu();
                window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.End });
                window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                Assert.Null(settings.Value.DelegateWith);
                Assert.True(rows.WithMenu.IsOpen);
                rows.WithMenu.SetOptions([("dextra", "Dextra"), ("Claude", "Claude"), ("Codex", "Codex — unavailable: لا يمكن تعطيل الأدوات " + new string('x', 120))], "dextra");
                window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                Dispatcher.UIThread.RunJobs();
                Assert.False(rows.WithMenu.IsOpen);
                Assert.False(rows.WithMenu.Items.Single(i => i.Id == "Codex").IsEnabled);
                rows.WithMenu.Select("Codex"); Assert.Null(settings.Value.DelegateWith);
                rows.WithMenu.Select("Claude"); Assert.Equal(new DelegateWho(ProviderKind.Claude), settings.Value.DelegateWith);
            }
            finally { window.Content = null; window.Hide(); }
        });
    }
    [Fact]
    public void Every_registry_provider_is_either_selectable_or_visibly_unavailable_with_a_reason()
    {
        headless.OnUiThread(() =>
        {
            var settings = new DextraSettings(new InMemorySettingsStore());
            var offered = SwiftEnum<ProviderKind>.AllCases.ToArray();
            Assert.Equal(16, offered.Length);
            var rows = new AssistantRows(settings, new BrainChoices(() => offered, _ => [], _ => []));
            foreach (var provider in offered)
            {
                var item = rows.WithMenu.Items.Single(i => i.Id == provider.ToString());
                var supported = provider is ProviderKind.Claude or ProviderKind.Pi or ProviderKind.Deepseek or ProviderKind.Meta
                    or ProviderKind.Zai or ProviderKind.OpenRouter or ProviderKind.Local or ProviderKind.Commandcode;
                Assert.Equal(supported, item.IsEnabled);
                if (!supported)
                {
                    Assert.Contains("unavailable", item.Label.Text!, StringComparison.OrdinalIgnoreCase);
                    Assert.True(item.Label.Text!.Length > provider.DisplayName.Length + 18);
                    rows.WithMenu.Select(provider.ToString());
                    Assert.Null(settings.Value.DelegateWith);
                    rows.Show();
                    Assert.Equal(AssistantRows.DextraOption, rows.WithMenu.SelectedId);
                }
            }
        });
    }

    [Fact]
    public void An_old_unavailable_choice_remains_visible_and_checked_and_is_never_replaced_with_another_provider()
    {
        headless.OnUiThread(() =>
        {
            var settings = new DextraSettings(new InMemorySettingsStore());
            settings.Update(p => p with { DelegateWith = new DelegateWho(ProviderKind.Codex, "saved-model"), BrainProvider = ProviderKind.Codex });
            var rows = new AssistantRows(settings, BrainChoices.None);
            var item = rows.WithMenu.Items.Single(i => i.Id == ProviderKind.Codex.ToString());
            Assert.True(item.Checked);
            Assert.False(item.IsEnabled);
            Assert.Contains("unavailable", item.Label.Text!, StringComparison.OrdinalIgnoreCase);
            Assert.False(rows.WithMenu.Items.Single(i => i.Id == AssistantRows.DextraOption).IsEnabled);
            Assert.Equal(new DelegateWho(ProviderKind.Codex, "saved-model"), settings.Value.DelegateWith);
        });
    }
}

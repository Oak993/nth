// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-15; VT-16): voice-SPEC V-16.3.1, Q-V-16's Providers field pattern.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.Core.Port;
using Marrow.Services.Port.Voice.Cloud;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

/// <summary>The Providers masked, monospaced field and eye: edits save immediately through SecretStore; blank removes the key. Cancel closes the fold.</summary>
internal sealed class VoiceKeyEditor
{
    private readonly VoiceKeyStore keys = new();
    private readonly TextBox field;
    private readonly Border eyeLabel;
    private readonly Button eye;
    private readonly TextBlock refusal;
    private readonly CzNumber labelFade;
    private bool updating;
    private bool revealed;
    private string input = "";
    private string labelTarget = VoiceTypingText.KeyAdd;
    private double labelStartOpacity = 1;

    public CzCapsuleButton Button { get; } = new(VoiceTypingText.KeyAdd);
    public CzFoldRow Fold { get; }

    public VoiceKeyEditor()
    {
        // SettingsView's Providers row: 200 px, 12 pt monospaced, rounded border, masked with U+2022 until the eye is pressed.
        field = PlainControls.TextField(VoiceTypingText.KeyPlaceholder, new Binding<string>(() => input, value => { input = value; Save(value); }));
        field.PasswordChar = '\u2022';
        field.Width = 200;
        SwiftFont.System(12, design: SwiftFont.Design.Monospaced).Apply(field);
        ChatDialogControls.TextEditor(field, style: "RoundedBorder");
        AutomationProperties.SetName(field, VoiceTypingText.KeyTitle);
        eyeLabel = new Border { Background = Brushes.Transparent };
        eye = PlainControls.Button(eyeLabel, () => { revealed = !revealed; RefreshEye(); });
        refusal = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Chrome.SecondaryText.Brush(field), IsVisible = false }
            .Font(SwiftFont.System(SwiftFont.TextStyle.Subheadline));
        Fold = new CzFoldRow(new StackPanel
        {
            Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { field, eye } }, refusal },
        }, initiallyOpen: false);
        var label = (TextBlock)Button.Child!;
        labelFade = new CzNumber(Button.Motion, 1, value =>
        {
            // OD-0076: retain the outgoing word until it has faded away; replace at the zero-opacity midpoint.
            if (value >= .5) label.Text = labelTarget;
            label.Opacity = value < .5 ? labelStartOpacity * (1 - value * 2) : value * 2 - 1;
        });
        Button.Invoked += () => { if (Fold.Open) Close(); else Open(); };
        RefreshEye();
    }

    private void Save(string value)
    {
        if (updating || !Fold.Open) return;
        // OD-0012 E5: the same refusal as Providers. A delete can be refused as well; read back before claiming a blank removed the credential.
        var saved = keys.Save(value);
        if (string.IsNullOrWhiteSpace(value) && keys.Read() is not null) saved = false;
        refusal.Text = saved ? "" : VoiceTypingText.KeyRefusal;
        refusal.IsVisible = !saved;
    }

    private void Open()
    {
        updating = true;
        try { input = keys.Read() ?? ""; field.Text = input; }
        finally { updating = false; }
        revealed = false;
        RefreshEye();
        refusal.Text = "";
        refusal.IsVisible = false;
        Fold.Open = true;
        SetLabel(VoiceTypingText.KeyCancel);
        field.Focus();
    }

    public void Close()
    {
        Fold.Open = false;
        updating = true;
        try { input = ""; field.Text = ""; }
        finally { updating = false; }
        revealed = false;
        RefreshEye();
        refusal.Text = "";
        refusal.IsVisible = false;
        SetLabel(VoiceTypingText.KeyAdd);
    }

    private void SetLabel(string text)
    {
        var label = (TextBlock)Button.Child!;
        if (labelTarget == text) return;
        labelTarget = text;
        labelStartOpacity = label.Opacity;
        labelFade.Jump(0);
        AutomationProperties.SetName(Button, text);
        labelFade.To(1, CzMotion.Ease(.25));
    }

    private void RefreshEye()
    {
        field.PasswordChar = revealed ? '\0' : '\u2022';
        var text = revealed ? "Hide the API key" : "Show the API key";
        eyeLabel.Child = SwiftImage.SystemName(revealed ? "eye.slash" : "eye").Font(SwiftFont.System(12)).ForegroundStyle(Chrome.SecondaryText);
        ToolTip.SetTip(eye, text);
        AutomationProperties.SetName(eye, text);
    }
}

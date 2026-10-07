// PORT-ONLY: multi-account step 3a (OD-0115, OD-0119). "Pill in the composer": ONE style for every capsule, Merged capsule until the user picks (multi-account.html setCapStyle).
namespace Marrow.Core.Port.MultiAccount.Capsule;

public sealed class CapsuleStyleSetting
{
    public const string SettingsKey = "composerPillStyle";
    private readonly ISettingsStore settings;
    public event Action? Changed;

    public CapsuleStyleSetting(ISettingsStore settings) => this.settings = settings;

    public string Value
    {
        get => settings.Object(SettingsKey) is string stored && CapsuleStyles.IsKnown(stored) ? stored : CapsuleStyles.Default;
        set
        {
            if (!CapsuleStyles.IsKnown(value) || value == Value) return;
            settings.Set(SettingsKey, value);
            Changed?.Invoke();
        }
    }
}

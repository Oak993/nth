using Marrow.Core.Port.MultiAccount.Capsule;

namespace Marrow.App;

public sealed partial class AppSettings
{
    private CapsuleStyleSetting? capsuleStyle;

    /// <summary>"Pill in the composer" (Settings > Customization): the one style every capsule follows (OD-0115, OD-0119).</summary>
    public CapsuleStyleSetting CapsuleStyle => capsuleStyle ??= new(defaults);
}

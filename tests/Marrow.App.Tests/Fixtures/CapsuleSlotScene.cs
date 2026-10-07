using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Fixtures;

// The rig, style setting, subject and slot of one ComposerCapsuleSlotTests run (a fixture, not a nested helper type: one type per file).
internal sealed class CapsuleSlotScene
{
    public required AccountsRig Rig { get; init; }
    public required CapsuleStyleSetting Style { get; init; }
    public CapsuleSubject? Subject { get; set; }
    public ComposerCapsuleSlot? Slot { get; set; }
}

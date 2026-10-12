using Marrow.Core.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Xunit;

namespace Marrow.App.Tests.Views.Composer;

// VT-12, V-T132 and V-T133 (ComposerVoiceCleanPillTests holds the scenes, the detector and the judged render): the 224-identity matrix, one class per scale so each runs well
// under the gate's 500 s. Every identity renders once; its two tests assert on that render (ComposerVoiceCleanPillTests.Judge).
[Collection("Headless")]
public sealed class ComposerVoiceCleanPillAt150Tests(HeadlessSession headless)
{
    public static TheoryData<VoiceListeningStyle, int, bool, string, double> Matrix() => ComposerVoiceCleanPillTests.MatrixAt(1.5);

    [Theory] // V-T132
    [MemberData(nameof(Matrix))]
    [Trait("Decision", "ED-292")]
    public void Nothing_of_the_pills_material_shows_beside_the_orb(VoiceListeningStyle style, int diameter, bool dark, string state, double scale)
        => ComposerVoiceCleanPillTests.BesideTheOrb(headless, style, diameter, dark, state, scale);

    [Theory] // V-T133
    [MemberData(nameof(Matrix))]
    [Trait("Decision", "ED-292")]
    public void The_pill_and_a_margin_round_it_have_no_specks(VoiceListeningStyle style, int diameter, bool dark, string state, double scale)
        => ComposerVoiceCleanPillTests.NoSpecks(headless, style, diameter, dark, state, scale);
}

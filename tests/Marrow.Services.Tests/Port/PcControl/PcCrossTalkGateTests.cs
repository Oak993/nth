using System.Globalization;
using System.Text;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, the zero cross-talk gate (DESIGN.md 2.8 "Zero cross-talk", 2.9, 5.2 "Cross-talk (hard gate)"; P7;
// ED-NEW-pcctl-31). Across 1,000 seeded random interleavings of the user typing (in their own window, or in the Edit of the
// window Tactus types into) and Tactus typing into that Edit (a stock Win32 control, rung R2): no Tactus message reaches the
// user's window and no Tactus text lands in it, nothing is injected at all (real input is off), the Edit holds exactly the
// text Tactus typed, and no Tactus write lands in the Edit while it is the user's (focused and typed into within the last
// 2 s). The only refusal allowed is user_typing_here.
[Trait("Decision", "ED-NEW-pcctl-31")]
public sealed class PcCrossTalkGateTests
{
    private const int Runs = 1000;
    private const nint UserWindow = 0x1a;
    private const nint TargetWindow = 0x2c;

    private static string Word(Random random)
    {
        var text = new StringBuilder();
        for (var i = random.Next(1, 6); i > 0; i--) text.Append((char)('a' + random.Next(26)));
        return text.ToString();
    }

    [Fact]
    public async Task A_thousand_random_interleavings_of_user_and_Tactus_typing_never_cross()
    {
        var random = new Random(20261012);
        var typedCalls = 0;
        var waitedCalls = 0;
        for (var run = 0; run < Runs; run++)
        {
            var harness = new PcEngineHarness();
            var user = harness.Notepad(UserWindow);
            var target = harness.Classic(TargetWindow);
            var engine = harness.Engine();
            var messages = new List<(nint App, long At, string What)>();
            harness.Desktop.OnMessage = (app, what) => messages.Add((app.Handle, harness.Desktop.Clock.NowMs, what));
            var look = await PcEngineHarness.Call(engine, "pc_observe", "{\"window\":\"w:2c\"}");
            Assert.True(look.Ok, look.Render());
            var edit = target.Root.Children[0];
            var document = user.Root.Children[0];
            var expected = new StringBuilder();
            var lastUserKeyInEdit = long.MinValue / 2;
            for (var step = random.Next(2, 9); step > 0; step--)
            {
                switch (random.Next(3))
                {
                    case 0:
                        // the user types in their own window: it comes in front with its document focused
                        edit.States &= ~PcStates.Focused;
                        document.States |= PcStates.Focused;
                        harness.Desktop.FocusHandle = UserWindow;
                        harness.UserTypes(UserWindow, (ushort)('A' + random.Next(26)));
                        break;
                    case 1:
                        // the user types in the Edit Tactus types into: for the next 2 s it is the user's control
                        document.States &= ~PcStates.Focused;
                        edit.States |= PcStates.Focused;
                        harness.Desktop.FocusHandle = edit.NativeWindow;
                        harness.Desktop.Bump(TargetWindow);
                        harness.UserTypes(TargetWindow, (ushort)('A' + random.Next(26)));
                        lastUserKeyInEdit = harness.Desktop.Clock.NowMs;
                        break;
                    default:
                        var text = Word(random);
                        var before = messages.Count;
                        var start = harness.Desktop.Clock.NowMs;
                        var typed = await PcEngineHarness.Call(engine, "pc_type", "{\"window\":\"w:2c\",\"element\":2,\"text\":\"" + text + "\"}");
                        if (typed.Ok) expected.Append(text);
                        else Assert.True(typed.Error == PcErrorCode.UserTypingHere, "run " + run.ToString(CultureInfo.InvariantCulture) + ": " + typed.Render());
                        typedCalls++;
                        if (harness.Desktop.Clock.NowMs > start) waitedCalls++;
                        foreach (var (app, at, what) in messages.Skip(before))
                        {
                            if (app != TargetWindow || !edit.States.HasFlag(PcStates.Focused)) continue;
                            Assert.True(at - lastUserKeyInEdit >= PcControlEngine.UserActiveMs,
                                "run " + run.ToString(CultureInfo.InvariantCulture) + ": " + what + " landed " + (at - lastUserKeyInEdit).ToString(CultureInfo.InvariantCulture) + " ms after the user's key in the Edit");
                        }
                        break;
                }
            }
            Assert.Equal(expected.ToString(), edit.Value);
            Assert.Equal("", document.Value);
            Assert.DoesNotContain(messages, m => m.App == UserWindow);
            Assert.Empty(harness.RealHost.Sent);
        }
        // the interleavings did exercise both paths: writes at once and writes that waited for the user
        Assert.True(typedCalls > Runs);
        Assert.True(waitedCalls > 0);
    }
}

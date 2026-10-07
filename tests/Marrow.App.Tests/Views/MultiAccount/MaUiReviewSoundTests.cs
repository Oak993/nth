using System.Reflection;
using System.Reflection.Emit;
using Marrow.App.Port.MultiAccount.Handshake;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Handshake;
using Marrow.Testing;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.MultiAccount;

public sealed class MaUiReviewSoundTests
{
    [Fact, Trait("Feature", "MA-8"), Trait("Rule", "R-TEST-4")]
    public void MAUI_056_Shipped_App_consumes_the_locked_enabled_sound_recipe()
    {
        // OD-0121: sound "include it"; HTML 3053-3066 gives the complete sine recipe.
        // First pin the recipe. Then audit the BUILT App's method references: the existing
        // Core sound queue must have a consumer for the sound to ship. This is a static
        // wiring check, not an assertion that a Core model should itself play audio.
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude); var b = rig.ConnectAnother(ProviderKind.Claude);
        var accounts = new FakeThreadAccountPort(rig, ProviderKind.Claude); var cards = new FakeCardPort();
        var clock = new ManualTimeProvider(); var thread = Guid.NewGuid();
        using var motion = new HandshakeMotionModel(thread, cards, () => accounts.Accounts, clock) { SoundEnabled = true };
        cards.Raise(new(thread, HandshakeEventKind.PhaseChanged, HandshakePhase.Idle, HandshakePhase.Linking,
            a.Id.ToString(), b.Id.ToString(), HandshakeReason.NearLimit, clock.GetUtcNow()));
        Assert.Equal(new[] { new HandshakeTone(523.25, 1.1, .03, 0, .04, .0001, .05), new HandshakeTone(783.99, 1.3, .022, .08, .04, .0001, .05) }, motion.TakeSounds());
        cards.Raise(new(thread, HandshakeEventKind.InSyncReady, HandshakePhase.Linking, HandshakePhase.InSync,
            a.Id.ToString(), b.Id.ToString(), HandshakeReason.NearLimit, clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new[] { new HandshakeTone(1318.5, .18, .025, 0, .04, .0001, .05) }, motion.TakeSounds());
        var app = typeof(HandshakeCanvas).Assembly;
        Assert.Contains(app.GetTypes().SelectMany(Methods).SelectMany(MethodReferences),
            method => method.DeclaringType == typeof(HandshakeMotionModel) && method.Name == nameof(HandshakeMotionModel.TakeSounds));
    }

    private static IEnumerable<MethodBase> Methods(Type type) =>
        type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Cast<MethodBase>()
            .Concat(type.GetConstructors(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));

    private static IEnumerable<MethodBase> MethodReferences(MethodBase method)
    {
        var bytes = method.GetMethodBody()?.GetILAsByteArray();
        if (bytes is null) yield break;
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => unchecked((ushort)code.Value));
        for (var at = 0; at < bytes.Length;)
        {
            var value = (ushort)bytes[at++];
            if (value == 0xFE) value = (ushort)(0xFE00 | bytes[at++]);
            var operand = codes[value].OperandType;
            if (operand == OperandType.InlineMethod)
            {
                MethodBase? reference;
                try { reference = method.Module.ResolveMethod(BitConverter.ToInt32(bytes, at), method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null); }
                catch (ArgumentException) { reference = null; }
                if (reference is not null) yield return reference;
            }
            at += operand switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, at),
                _ => 4,
            };
        }
    }
}

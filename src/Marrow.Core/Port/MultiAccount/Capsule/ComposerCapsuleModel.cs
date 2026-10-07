// PORT-ONLY: multi-account step 3a (OD-0115 composer=A). What the composer's capsule is for a thread, read from the existing account and pill stores and the usage the service reported:
// nothing, the plain account capsule, or the pill capsule. A view-model only: it changes nothing in the engine.
using System.Globalization;
using Marrow.Core.Accounts;
using Marrow.Core.Accounts.Pills;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount.Settings;

namespace Marrow.Core.Port.MultiAccount.Capsule;

/// <summary>The thread the capsule is for: its service, the account it runs on, the pill relay it is in (if any), whether a Handshake runs and the account on stand-by for it.</summary>
public sealed record CapsuleSubject(ProviderKind Service, Guid ActiveAccountId, PillThreadState? Relay, bool Handshake, Guid? StandbyAccountId);

/// <summary>What the capsule shows. <see cref="Pill"/> is null for the plain account capsule. <see cref="Use"/> is the used share of the window it shows (0 when the service reported none).</summary>
public sealed record CapsuleContent(string Mono, string AccountName, double Use, bool Hot, string Aria, CapsuleView? Pill);

public sealed class ComposerCapsuleModel
{
    // The mark's tint per service and position, taken from the design page's own accounts (Claude, Codex, OpenRouter, Local); a service the page does not show takes its neutral grey. Never the usage light.
    private static readonly Dictionary<ProviderKind, uint[]> Hues = new()
    {
        [ProviderKind.Claude] = [0xD97757, 0x8E7CFF, 0x2BB3A3],
        [ProviderKind.Codex] = [0x10A37F, 0x5B8DEF],
        [ProviderKind.OpenRouter] = [0x4D6BFE, 0xE0A030],
        [ProviderKind.Local] = [0x8A94A6, 0xB08D57],
    };
    private static readonly uint[] Neutral = [0x8A94A6];

    private readonly AccountStore accounts;
    private readonly PillStore pills;
    private readonly Func<Account, bool> connected;
    private readonly Func<Guid, AccountUsageLine?> usage;

    public ComposerCapsuleModel(AccountStore accounts, PillStore pills, Func<Account, bool> connected, Func<Guid, AccountUsageLine?> usage)
    {
        this.accounts = accounts;
        this.pills = pills;
        this.connected = connected;
        this.usage = usage;
    }

    /// <summary>Null when the capsule is not shown: one account of the service and no pill relay.</summary>
    public CapsuleContent? For(CapsuleSubject subject)
    {
        if (subject.Relay is { } relay && PillRelayResolver.Resolve(pills.Pills, relay) is { Pill: { } pill, StepIndex: { } step } && pill.Steps.Count > 0)
            return PillContent(subject, pill, step);
        var usable = accounts.ForService(subject.Service).Count(a => a.Enabled && connected(a));
        if (usable < 2) return null;
        return PlainContent(subject);
    }

    private CapsuleContent PlainContent(CapsuleSubject subject)
    {
        var account = accounts.Get(subject.ActiveAccountId);
        var name = NameOf(account);
        var (use, window) = Window(account.Id);
        var aria = "Account: " + name + (use is { } shown ? ", " + Percent(shown) + " of its " + window + " limit used" : "") + ". Change account";
        return new CapsuleContent(MonoOf(account), name, use ?? 0, CapsuleLayout.IsHot(use ?? 0), aria, null);
    }

    private CapsuleContent PillContent(CapsuleSubject subject, Pill pill, int step)
    {
        var steps = new List<CapsuleStep>();
        var accountsOfSteps = new List<Account?>();
        foreach (var s in pill.Steps)
        {
            var account = Guid.TryParse(s.AccountId, out var id) ? accounts.Accounts.FirstOrDefault(a => a.Id == id) : null;
            accountsOfSteps.Add(account);
            steps.Add(new CapsuleStep(account is null ? "?" : MonoOf(account), account is null ? Neutral[0] : HueOf(account), account?.Service == ProviderKind.Local));
        }
        var cur = Math.Max(0, Math.Min(pill.Steps.Count - 1, step));
        int? stand = subject.StandbyAccountId is { } standby && accountsOfSteps.FindIndex(a => a?.Id == standby) is >= 0 and var at ? at : null;
        var currentAccount = accountsOfSteps[cur];
        var use = currentAccount is null ? null : Window(currentAccount.Id).Use;
        var next = stand ?? (cur + 1 < steps.Count ? cur + 1 : (int?)null);
        double? nextUse = next is { } n && accountsOfSteps[n] is { } nextAccount ? Window(nextAccount.Id).Use : null;
        var view = new CapsuleView(steps, cur, null, stand, subject.Handshake, use, nextUse, pill.Name);
        var current = currentAccount is null ? "" : NameOf(currentAccount);
        var aria = "Pill " + pill.Name + ", step " + (cur + 1) + " of " + steps.Count + ": " + current + ". Open accounts and pills";
        return new CapsuleContent(steps[cur].Mono, current, use ?? 0, false, aria, view);
    }

    /// <summary>The used share the capsule shows: the 5-hour window, or the week on a plan that has no 5-hour one. Null when the service reported nothing.</summary>
    private (double? Use, string Window) Window(Guid id)
    {
        if (usage(id) is not { } line) return (null, "5-hour");
        return line.NoFiveHour ? (line.Week, "weekly") : (line.FiveHour, "5-hour");
    }

    private string NameOf(Account account) => accounts.DisplayName(account.Id) ?? accounts.DefaultName(account.Id);

    private string MonoOf(Account account) => AccountMono.Of(NameOf(account), account.CustomName is not null);

    private uint HueOf(Account account)
    {
        var position = accounts.ForService(account.Service).ToList().FindIndex(a => a.Id == account.Id);
        return MarkHue(account.Service, position);
    }

    /// <summary>The shared mark hue of an account in its service's zero-based order.</summary>
    public static uint MarkHue(ProviderKind service, int position)
    {
        var palette = Hues.GetValueOrDefault(service, Neutral);
        return palette[Math.Max(0, position) % palette.Length];
    }

    private static string Percent(double use) => Math.Round(Math.Min(1, use) * 100, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%";
}

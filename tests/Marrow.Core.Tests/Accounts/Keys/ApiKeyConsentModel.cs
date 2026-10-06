using Marrow.Core.Accounts.Keys;

namespace Marrow.Core.Tests.Accounts.Keys;

/// <summary>
/// One seeded random life of a thread on a service's keys, judged by an independent model of OD-0116: a thread stays on the key it is on
/// unless it is pinned to another by the user or moving was agreed (remembered, or once). A key turned off or without a secret is a forced move like out and cap.
/// </summary>
public sealed class ApiKeyConsentModel(int seed)
{
    private static readonly string[] Ids = ["a", "b", "c"];
    private readonly Random random = new(seed);
    private readonly ApiKeyRig rig = new();
    private readonly List<string> script = [];
    private string? anchor;
    private string? pin;
    private bool once;

    public List<string> Violations { get; } = [];

    public int Moves { get; private set; }

    public int Refusals { get; private set; }

    public string Describe => "seed " + seed + ": " + string.Join("; ", script);

    private ApiKeyEntry Entry(string id) => rig.Store.Keys(ApiKeyRig.Service).First(key => key.Id == id);

    public void Run()
    {
        rig.Moves.Default = new[] { KeyMoveSetting.Unset, KeyMoveSetting.Unset, KeyMoveSetting.Allow, KeyMoveSetting.Deny }[random.Next(4)];
        rig.Store.SetOrder(ApiKeyRig.Service, (KeyOrderMode)random.Next(3));
        foreach (var id in Ids) rig.Credits.Set(id, random.Next(3) == 0 ? null : random.Next(0, 20));
        var initial = random.Next(3) == 0 ? null : Ids[random.Next(3)];
        script.Add("default=" + rig.Moves.Default + " initial=" + initial);
        anchor = initial;
        var rotation = rig.Pool.ForThread(ApiKeyRig.Thread, initial);
        var continuation = (IApiKeyContinuation)rotation;
        var rejected = false;

        for (var step = 0; step < 30; step++)
        {
            var action = random.Next(16);
            script.Add("#" + action);
            switch (action)
            {
                case 0 or 1 or 2 or 3:
                    Judge("Current", rotation.Current(), false);
                    once = false;
                    rejected = false;
                    break;
                case 4 or 5:
                    if (anchor is null) break;
                    var status = random.Next(2) == 0 ? 402 : 429;
                    var before = Pending();
                    var failed = rotation.OnFailure(status, null);
                    rejected = failed is null;
                    Judge("OnFailure", failed, true, before);
                    if (failed is not null) once = false;
                    break;
                case 6: rotation.AllowMoveOnce(); once = true; break;
                case 7:
                    if (anchor is null || !rejected) break;
                    var proposed = continuation.ContinueRejectedRequest();
                    Judge("ContinueRejectedRequest", proposed, true);
                    if (proposed is not null) { once = false; rejected = false; }
                    break;
                case 8: rig.Moves.Default = (KeyMoveSetting)random.Next(3); script.Add("default=" + rig.Moves.Default); break;
                case 9: rig.Moves.Set(ApiKeyRig.Thread, (KeyMoveSetting)random.Next(3)); break;
                case 10:
                    var id = Ids[random.Next(3)];
                    rig.Store.Update(id, key => key with { Allowed = !key.Allowed });
                    break;
                case 11: rig.Credits.Set(Ids[random.Next(3)], random.Next(3) == 0 ? null : random.Next(0, 20)); break;
                case 12:
                    rig.Store.SetOrder(ApiKeyRig.Service, (KeyOrderMode)random.Next(3));
                    break;
                case 13:
                    pin = random.Next(3) == 0 ? null : Ids[random.Next(3)];
                    rig.Pool.Pin(ApiKeyRig.Thread, pin);
                    break;
                case 14: rig.Clock.Advance(TimeSpan.FromMinutes(random.Next(1, 90))); break;
                case 15:
                    var capped = Ids[random.Next(3)];
                    rig.Store.Update(capped, key => key with { MonthlyCapUsd = random.Next(2) == 0 ? null : random.Next(1, 5) });
                    if (anchor is not null) rotation.RecordSpend(random.Next(0, 4));
                    break;
            }
        }
    }

    private (KeyMoveSetting Setting, bool Once, string? Pin) Pending() =>
        (rig.Moves.Setting(ApiKeyRig.Thread), once, pin);

    private void Judge(string call, KeyChoice? chosen, bool failure, (KeyMoveSetting Setting, bool Once, string? Pin)? before = null)
    {
        var state = before ?? Pending();
        if (chosen is null) { Refusals++; return; }
        script.Add(call + "->" + chosen.Id);
        string? why = null;
        if (state.Pin is { } held && chosen.Id != held) why = "the thread is pinned to " + held;
        else if (anchor is null) why = null;
        else if (chosen.Id != anchor && state.Pin != chosen.Id && state.Setting != KeyMoveSetting.Allow && !state.Once)
            why = "moved from " + anchor + " without agreement (setting " + state.Setting + ")";
        if (why is not null) Violations.Add(call + " " + why);
        if (anchor is not null && chosen.Id != anchor) Moves++;
        anchor = chosen.Id;
    }
}

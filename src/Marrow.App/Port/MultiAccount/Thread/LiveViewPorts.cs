using Marrow.App.Port.MultiAccount.Opinions;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Link;
using Marrow.Core.Port.MultiAccount.Opinions;
using Marrow.Core.Port.MultiAccount.Thread;

namespace Marrow.App.Port.MultiAccount.Thread;

internal sealed partial class LiveThreadPorts
{
    private sealed record OpinionEntries(string Prompt, IReadOnlyList<ThreadAccount> Accounts, string? Combined = null, string? Byline = null);
    private readonly Dictionary<(Guid Thread, Guid Round), OpinionEntries> opinionEntries = [];
    private readonly Dictionary<Guid, List<WeakReference<IDisposable>>> views = [];
    private OtherServiceConsentStore? viewConsent;
    private OtherServiceConsentStore ViewConsent => viewConsent ??= app.Settings.ViewConversationConsent();

    private IThreadAccountPort[] AllPorts(Guid thread) => SwiftEnum<ProviderKind>.AllCases
        .Select(service => Account(thread, service)).OfType<IThreadAccountPort>().ToArray();

    private T OwnView<T>(Guid thread, T model) where T : class, IDisposable
    {
        // Only attached views observe. Weak ownership lets discarded composers be collected, while retirement
        // and app shutdown can still suspend every surviving model, including a composer in another window.
        model.Dispose();
        if (!views.TryGetValue(thread, out var models)) views[thread] = models = [];
        models.RemoveAll(reference => !reference.TryGetTarget(out _));
        models.Add(new(model));
        return model;
    }
    private void DisposeViews(Guid thread)
    {
        if (!views.Remove(thread, out var models)) return;
        foreach (var reference in models) if (reference.TryGetTarget(out var model)) model.Dispose();
    }
    internal LinkUpModel? LinkModel(Guid thread)
    {
        MainActor.AssumeIsolated();
        if (disposed || app.Thread(thread) is not { } record) return null;
        var ports = AllPorts(thread);
        return OwnView(thread, new LinkUpModel(thread, ports.Single(port => port.Service == record.Provider), ports, Links, ViewConsent));
    }
    internal AskOthersModel? AskOthers(Guid thread)
    {
        MainActor.AssumeIsolated();
        if (disposed || app.Thread(thread) is not { } record) return null;
        var ports = AllPorts(thread);
        return OwnView(thread, new AskOthersModel(thread, ports.Single(port => port.Service == record.Provider), ports,
            new LiveOpinionSendPort(Opinions, SendOpinionAsync), ViewConsent, app.Providers.Models,
            account => !account.IsOut && app.CanUseAccount(account.Id)));
    }
    internal OpinionTimelineContext? Timeline(Guid thread)
    {
        MainActor.AssumeIsolated();
        if (disposed || app.Thread(thread) is null) return null;
        return new(round => opinionEntries.GetValueOrDefault((thread, round))?.Prompt,
            round => opinionEntries.GetValueOrDefault((thread, round))?.Combined,
            (round, account) => Opinions.Rounds(thread).FirstOrDefault(value => value.Id == round)?.Answers.FirstOrDefault(answer => answer.AccountId == account)?.Error,
            round => opinionEntries.GetValueOrDefault((thread, round))?.Byline,
            round => opinionEntries.GetValueOrDefault((thread, round))?.Accounts);
    }
    private async Task<Guid> SendOpinionAsync(Guid thread, string message, IReadOnlyList<Guid> selected, CancellationToken ct)
    {
        MainActor.AssumeIsolated(); ObjectDisposedException.ThrowIf(disposed, this);
        var runtime = app.Runtime(thread);
        await runtime.EnsureLoaded(); ct.ThrowIfCancellationRequested();
        if (disposed || retired.Contains(thread) || app.Thread(thread) is null) throw new OperationCanceledException();
        var source = Active(thread);
        var asked = selected.Append(source).ToHashSet();
        // Immutable account records preserve historical names, monograms and defaults after rename/removal.
        // The answering head uses the composer's model; other heads use their own account defaults.
        var snapshot = AllPorts(thread).SelectMany(port => port.Accounts).DistinctBy(account => account.Id)
            .Where(account => asked.Contains(account.Id)).Select(account => account.Id == source
                ? account with { Defaults = account.Defaults with { Model = runtime.Thread?.Model, Effort = runtime.Thread?.Effort, FastMode = runtime.Thread?.FastMode ?? false } } : account).ToArray();
        var entry = runtime.AppendOpinionPrompt(message, ct);
        var started = false;
        try
        {
            var round = await Opinions.AskAsync(thread, message, selected, id =>
            {
                opinionEntries[(thread, id)] = new(entry, Array.AsReadOnly(snapshot));
                started = true;
            }, ct);
            runtime.OpinionAssociationsChanged();
            return round;
        }
        catch
        {
            if (!started) runtime.RemoveOpinionPrompt(entry);
            throw;
        }
    }
}

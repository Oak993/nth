// PORT-ONLY: T11 step 2 (OD-0114..OD-0117). Where the accounts of Settings > Providers (OD-0149) and the Pills page get their models. A test overrides it; the app's default builds them once per AppModel over the foundations (AccountStore, AccountDecisionStore, PillStore).
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Settings;

namespace Marrow.App.Port.MultiAccount.Settings;

/// <summary>Everything the pages need, built over the real stores or over test doubles.</summary>
internal sealed class MultiAccountContext(
    AccountsSettingsModel accounts,
    PillsSettingsModel pills,
    Func<Guid, PillEditorModel> editor,
    Func<ProviderKind, IReadOnlyList<ModelOption>> models,
    Action openProviders)
{
    public AccountsSettingsModel Accounts { get; } = accounts;
    public PillsSettingsModel Pills { get; } = pills;
    public Func<Guid, PillEditorModel> Editor { get; } = editor;

    /// <summary>The models a service offers (for a pill step's model and effort).</summary>
    public Func<ProviderKind, IReadOnlyList<ModelOption>> Models { get; } = models;

    /// <summary>The Pills page's button: Settings > Providers, where accounts are connected (OD-0149). The "Usage details" and "Keys and order" jumps of the Models page went with it: usage and keys are in the
    /// service's own card now.</summary>
    public Action OpenProviders { get; } = openProviders;
}

/// <summary>Resolves the context for a control in the tree; a holder so a test can say "none".</summary>
internal sealed record MultiAccountOffer(Func<Control, MultiAccountContext?> Resolve);

internal static class MultiAccountSettingsSeams
{
    private static readonly ConditionalWeakTable<AppModel, MultiAccountContext> Built = new();

    private static MultiAccountContext? Default(Control control)
    {
        AppModel model;
        try { model = AppEnvironment.GetAppModel(control); }
        catch (InvalidOperationException) { return null; }
        return Built.GetValue(model, MultiAccountContexts.For);
    }

    /// <summary>The context of the pages. Unbound (no context: the sections stay empty) until <see cref="Bind"/>, so a test of another page never builds the app's accounts; a test of these pages overrides it.</summary>
    public static Ambient<MultiAccountOffer> Context { get; } = new(new MultiAccountOffer(_ => null));

    /// <summary>Program.Main calls this once at start-up: the pages then read the running AppModel (the pattern of SkillsStartup.Bind).</summary>
    public static void Bind() => Context.SetProcessDefault(new MultiAccountOffer(Default));

    public static MultiAccountContext? For(Control control) => Context.Current.Resolve(control);
}

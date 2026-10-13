// PORT-ONLY: web surfaces W13 (DESIGN.md 6.5 "the only way out is the bridge"; ED-NEW-web-w13-4). The seams the bridge
// reaches nth through: the pane's consent cards, nth's own file sheet and the composer (IArtifactBridgeHost, drawn by
// Tabula's pane), the chat's model (IArtifactModelClient) and the MCP tools with their normal approvals and Tactus guards
// (IArtifactToolClient). Until a seam is bound it refuses (no consent is ever assumed). No Swift counterpart.
using System.Text.Json;

namespace Marrow.Services.Web;

/// <summary>An artifact of a thread, as the bridge's seams see it.</summary>
public sealed record ArtifactAddress(Guid Thread, string Id);

/// <summary>Settings › Tabula › "Artifacts may use your model".</summary>
public enum ArtifactModelPolicy
{
    /// <summary>Ask each artifact (the default): the first call shows the consent card.</summary>
    Ask,
    Never,
}

/// <summary>One message of an artifact's own conversation (nth keeps nothing between calls).</summary>
public sealed record ArtifactModelMessage(string Role, string Content);

/// <summary>
/// DESIGN 6.5: what a call to the chat's model carries, and all it carries: the artifact's own system text after nth's
/// untrusted-app line, the artifact's own messages and the output cap. No thread history, no project files, no memory,
/// no tools.
/// </summary>
public sealed record ArtifactCompletionRequest(string System, IReadOnlyList<ArtifactModelMessage> Messages, int MaxTokens);

/// <summary>The reply text and the tokens it used (the input and the output), for the limits and the cost meter.</summary>
public sealed record ArtifactCompletionResult(string Text, int InputTokens, int OutputTokens)
{
    public int TotalTokens => Math.Max(0, InputTokens) + Math.Max(0, OutputTokens);
}

/// <summary>The chat's model, as artifacts use it: the same provider and model as the thread, in a fresh context.</summary>
public interface IArtifactModelClient
{
    /// <summary>The model's display name for the consent card ("Claude Sonnet"), or null when the thread has no model to use.</summary>
    string? ModelName(Guid thread);

    /// <summary>Runs one context-free call; <paramref name="onDelta"/> receives the text as it streams.</summary>
    Task<ArtifactCompletionResult> CompleteAsync(Guid thread, ArtifactCompletionRequest request, Action<string>? onDelta, CancellationToken cancellationToken);
}

/// <summary>An MCP tool's result as structured content (the tool's own JSON result object).</summary>
public sealed record ArtifactToolCallResult(JsonElement Content);

/// <summary>The thread's MCP tools, called through the normal tool approvals and Tactus guards.</summary>
public interface IArtifactToolClient
{
    Task<ArtifactToolCallResult> CallAsync(Guid thread, string server, string tool, JsonElement arguments, CancellationToken cancellationToken);
}

/// <summary>A file the user chose in nth's "Choose a file" sheet: its name (never a path), type and bytes.</summary>
public sealed record ArtifactPickedFile(string Name, string Type, byte[] Bytes);

/// <summary>nth's glass tokens as CSS variables (each name starts "--nth-") and the light or dark appearance.</summary>
public sealed record ArtifactTheme(bool Dark, IReadOnlyDictionary<string, string> Variables)
{
    public static ArtifactTheme Default { get; } = new(false, new Dictionary<string, string>());
}

/// <summary>What the pane draws for the bridge: the consent cards, nth's file sheet, the composer draft and the theme.</summary>
public interface IArtifactBridgeHost
{
    /// <summary>
    /// The first call's consent card: "This artifact wants to use &lt;model&gt;. It cannot see this chat, your files or your
    /// other artifacts. Each use counts toward your plan." True for Allow for this artifact, false for Not now.
    /// </summary>
    Task<bool> ConfirmModelAsync(ArtifactAddress artifact, string modelName, CancellationToken cancellationToken);

    /// <summary>The first call per tool: "This artifact wants to use &lt;server&gt; · &lt;tool&gt;". True for Allow for this artifact.</summary>
    Task<bool> ConfirmToolAsync(ArtifactAddress artifact, string server, string tool, CancellationToken cancellationToken);

    /// <summary>nth's own "Choose a file" sheet over the pane; empty when the user cancels.</summary>
    Task<IReadOnlyList<ArtifactPickedFile>> PickFilesAsync(ArtifactAddress artifact, string accept, bool multiple, bool fromDrop, CancellationToken cancellationToken);

    /// <summary>
    /// ED-NEW-secfinal-40 (final security gate, N4): nth's own card over the pane, for each <c>nth.send</c>: "This artifact wants
    /// to add to your message", with how the text starts. True only when the user presses its button; the page cannot answer it.
    /// </summary>
    Task<bool> ConfirmDraftAsync(ArtifactAddress artifact, string text, CancellationToken cancellationToken);

    /// <summary>Puts <paramref name="text"/> in the main composer as a draft; the user sends.</summary>
    void Draft(ArtifactAddress artifact, string text);

    ArtifactTheme Theme { get; }

    event Action? ThemeChanged;
}

/// <summary>The host before Tabula's pane binds one: every consent is Not now, the sheet picks nothing, no draft is made.</summary>
public sealed class UnboundArtifactBridgeHost : IArtifactBridgeHost
{
    public static UnboundArtifactBridgeHost Instance { get; } = new();

    public Task<bool> ConfirmModelAsync(ArtifactAddress artifact, string modelName, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<bool> ConfirmToolAsync(ArtifactAddress artifact, string server, string tool, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<IReadOnlyList<ArtifactPickedFile>> PickFilesAsync(ArtifactAddress artifact, string accept, bool multiple, bool fromDrop, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ArtifactPickedFile>>([]);

    public Task<bool> ConfirmDraftAsync(ArtifactAddress artifact, string text, CancellationToken cancellationToken) => Task.FromResult(false);

    public void Draft(ArtifactAddress artifact, string text) { }

    public ArtifactTheme Theme => ArtifactTheme.Default;

    public event Action? ThemeChanged { add { } remove { } }
}

/// <summary>A bridge call that fails with a code the page can test ("rate_limited", "denied" ...).</summary>
public sealed class ArtifactBridgeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>The codes a bridge call rejects with.</summary>
public static class ArtifactBridgeErrors
{
    public const string RateLimited = "rate_limited";
    public const string Denied = "denied";
    public const string Unavailable = "unavailable";
    public const string InvalidRequest = "invalid_request";
    public const string QuotaExceeded = "quota_exceeded";
    public const string TooLarge = "too_large";
    public const string NotFound = "not_found";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
}

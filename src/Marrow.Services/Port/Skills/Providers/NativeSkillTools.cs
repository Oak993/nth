// PORT-ONLY: SK-11f / WU-12, DL-14 (skills-PLAN.md 3.3 native row; skills-ONDEMAND-PLAN.md 4.1, 5.1 S4, 5.5 R4; OD-0099 progressive-disclosure amendment). Marrow's own tools for the
// API providers, which run no CLI and so have no skills folder to point at: a catalogue block for the system prompt (names and descriptions of the installed, enabled skills,
// never a body), use_skill (the full text on invocation, files a skill's instructions mention on request), and request_tool (ask the user to download a missing tool).
// The block and use_skill list what the session's generation holds (SK-9) plus, behind its switch, the user's own skills (Q17). Nothing here downloads anything.
using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Generations;

namespace Marrow.Services.Port.Skills.Providers;

public sealed class NativeSkillTools
{
    public const string UseSkillName = "use_skill";
    public const string RequestToolName = "request_tool";
    private const string Header = "Skills (call use_skill with a skill's name to read its instructions):";
    private const string BadPath = "Error: the file path must be a relative path inside the skill's folder.";

    private readonly Generation? generation;
    private readonly NativeUserSkills? ownSkills;
    private readonly IToolRequestOffer? offer;
    private readonly Marrow.Services.Port.Advisor.AdvisorRegistration? advisor;
    private readonly Marrow.Services.Web.ArtifactNativeTools? artifacts;
    private readonly Marrow.Services.Web.WebNativeTools? web;

    /// <param name="generation">The session's generation (SK-9), which the caller keeps alive for the session; null when nothing is installed.</param>
    /// <param name="ownSkills">The user's own skills; consulted only while its switch is on.</param>
    /// <param name="offer">The offer flow request_tool asks; the tool is offered to the model only while it says something is installed.</param>
    /// <param name="advisor">PORT-ONLY (Ignis Advisor, ED-NEW-ignisadvisor-2): a Hydra head's advisor registration; ask_advisor is offered only with one.</param>
    /// <param name="artifacts">PORT-ONLY (web surfaces W12, ED-NEW-web-w12-1): the chat's artifact tools (Tabula); offered only with them.</param>
    /// <param name="web">PORT-ONLY (web surfaces W11, ED-NEW-web-w11-1): the chat's Visus tools (web_*); offered while Visus model access is on.</param>
    public NativeSkillTools(Generation? generation, NativeUserSkills? ownSkills = null, IToolRequestOffer? offer = null, Marrow.Services.Port.Advisor.AdvisorRegistration? advisor = null,
        Marrow.Services.Web.ArtifactNativeTools? artifacts = null, Marrow.Services.Web.WebNativeTools? web = null)
    {
        this.generation = generation;
        this.ownSkills = ownSkills;
        this.offer = offer;
        this.advisor = advisor;
        this.artifacts = artifacts;
        this.web = web;
    }

    private bool UseSkillOffered => Listing().Count > 0;
    private bool RequestToolOffered => offer is { Available: true };

    /// <summary>The skills the model is told about: the user's own first, then Marrow's that the user's do not shadow. Explicit-only skills are never listed.</summary>
    private List<(string Name, string Description)> Listing()
    {
        var result = new List<(string, string)>();
        var own = ownSkills?.List() ?? [];
        result.AddRange(own.Select(s => (s.Name, s.Description)));
        var shadowed = own.Select(s => GenerationFilters.Normalize(s.Name)).ToHashSet(StringComparer.Ordinal);
        if (generation is not null)
            result.AddRange(generation.Skills.Where(s => !s.ExplicitOnly && !shadowed.Contains(GenerationFilters.Normalize(s.Name))).Select(s => (s.Name, s.Description)));
        return result;
    }

    /// <summary>The block for the system prompt, or null when there is no skill to list.</summary>
    public string? CatalogueBlock()
    {
        var listing = Listing();
        return listing.Count == 0 ? null : Header + "\n" + string.Join("\n", listing.Select(s => s.Name + ": " + s.Description));
    }

    /// <summary>
    /// PORT-ONLY (web surfaces W17, ED-NEW-web-w17-1): the teaching block for the system prompt, or null when the chat has no artifact
    /// tools (so a prompt without them stays exactly as it was). <paramref name="profile"/> is Lite for a small local model, and
    /// <paramref name="toolsUsable"/> false when it cannot call tools.
    /// </summary>
    public string? UiTeachingBlock(Marrow.Core.NthUi.NthUiProfile profile = Marrow.Core.NthUi.NthUiProfile.Full, bool toolsUsable = true) =>
        artifacts?.TeachingBlock(profile, toolsUsable);

    /// <summary>PORT-ONLY (web surfaces W11): the Visus tools' instructions for the system prompt, or null while they are not offered.</summary>
    public string? WebBlock() => web?.Instructions;

    /// <summary>PORT-ONLY (web surfaces W11): the chat's Visus tools, for a session that sends their screenshots as pictures.</summary>
    public Marrow.Services.Web.WebNativeTools? Web => web;

    /// <summary>PORT-ONLY (web surfaces W17): whether this chat's model is taught live views, which is when it has the artifact tools.</summary>
    public bool TeachesLiveViews => artifacts is not null;

    /// <summary>PORT-ONLY (web surfaces W25): the names of the nth-ui tools offered now (artifact tools, the reference, ask_user); empty without them.</summary>
    public IReadOnlyList<string> OfferedUiTools => artifacts?.Offered ?? [];

    /// <summary>The function-tool definitions to add to the request: only the tools that are offered at the moment.</summary>
    public IReadOnlyList<JSONValue> Definitions
    {
        get
        {
            var definitions = new List<JSONValue>();
            if (UseSkillOffered) definitions.Add(NativeSkillToolDefinitions.UseSkill());
            if (RequestToolOffered) definitions.Add(NativeSkillToolDefinitions.RequestTool());
            if (advisor is not null) definitions.Add(Marrow.Services.Port.Advisor.AdvisorTool.FunctionDefinition());
            if (artifacts is not null) definitions.AddRange(artifacts.Definitions);
            if (web is not null) definitions.AddRange(web.Definitions);
            return definitions;
        }
    }

    /// <summary>True for a tool name Marrow handles and is offering now. Anything else stays the unknown tool it always was.</summary>
    public bool Owns(string toolName) => toolName switch
    {
        UseSkillName => UseSkillOffered,
        RequestToolName => RequestToolOffered,
        Marrow.Services.Port.Advisor.AdvisorTool.Name => advisor is not null,
        _ => (artifacts is not null && artifacts.Owns(toolName)) || (web is not null && web.Owns(toolName)),
    };

    /// <summary>
    /// Runs one of the tools this object owns. A refusal or an error is a result with <see cref="NativeToolResult.Failed"/> set, not an exception; a wait on the user is cancelled by
    /// the turn's own cancellation and then throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public Task<NativeToolResult> Execute(string toolName, JSONValue arguments)
    {
        // The session asks Owns first (is the tool offered now). A request_tool already in flight when the last item is removed still reaches its offer, which answers it.
        if (toolName == UseSkillName && UseSkillOffered) return Task.FromResult(UseSkill(arguments));
        if (toolName == RequestToolName && offer is not null) return RequestTool(arguments);
        if (toolName == Marrow.Services.Port.Advisor.AdvisorTool.Name && advisor is not null) return AskAdvisor(advisor, arguments);
        if (artifacts is not null && artifacts.Owns(toolName)) return artifacts.Execute(toolName, arguments);
        if (web is not null && web.Owns(toolName)) return web.Execute(toolName, arguments);
        throw new InvalidOperationException($"{toolName} is not a tool this object runs.");
    }

    /// <summary>PORT-ONLY (web surfaces W25): true for a tool whose card replaces its row, so the loop emits no row events for it.</summary>
    public static bool HidesRow(string toolName) => Marrow.Services.Web.ArtifactNativeTools.HidesRow(toolName);

    /// <summary>The timeline row's title for a call, known before it runs (U+00B7 MIDDLE DOT, as the MCP rows use).</summary>
    public static string Title(string toolName, JSONValue arguments) =>
        toolName == UseSkillName ? "Skill" + (arguments["name"]?.JSONValueString is { Length: > 0 } name ? " \u00B7 " + name : "")
        : toolName == Marrow.Services.Port.Advisor.AdvisorTool.Name ? Marrow.Services.Port.Advisor.AdvisorTool.Title
        : Marrow.Services.Web.ArtifactNativeTools.Names.Contains(toolName) ? Marrow.Services.Web.ArtifactNativeTools.Title(toolName, arguments)
        : Marrow.Core.Web.WebToolSchemas.IsWebTool(toolName) ? Marrow.Services.Web.WebNativeTools.Title(toolName) : toolName;

    // PORT-ONLY (Ignis Advisor, ED-NEW-ignisadvisor-2): one piece of advice from the head's lead; an error is a failed row, never a throw.
    private static async Task<NativeToolResult> AskAdvisor(Marrow.Services.Port.Advisor.AdvisorRegistration advisor, JSONValue arguments)
    {
        var reply = await Marrow.Services.Port.Advisor.AdvisorHost.Ask(advisor.Thread, advisor.Choice,
            arguments["question"]?.JSONValueString, arguments["context"]?.JSONValueString, CancellationToken.None).ConfigureAwait(true);
        return new NativeToolResult(Marrow.Services.Port.Advisor.AdvisorTool.Title, reply.Text, reply.Failed, []);
    }

    private NativeToolResult UseSkill(JSONValue arguments)
    {
        var name = arguments["name"]?.JSONValueString ?? "";
        var title = Title(UseSkillName, arguments);
        if (name.Length == 0) return Failure(title, "Error: a skill name is required.");
        var file = arguments["file"]?.JSONValueString;
        var instructions = string.IsNullOrEmpty(file) || file == "SKILL.md";
        var relative = instructions ? "SKILL.md" : file!;
        try
        {
            string? folder, itemId = null, text;
            if (ownSkills?.List().FirstOrDefault(s => s.Name == name) is { } own)
            {
                folder = own.Folder;
                text = ownSkills.Read(name, relative);
            }
            else if (generation?.Skills.FirstOrDefault(s => s.Name == name && !s.ExplicitOnly) is { } skill)
            {
                folder = skill.Folder;
                itemId = skill.Id[..skill.Id.IndexOf('/', StringComparison.Ordinal)];
                text = generation.ReadReference(skill.Name, relative);
            }
            else return Failure(title, $"Error: unknown skill {name}.");
            if (text is null) return Failure(title, $"Error: unknown skill {name}.");
            return instructions
                ? new NativeToolResult(title, $"Skill folder: {folder}\n\n{text}", false, [new SkillUsed(name, itemId)])
                : new NativeToolResult(title, text, false, []);
        }
        catch (ArgumentException error) when (error is not DecoderFallbackException) { return Failure(title, BadPath); }
        catch (Exception error) when (error is InvalidOperationException or IOException or DecoderFallbackException or InvalidDataException)
        {
            return Failure(title, "Error: " + error.Message);
        }
    }

    private async Task<NativeToolResult> RequestTool(JSONValue arguments)
    {
        var name = arguments["name"]?.JSONValueString ?? "";
        var reason = arguments["reason"]?.JSONValueString ?? "";
        if (name.Trim().Length == 0) return Failure(RequestToolName, "Error: a tool name is required.");
        if (!offer!.InCatalogue(name)) return Failure(RequestToolName, $"Error: {name} is not in the catalogue.");
        var outcome = await offer.Ask(name, reason, SwiftTask.CancellationToken);
        return new NativeToolResult(RequestToolName, outcome.InstalledPath is { } path ? $"installed at {path}" : "declined", false, [new ToolRequested(name, reason)]);
    }

    private static NativeToolResult Failure(string title, string output) => new(title, output, true, []);
}

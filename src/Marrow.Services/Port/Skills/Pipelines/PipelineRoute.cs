using System.Text.Json.Serialization;
using Marrow.Services.Port.Skills.Shims;
namespace Marrow.Services.Port.Skills.Pipelines;
// PORT-ONLY: SK-8. Immutable version routing prepared before state.json selects its pin.
public sealed record PipelineRoute(string Id, string Pin, string Folder, string[] SparePaths, Dictionary<string, ShimDefinition> Shims)
{
    /// <summary>
    /// ED-NEW-dl-7: shims this item publishes that run a shim of another installed item (pip runs Python's interpreter with <c>-m pip</c>). Resolved at each run against the pin
    /// state.json selects for that item, so an update of the interpreter moves pip with it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, PipelineShimLink>? Links { get; init; }

    /// <summary>ED-NEW-dl-7: a Python package set; its folder is on the Python path of every Python shim run (ONDEMAND 3.4 "listed in the Python ._pth at launch time").</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PythonPackages { get; init; }

    /// <summary>ED-NEW-dl-7: environment every shimmed child gets while this pin is selected (Playwright's browsers folder, Node's package tree).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Exports { get; init; }
}

/// <summary>A shim that runs <see cref="Shim"/> of the installed item <see cref="Item"/> with <see cref="Arguments"/> first.</summary>
public sealed record PipelineShimLink(string Item, string Shim, string[] Arguments);

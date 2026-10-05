// PORT-ONLY: SK-13 / ED-326 (6)(a), ED-325 "Restart trust". The frozen installed-pin descriptors a restarted pipeline is built over.
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Port.Skills.Host;

/// <summary>
/// The trusted descriptors (<see cref="Versions"/>, and the empty-directory inventory of each, <see cref="Directories"/>) for the installed records of state.json, and the ids of the
/// installed records no descriptor could be rebuilt for (<see cref="Unrestored"/>). A descriptor is never read from an installed folder: ED-321 (3) and ED-325.
/// </summary>
public sealed record TrustedInstalls(
    IReadOnlyList<StoreItem> Versions,
    IReadOnlyDictionary<(string Id, string Pin), IReadOnlyList<string>> Directories,
    IReadOnlyList<string> Unrestored);

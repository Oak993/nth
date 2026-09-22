using Marrow.Core.Port;

namespace Marrow.Core.Tests.Fixtures;

/// <summary>A model record holding the three value collections.</summary>
public sealed record HolderSample(ValueList<string> Items, ValueMap<string, int> Counts, ValueSet<string> Tags);

/// <summary>The pitfall shape: a record holding a BCL list compares the list by reference.</summary>
public sealed record ListHolderSample(List<string> Items);

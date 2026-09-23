// PORT-ONLY: lets Marrow.Core.Tests reach the module-internal Swift types that R-NAME-6 keeps internal (for example
// OnceContinuation and OutputCollector, Shell.swift:218-325), so their requirements are tested directly.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Marrow.Core.Tests")]

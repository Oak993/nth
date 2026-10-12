using System.Runtime.CompilerServices;

// PORT-ONLY: fake-stream tests reach the same connected-channel method as production (C7b U4).
[assembly: InternalsVisibleTo("Marrow.Setup.Tests")]

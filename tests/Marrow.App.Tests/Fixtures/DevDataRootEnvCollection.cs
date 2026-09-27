using Xunit;

namespace Marrow.App.Tests;

// Swift: none (port-only, ED-212). R-TEST-5: every test that mutates the process-wide MARROW_DEV_DATA_ROOT
// variable in this assembly joins this collection, and DisableParallelization keeps two of them from ever
// interleaving with each other or with a future class that joins it.
[CollectionDefinition("Dev data root env", DisableParallelization = true)]
public sealed class DevDataRootEnvCollection;

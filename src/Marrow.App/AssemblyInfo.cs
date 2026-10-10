using System.Reflection;

// Pinned project.yml:13-14,44,56; OD-0002 authorizes the product identity rename; OD-0135 names the product nth.
#if DEBUG
[assembly: AssemblyMetadata("CFBundleDisplayName", "nth Dev")]
#else
[assembly: AssemblyMetadata("CFBundleDisplayName", "nth")]
#endif
[assembly: AssemblyMetadata("CFBundleShortVersionString", "1.0.0")]
[assembly: AssemblyMetadata("CFBundleVersion", "1")]

// PR-UPDATE-113: project.yml:46 INFOPLIST_KEY_NSHumanReadableCopyright, byte for byte (R-STR-9). AssemblyCopyright
// maps to the executable's version resource LegalCopyright field, File Explorer's Windows analogue of Finder's Get
// Info (this record's own windows_path). U+00A9 COPYRIGHT SIGN.
[assembly: AssemblyCopyright("Copyright © 2026 Jordy Spruit (Droppy). Droppy Code by Jordy Spruit (Droppy), https://getdroppycode.app. GNU AGPL v3.0 with additional attribution terms.")]

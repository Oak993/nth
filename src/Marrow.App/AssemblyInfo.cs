using System.Reflection;

// Pinned project.yml:13-14,44,56; OD-0002 authorizes the product identity rename.
#if DEBUG
[assembly: AssemblyMetadata("CFBundleDisplayName", "Marrow Dev")]
#else
[assembly: AssemblyMetadata("CFBundleDisplayName", "Marrow")]
#endif
[assembly: AssemblyMetadata("CFBundleShortVersionString", "1.6.0")]
[assembly: AssemblyMetadata("CFBundleVersion", "19")]

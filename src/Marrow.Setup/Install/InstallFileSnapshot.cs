namespace Marrow.Setup.Install;

// PORT-ONLY: ED-NEW-inst-c7-1. Identity and length are queried from the same metadata handle.
public readonly record struct InstallFileSnapshot(InstallFileIdentity Identity, long Length);

using System.Buffers.Binary;
namespace Marrow.Services.Tests.Port.Skills.Install;
// A test-built PE32+ executable. Its only instruction returns the fixture exit code to the Windows loader.
// No compiler, Python, installer, registry access, network imports or external CLI is needed.
internal static class StandInExecutable
{
    public static byte[] Build(byte exitCode)
    {
        var pe = new byte[1024];
        void U16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(pe.AsSpan(offset), value);
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(offset), value);
        void U64(int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(pe.AsSpan(offset), value);
        U16(0, 0x5a4d); U32(0x3c, 0x80); U32(0x80, 0x00004550);
        U16(0x84, 0x8664); U16(0x86, 1); U16(0x94, 240); U16(0x96, 0x22);
        var opt = 0x98;
        U16(opt, 0x20b); U32(opt + 4, 512); U32(opt + 16, 0x1000); U32(opt + 20, 0x1000);
        U64(opt + 24, 0x140000000); U32(opt + 32, 0x1000); U32(opt + 36, 0x200);
        U16(opt + 40, 6); U16(opt + 48, 6); U32(opt + 56, 0x2000); U32(opt + 60, 512);
        U16(opt + 68, 3); U16(opt + 70, 0x8100); U64(opt + 72, 0x100000); U64(opt + 80, 0x1000);
        U64(opt + 88, 0x100000); U64(opt + 96, 0x1000); U32(opt + 108, 16);
        var section = opt + 240;
        ".text"u8.CopyTo(pe.AsSpan(section)); U32(section + 8, 6); U32(section + 12, 0x1000);
        U32(section + 16, 512); U32(section + 20, 512); U32(section + 36, 0x60000020);
        pe[512] = 0xb8; U32(513, exitCode); pe[517] = 0xc3; // mov eax, exitCode; ret
        return pe;
    }

    // The same image whose only instruction jumps to itself: it stays alive until the runner's cancellation kills it,
    // so a test can read the exact command line Windows handed to it (integ-sk4-sk5 review).
    public static byte[] BuildSpinning()
    {
        var pe = Build(0);
        pe[512] = 0xeb; pe[513] = 0xfe; // jmp $
        return pe;
    }
}

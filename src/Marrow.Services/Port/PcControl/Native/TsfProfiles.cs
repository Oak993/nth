// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-44; DESIGN.md 2.7 "IME composition", critique C4). Which kind of
// TSF profile serves an input language: TSF's active keyboard profile when it is for that language (exact in Windows'
// default mode, where the input method is shared by every app window), otherwise whether the language has any enabled
// keyboard text service (an input processor) at all, which errs on the longer wait. Reading only: nothing is activated,
// registered or changed. Used from the IME signals' own STA thread; never by automated tests (they call PcImeLanguage).
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.PcControl.Native;

public sealed class TsfProfiles
{
    private const uint TF_PROFILETYPE_INPUTPROCESSOR = 0x0001;
    private const uint TF_PROFILETYPE_KEYBOARDLAYOUT = 0x0002;
    private const uint TF_IPP_FLAG_ENABLED = 0x0002;
    private static readonly Guid CLSID_TF_InputProcessorProfiles = new("33C53A50-F456-4884-B049-85FD643ECFED");
    private static readonly Guid GUID_TFCAT_TIP_KEYBOARD = new("34745C63-B2F0-4784-8B67-5E12C8701A31");
    private static readonly TimeSpan ListFor = TimeSpan.FromMinutes(1);

    private readonly Dictionary<ushort, (PcInputProfileType Type, long At)> listed = [];
    private ITfInputProcessorProfileMgr? manager;
    private bool unavailable;

    /// <summary>The kind of profile serving <paramref name="languageId"/>; Unknown when TSF cannot be read.</summary>
    public PcInputProfileType TypeOf(ushort languageId)
    {
        if (Manager() is not { } profiles) return PcInputProfileType.Unknown;
        try
        {
            var category = GUID_TFCAT_TIP_KEYBOARD;
            if (profiles.GetActiveProfile(ref category, out var active) == 0 && active.LanguageId == languageId)
            {
                return active.ProfileType switch
                {
                    TF_PROFILETYPE_INPUTPROCESSOR => PcInputProfileType.InputProcessor,
                    TF_PROFILETYPE_KEYBOARDLAYOUT => PcInputProfileType.KeyboardLayout,
                    _ => PcInputProfileType.Unknown,
                };
            }
            var now = Environment.TickCount64;
            if (listed.TryGetValue(languageId, out var known) && now - known.At < ListFor.TotalMilliseconds) return known.Type;
            var type = Listed(profiles, languageId);
            listed[languageId] = (type, now);
            return type;
        }
        catch (COMException)
        {
            return PcInputProfileType.Unknown;
        }
        catch (InvalidCastException)
        {
            return PcInputProfileType.Unknown;
        }
    }

    // Any enabled keyboard text service for the language makes it an input processor; only plain layouts make it one.
    private static PcInputProfileType Listed(ITfInputProcessorProfileMgr profiles, ushort languageId)
    {
        if (profiles.EnumProfiles(languageId, out var list) != 0 || list is null) return PcInputProfileType.Unknown;
        try
        {
            var layout = false;
            while (list.Next(1, out var profile, out var fetched) == 0 && fetched == 1)
            {
                if ((profile.Flags & TF_IPP_FLAG_ENABLED) == 0) continue;
                if (profile.ProfileType == TF_PROFILETYPE_INPUTPROCESSOR && profile.Category == GUID_TFCAT_TIP_KEYBOARD) return PcInputProfileType.InputProcessor;
                layout |= profile.ProfileType == TF_PROFILETYPE_KEYBOARDLAYOUT;
            }
            return layout ? PcInputProfileType.KeyboardLayout : PcInputProfileType.Unknown;
        }
        finally
        {
            Marshal.ReleaseComObject(list);
        }
    }

    private ITfInputProcessorProfileMgr? Manager()
    {
        if (manager is not null || unavailable) return manager;
        try
        {
            var type = Type.GetTypeFromCLSID(CLSID_TF_InputProcessorProfiles, throwOnError: true)!;
            manager = (ITfInputProcessorProfileMgr)Activator.CreateInstance(type)!;
        }
        catch (COMException)
        {
            unavailable = true;
        }
        catch (InvalidCastException)
        {
            unavailable = true;
        }
        return manager;
    }
}

/// <summary>TF_INPUTPROCESSORPROFILE.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TfInputProcessorProfile
{
    public uint ProfileType;
    public ushort LanguageId;
    public Guid Clsid;
    public Guid Profile;
    public Guid Category;
    public nint SubstituteLayout;
    public uint Caps;
    public nint Layout;
    public uint Flags;
}

// ITfInputProcessorProfileMgr: the eight methods after IUnknown; only EnumProfiles and GetActiveProfile are called.
[ComImport, Guid("71C6E74C-0F28-11D8-A82A-00065B84435C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfInputProcessorProfileMgr
{
    void Slot01_ActivateProfile();
    void Slot02_DeactivateProfile();
    void Slot03_GetProfile();
    [PreserveSig] int EnumProfiles(ushort languageId, [MarshalAs(UnmanagedType.Interface)] out IEnumTfInputProcessorProfiles? profiles);
    void Slot05_ReleaseInputProcessor();
    void Slot06_RegisterProfile();
    void Slot07_UnregisterProfile();
    [PreserveSig] int GetActiveProfile(ref Guid category, out TfInputProcessorProfile profile);
}

[ComImport, Guid("71C6E74D-0F28-11D8-A82A-00065B84435C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumTfInputProcessorProfiles
{
    void Slot01_Clone();
    [PreserveSig] int Next(uint count, out TfInputProcessorProfile profile, out uint fetched);
    [PreserveSig] int Reset();
    [PreserveSig] int Skip(uint count);
}

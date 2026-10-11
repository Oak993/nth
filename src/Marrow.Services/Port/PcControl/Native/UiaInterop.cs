// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, ED-NEW-pcctl-1, ED-NEW-pcctl-4; DESIGN.md 3.1). UI Automation's COM
// client interfaces, declared with [ComImport] (ED-NEW-pcctl-3). Each interface lists its methods in the exact vtable
// order of UIAutomationClient.h; a method Tactus never calls is a placeholder that only holds its slot, and slots after
// the last one used are left out. Only UiaReader calls these, on its own worker thread; no automated test does.
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.PcControl.Native;

internal static class UiaIds
{
    public static readonly Guid CUIAutomation8 = new("e22ad333-b25f-460c-83d0-0581107395c9");

    // TreeScope and AutomationElementMode.
    public const int TreeScopeElement = 1;
    public const int TreeScopeSubtree = 7;
    public const int ElementModeFull = 1;

    // UIA_PropertyIds.
    public const int RuntimeId = 30000;
    public const int BoundingRectangle = 30001;
    public const int ProcessId = 30002;
    public const int ControlType = 30003;
    public const int LocalizedControlType = 30004;
    public const int Name = 30005;
    public const int AcceleratorKey = 30006;
    public const int HasKeyboardFocus = 30008;
    public const int IsKeyboardFocusable = 30009;
    public const int IsEnabled = 30010;
    public const int AutomationId = 30011;
    public const int IsPassword = 30019;
    public const int NativeWindowHandle = 30020;
    public const int IsOffscreen = 30022;
    public const int IsExpandCollapsePatternAvailable = 30028;
    public const int IsInvokePatternAvailable = 30031;
    public const int IsRangeValuePatternAvailable = 30033;
    public const int IsScrollPatternAvailable = 30034;
    public const int IsScrollItemPatternAvailable = 30035;
    public const int IsSelectionItemPatternAvailable = 30036;
    public const int IsTextPatternAvailable = 30040;
    public const int IsTogglePatternAvailable = 30041;
    public const int IsValuePatternAvailable = 30043;
    public const int IsWindowPatternAvailable = 30044;
    public const int ValueValue = 30045;
    public const int ValueIsReadOnly = 30046;
    public const int RangeValueValue = 30047;
    public const int RangeValueIsReadOnly = 30048;
    public const int ScrollHorizontallyScrollable = 30057;
    public const int ScrollVerticallyScrollable = 30058;
    public const int ExpandCollapseState = 30070;
    public const int SelectionItemIsSelected = 30079;
    public const int ToggleState = 30086;

    // UIA_PatternIds.
    public const int InvokePattern = 10000;
    public const int ValuePattern = 10002;
    public const int RangeValuePattern = 10003;
    public const int ScrollPattern = 10004;
    public const int ExpandCollapsePattern = 10005;
    public const int WindowPattern = 10009;
    public const int SelectionItemPattern = 10010;
    public const int TextPattern = 10014;
    public const int TogglePattern = 10015;
    public const int ScrollItemPattern = 10017;

    // ExpandCollapseState, ToggleState, ScrollAmount, WindowVisualState.
    public const int Collapsed = 0;
    public const int Expanded = 1;
    public const int PartiallyExpanded = 2;
    public const int LeafNode = 3;
    public const int ToggleOff = 0;
    public const int ToggleOn = 1;
    public const int ToggleIndeterminate = 2;
    public const int LargeDecrement = 0;
    public const int SmallDecrement = 1;
    public const int NoAmount = 2;
    public const int LargeIncrement = 3;
    public const int SmallIncrement = 4;
    public const int VisualNormal = 0;
    public const int VisualMaximized = 1;
    public const int VisualMinimized = 2;
}

// IUIAutomation's 55 methods, then IUIAutomation2's six (AutoSetFocus, ConnectionTimeout, TransactionTimeout).
[ComImport, Guid("34723aff-0c9d-49d0-9896-7ab52df8cd8a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomation2
{
    void Slot01_CompareElements();
    void Slot02_CompareRuntimeIds();
    void Slot03_GetRootElement();
    [PreserveSig] int ElementFromHandle(nint window, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement? element);
    void Slot05_ElementFromPoint();
    [PreserveSig] int GetFocusedElement([MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement? element);
    void Slot07_GetRootElementBuildCache();
    [PreserveSig] int ElementFromHandleBuildCache(nint window, IUIAutomationCacheRequest cacheRequest, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement? element);
    void Slot09_ElementFromPointBuildCache();
    void Slot10_GetFocusedElementBuildCache();
    void Slot11_CreateTreeWalker();
    void Slot12_get_ControlViewWalker();
    void Slot13_get_ContentViewWalker();
    void Slot14_get_RawViewWalker();
    void Slot15_get_RawViewCondition();
    [PreserveSig] int get_ControlViewCondition([MarshalAs(UnmanagedType.Interface)] out IUIAutomationCondition? condition);
    void Slot17_get_ContentViewCondition();
    [PreserveSig] int CreateCacheRequest([MarshalAs(UnmanagedType.Interface)] out IUIAutomationCacheRequest? request);
    void Slot19_CreateTrueCondition();
    void Slot20_CreateFalseCondition();
    [PreserveSig] int CreatePropertyCondition(int propertyId, [MarshalAs(UnmanagedType.Struct)] object value, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationCondition? condition);
    void Slot22_CreatePropertyConditionEx();
    void Slot23_CreateAndCondition();
    void Slot24_CreateAndConditionFromArray();
    void Slot25_CreateAndConditionFromNativeArray();
    void Slot26_CreateOrCondition();
    void Slot27_CreateOrConditionFromArray();
    void Slot28_CreateOrConditionFromNativeArray();
    void Slot29_CreateNotCondition();
    void Slot30_AddAutomationEventHandler();
    void Slot31_RemoveAutomationEventHandler();
    void Slot32_AddPropertyChangedEventHandlerNativeArray();
    void Slot33_AddPropertyChangedEventHandler();
    void Slot34_RemovePropertyChangedEventHandler();
    void Slot35_AddStructureChangedEventHandler();
    void Slot36_RemoveStructureChangedEventHandler();
    void Slot37_AddFocusChangedEventHandler();
    void Slot38_RemoveFocusChangedEventHandler();
    void Slot39_RemoveAllEventHandlers();
    void Slot40_IntNativeArrayToSafeArray();
    void Slot41_IntSafeArrayToNativeArray();
    void Slot42_RectToVariant();
    void Slot43_VariantToRect();
    void Slot44_SafeArrayToRectNativeArray();
    void Slot45_CreateProxyFactoryEntry();
    void Slot46_get_ProxyFactoryMapping();
    void Slot47_GetPropertyProgrammaticName();
    void Slot48_GetPatternProgrammaticName();
    void Slot49_PollForPotentialSupportedPatterns();
    void Slot50_PollForPotentialSupportedProperties();
    void Slot51_CheckNotSupported();
    void Slot52_get_ReservedNotSupportedValue();
    void Slot53_get_ReservedMixedAttributeValue();
    void Slot54_ElementFromIAccessible();
    void Slot55_ElementFromIAccessibleBuildCache();
    void Slot56_get_AutoSetFocus();
    void Slot57_put_AutoSetFocus();
    void Slot58_get_ConnectionTimeout();
    [PreserveSig] int put_ConnectionTimeout(uint milliseconds);
    void Slot60_get_TransactionTimeout();
    [PreserveSig] int put_TransactionTimeout(uint milliseconds);
}

[ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationCondition
{
}

[ComImport, Guid("b32a92b5-bc25-4078-9c08-d7ee95c48e03"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationCacheRequest
{
    [PreserveSig] int AddProperty(int propertyId);
    [PreserveSig] int AddPattern(int patternId);
    void Slot03_Clone();
    void Slot04_get_TreeScope();
    [PreserveSig] int put_TreeScope(int scope);
    void Slot06_get_TreeFilter();
    [PreserveSig] int put_TreeFilter(IUIAutomationCondition filter);
    void Slot08_get_AutomationElementMode();
    [PreserveSig] int put_AutomationElementMode(int mode);
}

[ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationElementArray
{
    [PreserveSig] int get_Length(out int length);
    [PreserveSig] int GetElement(int index, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement? element);
}

[ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationElement
{
    [PreserveSig] int SetFocus();
    [PreserveSig] int GetRuntimeId([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] out int[]? runtimeId);
    [PreserveSig] int FindFirst(int scope, IUIAutomationCondition condition, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement? found);
    void Slot04_FindAll();
    [PreserveSig] int FindFirstBuildCache(int scope, IUIAutomationCondition condition, IUIAutomationCacheRequest cacheRequest, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement? found);
    void Slot06_FindAllBuildCache();
    [PreserveSig] int BuildUpdatedCache(IUIAutomationCacheRequest cacheRequest, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement? updated);
    [PreserveSig] int GetCurrentPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object? value);
    void Slot09_GetCurrentPropertyValueEx();
    [PreserveSig] int GetCachedPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object? value);
    void Slot11_GetCachedPropertyValueEx();
    void Slot12_GetCurrentPatternAs();
    void Slot13_GetCachedPatternAs();
    [PreserveSig] int GetCurrentPattern(int patternId, [MarshalAs(UnmanagedType.IUnknown)] out object? pattern);
    void Slot15_GetCachedPattern();
    void Slot16_GetCachedParent();
    [PreserveSig] int GetCachedChildren([MarshalAs(UnmanagedType.Interface)] out IUIAutomationElementArray? children);
}

[ComImport, Guid("fb377fbe-8ea6-46d5-9c73-6499642d3059"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationInvokePattern
{
    [PreserveSig] int Invoke();
}

[ComImport, Guid("94cf8058-9b8d-4ab9-8bfd-4cd0a33c8c70"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationTogglePattern
{
    [PreserveSig] int Toggle();
}

[ComImport, Guid("a8efa66a-0fda-421a-9194-38021f3578ea"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationSelectionItemPattern
{
    [PreserveSig] int Select();
}

[ComImport, Guid("619be086-1f4e-4ee4-bafa-210128738730"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationExpandCollapsePattern
{
    [PreserveSig] int Expand();
    [PreserveSig] int Collapse();
}

[ComImport, Guid("a94cd8b1-0844-4cd6-9d2d-640537ab39e9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationValuePattern
{
    [PreserveSig] int SetValue([MarshalAs(UnmanagedType.BStr)] string value);
    [PreserveSig] int get_CurrentValue([MarshalAs(UnmanagedType.BStr)] out string? value);
    [PreserveSig] int get_CurrentIsReadOnly(out int readOnly);
}

[ComImport, Guid("59213f4f-7346-49e5-b120-80555987a148"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationRangeValuePattern
{
    [PreserveSig] int SetValue(double value);
    [PreserveSig] int get_CurrentValue(out double value);
    [PreserveSig] int get_CurrentIsReadOnly(out int readOnly);
    [PreserveSig] int get_CurrentMaximum(out double value);
    [PreserveSig] int get_CurrentMinimum(out double value);
}

[ComImport, Guid("88f4d42a-e881-459d-a77c-73bbbb7e02dc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationScrollPattern
{
    [PreserveSig] int Scroll(int horizontalAmount, int verticalAmount);
}

[ComImport, Guid("b488300f-d015-4f19-9c29-bb595e3645ef"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationScrollItemPattern
{
    [PreserveSig] int ScrollIntoView();
}

[ComImport, Guid("0faef453-9208-43ef-bbb2-3b485177864f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationWindowPattern
{
    [PreserveSig] int Close();
    [PreserveSig] int WaitForInputIdle(int milliseconds, out int success);
    [PreserveSig] int SetWindowVisualState(int state);
}

[ComImport, Guid("32eba289-3583-42c9-9c59-3b6d9a1e9b6a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationTextPattern
{
    void Slot01_RangeFromPoint();
    void Slot02_RangeFromChild();
    void Slot03_GetSelection();
    void Slot04_GetVisibleRanges();
    [PreserveSig] int get_DocumentRange([MarshalAs(UnmanagedType.Interface)] out IUIAutomationTextRange? range);
}

[ComImport, Guid("a543cc6a-f4ae-494b-8239-c814481187a8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationTextRange
{
    void Slot01_Clone();
    void Slot02_Compare();
    void Slot03_CompareEndpoints();
    void Slot04_ExpandToEnclosingUnit();
    void Slot05_FindAttribute();
    void Slot06_FindText();
    void Slot07_GetAttributeValue();
    void Slot08_GetBoundingRectangles();
    void Slot09_GetEnclosingElement();
    [PreserveSig] int GetText(int maxLength, [MarshalAs(UnmanagedType.BStr)] out string? text);
}

using System.Runtime.InteropServices;

namespace dockus.Core.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct PROPERTYKEY { public PROPERTYKEY(Guid id, uint pid) { fmtid = id; this.pid = pid; } private Guid fmtid; private uint pid; }

[StructLayout(LayoutKind.Explicit)]
internal struct PROPVARIANT { [FieldOffset(0)] public ushort varType; [FieldOffset(8)] public IntPtr pwszVal; }

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
internal struct APPBARDATA
{
    public uint cbSize;
    public IntPtr hWnd;
    public uint uCallbackMessage;
    public uint uEdge;
    public RECT rc;
    public IntPtr lParam;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RECT { public int left, top, right, bottom; }
[StructLayout(LayoutKind.Sequential)]
internal struct SYSTEM_POWER_STATUS
{
    public byte ACLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte Reserved1;
    public int BatteryLifeTime;
    public int BatteryFullLifeTime;
}
// Display access: laptop panel via WMI, external monitors via DDC/CI (dxva2, VCP 0x10).
// Same technique as Lenovo Display Control Center, without the rest.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Native
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PHYSICAL_MONITOR { public IntPtr hPhysicalMonitor; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprc, IntPtr data);

    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplayDevices(string device, uint index, ref DISPLAY_DEVICE dd, uint flags);
    [DllImport("dxva2.dll", SetLastError = true)] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);
    [DllImport("dxva2.dll", SetLastError = true)] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);
    [DllImport("dxva2.dll", SetLastError = true)] public static extern bool DestroyPhysicalMonitor(IntPtr hPhysicalMonitor);
    [DllImport("dxva2.dll", SetLastError = true)] public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte code, IntPtr type, out uint current, out uint max);
    [DllImport("dxva2.dll", SetLastError = true)] public static extern bool SetVCPFeature(IntPtr h, byte code, uint value);
    [DllImport("kernel32.dll")] public static extern bool AttachConsole(int pid);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
}

abstract class Display
{
    public string Name;
    public uint Max = 100;
    public uint Current;

    public abstract bool Read();
    public abstract bool Set(uint percent);
    public virtual void Release() { }

    public int Percent { get { return Max == 0 ? 0 : (int)Math.Round(Current * 100.0 / Max); } }

    // laptop panel first, then external DDC/CI monitors
    public static List<Display> EnumerateAll()
    {
        var all = new List<Display>();
        var panel = LaptopPanel.TryCreate();
        if (panel != null) all.Add(panel);
        all.AddRange(Monitor.EnumerateDdc());
        return all;
    }

    public static void Release(IEnumerable<Display> displays)
    {
        foreach (var d in displays) d.Release();
    }
}

// Internal panel: brightness via WMI (root\wmi WmiMonitorBrightness), no DDC/CI.
class LaptopPanel : Display
{
    public static LaptopPanel TryCreate()
    {
        var p = new LaptopPanel { Name = L.LaptopDisplay };
        return p.Read() ? p : null;
    }

    public override bool Read()
    {
        try
        {
            using (var s = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE"))
            foreach (ManagementObject o in s.Get())
            {
                Current = (byte)o["CurrentBrightness"];
                return true;
            }
        }
        catch { }
        return false;
    }

    public override bool Set(uint percent)
    {
        try
        {
            using (var s = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE"))
            foreach (ManagementObject o in s.Get())
            {
                o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)Math.Min(percent, 100u) });
                Current = Math.Min(percent, 100u);
                return true;
            }
        }
        catch { }
        return false;
    }
}

class Monitor : Display
{
    const byte VcpBrightness = 0x10;
    readonly object ddcLock = new object();

    public IntPtr Handle;

    public override void Release() { Native.DestroyPhysicalMonitor(Handle); }

    public override bool Read()
    {
        lock (ddcLock)
        {
            // DDC/CI is flaky on some monitors; retry a few times like most tools do.
            for (int i = 0; i < 3; i++)
            {
                uint cur, max;
                if (Native.GetVCPFeatureAndVCPFeatureReply(Handle, VcpBrightness, IntPtr.Zero, out cur, out max) && max > 0)
                {
                    Current = cur; Max = max; return true;
                }
                Thread.Sleep(40);
            }
            return false;
        }
    }

    public override bool Set(uint percent)
    {
        uint value = (uint)Math.Round(Math.Min(percent, 100u) * Max / 100.0);
        lock (ddcLock)
        {
            for (int i = 0; i < 3; i++)
            {
                if (Native.SetVCPFeature(Handle, VcpBrightness, value)) { Current = value; return true; }
                Thread.Sleep(40);
            }
            return false;
        }
    }

    // ---- enumeration ------------------------------------------------------

    public static List<Monitor> EnumerateDdc()
    {
        var all = new List<Monitor>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, hdc, rc, data) =>
        {
            var info = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX)) };
            Native.GetMonitorInfo(hMon, ref info);

            uint n;
            if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out n) || n == 0) return true;
            var phys = new Native.PHYSICAL_MONITOR[n];
            if (!Native.GetPhysicalMonitorsFromHMONITOR(hMon, n, phys)) return true;

            for (uint i = 0; i < n; i++)
            {
                var m = new Monitor { Handle = phys[i].hPhysicalMonitor };
                m.Name = FriendlyName(info.szDevice, i) ?? phys[i].szPhysicalMonitorDescription;
                if (m.Read()) all.Add(m);
                else Native.DestroyPhysicalMonitor(m.Handle); // internal panel etc. - no DDC/CI
            }
            return true;
        }, IntPtr.Zero);

        // disambiguate identical model names
        foreach (var g in all.GroupBy(x => x.Name).Where(g => g.Count() > 1))
        {
            int k = 1;
            foreach (var m in g) m.Name += " #" + k++;
        }
        return all;
    }

    // \\.\DISPLAY1 + index -> "\\?\DISPLAY#LEN66F2#5&..." -> EDID monitor name from registry
    static string FriendlyName(string adapter, uint index)
    {
        try
        {
            var dd = new Native.DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(Native.DISPLAY_DEVICE)) };
            if (!Native.EnumDisplayDevices(adapter, index, ref dd, 1 /* EDD_GET_DEVICE_INTERFACE_NAME */)) return null;
            var parts = dd.DeviceID.Split('#');
            if (parts.Length < 3) return null;
            using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + parts[1] + @"\" + parts[2] + @"\Device Parameters"))
            {
                var edid = key == null ? null : key.GetValue("EDID") as byte[];
                if (edid == null || edid.Length < 128) return null;
                // four 18-byte descriptors at 54; tag 0xFC = monitor name
                for (int off = 54; off <= 108; off += 18)
                {
                    if (edid[off] == 0 && edid[off + 1] == 0 && edid[off + 3] == 0xFC)
                        return Encoding.ASCII.GetString(edid, off + 5, 13).Split('\n')[0].Trim();
                }
            }
        }
        catch { }
        return null;
    }
}

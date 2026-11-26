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
    public Rectangle Bounds;   // desktop coordinates; Empty if unknown
    public bool Primary;
    public string DeviceId;    // "\\?\DISPLAY#LEN66F2#5&...#{guid}"; null if unknown

    // what the app last asked for; the health check re-sends it to a monitor that missed it
    public volatile int Desired = -1;
    public volatile bool Dirty;    // written but not yet read back
    protected int retries;
    bool lastApplyOk = true;

    public abstract bool Read();
    public abstract bool Set(uint percent);
    public virtual void Release() { }

    public int Percent { get { return Max == 0 ? 0 : (int)Math.Round(Current * 100.0 / Max); } }

    public bool Apply(int percent)
    {
        Desired = percent;
        Dirty = true;
        retries = 0;
        bool ok = Set((uint)percent);
        if (ok != lastApplyOk) Log.Write("{0}: write {1} % {2}", Name, percent, ok ? "ok again" : "FAILED (off?), will retry");
        lastApplyOk = ok;
        return ok;
    }

    // all controllable displays, ordered like the Windows arrangement (left to right, then top to bottom)
    public static List<Display> EnumerateAll()
    {
        var all = new List<Display>();
        var panel = LaptopPanel.TryCreate();
        if (panel != null) all.Add(panel);
        all.AddRange(Monitor.EnumerateDdc());
        var ordered = Arrange(all);

        // identical models: number them in arrangement order, so "#1" is the leftmost
        foreach (var g in ordered.GroupBy(x => x.Name).Where(g => g.Count() > 1).ToList())
        {
            int k = 1;
            foreach (var d in g) d.Name += " #" + k++;
        }
        return ordered;
    }

    // reading order of the Windows layout: displays overlapping vertically by at least half form a row;
    // rows top to bottom, left to right within a row; unknown positions last
    public static List<Display> Arrange(List<Display> all)
    {
        var rows = new List<List<Display>>();
        var spans = new List<int[]>();
        foreach (var d in all.Where(x => !x.Bounds.IsEmpty).OrderBy(x => x.Bounds.Top))
        {
            int row = -1;
            for (int i = 0; i < spans.Count && row < 0; i++)
            {
                int overlap = Math.Min(spans[i][1], d.Bounds.Bottom) - Math.Max(spans[i][0], d.Bounds.Top);
                if (overlap * 2 >= Math.Min(d.Bounds.Height, spans[i][1] - spans[i][0])) row = i;
            }
            if (row < 0) { rows.Add(new List<Display>()); spans.Add(new[] { d.Bounds.Top, d.Bounds.Bottom }); row = rows.Count - 1; }
            rows[row].Add(d);
            spans[row][0] = Math.Min(spans[row][0], d.Bounds.Top);
            spans[row][1] = Math.Max(spans[row][1], d.Bounds.Bottom);
        }
        var result = rows.SelectMany(r => r.OrderBy(x => x.Bounds.X)).ToList();
        result.AddRange(all.Where(x => x.Bounds.IsEmpty));
        return result;
    }

    public class Output { public string DeviceId; public Rectangle Bounds; public bool Primary; public IntPtr HMonitor; public uint Index; }

    // every physical output with its device interface id ("\\?\DISPLAY#LEN66F2#5&...#{guid}") and desktop rectangle
    public static List<Output> Outputs()
    {
        var list = new List<Output>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, hdc, rc, data) =>
        {
            var info = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX)) };
            if (!Native.GetMonitorInfo(hMon, ref info)) return true;
            var r = info.rcMonitor;
            for (uint i = 0; ; i++)
            {
                var dd = new Native.DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(Native.DISPLAY_DEVICE)) };
                if (!Native.EnumDisplayDevices(info.szDevice, i, ref dd, 1 /* EDD_GET_DEVICE_INTERFACE_NAME */)) break;
                list.Add(new Output
                {
                    DeviceId = dd.DeviceID, HMonitor = hMon, Index = i, Primary = (info.dwFlags & 1) != 0,
                    Bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom)
                });
            }
            return true;
        }, IntPtr.Zero);
        return list;
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
        if (!p.Read()) return null;
        p.Locate();
        return p;
    }

    // WMI instance "DISPLAY\AUO408D\5&655aead&0&UID256_0" <-> device id "\\?\DISPLAY#AUO408D#5&655aead&0&UID256#{...}"
    void Locate()
    {
        try
        {
            string inst = null;
            using (var s = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName FROM WmiMonitorBrightness WHERE Active=TRUE"))
            foreach (ManagementObject o in s.Get()) { inst = (string)o["InstanceName"]; break; }
            if (inst == null) return;
            var parts = inst.Split('\\');
            if (parts.Length < 3) return;
            string pnp = parts[2];
            int us = pnp.LastIndexOf('_');
            if (us > 0) pnp = pnp.Substring(0, us);
            string key = ("#" + parts[1] + "#" + pnp + "#").ToUpperInvariant();
            foreach (var o in Outputs())
                if (o.DeviceId != null && o.DeviceId.ToUpperInvariant().Contains(key)) { Bounds = o.Bounds; Primary = o.Primary; DeviceId = o.DeviceId; return; }
        }
        catch { }
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

    public IntPtr Handle;          // small integer, 0 is a valid handle
    bool released;
    public volatile bool Offline;  // last health check got no DDC/CI answer (monitor switched off)

    public override void Release()
    {
        lock (ddcLock)
        {
            if (!released) Native.DestroyPhysicalMonitor(Handle);
            released = true;
        }
    }

    // Periodic check. A monitor that is switched off ignores DDC/CI, so writes made meanwhile
    // (e.g. the morning brightening) are lost; once it answers again, send it what it missed.
    // Also reads back fresh writes, because some monitors accept a write and drop it.
    // Values set via the monitor's own buttons are left alone.
    public void Heal()
    {
        lock (ddcLock)
        {
            if (released) return;
            bool wasOffline = Offline;
            if (!Read())
            {
                if (!wasOffline) Log.Write("{0}: no DDC/CI answer (switched off?)", Name);
                Offline = true;
                return;
            }
            Offline = false;
            if (wasOffline) Log.Write("{0}: answering again, at {1} %", Name, Percent);
            int want = Desired;
            if (want < 0 || (!wasOffline && !Dirty)) return;
            if (Math.Abs(Percent - want) <= 1 || (retries >= 3 && !wasOffline)) { Dirty = false; retries = 0; return; }
            int was = Percent;
            retries++;
            bool ok = Set((uint)want);
            Dirty = true; // read back once more on the next check
            Log.Write("{0}: at {1} % instead of {2} %, re-sent {3}", Name, was, want, ok ? "ok" : "FAILED");
        }
    }

    public override bool Read()
    {
        lock (ddcLock)
        {
            if (released) return false;
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
            if (released) return false;
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
                m.DeviceId = DeviceInterface(info.szDevice, i);
                m.Name = FriendlyName(m.DeviceId) ?? phys[i].szPhysicalMonitorDescription;
                var r = info.rcMonitor;
                m.Bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                m.Primary = (info.dwFlags & 1) != 0;
                if (m.Read()) all.Add(m);
                else Native.DestroyPhysicalMonitor(m.Handle); // internal panel etc. - no DDC/CI
            }
            return true;
        }, IntPtr.Zero);
        return all;
    }

    // \\.\DISPLAY1 + index -> "\\?\DISPLAY#LEN66F2#5&..."
    static string DeviceInterface(string adapter, uint index)
    {
        var dd = new Native.DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(Native.DISPLAY_DEVICE)) };
        return Native.EnumDisplayDevices(adapter, index, ref dd, 1 /* EDD_GET_DEVICE_INTERFACE_NAME */) ? dd.DeviceID : null;
    }

    // device interface -> EDID monitor name from registry
    static string FriendlyName(string deviceId)
    {
        try
        {
            if (deviceId == null) return null;
            var parts = deviceId.Split('#');
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

// BrightnessGlass - tray app for laptop panel + external monitors.
//
//   BrightnessGlass.exe                 tray app (left click / hotkey = sliders, right click = menu)
//   BrightnessGlass.exe list            print displays and current brightness
//   BrightnessGlass.exe sun             print today's twilight schedule and auto target
//   BrightnessGlass.exe 40              set master ("All") to 40 % (laptop keeps its minimum)
//   BrightnessGlass.exe 40 P34w         set one display directly
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

class Settings
{
    const string Key = @"Software\BrightnessDDC";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public double Lat = 51.16, Lon = 10.45;            // centre of Germany until a location is known
    public bool AutoLocate = true;                     // use the Windows location service
    public int DimOffsetMin = 15;                      // dimming starts this long before sunset
    public int LaptopMin = 12;                         // laptop panel at master 0 %
    public int DayLevel = 100, NightLevel = 0;         // master level in daylight / darkness
    public int Master = 100;                           // last master value
    public bool Auto = true;
    // Ctrl+Alt+digit collides with AltGr on German layouts, hence F-keys for the presets
    public Keys HotkeyPanel = Keys.Control | Keys.Alt | Keys.B;
    public Keys Hotkey0 = Keys.Control | Keys.Alt | Keys.F10;
    public Keys Hotkey50 = Keys.Control | Keys.Alt | Keys.F11;
    public Keys Hotkey100 = Keys.Control | Keys.Alt | Keys.F12;

    public static Settings Load()
    {
        var s = new Settings();
        using (var k = Registry.CurrentUser.CreateSubKey(Key))
        {
            s.Lat = Dbl(k, "Latitude", s.Lat);
            s.Lon = Dbl(k, "Longitude", s.Lon);
            s.AutoLocate = (int)k.GetValue("AutoLocate", s.AutoLocate ? 1 : 0) == 1;
            s.DimOffsetMin = (int)k.GetValue("DimOffsetMin", s.DimOffsetMin);
            s.LaptopMin = (int)k.GetValue("LaptopMin", s.LaptopMin);
            s.DayLevel = (int)k.GetValue("DayLevel", s.DayLevel);
            s.NightLevel = (int)k.GetValue("NightLevel", s.NightLevel);
            s.Master = (int)k.GetValue("Master", s.Master);
            s.Auto = (int)k.GetValue("AutoTwilight", s.Auto ? 1 : 0) == 1;
            s.HotkeyPanel = (Keys)(int)k.GetValue("HotkeyPanel", (int)s.HotkeyPanel);
            s.Hotkey0 = (Keys)(int)k.GetValue("Hotkey0", (int)s.Hotkey0);
            s.Hotkey50 = (Keys)(int)k.GetValue("Hotkey50", (int)s.Hotkey50);
            s.Hotkey100 = (Keys)(int)k.GetValue("Hotkey100", (int)s.Hotkey100);
        }
        return s;
    }

    public void Save()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(Key))
        {
            k.SetValue("Latitude", Lat.ToString(Inv));
            k.SetValue("Longitude", Lon.ToString(Inv));
            k.SetValue("AutoLocate", AutoLocate ? 1 : 0, RegistryValueKind.DWord);
            k.SetValue("DimOffsetMin", DimOffsetMin, RegistryValueKind.DWord);
            k.SetValue("LaptopMin", LaptopMin, RegistryValueKind.DWord);
            k.SetValue("DayLevel", DayLevel, RegistryValueKind.DWord);
            k.SetValue("NightLevel", NightLevel, RegistryValueKind.DWord);
            k.SetValue("Master", Master, RegistryValueKind.DWord);
            k.SetValue("AutoTwilight", Auto ? 1 : 0, RegistryValueKind.DWord);
            k.SetValue("HotkeyPanel", (int)HotkeyPanel, RegistryValueKind.DWord);
            k.SetValue("Hotkey0", (int)Hotkey0, RegistryValueKind.DWord);
            k.SetValue("Hotkey50", (int)Hotkey50, RegistryValueKind.DWord);
            k.SetValue("Hotkey100", (int)Hotkey100, RegistryValueKind.DWord);
        }
    }

    static double Dbl(RegistryKey k, string name, double def)
    {
        double v;
        return double.TryParse(k.GetValue(name, "") as string, NumberStyles.Float, Inv, out v) ? v : def;
    }

    // master 0..100 -> display percent; laptop keeps a floor, externals follow 1:1
    public int Map(Display d, int master)
    {
        int min = d is LaptopPanel ? LaptopMin : 0;
        return (int)Math.Round(min + (100 - min) * Math.Max(0, Math.Min(100, master)) / 100.0);
    }

    public static string HotkeyText(Keys k)
    {
        if ((k & Keys.KeyCode) == Keys.None) return L.KeyNone;
        var parts = new List<string>();
        if ((k & Keys.Control) != 0) parts.Add(L.KeyCtrl);
        if ((k & Keys.Alt) != 0) parts.Add("Alt");
        if ((k & Keys.Shift) != 0) parts.Add(L.KeyShift);
        parts.Add((k & Keys.KeyCode).ToString());
        return string.Join(" + ", parts);
    }
}

// Solar elevation (NOAA approximation, accurate to well under 1 degree).
static class Sun
{
    static double Rad(double d) { return d * Math.PI / 180; }
    static double Deg(double r) { return r * 180 / Math.PI; }

    public static double Elevation(DateTime utc, double lat, double lon)
    {
        double jd = utc.ToOADate() + 2415018.5;
        double t = (jd - 2451545.0) / 36525.0;
        double l0 = (280.46646 + t * (36000.76983 + t * 0.0003032)) % 360;
        double m = 357.52911 + t * (35999.05029 - 0.0001537 * t);
        double e = 0.016708634 - t * (0.000042037 + 0.0000001267 * t);
        double c = Math.Sin(Rad(m)) * (1.914602 - t * (0.004817 + 0.000014 * t))
                 + Math.Sin(Rad(2 * m)) * (0.019993 - 0.000101 * t)
                 + Math.Sin(Rad(3 * m)) * 0.000289;
        double omega = 125.04 - 1934.136 * t;
        double lambda = l0 + c - 0.00569 - 0.00478 * Math.Sin(Rad(omega));
        double eps0 = 23 + (26 + (21.448 - t * (46.815 + t * (0.00059 - t * 0.001813))) / 60) / 60;
        double eps = eps0 + 0.00256 * Math.Cos(Rad(omega));
        double decl = Math.Asin(Math.Sin(Rad(eps)) * Math.Sin(Rad(lambda)));
        double y = Math.Pow(Math.Tan(Rad(eps) / 2), 2);
        double eqTime = 4 * Deg(y * Math.Sin(2 * Rad(l0)) - 2 * e * Math.Sin(Rad(m))
                      + 4 * e * y * Math.Sin(Rad(m)) * Math.Cos(2 * Rad(l0))
                      - 0.5 * y * y * Math.Sin(4 * Rad(l0)) - 1.25 * e * e * Math.Sin(2 * Rad(m)));
        double solarMinutes = ((utc.TimeOfDay.TotalMinutes + eqTime + 4 * lon) % 1440 + 1440) % 1440;
        double ha = solarMinutes / 4 - 180;
        double cosZen = Math.Sin(Rad(lat)) * Math.Sin(decl) + Math.Cos(Rad(lat)) * Math.Cos(decl) * Math.Cos(Rad(ha));
        return 90 - Deg(Math.Acos(Math.Max(-1, Math.Min(1, cosZen))));
    }
}

// Today's twilight schedule (local time) and the resulting day factor 1 = day, 0 = night.
// Evening: full brightness until sunset - offset, dark at civil dusk (sun -6 deg).
// Morning mirrors it: brightening from civil dawn until sunrise + offset.
class Twilight
{
    public DateTime? Dawn, Sunrise, Sunset, Dusk;
    public DateTime EveningStart, MorningEnd;

    public static Twilight For(DateTime localDate, Settings s)
    {
        var tw = new Twilight();
        double prev = double.NaN;
        for (int min = 0; min <= 1440; min++)
        {
            var local = localDate.Date.AddMinutes(min);
            double el = Sun.Elevation(local.ToUniversalTime(), s.Lat, s.Lon);
            if (!double.IsNaN(prev))
            {
                if (prev < -6 && el >= -6 && tw.Dawn == null) tw.Dawn = local;
                if (prev < -0.833 && el >= -0.833 && tw.Sunrise == null) tw.Sunrise = local;
                if (prev >= -0.833 && el < -0.833) tw.Sunset = local;
                if (prev >= -6 && el < -6) tw.Dusk = local;
            }
            prev = el;
        }
        if (tw.Sunset != null) tw.EveningStart = tw.Sunset.Value.AddMinutes(-s.DimOffsetMin);
        if (tw.Sunrise != null) tw.MorningEnd = tw.Sunrise.Value.AddMinutes(s.DimOffsetMin);
        return tw;
    }

    public double DayFactor(DateTime local)
    {
        if (Dawn == null || Dusk == null || Sunrise == null || Sunset == null) return 1; // polar edge case
        if (local < Dawn.Value || local >= Dusk.Value) return 0;
        if (local < MorningEnd) return Smooth(Frac(local, Dawn.Value, MorningEnd));
        if (local >= EveningStart) return 1 - Smooth(Frac(local, EveningStart, Dusk.Value));
        return 1;
    }

    static double Frac(DateTime t, DateTime a, DateTime b)
    {
        double span = (b - a).TotalMinutes;
        return span <= 0 ? 1 : Math.Max(0, Math.Min(1, (t - a).TotalMinutes / span));
    }

    static double Smooth(double x) { return x * x * (3 - 2 * x); }

    public string Summary()
    {
        if (Sunset == null) return L.NoSunset;
        return string.Format(L.DimSummary, EveningStart, Dusk, Sunset);
    }
}

class Controller
{
    public readonly Settings S;
    public List<Display> Displays = new List<Display>();
    Twilight today;
    int lastTarget = -1;

    public Controller(Settings s) { S = s; }

    public Twilight Today
    {
        get
        {
            if (today == null || today.Sunset == null || today.Sunset.Value.Date != DateTime.Today) today = Twilight.For(DateTime.Today, S);
            return today;
        }
    }

    public void Invalidate() { today = null; lastTarget = -1; }

    public int AutoTarget()
    {
        double f = Today.DayFactor(DateTime.Now);
        return (int)Math.Round(S.NightLevel + (S.DayLevel - S.NightLevel) * f);
    }

    public void SetMaster(int master, bool save = true)
    {
        S.Master = master;
        if (save) S.Save();
        var work = Displays.Select(d => new KeyValuePair<Display, int>(d, S.Map(d, master))).ToList();
        // DDC/CI writes take ~50 ms each; keep them off the UI thread
        ThreadPool.QueueUserWorkItem(_ => { foreach (var kv in work) kv.Key.Set((uint)kv.Value); });
    }

    public void SetSingle(Display d, int percent)
    {
        ThreadPool.QueueUserWorkItem(_ => d.Set((uint)percent));
    }

    // timer tick; only writes when the auto target actually moved
    public void ApplyAuto(bool force)
    {
        if (!S.Auto) return;
        int t = AutoTarget();
        if (!force && t == lastTarget) return;
        lastTarget = t;
        SetMaster(t);
    }

    // user moved "All" while auto is on: remember it as the new day or night level
    public void LearnMaster(int v)
    {
        if (!S.Auto) return;
        double f = Today.DayFactor(DateTime.Now);
        if (f >= 0.5) S.DayLevel = Clamp((v - S.NightLevel * (1 - f)) / f);
        else S.NightLevel = Clamp((v - S.DayLevel * f) / (1 - f));
        lastTarget = v;
        S.Save();
    }

    static int Clamp(double v) { return (int)Math.Round(Math.Max(0, Math.Min(100, v))); }

    public string Status()
    {
        return Today.Summary() + (S.Auto ? string.Format(L.Target, AutoTarget()) : "");
    }
}

// Coarse location from the Windows location service (local API, no web request); stops after the first fix.
class Locator
{
    System.Device.Location.GeoCoordinateWatcher watcher;

    public void Locate(Action<double, double> onFix)
    {
        try
        {
            if (watcher != null) watcher.Dispose();
            watcher = new System.Device.Location.GeoCoordinateWatcher(System.Device.Location.GeoPositionAccuracy.Default);
            var w = watcher;
            w.PositionChanged += (s, e) =>
            {
                var c = e.Position.Location;
                if (c.IsUnknown) return;
                w.Stop();
                // ~1 km is plenty for sun position and keeps the stored value coarse
                onFix(Math.Round(c.Latitude, 2), Math.Round(c.Longitude, 2));
            };
            w.Start(true);
        }
        catch { } // location service off or denied: keep the configured coordinates
    }
}

class HotkeyBox : TextBox
{
    public Keys Value;

    public HotkeyBox(Keys initial)
    {
        ReadOnly = true; Width = 200; Value = initial;
        Text = Settings.HotkeyText(Value);
        BackColor = SystemColors.Window;
    }

    // capture Alt/F10 etc. before the dialog treats them as menu/navigation keys
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var code = keyData & Keys.KeyCode;
        var mods = keyData & Keys.Modifiers;
        if (code == Keys.Tab && mods == Keys.None) return base.ProcessCmdKey(ref msg, keyData);
        if (code == Keys.Back || code == Keys.Delete) Value = Keys.None;
        else if (code == Keys.ControlKey || code == Keys.Menu || code == Keys.ShiftKey || code == Keys.LWin || code == Keys.RWin) return true;
        else if (mods == Keys.None) return true; // require at least one modifier
        else Value = code | mods;
        Text = Settings.HotkeyText(Value);
        return true;
    }
}

class SettingsForm : Form
{
    readonly Settings s;
    readonly HotkeyBox hkPanel, hk0, hk50, hk100;
    readonly NumericUpDown lat = Num(-90, 90, 4), lon = Num(-180, 180, 4);
    readonly CheckBox autoLocate = new CheckBox { Text = L.AutoLocate, AutoSize = true, Margin = new Padding(3, 4, 3, 4) };
    readonly NumericUpDown offset = Num(0, 120, 0), laptopMin = Num(0, 100, 0), day = Num(0, 100, 0), night = Num(0, 100, 0);

    static NumericUpDown Num(int min, int max, int dec)
    {
        return new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = dec, Increment = dec > 0 ? 0.01m : 1, Width = 90 };
    }

    public SettingsForm(Settings s)
    {
        this.s = s;
        Text = L.SettingsTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        TopMost = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        hkPanel = new HotkeyBox(s.HotkeyPanel);
        hk0 = new HotkeyBox(s.Hotkey0);
        hk50 = new HotkeyBox(s.Hotkey50);
        hk100 = new HotkeyBox(s.Hotkey100);

        lat.Value = (decimal)s.Lat; lon.Value = (decimal)s.Lon;
        autoLocate.Checked = s.AutoLocate;
        autoLocate.CheckedChanged += (o, e) => { lat.Enabled = lon.Enabled = !autoLocate.Checked; };
        lat.Enabled = lon.Enabled = !s.AutoLocate;
        offset.Value = s.DimOffsetMin; laptopMin.Value = s.LaptopMin;
        day.Value = s.DayLevel; night.Value = s.NightLevel;

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(12), Dock = DockStyle.Fill };
        Header(grid, L.HdrHotkeys);
        Row(grid, L.OpenPanel, hkPanel);
        Row(grid, string.Format(L.AllTo, 0), hk0);
        Row(grid, string.Format(L.AllTo, 50), hk50);
        Row(grid, string.Format(L.AllTo, 100), hk100);
        Header(grid, L.HdrLocation);
        grid.Controls.Add(autoLocate);
        grid.SetColumnSpan(autoLocate, 2);
        Row(grid, L.Latitude, lat);
        Row(grid, L.Longitude, lon);
        var near = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 4, 3, 4) };
        Action updateNear = () => near.Text = L.Location + ": " + (Places.Near((double)lat.Value, (double)lon.Value) ?? L.LocationUnknown);
        lat.ValueChanged += (o, e) => updateNear();
        lon.ValueChanged += (o, e) => updateNear();
        updateNear();
        grid.Controls.Add(near);
        grid.SetColumnSpan(near, 2);
        Row(grid, L.DimOffset, offset);
        Row(grid, L.DayLevel, day);
        Row(grid, L.NightLevel, night);
        Header(grid, L.HdrLaptop);
        Row(grid, L.LaptopMin, laptopMin);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = L.Cancel, DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
        buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
        grid.Controls.Add(new Label { Text = "BrightnessGlass " + Program.Version, AutoSize = true, ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Left | AnchorStyles.Bottom, Margin = new Padding(3, 12, 3, 8) });
        grid.Controls.Add(buttons);
        Controls.Add(grid);
        AcceptButton = ok; CancelButton = cancel;
    }

    static void Header(TableLayoutPanel g, string text)
    {
        var l = new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI Semibold", 9.5f), Margin = new Padding(3, 12, 3, 4) };
        g.Controls.Add(l);
        g.SetColumnSpan(l, 2);
    }

    static void Row(TableLayoutPanel g, string label, Control c)
    {
        g.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 12, 3) });
        g.Controls.Add(c);
    }

    public void Commit()
    {
        s.HotkeyPanel = hkPanel.Value; s.Hotkey0 = hk0.Value; s.Hotkey50 = hk50.Value; s.Hotkey100 = hk100.Value;
        s.Lat = (double)lat.Value; s.Lon = (double)lon.Value;
        s.AutoLocate = autoLocate.Checked;
        s.DimOffsetMin = (int)offset.Value; s.LaptopMin = (int)laptopMin.Value;
        s.DayLevel = (int)day.Value; s.NightLevel = (int)night.Value;
        s.Save();
    }
}

// Receives WM_HOTKEY for global RegisterHotKey registrations (one id per action).
class HotkeyWindow : NativeWindow, IDisposable
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    const int WM_HOTKEY = 0x0312;
    readonly HashSet<int> registered = new HashSet<int>();
    public event Action<int> Pressed;

    public HotkeyWindow() { CreateHandle(new CreateParams()); }

    public bool Register(int id, Keys k)
    {
        if (registered.Remove(id)) UnregisterHotKey(Handle, id);
        if ((k & Keys.KeyCode) == Keys.None) return true;
        uint mods = 0x4000; // MOD_NOREPEAT
        if ((k & Keys.Alt) != 0) mods |= 1;
        if ((k & Keys.Control) != 0) mods |= 2;
        if ((k & Keys.Shift) != 0) mods |= 4;
        if (!RegisterHotKey(Handle, id, mods, (uint)(k & Keys.KeyCode))) return false;
        registered.Add(id);
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && Pressed != null) Pressed(m.WParam.ToInt32());
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (var id in registered) UnregisterHotKey(Handle, id);
        registered.Clear();
        DestroyHandle();
    }
}

class TrayApp : ApplicationContext
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "BrightnessDDC";
    const int HkPanel = 1, Hk0 = 2, Hk50 = 3, Hk100 = 4;

    readonly Controller ctl = new Controller(Settings.Load());
    readonly NotifyIcon tray;
    readonly HotkeyWindow hotkeyWnd = new HotkeyWindow();
    readonly System.Windows.Forms.Timer autoTimer = new System.Windows.Forms.Timer { Interval = 60000 };
    readonly GlassPopup popup;
    readonly SynchronizationContext ui;
    readonly Locator locator = new Locator();

    public TrayApp()
    {
        ctl.Displays = Display.EnumerateAll();
        popup = new GlassPopup(ctl, OpenSettings, ExitThread, PresetHint);
        ui = SynchronizationContext.Current;

        var autostart = new ToolStripMenuItem(L.MenuAutostart) { Checked = IsAutostart(), CheckOnClick = true };
        autostart.CheckedChanged += (s, e) => SetAutostart(autostart.Checked);

        var menu = new ContextMenuStrip();
        menu.Items.Add(L.MenuSettings, null, (s, e) => OpenSettings());
        menu.Items.Add(L.MenuRescan, null, (s, e) => Rescan());
        menu.Items.Add(autostart);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L.MenuQuit, null, (s, e) => ExitThread());

        tray = new NotifyIcon { Icon = MakeIcon(), ContextMenuStrip = menu, Visible = true };
        UpdateTooltip();
        tray.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) popup.Toggle(); };

        hotkeyWnd.Pressed += OnHotkey;
        RegisterHotkeys();

        autoTimer.Tick += (s, e) => { ctl.ApplyAuto(false); UpdateTooltip(); popup.RefreshValues(); };
        autoTimer.Start();
        ctl.ApplyAuto(true);
        LocateIfEnabled();

        SystemEvents.PowerModeChanged += (s, e) =>
        {
            if (e.Mode != PowerModes.Resume) return;
            ui.Post(_ => { ctl.Invalidate(); ctl.ApplyAuto(true); LocateIfEnabled(); }, null); // maybe a new place after wake-up
        };
        SystemEvents.DisplaySettingsChanged += (s, e) => ui.Post(_ => Rescan(), null);
    }

    void LocateIfEnabled()
    {
        if (!ctl.S.AutoLocate) return;
        locator.Locate((lat, lon) => ui.Post(_ =>
        {
            if (Math.Abs(lat - ctl.S.Lat) < 0.005 && Math.Abs(lon - ctl.S.Lon) < 0.005) return;
            ctl.S.Lat = lat; ctl.S.Lon = lon; ctl.S.Save();
            ctl.Invalidate();
            ctl.ApplyAuto(true);
            popup.RefreshValues();
        }, null));
    }

    void OnHotkey(int id)
    {
        if (id == HkPanel) { popup.Toggle(); return; }
        int v = id == Hk0 ? 0 : id == Hk50 ? 50 : 100;
        ctl.SetMaster(v);
        UpdateTooltip();
        popup.RefreshValues();
    }

    string PresetHint()
    {
        return Settings.HotkeyText(ctl.S.HotkeyPanel);
    }

    void RegisterHotkeys()
    {
        var failed = new List<string>();
        var s = ctl.S;
        if (!hotkeyWnd.Register(HkPanel, s.HotkeyPanel)) failed.Add(Settings.HotkeyText(s.HotkeyPanel));
        if (!hotkeyWnd.Register(Hk0, s.Hotkey0)) failed.Add(Settings.HotkeyText(s.Hotkey0));
        if (!hotkeyWnd.Register(Hk50, s.Hotkey50)) failed.Add(Settings.HotkeyText(s.Hotkey50));
        if (!hotkeyWnd.Register(Hk100, s.Hotkey100)) failed.Add(Settings.HotkeyText(s.Hotkey100));
        if (failed.Count > 0)
            tray.ShowBalloonTip(5000, L.AppName, string.Format(L.HotkeyTaken, string.Join(", ", failed)), ToolTipIcon.Warning);
    }

    void UpdateTooltip()
    {
        string t = string.Format(L.Tooltip, ctl.S.Master) + (ctl.S.Auto ? " (auto)" : "");
        tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
    }

    void OpenSettings()
    {
        using (var f = new SettingsForm(ctl.S))
        {
            if (f.ShowDialog() != DialogResult.OK) return;
            f.Commit();
        }
        RegisterHotkeys();
        ctl.Invalidate();
        if (ctl.S.Auto) ctl.ApplyAuto(true); else ctl.SetMaster(ctl.S.Master); // laptop floor may have changed
        UpdateTooltip();
        LocateIfEnabled();
    }

    void Rescan()
    {
        popup.Hide();
        Display.Release(ctl.Displays);
        ctl.Displays = Display.EnumerateAll();
        ctl.Invalidate();
        if (ctl.S.Auto) ctl.ApplyAuto(true); else ctl.SetMaster(ctl.S.Master, false);
    }

    protected override void ExitThreadCore()
    {
        tray.Visible = false;
        tray.Dispose();
        hotkeyWnd.Dispose();
        Display.Release(ctl.Displays);
        base.ExitThreadCore();
    }

    static bool IsAutostart()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(RunName) != null;
    }

    static void SetAutostart(bool on)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
            else k.DeleteValue(RunName, false);
        }
    }

    static Icon MakeIcon()
    {
        using (var bmp = new Bitmap(32, 32))
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var c = Color.White;
            using (var pen = new Pen(c, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    g.DrawLine(pen, 16 + (float)(Math.Cos(a) * 10.5), 16 + (float)(Math.Sin(a) * 10.5),
                                    16 + (float)(Math.Cos(a) * 14), 16 + (float)(Math.Sin(a) * 14));
                }
                g.DrawEllipse(pen, 10, 10, 12, 12);
            }
            IntPtr h = bmp.GetHicon();
            var icon = (Icon)Icon.FromHandle(h).Clone();
            Native.DestroyIcon(h);
            return icon;
        }
    }
}

static class Program
{
    public static string Version
    {
        get
        {
            var a = (System.Reflection.AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                typeof(Program).Assembly, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
            return a != null ? a.InformationalVersion : "0.0.0-dev";
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0) return Cli(args);

        bool created;
        using (new Mutex(true, "BrightnessDDC_SingleInstance", out created))
        {
            if (!created) return 0;
            Glass.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApp());
        }
        return 0;
    }

    static int Cli(string[] args)
    {
        Native.AttachConsole(-1);
        Console.WriteLine();
        if (args[0].Equals("--version", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("BrightnessGlass " + Version);
            return 0;
        }
        var s = Settings.Load();
        var displays = Display.EnumerateAll();
        try
        {
            if (args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var d in displays) Console.WriteLine("{0,-24} {1,3} %  (raw {2}/{3})", d.Name, d.Percent, d.Current, d.Max);
                Console.WriteLine("All (master)             {0,3} %", s.Master);
                return 0;
            }

            if (args[0].Equals("sun", StringComparison.OrdinalIgnoreCase))
            {
                var ctl = new Controller(s);
                var tw = ctl.Today;
                Console.WriteLine("Location {0}, {1}  auto {2}  day {3} % / night {4} %", s.Lat, s.Lon, s.Auto ? "on" : "off", s.DayLevel, s.NightLevel);
                Console.WriteLine("Dawn {0:HH:mm}  sunrise {1:HH:mm}  sunset {2:HH:mm}  dusk {3:HH:mm}", tw.Dawn, tw.Sunrise, tw.Sunset, tw.Dusk);
                Console.WriteLine(tw.Summary());
                var start = tw.EveningStart.AddMinutes(-10);
                for (var t = start; t <= tw.Dusk.Value.AddMinutes(10); t = t.AddMinutes(5))
                {
                    int m = (int)Math.Round(s.NightLevel + (s.DayLevel - s.NightLevel) * tw.DayFactor(t));
                    Console.Write("{0:HH:mm}  all {1,3} %", t, m);
                    foreach (var d in displays) Console.Write("  {0} {1,3} %", d.Name, s.Map(d, m));
                    Console.WriteLine();
                }
                Console.WriteLine("Now: target all {0} %", ctl.AutoTarget());
                return 0;
            }

            uint pct;
            if (!uint.TryParse(args[0], out pct) || pct > 100)
            {
                Console.WriteLine("Usage: BrightnessGlass.exe [list | sun | --version | <0-100> [name-filter]]");
                return 1;
            }

            if (args.Length == 1)
            {
                s.Master = (int)pct; s.Save();
                foreach (var d in displays)
                {
                    int v = s.Map(d, (int)pct);
                    Console.WriteLine("{0,-24} -> {1} % {2}", d.Name, v, d.Set((uint)v) ? "ok" : "FAILED");
                }
                return 0;
            }

            var targets = displays.Where(d => d.Name.IndexOf(args[1], StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (targets.Count == 0) { Console.WriteLine("No matching display."); return 2; }
            int rc = 0;
            foreach (var d in targets)
            {
                bool ok = d.Set(pct);
                Console.WriteLine("{0,-24} -> {1} % {2}", d.Name, pct, ok ? "ok" : "FAILED");
                if (!ok) rc = 3;
            }
            return rc;
        }
        finally { Display.Release(displays); }
    }
}

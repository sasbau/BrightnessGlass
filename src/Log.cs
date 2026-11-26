// Small diagnostic log: %LOCALAPPDATA%\BrightnessGlass\BrightnessGlass.log (one old generation kept).
// Records display changes, monitors going silent and coming back, and what the auto mode wrote.
using System;
using System.Globalization;
using System.IO;

static class Log
{
    const long MaxBytes = 512 * 1024;
    static readonly object sync = new object();
    static string path;
    public static bool Enabled; // only the tray app logs, not the command line

    public static void Write(string format, params object[] args)
    {
        if (!Enabled) return;
        try
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ", CultureInfo.InvariantCulture)
                        + string.Format(CultureInfo.InvariantCulture, format, args) + Environment.NewLine;
            lock (sync)
            {
                if (path == null)
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BrightnessGlass");
                    Directory.CreateDirectory(dir);
                    path = Path.Combine(dir, "BrightnessGlass.log");
                }
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    string old = path + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(path, old);
                }
                File.AppendAllText(path, line);
            }
        }
        catch { } // logging must never break the app
    }
}

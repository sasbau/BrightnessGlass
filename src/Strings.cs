// UI strings in German and English; German when the Windows UI language is German, English otherwise.
using System.Globalization;

static class L
{
    public static readonly bool De = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";
    static string T(string de, string en) { return De ? de : en; }

    public static readonly string AppName = T("Helligkeit", "Brightness");

    // hotkeys
    public static readonly string KeyNone = T("(keiner)", "(none)");
    public static readonly string KeyCtrl = T("Strg", "Ctrl");
    public static readonly string KeyShift = T("Umschalt", "Shift");

    // twilight / status
    public static readonly string NoSunset = T("kein Sonnenuntergang heute", "no sunset today");
    public static readonly string DimSummary = T("Dimmen {0:HH:mm}–{1:HH:mm} (Untergang {2:HH:mm})", "Dimming {0:HH:mm}–{1:HH:mm} (sunset {2:HH:mm})");
    public static readonly string Target = T("  ·  Ziel {0} %", "  ·  target {0} %");

    // popup
    public static readonly string AutoTitle = T("Automatisch nach Sonnenstand", "Follow the sun automatically");
    public static readonly string AllDisplays = T("Alle Bildschirme", "All displays");
    public static readonly string LaptopDisplay = T("Laptop-Display", "Laptop display");

    // menu / tray
    public static readonly string MenuSettings = T("Einstellungen…", "Settings…");
    public static readonly string MenuRescan = T("Monitore neu erkennen", "Rescan displays");
    public static readonly string MenuAutostart = T("Mit Windows starten", "Start with Windows");
    public static readonly string MenuQuit = T("Beenden", "Quit");
    public static readonly string Tooltip = T("Helligkeit · Alle {0} %", "Brightness · All {0} %");
    public static readonly string HotkeyTaken = T("Schon belegt: {0}. Bitte in den Einstellungen andere wählen.", "Already in use: {0}. Please choose others in the settings.");

    // settings dialog
    public static readonly string SettingsTitle = T("Einstellungen", "Settings");
    public static readonly string NearCity = T("Nähe {0} ({1} km)", "near {0} ({1} km)");
    public static readonly string Location = T("Standort", "Location");
    public static readonly string LocationUnknown = T("unbekannt", "unknown");
    public static readonly string HdrHotkeys = T("Hotkeys (ins Feld klicken, Kombination drücken, Entf = keiner)", "Hotkeys (click a field, press the combination, Del = none)");
    public static readonly string OpenPanel = T("Panel öffnen", "Open panel");
    public static readonly string AllTo = T("Alle auf {0} %", "All to {0} %");
    public static readonly string HdrLocation = T("Standort und Dämmerung", "Location and twilight");
    public static readonly string AutoLocate = T("Standort automatisch ermitteln (Windows-Standortdienst, ~1 km)", "Detect location automatically (Windows location service, ~1 km)");
    public static readonly string Latitude = T("Breitengrad", "Latitude");
    public static readonly string Longitude = T("Längengrad", "Longitude");
    public static readonly string DimOffset = T("Dimmen beginnt Minuten vor Sonnenuntergang", "Start dimming minutes before sunset");
    public static readonly string DayLevel = T("„Alle“ tagsüber (%)", "\"All\" during the day (%)");
    public static readonly string NightLevel = T("„Alle“ nachts (%)", "\"All\" at night (%)");
    public static readonly string HdrLaptop = T("Laptop", "Laptop");
    public static readonly string LaptopMin = T("Laptop-Minimum bei „Alle“ = 0 % (in %)", "Laptop minimum when \"All\" = 0 % (in %)");
    public static readonly string Cancel = T("Abbrechen", "Cancel");
}

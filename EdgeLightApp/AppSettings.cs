using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace EdgeLightApp
{
    public class AppSettings
    {
        public double ColorTemperature { get; set; } = 0.5;
        public double Thickness        { get; set; } = 100;   // 100 % on fresh install
        public double Opacity          { get; set; } = 1.0;   // 100 % brightness on fresh install
        public bool   IsLightOn        { get; set; } = false;
        public bool   StartWithWindows { get; set; } = false;

        /// <summary>When true, the edge light turns on/off automatically with camera activity.</summary>
        public bool   AutoCameraLight  { get; set; } = false;

        /// <summary>When true, the global Alt+L hotkey toggles the edge light on/off.</summary>
        public bool   HotkeyEnabled    { get; set; } = false;

        /// <summary>Name of the active colour preset, or "Custom".</summary>
        public string SelectedPreset { get; set; } = "Neutral";

        /// <summary>
        /// Per-monitor enable flag keyed by Screen.DeviceName (e.g. "\\.\DISPLAY1").
        /// Missing key → monitor is enabled (backwards-compatible default).
        /// </summary>
        public Dictionary<string, bool> MonitorEnabled { get; set; } = new();

        // ── Persistence ──────────────────────────────────────────────────────

        private static readonly string SettingsDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AuraEdge");
        private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

        public void Save()
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }
    }
}

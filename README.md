<div align="center">

# ✦ AuraEdge

**Ambient edge-lighting overlay for Windows — inspired by macOS Tahoe.**

AuraEdge draws a glowing, colour-tuneable ring around the edges of every connected monitor.  
It reacts to your cursor in real time, integrates with your webcam, and disappears from screen recordings — all without touching a single display driver.

![AuraEdge banner placeholder](https://placehold.co/900x200/0c0c18/6c47db?text=AuraEdge+%E2%80%94+Ambient+Edge+Lighting+for+Windows)

[![.NET 10](https://img.shields.io/badge/.NET-10.0--windows-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/UI-WPF-blue)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2B-0078D4?logo=windows)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/license-MIT-green)](Installer/license.txt)
<div align="center">

# Direct Download
[![Download](https://img.shields.io/badge/Download-AuraEdge_Setup.exe-blue?style=for-the-badge&logo=github)](https://github.com/apranav/AuraEdge/releases/download/stable/AuraEdge_Setup.exe)

</div>
</div>

---

## ✨ Features

| Feature | Detail |
|---|---|
| 🌈 **Colour temperature** | Slide from cool icy-blue → neutral white → warm sunset-orange |
| 💡 **Brightness presets** | Cool / Neutral / Warm / Sunset one-click presets |
| 📺 **Multi-monitor** | Per-monitor on/off toggle; individual DPI-aware scaling |
| 🖱️ **Cursor halo** | Soft transparent hole follows the cursor in real time — no annoying border in your face |
| 🌟 **Glow bloom** | Dual-layer render: sharp inner ring + blurred outer bloom for a realistic light feel |
| 🔆 **Auto-brightness** | Saves your current screen brightness, bumps to 100 % when the light is ON, restores when OFF |
| 📷 **Camera auto-light** | Turns the edge light on automatically when your webcam is active (Zoom / Teams / Meet…) and off when you hang up |
| ⌨️ **Global hotkey** | `Alt + L` toggles the light from any app — no need to open the panel |
| 🚀 **Start with Windows** | Optional startup entry so AuraEdge is always ready when you boot |
| 🎞️ **Screen-capture hidden** | The overlay is invisible to OBS, Teams screen-share, screenshots — only YOU see it |
| 🗂️ **System tray** | Lives in the system tray; double-click to restore, right-click to quit |
| 💾 **Settings persistence** | All settings saved to `%APPDATA%\AuraEdge\settings.json` — survives restarts |
| 🖥️ **DPI-aware** | Uses `GetDpiForMonitor` (Win32 shcore.dll) per screen — looks pixel-perfect on 4 K and mixed-DPI setups |

---

## 🖥️ System Requirements

- Windows 10 version 2004 or later (Windows 11 recommended)
- .NET 10 Runtime *(bundled in the self-contained release — nothing to install)*
- Any GPU that supports WPF compositing (essentially everything)

---

## 🚀 Installation

### Option A — Download the release (recommended)

1. Go to [**Releases**](../../releases) and grab the latest `AuraEdge-Setup.exe`
2. Run the installer — it handles everything including the startup shortcut
3. AuraEdge appears in your system tray instantly

### Option B — Portable EXE

Download `AuraEdge.exe` from Releases and place it anywhere.  
On first run it creates `%APPDATA%\AuraEdge\settings.json` automatically.

---

## 🔨 Build from Source

### Prerequisites

| Tool | Version |
|---|---|
| Visual Studio 2022 | 17.12 + (or VS Code with C# Dev Kit) |
| .NET SDK | 10.0 + |

### Steps

```bash
git clone https://github.com/apranav/AuraEdge.git
cd AuraEdge
dotnet build EdgeLightApp/EdgeLightApp.csproj -c Release
```

### Publish self-contained single-file EXE

```bash
dotnet publish EdgeLightApp/EdgeLightApp.csproj \
  -c Release -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o ./publish
```

---

## ⚙️ How It Works

```
SettingsWindow (WPF, slide-in panel)
  │
  ├─ BrightnessController  — WMI WmiMonitorBrightness / WmiMonitorBrightnessMethods
  ├─ CameraMonitor         — polls HKCU registry CapabilityAccessManager every 2 s
  └─ MainWindow × N        — one transparent, click-through, always-on-top WPF window
                              per connected monitor
       ├─ GlowBorder       — blurred thick border (ambient bloom)
       ├─ LightBorder      — sharp thin border (crisp ring)
       └─ OpacityMask      — RadialGradientBrush updated at 20 fps to create cursor halo
```

WPF `AllowsTransparency="True"` + `WS_EX_TRANSPARENT` Win32 style makes the overlay
fully click-through. `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` hides it from
screen captures while keeping it visible on the physical display.

---

## 🗂️ Project Structure

```
AuraEdge/
├── EdgeLightApp/
│   ├── App.xaml / App.xaml.cs          — WPF application entry point
│   ├── AppSettings.cs                  — JSON settings model & persistence
│   ├── BrightnessController.cs         — WMI screen-brightness control
│   ├── CameraMonitor.cs                — Webcam activity detection via registry
│   ├── MainWindow.xaml / .cs           — Transparent overlay window (one per monitor)
│   ├── SettingsWindow.xaml / .cs       — Slide-in settings panel
│   ├── AppIcon.ico                     — App icon
│   └── EdgeLightApp.csproj             — Project file (.NET 10 WPF + WinForms)
├── Installer/
│   ├── AuraEdge.iss                    — Inno Setup script
│   └── license.txt                     — MIT License
└── EdgeLightApp.slnx                   — Solution file
```

---

## 🎛️ Settings Panel

Hover the thin strip on the right edge of your primary monitor to expand the panel.

| Control | Description |
|---|---|
| Power toggle (ON/OFF) | Show or hide the edge light |
| Monitor chips (M1 M2 …) | Enable/disable the overlay per screen |
| Color Temperature slider | Cool → Warm |
| Thickness slider | Ring width (10–100 px) |
| Brightness slider | Overlay opacity (10–100 %) |
| Start with Windows | Register/remove autostart |
| Camera auto-light | Auto-toggle with webcam |
| Global hotkey (Alt+L) | Keyboard toggle |
| Quit AuraEdge | Full exit |

---

## 📄 License

MIT — see [`Installer/license.txt`](Installer/license.txt)

---

<div align="center">

**Developed by [Krishna Pranav](https://github.com/krishnapranav)**

*If you find AuraEdge useful, consider giving it a ⭐ — it helps a lot!*

</div>

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;
using Forms = System.Windows.Forms;
using WinReg = Microsoft.Win32;

namespace EdgeLightApp
{
    public partial class SettingsWindow : Window
    {
        // ── State ─────────────────────────────────────────────────────────────

        /// <summary>Overlay windows keyed by Screen.DeviceName.</summary>
        private Dictionary<string, MainWindow> _overlayMap = new();

        private CameraMonitor      _camMonitor;
        private BrightnessController _brightness;
        private Forms.NotifyIcon   _notifyIcon = null!;
        private AppSettings        _settings;
        private bool               _initialized    = false;
        private bool               _manualOverride  = false;
        private bool               _hotkeyRegistered = false;

        // ── Win32 ─────────────────────────────────────────────────────────────

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        [DllImport("gdi32.dll")]  static extern bool DeleteObject(IntPtr h);

        [DllImport("shcore.dll", SetLastError = true)]
        static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromPoint(PTXY pt, uint dwFlags);

        [StructLayout(LayoutKind.Sequential)] struct PTXY { public int X; public int Y; }

        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const int  MDT_EFFECTIVE_DPI        = 0;

        // ── Hotkey Win32 ──────────────────────────────────────────────────────

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int    HOTKEY_ID   = 0xAE01;   // arbitrary unique id
        private const uint   MOD_ALT     = 0x0001;
        private const uint   VK_L        = 0x4C;
        private const int    WM_HOTKEY   = 0x0312;

        // ── Startup registry ──────────────────────────────────────────────────

        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegName = "AuraEdge";

        // ── Colour presets ────────────────────────────────────────────────────

        private static readonly (string Name, double Temp)[] Presets =
        {
            ("Cool",    0.00),
            ("Neutral", 0.50),
            ("Warm",    0.75),
            ("Sunset",  1.00),
        };

        // ── Constructor ───────────────────────────────────────────────────────

        public SettingsWindow()
        {
            InitializeComponent();

            _settings   = AppSettings.Load();
            _brightness = new BrightnessController();
            _camMonitor = new CameraMonitor();
            _camMonitor.CameraStatusChanged += OnCameraStatusChanged;
            // Camera monitor is started conditionally below, only if AutoCameraLight is enabled.

            CreateOverlays();

            // Restore UI from saved settings (slider events blocked by _initialized flag)
            TempSlider.Value             = _settings.ColorTemperature;
            ThicknessSlider.Value        = _settings.Thickness;
            OpacitySlider.Value          = _settings.Opacity;
            PowerBtn.IsChecked           = _settings.IsLightOn;
            StartupCheckBox.IsChecked    = _settings.StartWithWindows;
            CameraAutoCheckBox.IsChecked = _settings.AutoCameraLight;
            HotkeyCheckBox.IsChecked     = _settings.HotkeyEnabled;

            HighlightActivePreset(_settings.SelectedPreset);
            ApplySettingsToOverlays();

            if (_settings.IsLightOn)
                _brightness.SaveAndIncrease(100);  // Restore 100 % on startup if light was on

            RefreshOverlayVisibility();

            // Only poll camera if the feature is enabled
            if (_settings.AutoCameraLight)
                _camMonitor.Start();

            // Hotkey is registered after the window handle is created (see OnSourceInitialized).
            // We store the intent here so OnSourceInitialized can act on it.

            InitializeSystemTray();
            WinReg.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _initialized = true;
        }

        // ── Window lifetime ───────────────────────────────────────────────────

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Hook the window message pump so we can catch WM_HOTKEY
            var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            src?.AddHook(WndProc);

            // Register hotkey now that we have a valid HWND
            if (_settings.HotkeyEnabled)
                RegisterHotkeyInternal();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.Icon = CreateWindowIcon();

            BuildMonitorList();
            UpdateDpiHint();

            // SizeToContent="Height" already auto-sized the window; cap at 90% of work area
            this.MaxHeight = SystemParameters.WorkArea.Height * 0.9;

            // Position after content height is known
            this.Left = SystemParameters.WorkArea.Width - this.Width;
            this.Top  = (SystemParameters.WorkArea.Height - this.ActualHeight) / 2;
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            this.Left = SystemParameters.WorkArea.Width - this.ActualWidth;
        }

        // X closes to tray; actual quit is "Quit App"
        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            this.WindowState = WindowState.Minimized;
            base.OnClosing(e);
        }

        // Always-on-top: re-assert when another window gains focus
        private void Window_Deactivated(object sender, EventArgs e)
        {
            Topmost = false;
            Topmost = true;
        }

        // ── DPI helpers ───────────────────────────────────────────────────────

        private double GetDpiScaleForScreen(Forms.Screen screen)
        {
            var centre = new PTXY
            {
                X = screen.Bounds.Left + screen.Bounds.Width  / 2,
                Y = screen.Bounds.Top  + screen.Bounds.Height / 2
            };
            IntPtr hMon = MonitorFromPoint(centre, MONITOR_DEFAULTTONEAREST);
            if (hMon != IntPtr.Zero &&
                GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
                return dpiX / 96.0;

            var primary = Forms.Screen.PrimaryScreen;
            return primary == null ? 1.0 : primary.Bounds.Width / SystemParameters.PrimaryScreenWidth;
        }

        private uint GetScreenDpiValue(Forms.Screen screen)
        {
            var centre = new PTXY
            {
                X = screen.Bounds.Left + screen.Bounds.Width  / 2,
                Y = screen.Bounds.Top  + screen.Bounds.Height / 2
            };
            IntPtr hMon = MonitorFromPoint(centre, MONITOR_DEFAULTTONEAREST);
            if (hMon != IntPtr.Zero &&
                GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0)
                return dpiX;
            return 96;
        }

        /// <summary>
        /// Returns a DPI-adapted brightness target (0-100).
        /// Kept for reference; ON-mode now always uses 100 % per user request.
        /// </summary>
        private int GetDpiAdaptedBrightness()
        {
            var primary = Forms.Screen.PrimaryScreen;
            if (primary == null) return 100;
            uint dpi = GetScreenDpiValue(primary);
            // 96 DPI → 100%.  144 DPI → 90%.  192 DPI → 80%.  Clamped [70, 100].
            int target = (int)Math.Round(100 - (dpi - 96) * (20.0 / 96.0));
            return Math.Clamp(target, 70, 100);
        }

        private void UpdateDpiHint()
        {
            var primary = Forms.Screen.PrimaryScreen;
            if (primary == null) return;
            uint dpi = GetScreenDpiValue(primary);
            DpiHintText.Text      = $"Primary display: {dpi} DPI";
            DpiHintText.Foreground = dpi > 96
                ? new SolidColorBrush(Color.FromRgb(0x4a, 0x7a, 0xb0))
                : new SolidColorBrush(Color.FromRgb(0x2a, 0x3a, 0x5a));
        }

        // ── Overlay management ────────────────────────────────────────────────

        private void CreateOverlays()
        {
            foreach (var w in _overlayMap.Values) w.Close();
            _overlayMap.Clear();

            int idx = 1;
            foreach (var screen in Forms.Screen.AllScreens)
            {
                double dpi = GetDpiScaleForScreen(screen);
                var win = new MainWindow(screen.WorkingArea, dpi);
                _overlayMap[screen.DeviceName] = win;
                idx++;
            }
        }

        private void RefreshOverlayVisibility()
        {
            foreach (var (deviceName, overlay) in _overlayMap)
            {
                bool enabled = IsMonitorEnabled(deviceName);
                if (!enabled) { overlay.Hide(); continue; }

                overlay.Show();
                if (_settings.IsLightOn) overlay.SetOnMode();
                else                     overlay.SetOffMode();
            }
        }

        private void ApplySettingsToOverlays()
        {
            var color = TemperatureToColor(_settings.ColorTemperature);
            foreach (var overlay in _overlayMap.Values)
            {
                overlay.UpdateColor(color);
                overlay.UpdateThickness(_settings.Thickness);
                overlay.UpdateOpacity(_settings.Opacity);
            }
        }

        private bool IsMonitorEnabled(string deviceName)
        {
            if (!_settings.MonitorEnabled.TryGetValue(deviceName, out bool enabled))
                return true;  // Default: enabled
            return enabled;
        }

        private void TurnLightOn()
        {
            // Snapshot current brightness then push to 100 % while light is on.
            _brightness.SaveAndIncrease(100);
            _settings.IsLightOn = true;
            _settings.Save();
            RefreshOverlayVisibility();
        }

        private void TurnLightOff()
        {
            _brightness.Restore();
            _settings.IsLightOn = false;
            _settings.Save();
            RefreshOverlayVisibility();
        }

        // ── Monitor list UI ───────────────────────────────────────────────────

        private void BuildMonitorList()
        {
            MonitorsList.Children.Clear();

            var allScreens = Forms.Screen.AllScreens;
            for (int i = 0; i < allScreens.Length; i++)
            {
                var  screen  = allScreens[i];
                int  num     = i + 1;
                uint dpi     = GetScreenDpiValue(screen);
                string label = screen.Primary
                    ? $"Display {num}  (Primary)"
                    : $"Display {num}";
                string info  = $"{screen.Bounds.Width}×{screen.Bounds.Height}  ·  {dpi} DPI";
                bool enabled = IsMonitorEnabled(screen.DeviceName);

                MonitorsList.Children.Add(BuildMonitorCard(screen.DeviceName, label, info, enabled));
            }
        }

        private UIElement BuildMonitorCard(string deviceName, string label, string info, bool enabled)
        {
            // ── Card border ─────────────────────────────────────────────────
            var card = new Border
            {
                Background    = new SolidColorBrush(Color.FromRgb(0x13, 0x13, 0x1c)),
                CornerRadius  = new CornerRadius(8),
                Margin        = new Thickness(0, 0, 0, 8),
                Padding       = new Thickness(12, 10, 12, 10)
            };

            // ── Inner grid: text | toggle ────────────────────────────────────
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Monitor icon + name
            var nameBlock = new TextBlock
            {
                Text       = "🖥  " + label,
                Foreground = new SolidColorBrush(Color.FromRgb(0xe2, 0xe2, 0xf0)),
                FontWeight = FontWeights.SemiBold,
                FontSize   = 12,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI")
            };

            // Resolution / DPI line
            var infoBlock = new TextBlock
            {
                Text       = info,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6a, 0x6a, 0x88)),
                FontSize   = 10,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                Margin     = new Thickness(22, 3, 0, 0)  // indent under icon
            };

            var textStack = new StackPanel();
            textStack.Children.Add(nameBlock);
            textStack.Children.Add(infoBlock);
            Grid.SetColumn(textStack, 0);

            // Sliding toggle
            var toggle = new ToggleButton
            {
                IsChecked         = enabled,
                Tag               = deviceName,
                Style             = (Style)FindResource("MonitorToggleStyle"),
                VerticalAlignment = VerticalAlignment.Center
            };
            toggle.Click += MonitorToggle_Click;
            Grid.SetColumn(toggle, 1);

            grid.Children.Add(textStack);
            grid.Children.Add(toggle);
            card.Child = grid;
            return card;
        }

        private void MonitorToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton tb && tb.Tag is string deviceName)
            {
                _settings.MonitorEnabled[deviceName] = tb.IsChecked ?? true;
                _settings.Save();
                RefreshOverlayVisibility();
            }
        }

        // ── Colour temperature ────────────────────────────────────────────────

        private static Color TemperatureToColor(double v)
        {
            byte r, g, b;
            if (v <= 0.5)
            {
                double t = v * 2.0;
                r = (byte)(200 + t * 55);
                g = (byte)(230 + t * 25);
                b = 255;
            }
            else
            {
                double t = (v - 0.5) * 2.0;
                r = 255;
                g = (byte)(255 - t * 55);
                b = (byte)(255 - t * 105);
            }
            return Color.FromRgb(r, g, b);
        }

        // ── Colour preset helpers ─────────────────────────────────────────────

        private void HighlightActivePreset(string presetName)
        {
            var active  = (Style)FindResource("PresetBtnActive");
            var inactive = (Style)FindResource("PresetBtn");
            CoolBtn.Style    = presetName == "Cool"    ? active : inactive;
            NeutralBtn.Style = presetName == "Neutral" ? active : inactive;
            WarmBtn.Style    = presetName == "Warm"    ? active : inactive;
            SunsetBtn.Style  = presetName == "Sunset"  ? active : inactive;
        }

        private void ApplyPreset(string name, double temp)
        {
            _settings.ColorTemperature = temp;
            _settings.SelectedPreset   = name;
            TempSlider.Value           = temp;     // triggers TempSlider_ValueChanged
            HighlightActivePreset(name);
            _settings.Save();
        }

        // ── System tray ───────────────────────────────────────────────────────

        private void InitializeSystemTray()
        {
            _notifyIcon = new Forms.NotifyIcon
            {
                Icon    = CreateTrayIcon(),
                Visible = true,
                Text    = "AuraEdge"
            };
            _notifyIcon.DoubleClick += (_, _) => { Show(); WindowState = WindowState.Normal; Activate(); };

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Show", null, (_, _) => { Show(); WindowState = WindowState.Normal; Activate(); });
            menu.Items.Add("Quit", null, (_, _) => QuitApp());
            _notifyIcon.ContextMenuStrip = menu;
        }

        private System.Drawing.Icon CreateTrayIcon()
        {
            using var bmp = new System.Drawing.Bitmap(32, 32);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);
                using var pen   = new System.Drawing.Pen(System.Drawing.Color.FromArgb(220, 108, 71, 219), 3);
                using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(60, 59, 130, 246));
                g.DrawRectangle(pen, 3, 3, 25, 25);
                g.FillRectangle(brush, 6, 6, 20, 20);
            }
            var hicon = bmp.GetHicon();
            var icon  = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hicon).Clone();
            DestroyIcon(hicon);
            return icon;
        }

        private System.Windows.Media.ImageSource CreateWindowIcon()
        {
            using var bmp = new System.Drawing.Bitmap(32, 32);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.FromArgb(12, 12, 18));
                using var pen   = new System.Drawing.Pen(System.Drawing.Color.FromArgb(220, 108, 71, 219), 3);
                using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(60, 59, 130, 246));
                g.DrawRectangle(pen, 3, 3, 25, 25);
                g.FillRectangle(brush, 6, 6, 20, 20);
            }
            var hBitmap = bmp.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally { DeleteObject(hBitmap); }
        }

        // ── Startup with Windows ──────────────────────────────────────────────

        private static void SetStartupRegistry(bool enable)
        {
            try
            {
                using var key = WinReg.Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                if (key == null) return;
                if (enable)
                {
                    string exePath = System.IO.Path.ChangeExtension(
                        System.Reflection.Assembly.GetExecutingAssembly().Location, ".exe");
                    if (!System.IO.File.Exists(exePath))
                        exePath = Environment.ProcessPath ?? exePath;
                    key.SetValue(AppRegName, $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue(AppRegName, throwOnMissingValue: false);
                }
            }
            catch { /* silently ignore — user may lack registry write rights */ }
        }

        // ── Global hotkey (Alt+L) ─────────────────────────────────────────────

        private void RegisterHotkeyInternal()
        {
            if (_hotkeyRegistered) return;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;   // Not yet initialised — OnSourceInitialized will retry
            _hotkeyRegistered = RegisterHotKey(hwnd, HOTKEY_ID, MOD_ALT, VK_L);
        }

        private void UnregisterHotkeyInternal()
        {
            if (!_hotkeyRegistered) return;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                UnregisterHotKey(hwnd, HOTKEY_ID);
            _hotkeyRegistered = false;
        }

        /// <summary>Window message hook — catches WM_HOTKEY and toggles the edge light.</summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ToggleLightOnOff();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void ToggleLightOnOff()
        {
            bool nowOn = !(_settings.IsLightOn);
            PowerBtn.IsChecked = nowOn;
            _manualOverride    = true;
            if (nowOn) TurnLightOn(); else TurnLightOff();
        }

        // ── Quit ──────────────────────────────────────────────────────────────

        private void QuitApp()
        {
            UnregisterHotkeyInternal();   // Always clean up to avoid id leaking into next process
            _brightness.Restore();
            WinReg.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _notifyIcon?.Dispose();
            _camMonitor?.Stop();
            foreach (var w in _overlayMap.Values) w.Close();
            System.Windows.Application.Current.Shutdown();
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            bool on = (sender as ToggleButton)?.IsChecked ?? false;
            _manualOverride = true;
            if (on) TurnLightOn(); else TurnLightOff();
        }

        private void Quit_Click(object sender, RoutedEventArgs e) => QuitApp();

        private void TempSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_initialized) return;
            // Manual drag → clear preset highlight
            if (_settings.SelectedPreset != "Custom")
            {
                _settings.SelectedPreset = "Custom";
                HighlightActivePreset("Custom");
            }
            var color = TemperatureToColor(e.NewValue);
            foreach (var o in _overlayMap.Values) o.UpdateColor(color);
            _settings.ColorTemperature = e.NewValue;
            _settings.Save();
        }

        private void ThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_initialized) return;
            foreach (var o in _overlayMap.Values) o.UpdateThickness(e.NewValue);
            _settings.Thickness = e.NewValue;
            _settings.Save();
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_initialized) return;
            foreach (var o in _overlayMap.Values) o.UpdateOpacity(e.NewValue);
            _settings.Opacity = e.NewValue;
            _settings.Save();
        }

        private void StartupCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_initialized) return;
            bool enable = StartupCheckBox.IsChecked ?? false;
            _settings.StartWithWindows = enable;
            _settings.Save();
            SetStartupRegistry(enable);
        }

        private void HotkeyCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_initialized) return;
            bool enable = HotkeyCheckBox.IsChecked ?? false;
            _settings.HotkeyEnabled = enable;
            _settings.Save();

            if (enable) RegisterHotkeyInternal();
            else        UnregisterHotkeyInternal();
        }

        private void CameraAutoCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_initialized) return;
            bool enable = CameraAutoCheckBox.IsChecked ?? false;
            _settings.AutoCameraLight = enable;
            _settings.Save();

            if (enable)
                _camMonitor.Start();   // Begin polling immediately
            else
            {
                _camMonitor.Stop();    // Stop polling; do NOT auto-turn off the light
                _manualOverride = false;
            }
        }

        private void CoolBtn_Click(object sender, RoutedEventArgs e)    => ApplyPreset("Cool",    0.00);
        private void NeutralBtn_Click(object sender, RoutedEventArgs e) => ApplyPreset("Neutral", 0.50);
        private void WarmBtn_Click(object sender, RoutedEventArgs e)    => ApplyPreset("Warm",    0.75);
        private void SunsetBtn_Click(object sender, RoutedEventArgs e)  => ApplyPreset("Sunset",  1.00);

        /// <summary>
        /// Camera events only fire when AutoCameraLight is enabled.
        /// Manual override is respected: if the user toggled the light manually,
        /// the camera turning on will not override their choice until the camera stops.
        /// </summary>
        private void OnCameraStatusChanged(object? sender, bool isCameraOn)
        {
            Dispatcher.Invoke(() =>
            {
                // Feature guard — ignore events if the user has disabled auto camera light
                if (!_settings.AutoCameraLight) return;

                if (isCameraOn)
                {
                    if (_manualOverride) return;  // User has control; do not interfere
                    PowerBtn.IsChecked = true;
                    TurnLightOn();
                }
                else
                {
                    _manualOverride    = false;
                    PowerBtn.IsChecked = false;
                    TurnLightOff();
                }
            });
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                CreateOverlays();
                ApplySettingsToOverlays();
                RefreshOverlayVisibility();
                BuildMonitorList();   // Rebuild monitor cards for new screen topology
                UpdateDpiHint();
            });
        }
    }
}

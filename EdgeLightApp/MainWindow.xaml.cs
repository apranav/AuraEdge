using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfPoint = System.Windows.Point;

namespace EdgeLightApp
{
    public partial class MainWindow : Window
    {
        // ── Win32 ────────────────────────────────────────────────────────────

        const int WS_EX_TRANSPARENT = 0x00000020;
        const int GWL_EXSTYLE       = -20;

        /// <summary>Hides the window from screen captures/recordings while keeping it visible on the physical display.</summary>
        const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        [DllImport("user32.dll")] static extern int  GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern int  SetWindowLong(IntPtr hwnd, int index, int newStyle);
        [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        // ── State ────────────────────────────────────────────────────────────

        private DispatcherTimer _mouseTracker = null!;
        private double _currentThickness = 20;
        private System.Drawing.Rectangle _screenBounds;

        /// <summary>Physical-pixels-per-WPF-DIP scaling factor for this screen.</summary>
        private readonly double _dpiScale;

        // Reusable opacity-mask brush — updated each tick instead of recreated
        private readonly RadialGradientBrush _cursorMask;

        // ── Constructor ──────────────────────────────────────────────────────

        public MainWindow(System.Drawing.Rectangle workArea, double dpiScale)
        {
            InitializeComponent();

            _screenBounds = workArea;
            _dpiScale     = dpiScale;

            this.Left   = workArea.Left   / dpiScale;
            this.Top    = workArea.Top    / dpiScale;
            this.Width  = workArea.Width  / dpiScale;
            this.Height = workArea.Height / dpiScale;

            // Transparent at cursor → white (opaque) at radius edge
            _cursorMask = new RadialGradientBrush(Colors.Transparent, Colors.White)
            {
                MappingMode    = BrushMappingMode.RelativeToBoundingBox,
                SpreadMethod   = GradientSpreadMethod.Pad,
                Center         = new WpfPoint(0.5, 0.5),
                GradientOrigin = new WpfPoint(0.5, 0.5),
                RadiusX        = 0.05,
                RadiusY        = 0.05
            };

            InitializeMouseTracker();
        }

        // ── Win32 setup ──────────────────────────────────────────────────────

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr hwnd       = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);

            // Exclude from screen capture / recording / remote desktop sessions.
            // The window is still fully visible on the local physical display.
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
        }

        // ── Mouse tracker ────────────────────────────────────────────────────

        private void InitializeMouseTracker()
        {
            _mouseTracker          = new DispatcherTimer();
            _mouseTracker.Interval = TimeSpan.FromMilliseconds(50); // 20 fps
            _mouseTracker.Tick    += CheckMousePosition;
            // Starts only when SetOnMode() is called
        }

        private void CheckMousePosition(object? sender, EventArgs e)
        {
            if (!GetCursorPos(out POINT p)) return;

            double relXPhys = p.X - _screenBounds.Left;
            double relYPhys = p.Y - _screenBounds.Top;

            // Strict < so exactly-at-edge pixel (width/height) is treated as off-screen
            bool inScreen = relXPhys >= 0 && relXPhys < _screenBounds.Width
                         && relYPhys >= 0 && relYPhys < _screenBounds.Height;

            if (!inScreen)
            {
                LightContainer.OpacityMask = null;
                return;
            }

            double normX = (relXPhys / _dpiScale) / ActualWidth;
            double normY = (relYPhys / _dpiScale) / ActualHeight;

            // Hole radius halved: was Max(60, thickness*3.0), now Max(30, thickness*1.5)
            double radiusDips = Math.Max(30, _currentThickness * 1.5);
            double normRx     = radiusDips / ActualWidth;
            double normRy     = radiusDips / ActualHeight;

            _cursorMask.Center         = new WpfPoint(normX, normY);
            _cursorMask.GradientOrigin = new WpfPoint(normX, normY);
            _cursorMask.RadiusX        = normRx;
            _cursorMask.RadiusY        = normRy;

            if (LightContainer.OpacityMask == null)
                LightContainer.OpacityMask = _cursorMask;
        }

        // ── Display mode switching ────────────────────────────────────────────

        /// <summary>Switch to ON mode: show glowing edge, enable cursor hover effect.</summary>
        public void SetOnMode()
        {
            OnStateDisplay.Visibility  = Visibility.Visible;
            OffStateDisplay.Visibility = Visibility.Collapsed;
            _mouseTracker.Start();
        }

        /// <summary>
        /// Switch to OFF mode: show right-edge AuraEdge panel, stop cursor hover effect.
        /// </summary>
        public void SetOffMode()
        {
            OnStateDisplay.Visibility  = Visibility.Collapsed;
            OffStateDisplay.Visibility = Visibility.Visible;
            _mouseTracker.Stop();
            LightContainer.OpacityMask = null;
        }

        // ── Public update API ─────────────────────────────────────────────────

        public void UpdateThickness(double thickness)
        {
            _currentThickness = thickness;
            double corner = Math.Max(15, thickness);

            // Solid strip: +40% wider (multiplier 0.4 → 0.56)
            LightBorder.BorderThickness = new Thickness(Math.Max(3, thickness * 0.56));
            LightBorder.CornerRadius    = new CornerRadius(corner);
            // Transparent glow: −50% narrower (multiplier 1.0 → 0.5)
            GlowBorder.BorderThickness  = new Thickness(thickness * 0.5);
            GlowBorder.CornerRadius     = new CornerRadius(corner);
            // DPI-aware glow: halved to 50% of previous intensity (was ×1.5, now ×0.75)
            GlowBlur.Radius = (thickness * 0.75) / _dpiScale;
        }

        public void UpdateColor(Color color)
        {
            var brush = new SolidColorBrush(color);
            LightBorder.BorderBrush  = brush;
            GlowBorder.BorderBrush   = brush.Clone();
        }

        public void UpdateOpacity(double opacity)
        {
            LightContainer.BeginAnimation(Grid.OpacityProperty, null);
            LightContainer.Opacity = opacity;
        }
    }
}

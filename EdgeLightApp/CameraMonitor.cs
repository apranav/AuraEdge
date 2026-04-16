using Microsoft.Win32;
using System;
using System.Linq;
using System.Windows.Threading;

namespace EdgeLightApp
{
    public class CameraMonitor
    {
        // Nullable — correctly reflects that no subscriber is required.
        public event EventHandler<bool>? CameraStatusChanged;

        private DispatcherTimer _timer;
        private bool _wasCameraOn = false;

        // Registry paths where Windows logs camera usage
        private const string BasePath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";

        public CameraMonitor()
        {
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(2); // Check every 2 seconds
            _timer.Tick += CheckRegistry;
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();

        private void CheckRegistry(object? sender, EventArgs e)
        {
            bool isCameraOn = IsCameraActive();

            // Only notify if the status has actually changed
            if (isCameraOn != _wasCameraOn)
            {
                _wasCameraOn = isCameraOn;
                CameraStatusChanged?.Invoke(this, isCameraOn);
            }
        }

        private bool IsCameraActive()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(BasePath);
                if (key == null) return false;

                // 1. Check "Packaged" Apps (Windows Store apps, Camera App, New Teams)
                if (CheckSubKeysForActivity(key)) return true;

                // 2. Check "NonPackaged" Apps (Classic Zoom, Chrome, Old Teams, Desktop apps)
                using var nonPackagedKey = key.OpenSubKey("NonPackaged");
                if (nonPackagedKey != null && CheckSubKeysForActivity(nonPackagedKey))
                    return true;
            }
            catch
            {
                // If we don't have permission or registry is busy, assume off to be safe
                return false;
            }

            return false;
        }

        private static bool CheckSubKeysForActivity(RegistryKey parentKey)
        {
            foreach (var subKeyName in parentKey.GetSubKeyNames())
            {
                // Skip the "NonPackaged" folder itself when we are in the root
                if (subKeyName == "NonPackaged") continue;

                using var appKey = parentKey.OpenSubKey(subKeyName);

                // "LastUsedTimeStop" is 0 while the app is actively using the camera
                if (appKey?.GetValue("LastUsedTimeStop") is long time && time == 0)
                    return true;
            }
            return false;
        }
    }
}
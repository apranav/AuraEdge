using System;
using System.Management;

namespace EdgeLightApp
{
    public class BrightnessController
    {
        // -1 means "not yet saved"; any non-negative value is the original user brightness.
        private int _savedBrightness = -1;

        public int GetBrightness()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("root\\WMI",
                    "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                foreach (ManagementObject obj in searcher.Get())
                    return Convert.ToInt32(obj["CurrentBrightness"]);
            }
            catch { }
            return -1;
        }

        public void SetBrightness(int brightness)
        {
            try
            {
                brightness = Math.Clamp(brightness, 0, 100);
                using var searcher = new ManagementObjectSearcher("root\\WMI",
                    "SELECT * FROM WmiMonitorBrightnessMethods");
                foreach (ManagementObject obj in searcher.Get())
                    obj.InvokeMethod("WmiSetBrightness", new object[] { 1, brightness });
            }
            catch { }
        }

        /// <summary>
        /// Saves the current brightness (only on first call) and sets a new target level.
        /// Subsequent calls while light is on will NOT overwrite the saved value.
        /// </summary>
        public void SaveAndIncrease(int targetBrightness)
        {
            // Only snapshot the brightness once so rapid on/off cycles can't
            // accidentally save 100 (max) over the user's original value.
            if (_savedBrightness < 0)
            {
                int current = GetBrightness();
                if (current >= 0)
                    _savedBrightness = current;
            }
            SetBrightness(targetBrightness);
        }

        /// <summary>
        /// Restores the brightness that was saved by the first SaveAndIncrease call.
        /// </summary>
        public void Restore()
        {
            if (_savedBrightness >= 0)
            {
                SetBrightness(_savedBrightness);
                _savedBrightness = -1;
            }
        }
    }
}
